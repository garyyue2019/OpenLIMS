using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Modules.Batch;
using Xunit;

namespace OpenLIMS.Batch.IntegrationTests;

[CollectionDefinition("batch-postgres", DisableParallelization = true)]
public sealed class BatchPostgresCollection;

[Collection("batch-postgres")]
[Trait("Profile", "batch")]
public sealed class BatchPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_batch_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Batch_creation_members_and_evidence_atomically_persist_facts_audit_and_outbox()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, allocationDecision: AllocationStatusDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBatchService>();

        var batch = await service.CreateAsync(CreateRequest(), "corr-create", TestContext.Current.CancellationToken);
        var specimen = await service.AddMemberAsync(
            batch.BatchId, SpecimenMember(1), "corr-member", TestContext.Current.CancellationToken);
        var qc = await service.AddMemberAsync(
            batch.BatchId, QcMember(2), "corr-qc", TestContext.Current.CancellationToken);
        var evidence = await service.AddEvidenceAsync(
            batch.BatchId, EvidenceRequest(3), "corr-evidence", TestContext.Current.CancellationToken);

        Assert.Equal(1, batch.Version);
        Assert.Equal(2, specimen.BatchVersion);
        Assert.Equal(AllocationStatusDecisions.Allowed, specimen.AllocationGateDecision);
        Assert.Equal(3, qc.BatchVersion);
        Assert.Equal(4, evidence.BatchVersion);
        Assert.Equal(1, await CountAsync(connectionString, "batch.batch"));
        Assert.Equal(2, await CountAsync(connectionString, "batch.batch_member"));
        Assert.Equal(1, await CountAsync(connectionString, "batch.batch_evidence"));
        Assert.Equal(4, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(4, await CountAsync(connectionString, "platform.outbox"));
    }

    [Fact]
    public async Task Concurrent_member_adds_with_one_expected_version_append_only_once()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string batchId;
        await using (var setup = BuildProvider(connectionString, AllocationStatusDecisions.Allowed))
        {
            using var setupScope = setup.CreateScope();
            batchId = (await setupScope.ServiceProvider.GetRequiredService<IBatchService>()
                .CreateAsync(CreateRequest(), "corr-setup", TestContext.Current.CancellationToken)).BatchId;
        }

        await using var firstProvider = BuildProvider(connectionString, AllocationStatusDecisions.Allowed, "operator-a");
        await using var secondProvider = BuildProvider(connectionString, AllocationStatusDecisions.Allowed, "operator-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IBatchService>().AddMemberAsync(
            batchId, QcMember(1, "QC-A"), "corr-a", TestContext.Current.CancellationToken);
        var second = secondScope.ServiceProvider.GetRequiredService<IBatchService>().AddMemberAsync(
            batchId, QcMember(1, "QC-B"), "corr-b", TestContext.Current.CancellationToken);

        var outcomes = await Task.WhenAll(CaptureAsync(first), CaptureAsync(second));

        Assert.Single(outcomes, outcome => outcome.Error is null);
        var failed = Assert.Single(outcomes, outcome => outcome.Error is not null).Error!;
        Assert.Equal(BatchErrorCodes.ExpectedVersionConflict, failed.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "batch.batch_member"));
    }

    [Fact]
    public async Task Batch_facts_reject_update_and_delete()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, AllocationStatusDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBatchService>();
        var batch = await service.CreateAsync(CreateRequest(), "corr-immutable", TestContext.Current.CancellationToken);
        await service.AddMemberAsync(batch.BatchId, QcMember(1), "corr-member", TestContext.Current.CancellationToken);

        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update batch.batch_member set customer_id = 'HACK'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from batch.batch"));

        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
    }

    [Fact]
    public async Task Allocation_gate_blocked_fails_closed_without_member_facts()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string batchId;
        await using (var setup = BuildProvider(connectionString, AllocationStatusDecisions.Allowed))
        {
            using var setupScope = setup.CreateScope();
            batchId = (await setupScope.ServiceProvider.GetRequiredService<IBatchService>()
                .CreateAsync(CreateRequest(), "corr-setup", TestContext.Current.CancellationToken)).BatchId;
        }

        await using var blockedProvider = BuildProvider(connectionString, AllocationStatusDecisions.Blocked);
        using var blockedScope = blockedProvider.CreateScope();

        var exception = await Assert.ThrowsAsync<BatchDomainException>(() =>
            blockedScope.ServiceProvider.GetRequiredService<IBatchService>().AddMemberAsync(
                batchId, SpecimenMember(1), "corr-blocked", TestContext.Current.CancellationToken));

        Assert.Equal(BatchErrorCodes.EligibilityBlocked, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "batch.batch_member"));
        Assert.Equal(1, await CountAsync(connectionString, "batch.audit_attempt"));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_back_batch_facts_and_appends_failure_attempt(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString, AllocationStatusDecisions.Allowed);
            using var scope = provider.CreateScope();

            var exception = await Assert.ThrowsAsync<BatchDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<IBatchService>().CreateAsync(
                    CreateRequest(), $"corr-{failedWriter}", TestContext.Current.CancellationToken));

            Assert.Equal(BatchErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "batch.batch"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.audit_intent"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "batch.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
    }

    [Fact]
    public async Task Qc_failure_freezes_whole_batch_and_blocks_further_changes()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, AllocationStatusDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBatchService>();
        var batch = await service.CreateAsync(CreateRequest(), "corr-v1", TestContext.Current.CancellationToken);
        await service.AddMemberAsync(batch.BatchId, SpecimenMember(1), "corr-m1", TestContext.Current.CancellationToken);
        await service.AddMemberAsync(batch.BatchId, QcMember(2), "corr-m2", TestContext.Current.CancellationToken);

        var freeze = await service.FreezeAsync(
            batch.BatchId,
            new FreezeBatchRequest(3, BatchContract.RuleSetVersion, BatchFreezeCauses.QcFailure,
                new BatchVersionedReference("NEW-RUN-1", 1)),
            "corr-freeze",
            TestContext.Current.CancellationToken);
        var afterFreezeMember = await CaptureAsync(service.AddMemberAsync(
            batch.BatchId, QcMember(4, "QC-LATE"), "corr-late", TestContext.Current.CancellationToken));
        var status = await scope.ServiceProvider.GetRequiredService<IBatchStatusPort>().EvaluateAsync(
            new BatchStatusRequest("group-a", batch.BatchId, 4, BatchContract.RuleSetVersion)
            {
                CorrelationId = "corr-status"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, freeze.AffectedMemberCount);
        Assert.Equal(BatchErrorCodes.BatchFrozen, afterFreezeMember.Error!.ErrorCode);
        Assert.Equal(BatchStatusDecisions.Blocked, status.Decision);
        Assert.Contains(BatchStatusReasons.BatchFrozen, status.ReasonCodes);
        Assert.Equal(2, await CountAsync(connectionString, "batch.batch_member"));
        Assert.Equal(1, await CountAsync(connectionString, "batch.batch_freeze"));
    }

    [Fact]
    public async Task Current_version_is_allowed_while_stale_version_and_unknown_rule_are_unknown()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, AllocationStatusDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBatchService>();
        var batch = await service.CreateAsync(CreateRequest(), "corr-v1", TestContext.Current.CancellationToken);
        await service.AddMemberAsync(batch.BatchId, QcMember(1), "corr-v2", TestContext.Current.CancellationToken);
        var port = scope.ServiceProvider.GetRequiredService<IBatchStatusPort>();

        var allowed = await port.EvaluateAsync(
            Status(batch.BatchId, 2), TestContext.Current.CancellationToken);
        var stale = await port.EvaluateAsync(
            Status(batch.BatchId, 1), TestContext.Current.CancellationToken);
        var unknownRule = await port.EvaluateAsync(
            Status(batch.BatchId, 2) with { RuleSetVersion = "BATCH-EXECUTION@latest" },
            TestContext.Current.CancellationToken);

        Assert.Equal(BatchStatusDecisions.Allowed, allowed.Decision);
        Assert.Equal(BatchStatusDecisions.Unknown, stale.Decision);
        Assert.Contains(BatchStatusReasons.BatchVersionMismatch, stale.ReasonCodes);
        Assert.Equal(BatchStatusDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(BatchStatusReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
    }

    private static async Task<(object? Result, BatchDomainException? Error)> CaptureAsync<T>(Task<T> task)
    {
        try
        {
            return (await task, null);
        }
        catch (BatchDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        string allocationDecision,
        string actorId = "operator-a")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformDependencies(new PlatformDependencyOptions
        {
            PostgresConnectionString = connectionString,
            OidcAuthority = "https://issuer.invalid",
            OidcAudience = "openlims-api",
            ObjectStorageEndpoint = "https://storage.invalid",
            ObjectStorageBucket = "test",
            ObjectStorageAccessKey = "test",
            ObjectStorageSecretKey = "test",
            PostgresCommandTimeoutSeconds = 10,
            OidcMetadataTimeoutSeconds = 1,
            ObjectStorageProbeTimeoutSeconds = 1,
            DependencyProbeTimeoutSeconds = 2
        });
        services.AddSingleton<ICurrentOrganizationContext>(
            new DeploymentOrganizationContext(new OrganizationScope("group-a")));
        services.AddSingleton<ICurrentActorContext>(new FixedActorContext(new ActorContext(actorId, "group-a")));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        new BatchModule(connectionString).AddApiServices(services);
        services.RemoveAll<IBatchAuthorizationPort>();
        services.AddSingleton<IBatchAuthorizationPort>(new FixedAuthorizationPort(true));
        services.RemoveAll<IAllocationStatusPort>();
        services.AddSingleton<IAllocationStatusPort>(new FixedAllocationStatusPort(allocationDecision));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateBatchRequest CreateRequest() => new(
        BatchContract.RuleSetVersion, new BatchObjectContext("LEGAL-A", "LAB-A"), BatchTypes.Analytical);

    private static AddBatchMemberRequest SpecimenMember(long expectedVersion) => new(
        expectedVersion, BatchContract.RuleSetVersion, BatchMemberTypes.Specimen,
        "CUSTOMER-A", "ORDER-A", "TOYS",
        AllocationId: "00000000000000000000000000000031",
        ExpectedSubjectAllocationVersion: 2);

    private static AddBatchMemberRequest QcMember(long expectedVersion, string qcId = "QC-CTRL-7") => new(
        expectedVersion, BatchContract.RuleSetVersion, BatchMemberTypes.QcSample,
        "CUSTOMER-QC", "ORDER-QC", "TOYS",
        QcRef: new BatchVersionedReference(qcId, 1));

    private static AddBatchEvidenceRequest EvidenceRequest(long expectedVersion) => new(
        expectedVersion, BatchContract.RuleSetVersion, BatchEvidenceSources.Cds,
        new BatchVersionedReference("CDS-SEQ-9", 3), new string('a', 64));

    private static BatchStatusRequest Status(string batchId, long expectedVersion) => new(
        "group-a", batchId, expectedVersion, BatchContract.RuleSetVersion);

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for batch integration tests.");

    private static string ConnectionString() => new NpgsqlConnectionStringBuilder(AdminConnectionString())
    {
        Database = DedicatedDatabaseName
    }.ConnectionString;

    private static async Task EnsureDedicatedDatabaseAsync()
    {
        if (_databaseEnsured)
            return;

        await using var dataSource = NpgsqlDataSource.Create(AdminConnectionString());
        await using var exists = dataSource.CreateCommand("select 1 from pg_database where datname = $1");
        exists.Parameters.AddWithValue(DedicatedDatabaseName);
        if (await exists.ExecuteScalarAsync(TestContext.Current.CancellationToken) is null)
        {
            try
            {
                await using var create = dataSource.CreateCommand($"create database \"{DedicatedDatabaseName}\"");
                await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == "42P04")
            {
            }
        }

        _databaseEnsured = true;
    }

    private static async Task PrepareAsync(string connectionString)
    {
        await EnsureDedicatedDatabaseAsync();
        await PlatformMigrationRunner.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await BatchMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              batch.audit_attempt,
              batch.batch_freeze,
              batch.batch_evidence,
              batch.batch_member,
              batch.batch,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_batch_audit on platform.audit_intent;
                drop function if exists platform.fail_batch_audit();
                create or replace function platform.fail_batch_audit() returns trigger language plpgsql as $$
                begin
                  if new.action in ('CREATE_BATCH', 'ADD_BATCH_MEMBER', 'ADD_BATCH_EVIDENCE', 'FREEZE_BATCH') then
                    raise exception 'forced batch audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_batch_audit before insert on platform.audit_intent
                for each row execute function platform.fail_batch_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_batch_outbox on platform.outbox;
                drop function if exists platform.fail_batch_outbox();
                create or replace function platform.fail_batch_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type like 'Batch%' then
                    raise exception 'forced batch outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_batch_outbox before insert on platform.outbox
                for each row execute function platform.fail_batch_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_batch_audit on platform.audit_intent;
                drop function if exists platform.fail_batch_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_batch_outbox on platform.outbox;
                drop function if exists platform.fail_batch_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(string connectionString, string table)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand($"select count(*) from {table}");
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FixedAuthorizationPort(bool allowed) : IBatchAuthorizationPort
    {
        public ValueTask<BatchAuthorizationDecision> AuthorizeAsync(
            BatchAuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed ? BatchAuthorizationDecision.Permit : BatchAuthorizationDecision.Deny);
    }

    private sealed class FixedAllocationStatusPort(string decision) : IAllocationStatusPort
    {
        public ValueTask<AllocationStatusResult> EvaluateAsync(
            AllocationStatusRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AllocationStatusResult(
                decision,
                decision == AllocationStatusDecisions.Allowed ? [] : [AllocationStatusReasons.AllocationReleased],
                request.AllocationId,
                "ACTIVE",
                request.ExpectedSubjectAllocationVersion,
                AllocationContract.RuleSetVersion));
    }

    private sealed class FixedActorContext(ActorContext actor) : ICurrentActorContext
    {
        public ActorContext? Current { get; } = actor;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
