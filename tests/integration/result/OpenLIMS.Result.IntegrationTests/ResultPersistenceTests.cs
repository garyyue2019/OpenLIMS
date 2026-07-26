using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Result;
using OpenLIMS.Modules.Result;
using Xunit;

namespace OpenLIMS.Result.IntegrationTests;

[CollectionDefinition("result-postgres", DisableParallelization = true)]
public sealed class ResultPostgresCollection;

[Collection("result-postgres")]
[Trait("Profile", "result")]
public sealed class ResultPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_result_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Group_and_observation_atomically_persist_facts_audit_and_outbox()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, BatchStatusDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IResultGroupService>();

        var group = await service.CreateGroupAsync(GroupRequest(), "corr-create", TestContext.Current.CancellationToken);
        var observation = await service.AddObservationAsync(
            group.ResultGroupId, Observation(1, ResultObservationKinds.Initial), "corr-obs", TestContext.Current.CancellationToken);

        Assert.Equal(1, group.Version);
        Assert.Equal("ALLOWED", group.BatchGateDecision);
        Assert.Equal(2, observation.GroupVersion);
        Assert.Equal(1, await CountAsync(connectionString, "result.result_group"));
        Assert.Equal(1, await CountAsync(connectionString, "result.result_observation"));
        Assert.Equal(2, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(2, await CountAsync(connectionString, "platform.outbox"));
    }

    [Fact]
    public async Task Retest_flow_enforces_pre_rule_and_blocks_favorable_adoption()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, BatchStatusDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IResultGroupService>();
        var group = await service.CreateGroupAsync(GroupRequest(), "corr-v1", TestContext.Current.CancellationToken);
        var initial = await service.AddObservationAsync(
            group.ResultGroupId, Observation(1, ResultObservationKinds.Initial), "corr-initial", TestContext.Current.CancellationToken);

        var withoutRule = await CaptureAsync(service.AddObservationAsync(
            group.ResultGroupId, Retest(2), "corr-early", TestContext.Current.CancellationToken));
        await service.RecordAdoptionRuleAsync(
            group.ResultGroupId,
            new RecordAdoptionRuleRequest(2, ResultContract.RuleSetVersion,
                ResultAdoptionStrategies.RetestReplacesOriginal, new ResultVersionedReference("RULE-1", 1)),
            "corr-rule", TestContext.Current.CancellationToken);
        var retest = await service.AddObservationAsync(
            group.ResultGroupId, Retest(3), "corr-retest", TestContext.Current.CancellationToken);
        var favorable = await CaptureAsync(service.AdoptAsync(
            group.ResultGroupId,
            new AdoptResultRequest(4, ResultContract.RuleSetVersion, initial.ObservationId),
            "corr-cherry", TestContext.Current.CancellationToken));
        var compliant = await service.AdoptAsync(
            group.ResultGroupId,
            new AdoptResultRequest(4, ResultContract.RuleSetVersion, retest.ObservationId),
            "corr-adopt", TestContext.Current.CancellationToken);
        var status = await scope.ServiceProvider.GetRequiredService<IResultAdoptionPort>().EvaluateAsync(
            new ResultAdoptionStatusRequest("group-a", group.ResultGroupId, 5, ResultContract.RuleSetVersion)
            {
                CorrelationId = "corr-status"
            }, TestContext.Current.CancellationToken);

        Assert.Equal(ResultErrorCodes.AdoptionRuleRequired, withoutRule.Error!.ErrorCode);
        Assert.Equal(ResultErrorCodes.AdoptionStrategyViolation, favorable.Error!.ErrorCode);
        Assert.Equal(1, compliant.AdoptionVersion);
        Assert.Equal(ResultAdoptionDecisions.Allowed, status.Decision);
        Assert.Equal(retest.ObservationId, status.EffectiveTargetId);
    }

    [Fact]
    public async Task Result_facts_reject_update_and_delete()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, BatchStatusDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IResultGroupService>();
        var group = await service.CreateGroupAsync(GroupRequest(), "corr-immutable", TestContext.Current.CancellationToken);
        await service.AddObservationAsync(
            group.ResultGroupId, Observation(1, ResultObservationKinds.Initial), "corr-obs", TestContext.Current.CancellationToken);

        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update result.result_observation set value = '999'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from result.result_group"));

        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
    }

    [Fact]
    public async Task Batch_gate_blocked_fails_closed_without_group_facts()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, BatchStatusDecisions.Blocked);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<ResultDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<IResultGroupService>().CreateGroupAsync(
                GroupRequest(), "corr-blocked", TestContext.Current.CancellationToken));

        Assert.Equal(ResultErrorCodes.EligibilityBlocked, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "result.result_group"));
        Assert.Equal(1, await CountAsync(connectionString, "result.audit_attempt"));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_back_result_facts_and_appends_failure_attempt(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString, BatchStatusDecisions.Allowed);
            using var scope = provider.CreateScope();

            var exception = await Assert.ThrowsAsync<ResultDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<IResultGroupService>().CreateGroupAsync(
                    GroupRequest(), $"corr-{failedWriter}", TestContext.Current.CancellationToken));

            Assert.Equal(ResultErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "result.result_group"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.audit_intent"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "result.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
    }

    [Fact]
    public async Task Concurrent_observations_with_one_expected_version_append_only_once()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string groupId;
        await using (var setup = BuildProvider(connectionString, BatchStatusDecisions.Allowed))
        {
            using var setupScope = setup.CreateScope();
            groupId = (await setupScope.ServiceProvider.GetRequiredService<IResultGroupService>()
                .CreateGroupAsync(GroupRequest(), "corr-setup", TestContext.Current.CancellationToken)).ResultGroupId;
        }

        await using var firstProvider = BuildProvider(connectionString, BatchStatusDecisions.Allowed, "operator-a");
        await using var secondProvider = BuildProvider(connectionString, BatchStatusDecisions.Allowed, "operator-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IResultGroupService>().AddObservationAsync(
            groupId, Observation(1, ResultObservationKinds.Initial), "corr-a", TestContext.Current.CancellationToken);
        var second = secondScope.ServiceProvider.GetRequiredService<IResultGroupService>().AddObservationAsync(
            groupId, Observation(1, ResultObservationKinds.Initial), "corr-b", TestContext.Current.CancellationToken);

        var outcomes = await Task.WhenAll(CaptureAsync(first), CaptureAsync(second));

        Assert.Single(outcomes, outcome => outcome.Error is null);
        var failed = Assert.Single(outcomes, outcome => outcome.Error is not null).Error!;
        Assert.Equal(ResultErrorCodes.ExpectedVersionConflict, failed.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "result.result_observation"));
    }

    [Fact]
    public async Task Derivation_with_excluded_input_round_trips_and_stale_status_is_unknown()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, BatchStatusDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IResultGroupService>();
        var group = await service.CreateGroupAsync(GroupRequest(), "corr-v1", TestContext.Current.CancellationToken);
        var one = await service.AddObservationAsync(
            group.ResultGroupId, Observation(1, ResultObservationKinds.Initial), "corr-1", TestContext.Current.CancellationToken);
        var two = await service.AddObservationAsync(
            group.ResultGroupId, Observation(2, ResultObservationKinds.Initial), "corr-2", TestContext.Current.CancellationToken);
        var derivation = await service.AddDerivationAsync(
            group.ResultGroupId,
            new AddResultDerivationRequest(3, ResultContract.RuleSetVersion,
                new ResultVersionedReference("AGG-MEAN", 1), "12.0", "MG-KG",
                [
                    new ResultDerivationInput(one.ObservationId, true),
                    new ResultDerivationInput(two.ObservationId, false, "outlier excluded")
                ]),
            "corr-derive", TestContext.Current.CancellationToken);
        var loaded = await service.GetAsync(group.ResultGroupId, "corr-read", TestContext.Current.CancellationToken);
        var stale = await scope.ServiceProvider.GetRequiredService<IResultAdoptionPort>().EvaluateAsync(
            new ResultAdoptionStatusRequest("group-a", group.ResultGroupId, 1, ResultContract.RuleSetVersion)
            {
                CorrelationId = "corr-stale"
            }, TestContext.Current.CancellationToken);

        Assert.Equal(4, derivation.GroupVersion);
        var loadedDerivation = Assert.Single(loaded.Derivations);
        Assert.Equal(2, loadedDerivation.Inputs.Count);
        Assert.Contains(loadedDerivation.Inputs, input => !input.Included && input.Rationale == "outlier excluded");
        Assert.Equal(ResultAdoptionDecisions.Unknown, stale.Decision);
        Assert.Contains(ResultAdoptionReasons.GroupVersionMismatch, stale.ReasonCodes);
    }

    private static async Task<(object? Result, ResultDomainException? Error)> CaptureAsync<T>(Task<T> task)
    {
        try
        {
            return (await task, null);
        }
        catch (ResultDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString, string batchDecision, string actorId = "operator-a")
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
        new ResultModule(connectionString).AddApiServices(services);
        services.RemoveAll<IResultAuthorizationPort>();
        services.AddSingleton<IResultAuthorizationPort>(new FixedAuthorizationPort(true));
        services.RemoveAll<IBatchStatusPort>();
        services.AddSingleton<IBatchStatusPort>(new FixedBatchStatusPort(batchDecision));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateResultGroupRequest GroupRequest() => new(
        ResultContract.RuleSetVersion,
        new ResultObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        "00000000000000000000000000000050", 2, "MEMBER-1",
        new ResultVersionedReference("ITEM-PB", 1), new string('c', 64));

    private static AddResultObservationRequest Observation(long expectedVersion, string kind) => new(
        expectedVersion, ResultContract.RuleSetVersion, kind, "12.5", "MG-KG",
        new ResultEvidence(ResultEvidenceSources.Cds, new ResultVersionedReference("CDS-SEQ-1", 1), new string('a', 64), "PARSER-2.1"));

    private static AddResultObservationRequest Retest(long expectedVersion) =>
        Observation(expectedVersion, ResultObservationKinds.Retest) with
        {
            Value = "11.9",
            TriggerReason = "qc deviation",
            ApprovalRef = new ResultVersionedReference("APPROVAL-1", 1)
        };

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for result integration tests.");

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
        await ResultMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              result.audit_attempt,
              result.result_adoption,
              result.adoption_rule,
              result.derivation_input,
              result.result_derivation,
              result.result_observation,
              result.result_group,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_result_audit on platform.audit_intent;
                drop function if exists platform.fail_result_audit();
                create or replace function platform.fail_result_audit() returns trigger language plpgsql as $$
                begin
                  if new.action like '%RESULT%' or new.action like '%ADOPTION%' then
                    raise exception 'forced result audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_result_audit before insert on platform.audit_intent
                for each row execute function platform.fail_result_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_result_outbox on platform.outbox;
                drop function if exists platform.fail_result_outbox();
                create or replace function platform.fail_result_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type like 'Result%' then
                    raise exception 'forced result outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_result_outbox before insert on platform.outbox
                for each row execute function platform.fail_result_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_result_audit on platform.audit_intent;
                drop function if exists platform.fail_result_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_result_outbox on platform.outbox;
                drop function if exists platform.fail_result_outbox();
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

    private sealed class FixedAuthorizationPort(bool allowed) : IResultAuthorizationPort
    {
        public ValueTask<ResultAuthorizationDecision> AuthorizeAsync(
            ResultAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed ? ResultAuthorizationDecision.Permit : ResultAuthorizationDecision.Deny);
    }

    private sealed class FixedBatchStatusPort(string decision) : IBatchStatusPort
    {
        public ValueTask<BatchStatusResult> EvaluateAsync(
            BatchStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new BatchStatusResult(
                decision,
                decision == BatchStatusDecisions.Allowed ? [] : [BatchStatusReasons.BatchFrozen],
                request.BatchId, "ACTIVE", request.ExpectedBatchVersion, BatchContract.RuleSetVersion));
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
