using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Quantity;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Contracts.Scope;
using OpenLIMS.Modules.Allocation;
using Xunit;

namespace OpenLIMS.Allocation.IntegrationTests;

[CollectionDefinition("allocation-postgres", DisableParallelization = true)]
public sealed class AllocationPostgresCollection;

[Collection("allocation-postgres")]
[Trait("Profile", "allocation")]
public sealed class AllocationPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_allocation_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Creation_with_three_allowed_gates_atomically_persists_fact_audit_and_outbox()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, Gates.AllAllowed());
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ITestObjectAllocationService>().CreateAsync(
            Request(0),
            "corr-create",
            TestContext.Current.CancellationToken);

        Assert.Equal(AllocationStates.Active, result.State);
        Assert.Equal(1, result.SubjectAllocationVersion);
        Assert.Equal(3, result.ReceivingGate.PinnedVersion);
        Assert.Equal(2, result.ScopeGate.PinnedVersion);
        Assert.Equal(2, result.QuantityGate.PinnedVersion);
        Assert.Equal(1, await CountAsync(connectionString, "allocation.test_object_allocation"));
        Assert.Equal(1, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(1, await CountAsync(connectionString, "platform.outbox"));
        Assert.Equal(
            "true",
            await ScalarStringAsync(connectionString, "select exists (select 1 from allocation.test_object_allocation a join platform.outbox o on o.id = a.event_id)::text"));
    }

    [Theory]
    [InlineData("receiving-blocked", AllocationErrorCodes.EligibilityBlocked)]
    [InlineData("scope-unknown", AllocationErrorCodes.ApplicabilityUnknown)]
    [InlineData("quantity-blocked", AllocationErrorCodes.EligibilityBlocked)]
    public async Task Any_gate_not_allowed_fails_closed_without_facts(string failure, string expectedError)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var gates = failure switch
        {
            "receiving-blocked" => Gates.AllAllowed() with { ReceivingDecision = "BLOCKED" },
            "scope-unknown" => Gates.AllAllowed() with { ScopeDecision = "UNKNOWN" },
            _ => Gates.AllAllowed() with { QuantityDecision = "BLOCKED" }
        };
        await using var provider = BuildProvider(connectionString, gates);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<AllocationDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<ITestObjectAllocationService>().CreateAsync(
                Request(0),
                $"corr-{failure}",
                TestContext.Current.CancellationToken));

        Assert.Equal(expectedError, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "allocation.test_object_allocation"));
        Assert.Equal(0, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(0, await CountAsync(connectionString, "platform.outbox"));
        Assert.Equal(1, await CountAsync(connectionString, "allocation.audit_attempt"));
    }

    [Fact]
    public async Task Active_destructive_allocation_blocks_new_allocations_until_released()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, Gates.AllAllowed());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestObjectAllocationService>();

        var nonDestructiveA = await service.CreateAsync(
            Request(0), "corr-nd-a", TestContext.Current.CancellationToken);
        var nonDestructiveB = await service.CreateAsync(
            Request(1), "corr-nd-b", TestContext.Current.CancellationToken);
        var destructive = await service.CreateAsync(
            Request(2) with { Destructive = true, SequenceOrder = 3 },
            "corr-destructive",
            TestContext.Current.CancellationToken);
        var blocked = await Assert.ThrowsAsync<AllocationDomainException>(() =>
            service.CreateAsync(Request(3), "corr-blocked", TestContext.Current.CancellationToken));
        await service.ReleaseAsync(
            destructive.AllocationId,
            new ReleaseTestObjectAllocationRequest("Destructive step cancelled"),
            "corr-release",
            TestContext.Current.CancellationToken);
        var afterRelease = await service.CreateAsync(
            Request(4), "corr-after-release", TestContext.Current.CancellationToken);

        Assert.Equal(1, nonDestructiveA.SubjectAllocationVersion);
        Assert.Equal(2, nonDestructiveB.SubjectAllocationVersion);
        Assert.Equal(3, destructive.SubjectAllocationVersion);
        Assert.Equal(AllocationErrorCodes.DestructiveConflict, blocked.ErrorCode);
        Assert.Equal(5, afterRelease.SubjectAllocationVersion);
        Assert.Equal(4, await CountAsync(connectionString, "allocation.test_object_allocation"));
        Assert.Equal(1, await CountAsync(connectionString, "allocation.allocation_release"));
    }

    [Fact]
    public async Task Concurrent_allocations_with_one_expected_version_advance_subject_once()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var firstProvider = BuildProvider(connectionString, Gates.AllAllowed(), "planner-a");
        await using var secondProvider = BuildProvider(connectionString, Gates.AllAllowed(), "planner-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<ITestObjectAllocationService>().CreateAsync(
            Request(0), "corr-conc-a", TestContext.Current.CancellationToken);
        var second = secondScope.ServiceProvider.GetRequiredService<ITestObjectAllocationService>().CreateAsync(
            Request(0), "corr-conc-b", TestContext.Current.CancellationToken);

        var outcomes = await Task.WhenAll(CaptureAsync(first), CaptureAsync(second));

        Assert.Equal(1, Assert.Single(outcomes, outcome => outcome.Result is not null).Result!.SubjectAllocationVersion);
        Assert.Equal(
            AllocationErrorCodes.ExpectedVersionConflict,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "allocation.test_object_allocation"));
        Assert.Equal(1, await CountAsync(connectionString, "allocation.audit_attempt"));
    }

    [Fact]
    public async Task Posted_allocation_and_release_reject_update_and_delete()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, Gates.AllAllowed());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestObjectAllocationService>();
        var allocation = await service.CreateAsync(Request(0), "corr-immutable", TestContext.Current.CancellationToken);
        await service.ReleaseAsync(
            allocation.AllocationId,
            new ReleaseTestObjectAllocationRequest("Completed physically"),
            "corr-release",
            TestContext.Current.CancellationToken);

        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "update allocation.test_object_allocation set purpose = 'REWRITTEN'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "delete from allocation.allocation_release"));
        var doubleRelease = await Assert.ThrowsAsync<AllocationDomainException>(() =>
            service.ReleaseAsync(
                allocation.AllocationId,
                new ReleaseTestObjectAllocationRequest("Second release attempt"),
                "corr-double-release",
                TestContext.Current.CancellationToken));

        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
        Assert.Equal(AllocationErrorCodes.ValidationFailed, doubleRelease.ErrorCode);
    }

    [Fact]
    public async Task Status_authorization_denial_is_hash_audited_after_transaction_rollback()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string allocationId;
        await using (var creatorProvider = BuildProvider(connectionString, Gates.AllAllowed()))
        {
            using var creatorScope = creatorProvider.CreateScope();
            allocationId = (await creatorScope.ServiceProvider.GetRequiredService<ITestObjectAllocationService>()
                .CreateAsync(Request(0), "corr-create", TestContext.Current.CancellationToken)).AllocationId;
        }

        await using var deniedProvider = BuildProvider(
            connectionString, Gates.AllAllowed(), "denied-actor", authorizationAllowed: false);
        using var deniedScope = deniedProvider.CreateScope();
        var request = new AllocationStatusRequest("group-a", allocationId, 1, AllocationContract.RuleSetVersion)
        {
            CorrelationId = "corr-denied"
        };

        var exception = await Assert.ThrowsAsync<AllocationDomainException>(async () =>
            await deniedScope.ServiceProvider.GetRequiredService<IAllocationStatusPort>()
                .EvaluateAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(AllocationErrorCodes.NotAuthorized, exception.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "allocation.audit_attempt"));
        var targetHash = await ScalarStringAsync(connectionString, "select target_hash from allocation.audit_attempt limit 1");
        Assert.Equal(64, targetHash.Length);
        Assert.DoesNotContain(allocationId, targetHash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_back_allocation_facts_and_appends_failure_attempt(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString, Gates.AllAllowed());
            using var scope = provider.CreateScope();

            var exception = await Assert.ThrowsAsync<AllocationDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<ITestObjectAllocationService>().CreateAsync(
                    Request(0),
                    $"corr-{failedWriter}-failure",
                    TestContext.Current.CancellationToken));

            Assert.Equal(AllocationErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "allocation.test_object_allocation"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.audit_intent"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "allocation.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
    }

    [Fact]
    public async Task Current_active_allocation_is_allowed_while_stale_released_and_unknown_rule_fail_closed()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, Gates.AllAllowed());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestObjectAllocationService>();
        var port = scope.ServiceProvider.GetRequiredService<IAllocationStatusPort>();
        var allocation = await service.CreateAsync(Request(0), "corr-v1", TestContext.Current.CancellationToken);

        var allowed = await port.EvaluateAsync(
            Status(allocation.AllocationId, 1) with { CorrelationId = "corr-allowed" },
            TestContext.Current.CancellationToken);
        var stale = await port.EvaluateAsync(
            Status(allocation.AllocationId, 9) with { CorrelationId = "corr-stale" },
            TestContext.Current.CancellationToken);
        var unknownRule = await port.EvaluateAsync(
            Status(allocation.AllocationId, 1, "TASK-ALLOCATION@latest") with { CorrelationId = "corr-unknown" },
            TestContext.Current.CancellationToken);
        await service.ReleaseAsync(
            allocation.AllocationId,
            new ReleaseTestObjectAllocationRequest("Plan revised"),
            "corr-release",
            TestContext.Current.CancellationToken);
        var released = await port.EvaluateAsync(
            Status(allocation.AllocationId, 2) with { CorrelationId = "corr-released" },
            TestContext.Current.CancellationToken);

        Assert.Equal(AllocationStatusDecisions.Allowed, allowed.Decision);
        Assert.Equal(AllocationStatusDecisions.Unknown, stale.Decision);
        Assert.Contains(AllocationStatusReasons.SubjectVersionMismatch, stale.ReasonCodes);
        Assert.Equal(AllocationStatusDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(AllocationStatusReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
        Assert.Equal(AllocationStatusDecisions.Blocked, released.Decision);
        Assert.Contains(AllocationStatusReasons.AllocationReleased, released.ReasonCodes);
    }

    private static async Task<(TestObjectAllocationResult? Result, AllocationDomainException? Error)> CaptureAsync(
        Task<TestObjectAllocationResult> task)
    {
        try
        {
            return (await task, null);
        }
        catch (AllocationDomainException exception)
        {
            return (null, exception);
        }
    }

    private sealed record Gates(
        string ReceivingDecision,
        string ScopeDecision,
        string QuantityDecision)
    {
        public static Gates AllAllowed() => new("ALLOWED", "ALLOWED", "ALLOWED");
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        Gates gates,
        string actorId = "planner-a",
        bool authorizationAllowed = true)
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
        services.AddSingleton<ICurrentActorContext>(
            new FixedActorContext(new ActorContext(actorId, "group-a")));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        new AllocationModule(connectionString).AddApiServices(services);
        services.RemoveAll<IAllocationAuthorizationPort>();
        services.AddSingleton<IAllocationAuthorizationPort>(new FixedAuthorizationPort(authorizationAllowed));
        services.AddSingleton<IReceivingEligibilityPortV2>(new FixedReceivingPort(gates.ReceivingDecision));
        services.AddSingleton<IScopeProductionEligibilityPort>(new FixedScopePort(gates.ScopeDecision));
        services.AddSingleton<IQuantityAvailabilityPort>(new FixedQuantityPort(gates.QuantityDecision));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateTestObjectAllocationRequest Request(long expectedVersion) => new(
        expectedVersion,
        AllocationContract.RuleSetVersion,
        new AllocationObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        new AllocationSubjectReference(AllocationSubjectTypes.ReceivedItem, "ITEM-1", 1),
        new AllocationVersionedReference("SIA-1", 1),
        "ITEM-1",
        3,
        "00000000000000000000000000000030",
        2,
        new string('a', 64),
        new AllocationVersionedReference("PLAN-STEP-1", 1),
        "Tensile strength execution",
        (int)expectedVersion,
        false,
        "00000000000000000000000000000031",
        2,
        80.00m,
        "MASS",
        "GRAM",
        new AllocationVersionedReference("STORAGE-COND-1", 1),
        Now.AddDays(7));

    private static AllocationStatusRequest Status(
        string allocationId,
        long expectedVersion,
        string ruleSetVersion = AllocationContract.RuleSetVersion) => new(
        "group-a",
        allocationId,
        expectedVersion,
        ruleSetVersion);

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for allocation integration tests.");

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
        await AllocationMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              allocation.audit_attempt,
              allocation.allocation_release,
              allocation.test_object_allocation,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_allocation_audit on platform.audit_intent;
                drop function if exists platform.fail_allocation_audit();
                create or replace function platform.fail_allocation_audit() returns trigger language plpgsql as $$
                begin
                  if new.action in ('ASSIGN_TEST_OBJECT_ALLOCATION', 'RELEASE_TEST_OBJECT_ALLOCATION') then
                    raise exception 'forced allocation audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_allocation_audit before insert on platform.audit_intent
                for each row execute function platform.fail_allocation_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_allocation_outbox on platform.outbox;
                drop function if exists platform.fail_allocation_outbox();
                create or replace function platform.fail_allocation_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type in ('TestObjectAllocationAssigned.v1', 'TestObjectAllocationReleased.v1') then
                    raise exception 'forced allocation outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_allocation_outbox before insert on platform.outbox
                for each row execute function platform.fail_allocation_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_allocation_audit on platform.audit_intent;
                drop function if exists platform.fail_allocation_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_allocation_outbox on platform.outbox;
                drop function if exists platform.fail_allocation_outbox();
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

    private static async Task<string> ScalarStringAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return Convert.ToString(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private sealed class FixedAuthorizationPort(bool allowed) : IAllocationAuthorizationPort
    {
        public ValueTask<AllocationAuthorizationDecision> AuthorizeAsync(
            AllocationAuthorizationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            allowed ? AllocationAuthorizationDecision.Permit : AllocationAuthorizationDecision.Deny);
    }

    private sealed class FixedReceivingPort(string decision) : IReceivingEligibilityPortV2
    {
        public ValueTask<ReceivingEligibilityV2Result> EvaluateAsync(
            ReceivingEligibilityV2Request request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new ReceivingEligibilityV2Result(
                decision,
                "RELEASED",
                "MATCHED",
                "identity-1",
                "release-1",
                decision == "ALLOWED" ? [] : ["RELEASE_DECISION_REQUIRED"],
                request.ExpectedItemVersion,
                1,
                request.RuleSetVersion,
                decision == "ALLOWED" ? [request.RequestedAction] : [],
                [],
                Now.AddDays(30)));
    }

    private sealed class FixedScopePort(string decision) : IScopeProductionEligibilityPort
    {
        public ValueTask<ScopeProductionEligibilityResult> EvaluateAsync(
            ScopeProductionEligibilityRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new ScopeProductionEligibilityResult(
                decision,
                decision == "ALLOWED" ? [] : ["MATRIX_VERSION_MISMATCH"],
                request.ScopeMatrixId,
                request.ExpectedMatrixVersion,
                request.RuleSetVersion));
    }

    private sealed class FixedQuantityPort(string decision) : IQuantityAvailabilityPort
    {
        public ValueTask<QuantityAvailabilityResult> EvaluateAsync(
            QuantityAvailabilityRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new QuantityAvailabilityResult(
                decision,
                decision == "ALLOWED" ? [] : ["INSUFFICIENT_AVAILABLE"],
                request.QuantityAccountId,
                request.ExpectedAccountVersion,
                decision == "ALLOWED" ? 100.00m : 10.00m,
                request.RuleSetVersion));
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
