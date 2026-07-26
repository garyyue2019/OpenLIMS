using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Billing;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Quantity;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Contracts.Result;
using OpenLIMS.Contracts.Scope;
using OpenLIMS.Modules.Allocation;
using OpenLIMS.Modules.Batch;
using OpenLIMS.Modules.Billing;
using OpenLIMS.Modules.Quantity;
using OpenLIMS.Modules.Result;
using OpenLIMS.Modules.Scope;
using Xunit;

namespace OpenLIMS.Platform.ChainE2ETests;

[CollectionDefinition("platform-chain-postgres", DisableParallelization = true)]
public sealed class PlatformChainPostgresCollection;

/// <summary>
/// First real cross-module composition proof: scope, quantity, allocation, batch,
/// result and billing are wired into one container with their REAL public ports.
/// Only the receiving eligibility port is a permit stub, because the receiving
/// module predates the port discipline and is outside this card's scope.
/// </summary>
[Collection("platform-chain-postgres")]
[Trait("Profile", "platform")]
public sealed class PlatformChainE2ETests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_chain_test";
    private static bool _databaseEnsured;

    private static readonly string[] StepCorrelations =
    [
        "chain-01-scope", "chain-02-qty-account", "chain-03-qty-receipt", "chain-04-allocation",
        "chain-05-batch", "chain-06-member", "chain-07-group", "chain-08-obs-initial",
        "chain-09-rule", "chain-10-obs-retest", "chain-11-adopt", "chain-12-billing"
    ];

    [Fact]
    public async Task Full_chain_composes_with_real_ports_and_per_step_platform_evidence()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();

        var chain = await RunChainAsync(scope.ServiceProvider);

        Assert.Equal(BillingStages.BillableCandidate, chain.Evidence.Stage);
        Assert.Equal(chain.AdoptedObservationId, chain.Evidence.AdoptionTargetId);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from scope.scope_matrix_version"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from billing.billing_evidence"));

        var firstAuditIdPerStep = new List<long>();
        foreach (var correlationId in StepCorrelations)
        {
            var minAuditId = await CountAsync(
                connectionString,
                "select coalesce(min(audit_id), -1) from platform.audit_intent where correlation_id = @p",
                correlationId);
            Assert.True(minAuditId > 0, $"no audit intent recorded for {correlationId}");
            firstAuditIdPerStep.Add(minAuditId);
        }

        Assert.Equal(firstAuditIdPerStep.OrderBy(id => id), firstAuditIdPerStep);
        Assert.True(
            await CountAsync(connectionString, "select count(*) from platform.outbox") >= StepCorrelations.Length,
            "every chain command must leave an outbox event");
    }

    [Fact]
    public async Task Stale_allocation_version_blocks_batch_member_and_fails_closed()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var matrix = await CreateScopeMatrixAsync(services);
        var (account, receipt) = await CreateFundedQuantityAccountAsync(services);
        var allocation = await CreateAllocationAsync(services, matrix, account, receipt);
        var batchService = services.GetRequiredService<IBatchService>();
        var batch = await batchService.CreateAsync(BatchRequest(), "chain-05-batch", TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => batchService.AddMemberAsync(
            batch.BatchId,
            MemberRequest(batch.Version, allocation.AllocationId, allocation.SubjectAllocationVersion + 999),
            "chain-stale-member",
            TestContext.Current.CancellationToken));

        Assert.True(
            exception.Message.Contains(BatchErrorCodes.EligibilityBlocked, StringComparison.Ordinal) ||
            exception.Message.Contains(BatchErrorCodes.ApplicabilityUnknown, StringComparison.Ordinal),
            $"stale allocation must fail closed, got: {exception.Message}");
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from batch.batch_member"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from batch.audit_attempt"));
    }

    [Fact]
    public async Task Platform_audit_is_append_only_and_outbox_allows_only_dispatch_marking()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await ExecuteAsync(connectionString, """
            insert into platform.audit_intent
                (actor_id, organization_group_id, object_id, action, rule_version, correlation_id, occurred_at)
            values ('probe-actor', 'group-a', 'probe-object', 'PROBE', 'platform-0002', 'probe-corr', now());
            insert into platform.outbox (id, message_type, occurred_at)
            values ('probe-event', 'Platform.Probe', now());
            """);

        await AssertRejectedAsync(connectionString,
            "update platform.audit_intent set action = 'tampered' where object_id = 'probe-object'");
        await AssertRejectedAsync(connectionString,
            "delete from platform.audit_intent where object_id = 'probe-object'");
        await AssertRejectedAsync(connectionString,
            "delete from platform.outbox where id = 'probe-event'");
        await AssertRejectedAsync(connectionString,
            "update platform.outbox set message_type = 'tampered' where id = 'probe-event'");
        await AssertRejectedAsync(connectionString,
            "update platform.outbox set occurred_at = now() where id = 'probe-event'");

        await ExecuteAsync(connectionString,
            "update platform.outbox set dispatched_at = now() where id = 'probe-event'");
        await AssertRejectedAsync(connectionString,
            "update platform.outbox set dispatched_at = now() where id = 'probe-event'");

        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from platform.audit_intent where action = 'PROBE'"));
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from platform.outbox where message_type = 'Platform.Probe' and dispatched_at is not null"));
    }

    [Fact]
    public async Task Platform_migration_is_idempotent_and_readiness_requires_platform_0002()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await PlatformMigrationRunner.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await PlatformMigrationRunner.ApplyAsync(connectionString, TestContext.Current.CancellationToken);

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from platform.migration_history where migration_id = 'platform-0001'"));
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from platform.migration_history where migration_id = 'platform-0002'"));
        Assert.True(await PlatformMigrationRunner.IsCurrentAsync(dataSource, 10, TestContext.Current.CancellationToken));

        await ExecuteAsync(connectionString,
            "delete from platform.migration_history where migration_id = 'platform-0002'");
        Assert.False(await PlatformMigrationRunner.IsCurrentAsync(dataSource, 10, TestContext.Current.CancellationToken));

        await PlatformMigrationRunner.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        Assert.True(await PlatformMigrationRunner.IsCurrentAsync(dataSource, 10, TestContext.Current.CancellationToken));
    }

    private sealed record ChainOutcome(BillingEvidenceResult Evidence, string AdoptedObservationId);

    private async Task<ChainOutcome> RunChainAsync(IServiceProvider services)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var matrix = await CreateScopeMatrixAsync(services);
        var (account, receipt) = await CreateFundedQuantityAccountAsync(services);
        var allocation = await CreateAllocationAsync(services, matrix, account, receipt);

        var batchService = services.GetRequiredService<IBatchService>();
        var batch = await batchService.CreateAsync(BatchRequest(), "chain-05-batch", cancellationToken);
        var member = await batchService.AddMemberAsync(
            batch.BatchId,
            MemberRequest(batch.Version, allocation.AllocationId, allocation.SubjectAllocationVersion),
            "chain-06-member",
            cancellationToken);

        var resultService = services.GetRequiredService<IResultGroupService>();
        var group = await resultService.CreateGroupAsync(
            new CreateResultGroupRequest(
                ResultContract.RuleSetVersion,
                new ResultObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
                batch.BatchId,
                member.BatchVersion,
                member.MemberId,
                new ResultVersionedReference("ITEM-PB", 1),
                matrix.Lines[0].ScopeLineId),
            "chain-07-group",
            cancellationToken);
        await resultService.AddObservationAsync(
            group.ResultGroupId, Observation(group.Version, ResultObservationKinds.Initial),
            "chain-08-obs-initial", cancellationToken);
        await resultService.RecordAdoptionRuleAsync(
            group.ResultGroupId,
            new RecordAdoptionRuleRequest(
                group.Version + 1,
                ResultContract.RuleSetVersion,
                ResultAdoptionStrategies.RetestReplacesOriginal,
                new ResultVersionedReference("RULE-1", 1)),
            "chain-09-rule",
            cancellationToken);
        var retest = await resultService.AddObservationAsync(
            group.ResultGroupId,
            Observation(group.Version + 2, ResultObservationKinds.Retest) with
            {
                Value = "11.9",
                TriggerReason = "qc deviation",
                ApprovalRef = new ResultVersionedReference("APPROVAL-1", 1)
            },
            "chain-10-obs-retest",
            cancellationToken);
        var adoption = await resultService.AdoptAsync(
            group.ResultGroupId,
            new AdoptResultRequest(retest.GroupVersion, ResultContract.RuleSetVersion, retest.ObservationId),
            "chain-11-adopt",
            cancellationToken);

        var evidence = await services.GetRequiredService<IBillingEvidenceService>().CreateAsync(
            new CreateBillingEvidenceRequest(
                BillingContract.RuleSetVersion,
                new BillingObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
                group.ResultGroupId,
                adoption.GroupVersion,
                new BillingVersionedReference("CONTRACT-7", 2),
                "ITEM-PB-TEST",
                "PRICE-2026Q3",
                120.50m,
                new BillingVersionedReference("CNY", 1)),
            "chain-12-billing",
            cancellationToken);

        return new ChainOutcome(evidence, retest.ObservationId);
    }

    private static async Task<ScopeMatrixVersionResult> CreateScopeMatrixAsync(IServiceProvider services) =>
        await services.GetRequiredService<IScopeMatrixService>().CreateAsync(
            new SubmitScopeMatrixVersionRequest(
                0,
                ScopeContract.RuleSetVersion,
                new ScopeObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
                [
                    new ScopeLineInput(
                        ScopeSubjectTypes.FeatureNode,
                        new ScopeVersionedReference("FEATURE-1", 1),
                        new ScopeVersionedReference("MARKET-CN", 1),
                        new ScopeVersionedReference("REQ-1", 1),
                        new ScopeVersionedReference("ITEM-PB", 1),
                        new ScopeVersionedReference("METHOD-1", 1),
                        "OPTION-A",
                        new ScopeVersionedReference("SAMPLE-REQ-1", 1),
                        ScopeEvaluationModes.MeasuredOnly,
                        new ScopeVersionedReference("WC-A", 1),
                        "REPORT-1",
                        null,
                        null,
                        null,
                        null)
                ]),
            "chain-01-scope",
            TestContext.Current.CancellationToken);

    private static async Task<(QuantityAccountResult Account, QuantityEntryResult Receipt)>
        CreateFundedQuantityAccountAsync(IServiceProvider services)
    {
        var quantityService = services.GetRequiredService<IQuantityAccountService>();
        var account = await quantityService.CreateAsync(
            new CreateQuantityAccountRequest(
                QuantityContract.RuleSetVersion,
                new QuantityObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
                new QuantitySubjectReference(QuantitySubjectTypes.ReceivedItem, "ITEM-1", 1),
                true,
                QuantityDimensions.Mass,
                "GRAM",
                2,
                0.20m),
            "chain-02-qty-account",
            TestContext.Current.CancellationToken);
        var receipt = await quantityService.PostEntryAsync(
            account.QuantityAccountId,
            new PostQuantityEntryRequest(account.Version, QuantityContract.RuleSetVersion, QuantityEntryTypes.Receipt, 100.00m),
            "chain-03-qty-receipt",
            TestContext.Current.CancellationToken);
        return (account, receipt);
    }

    private static async Task<TestObjectAllocationResult> CreateAllocationAsync(
        IServiceProvider services,
        ScopeMatrixVersionResult matrix,
        QuantityAccountResult account,
        QuantityEntryResult receipt) =>
        await services.GetRequiredService<ITestObjectAllocationService>().CreateAsync(
            new CreateTestObjectAllocationRequest(
                0,
                AllocationContract.RuleSetVersion,
                new AllocationObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
                new AllocationSubjectReference(AllocationSubjectTypes.ReceivedItem, "ITEM-1", 1),
                new AllocationVersionedReference("SIA-1", 1),
                "ITEM-1",
                3,
                matrix.ScopeMatrixId,
                matrix.Version,
                matrix.Lines[0].ScopeLineId,
                new AllocationVersionedReference("PLAN-STEP-1", 1),
                "Tensile strength execution",
                0,
                false,
                account.QuantityAccountId,
                receipt.AccountVersion,
                80.00m,
                account.Dimension,
                account.Unit,
                new AllocationVersionedReference("STORAGE-COND-1", 1),
                Now.AddDays(7)),
            "chain-04-allocation",
            TestContext.Current.CancellationToken);

    private static CreateBatchRequest BatchRequest() => new(
        BatchContract.RuleSetVersion, new BatchObjectContext("LEGAL-A", "LAB-A"), BatchTypes.Analytical);

    private static AddBatchMemberRequest MemberRequest(
        long expectedBatchVersion, string allocationId, long expectedAllocationVersion) => new(
        expectedBatchVersion, BatchContract.RuleSetVersion, BatchMemberTypes.Specimen,
        "CUSTOMER-A", "ORDER-A", "TOYS",
        AllocationId: allocationId,
        ExpectedSubjectAllocationVersion: expectedAllocationVersion);

    private static AddResultObservationRequest Observation(long expectedVersion, string kind) => new(
        expectedVersion, ResultContract.RuleSetVersion, kind, "12.5", "MG-KG",
        new ResultEvidence(
            ResultEvidenceSources.Cds, new ResultVersionedReference("CDS-SEQ-1", 1), new string('a', 64), "PARSER-2.1"));

    private static ServiceProvider BuildProvider(string connectionString)
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
        services.AddSingleton<ICurrentActorContext>(new FixedActorContext(new ActorContext("operator-a", "group-a")));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();

        new ScopeModule(connectionString).AddApiServices(services);
        new QuantityModule(connectionString).AddApiServices(services);
        new AllocationModule(connectionString).AddApiServices(services);
        new BatchModule(connectionString).AddApiServices(services);
        new ResultModule(connectionString).AddApiServices(services);
        new BillingModule(connectionString).AddApiServices(services);

        services.RemoveAll<IScopeAuthorizationPort>();
        services.AddSingleton<IScopeAuthorizationPort>(new PermitScopeAuthorization());
        services.RemoveAll<IQuantityAuthorizationPort>();
        services.AddSingleton<IQuantityAuthorizationPort>(new PermitQuantityAuthorization());
        services.RemoveAll<IAllocationAuthorizationPort>();
        services.AddSingleton<IAllocationAuthorizationPort>(new PermitAllocationAuthorization());
        services.RemoveAll<IBatchAuthorizationPort>();
        services.AddSingleton<IBatchAuthorizationPort>(new PermitBatchAuthorization());
        services.RemoveAll<IResultAuthorizationPort>();
        services.AddSingleton<IResultAuthorizationPort>(new PermitResultAuthorization());
        services.RemoveAll<IBillingAuthorizationPort>();
        services.AddSingleton<IBillingAuthorizationPort>(new PermitBillingAuthorization());
        services.RemoveAll<IReceivingEligibilityPortV2>();
        services.AddSingleton<IReceivingEligibilityPortV2>(new PermitReceivingEligibilityPort());

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for platform chain E2E tests.");

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
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlatformMigrationRunner.ApplyAsync(connectionString, cancellationToken);
        await new ScopeModule(connectionString).ApplyMigrationAsync(cancellationToken);
        await new QuantityModule(connectionString).ApplyMigrationAsync(cancellationToken);
        await new AllocationModule(connectionString).ApplyMigrationAsync(cancellationToken);
        await new BatchModule(connectionString).ApplyMigrationAsync(cancellationToken);
        await new ResultModule(connectionString).ApplyMigrationAsync(cancellationToken);
        await new BillingModule(connectionString).ApplyMigrationAsync(cancellationToken);
        await ExecuteAsync(connectionString, """
            do $$
            declare r record;
            begin
              for r in
                select schemaname, tablename from pg_tables
                where schemaname in ('platform', 'scope', 'quantity', 'allocation', 'batch', 'result', 'billing')
                  and not (schemaname = 'platform' and tablename = 'migration_history')
              loop
                execute format('truncate table %I.%I cascade', r.schemaname, r.tablename);
              end loop;
            end;
            $$;
            """);
    }

    private static async Task AssertRejectedAsync(string connectionString, string sql)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, sql));
        Assert.Equal("55000", exception.SqlState);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(string connectionString, string sql, string? parameter = null)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql.Replace("@p", "$1"));
        if (parameter is not null)
            command.Parameters.AddWithValue(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed class PermitScopeAuthorization : IScopeAuthorizationPort
    {
        public ValueTask<ScopeAuthorizationDecision> AuthorizeAsync(
            ScopeAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ScopeAuthorizationDecision.Permit);
    }

    private sealed class PermitQuantityAuthorization : IQuantityAuthorizationPort
    {
        public ValueTask<QuantityAuthorizationDecision> AuthorizeAsync(
            QuantityAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(QuantityAuthorizationDecision.Permit);
    }

    private sealed class PermitAllocationAuthorization : IAllocationAuthorizationPort
    {
        public ValueTask<AllocationAuthorizationDecision> AuthorizeAsync(
            AllocationAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AllocationAuthorizationDecision.Permit);
    }

    private sealed class PermitBatchAuthorization : IBatchAuthorizationPort
    {
        public ValueTask<BatchAuthorizationDecision> AuthorizeAsync(
            BatchAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BatchAuthorizationDecision.Permit);
    }

    private sealed class PermitResultAuthorization : IResultAuthorizationPort
    {
        public ValueTask<ResultAuthorizationDecision> AuthorizeAsync(
            ResultAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResultAuthorizationDecision.Permit);
    }

    private sealed class PermitBillingAuthorization : IBillingAuthorizationPort
    {
        public ValueTask<BillingAuthorizationDecision> AuthorizeAsync(
            BillingAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BillingAuthorizationDecision.Permit);
    }

    private sealed class PermitReceivingEligibilityPort : IReceivingEligibilityPortV2
    {
        public ValueTask<ReceivingEligibilityV2Result> EvaluateAsync(
            ReceivingEligibilityV2Request request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReceivingEligibilityV2Result(
                "ALLOWED",
                "RELEASED",
                "MATCHED",
                "identity-1",
                "release-1",
                [],
                request.ExpectedItemVersion,
                1,
                request.RuleSetVersion,
                [request.RequestedAction],
                [],
                Now.AddDays(30)));
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
