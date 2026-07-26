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

/// <summary>
/// DEV-018 (ATC-PLT-001): request context and object-level authorization proven
/// as platform-level composition evidence — deployment-bound organization
/// context, capability-deny fail-closed, cross-organization existence hiding
/// (AC-SEC-001) and caller correlation pinned through the platform audit trail.
/// </summary>
[Collection("platform-chain-postgres")]
[Trait("Profile", "platform")]
public sealed class RequestContextAuthorizationE2ETests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_chain_test";

    [Fact]
    public async Task Caller_correlation_actor_and_organization_are_pinned_into_platform_audit()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();

        await CreateScopeMatrixAsync(scope.ServiceProvider, "req-ctx-scope");
        await CreateQuantityAccountAsync(scope.ServiceProvider, "req-ctx-qty");

        foreach (var correlationId in new[] { "req-ctx-scope", "req-ctx-qty" })
        {
            Assert.True(await CountAsync(connectionString,
                "select count(*) from platform.audit_intent where correlation_id = @p", correlationId) >= 1);
            Assert.Equal(0, await CountAsync(connectionString,
                """
                select count(*) from platform.audit_intent
                where correlation_id = @p
                  and (actor_id <> 'operator-a' or organization_group_id <> 'group-a')
                """, correlationId));
        }
    }

    [Fact]
    public async Task Capability_deny_fails_closed_with_attempt_audit_only()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, permitScopeCapability: false);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateScopeMatrixAsync(scope.ServiceProvider, "req-ctx-denied"));

        Assert.Contains(ScopeErrorCodes.NotAuthorized, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from scope.scope_matrix_version"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from scope.audit_attempt"));
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from scope.audit_attempt where correlation_id = @p", "req-ctx-denied"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.outbox"));
    }

    [Fact]
    public async Task Cross_organization_access_is_indistinguishable_from_missing_object()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string matrixId;
        await using (var ownerProvider = BuildProvider(connectionString))
        {
            using var ownerScope = ownerProvider.CreateScope();
            matrixId = (await CreateScopeMatrixAsync(ownerScope.ServiceProvider, "req-ctx-owner")).ScopeMatrixId;
        }

        await using var foreignProvider = BuildProvider(
            connectionString, deploymentOrganization: "group-b", actorOrganization: "group-b");
        using var foreignScope = foreignProvider.CreateScope();
        var service = foreignScope.ServiceProvider.GetRequiredService<IScopeMatrixService>();

        var crossOrganization = await Assert.ThrowsAnyAsync<Exception>(() =>
            service.GetVersionAsync(matrixId, 1, "req-ctx-cross", TestContext.Current.CancellationToken));
        var missing = await Assert.ThrowsAnyAsync<Exception>(() =>
            service.GetVersionAsync(Guid.NewGuid().ToString("N"), 1, "req-ctx-missing", TestContext.Current.CancellationToken));

        Assert.Contains(ScopeErrorCodes.ObjectNotAccessible, crossOrganization.Message, StringComparison.Ordinal);
        Assert.Contains(ScopeErrorCodes.ObjectNotAccessible, missing.Message, StringComparison.Ordinal);
        Assert.Equal(crossOrganization.GetType(), missing.GetType());
        Assert.Equal(crossOrganization.Message, missing.Message);
    }

    [Fact]
    public async Task Actor_organization_mismatch_fails_closed_without_facts()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(
            connectionString, deploymentOrganization: "group-a", actorOrganization: "group-b");
        using var scope = provider.CreateScope();

        var scopeException = await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateScopeMatrixAsync(scope.ServiceProvider, "req-ctx-mismatch-scope"));
        var quantityException = await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateQuantityAccountAsync(scope.ServiceProvider, "req-ctx-mismatch-qty"));

        Assert.Contains(ScopeErrorCodes.NotAuthorized, scopeException.Message, StringComparison.Ordinal);
        Assert.Contains(QuantityErrorCodes.NotAuthorized, quantityException.Message, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from scope.scope_matrix_version"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from quantity.quantity_account"));
    }

    private static async Task<ScopeMatrixVersionResult> CreateScopeMatrixAsync(
        IServiceProvider services, string correlationId) =>
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
            correlationId,
            TestContext.Current.CancellationToken);

    private static async Task<QuantityAccountResult> CreateQuantityAccountAsync(
        IServiceProvider services, string correlationId) =>
        await services.GetRequiredService<IQuantityAccountService>().CreateAsync(
            new CreateQuantityAccountRequest(
                QuantityContract.RuleSetVersion,
                new QuantityObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
                new QuantitySubjectReference(QuantitySubjectTypes.ReceivedItem, "ITEM-1", 1),
                true,
                QuantityDimensions.Mass,
                "GRAM",
                2,
                0.20m),
            correlationId,
            TestContext.Current.CancellationToken);

    private static ServiceProvider BuildProvider(
        string connectionString,
        bool permitScopeCapability = true,
        string deploymentOrganization = "group-a",
        string actorOrganization = "group-a")
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
            new DeploymentOrganizationContext(new OrganizationScope(deploymentOrganization)));
        services.AddSingleton<ICurrentActorContext>(
            new FixedActorContext(new ActorContext("operator-a", actorOrganization)));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();

        new ScopeModule(connectionString).AddApiServices(services);
        new QuantityModule(connectionString).AddApiServices(services);
        new AllocationModule(connectionString).AddApiServices(services);
        new BatchModule(connectionString).AddApiServices(services);
        new ResultModule(connectionString).AddApiServices(services);
        new BillingModule(connectionString).AddApiServices(services);

        services.RemoveAll<IScopeAuthorizationPort>();
        services.AddSingleton<IScopeAuthorizationPort>(new FixedScopeAuthorization(permitScopeCapability));
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

    private static async Task PrepareAsync(string connectionString)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await EnsureDatabaseAsync(cancellationToken);
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

    private static async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(AdminConnectionString());
        await using var exists = dataSource.CreateCommand("select 1 from pg_database where datname = $1");
        exists.Parameters.AddWithValue(DedicatedDatabaseName);
        if (await exists.ExecuteScalarAsync(cancellationToken) is null)
        {
            try
            {
                await using var create = dataSource.CreateCommand($"create database \"{DedicatedDatabaseName}\"");
                await create.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == "42P04")
            {
            }
        }
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

    private sealed class FixedScopeAuthorization(bool allowed) : IScopeAuthorizationPort
    {
        public ValueTask<ScopeAuthorizationDecision> AuthorizeAsync(
            ScopeAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed ? ScopeAuthorizationDecision.Permit : ScopeAuthorizationDecision.Deny);
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
