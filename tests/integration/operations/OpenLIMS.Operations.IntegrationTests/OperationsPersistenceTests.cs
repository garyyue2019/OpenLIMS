using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Operations;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Modules.Operations;
using Xunit;

namespace OpenLIMS.Operations.IntegrationTests;

[CollectionDefinition("operations-postgres", DisableParallelization = true)]
public sealed class OperationsPostgresCollection;

[Collection("operations-postgres")]
[Trait("Profile", "operations")]
public sealed class OperationsPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_operations_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Lineage_and_custody_persist_append_only_facts_with_platform_evidence()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, allowed: true);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOperationsService>();

        await service.CreateLineageEdgeAsync(Edge("SOURCE-A", "SAMPLE-B"), "corr-edge-1", TestContext.Current.CancellationToken);
        await service.CreateLineageEdgeAsync(
            Edge("SOURCE-C", "SAMPLE-B") with { RelationKind = LineageRelationKinds.CompositeFrom },
            "corr-edge-2",
            TestContext.Current.CancellationToken);
        await service.RecordCustodyEventAsync(
            Custody(CustodyEventKinds.Received, null, "DOCK"),
            "corr-custody-1",
            TestContext.Current.CancellationToken);
        var transferred = await service.RecordCustodyEventAsync(
            Custody(CustodyEventKinds.Transferred, "DOCK", "LAB"),
            "corr-custody-2",
            TestContext.Current.CancellationToken);
        var graph = await service.GetLineageAsync("SAMPLE-B", "corr-read-lineage", TestContext.Current.CancellationToken);
        var chain = await service.GetCustodyAsync("SAMPLE-B", "corr-read-custody", TestContext.Current.CancellationToken);

        Assert.Equal(2, graph.Edges.Count);
        Assert.Equal(2, chain.Events.Count);
        Assert.Equal(2, transferred.Sequence);
        Assert.Equal(2, await CountAsync(connectionString, "operations.lineage_edge"));
        Assert.Equal(2, await CountAsync(connectionString, "operations.custody_event"));
        Assert.Equal(6, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(4, await CountAsync(connectionString, "platform.outbox"));
    }

    [Fact]
    public async Task Lineage_cycle_fails_closed_without_new_edge()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, allowed: true);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOperationsService>();
        await service.CreateLineageEdgeAsync(Edge("SAMPLE-A", "SAMPLE-B"), "corr-first", TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<OperationsDomainException>(() =>
            service.CreateLineageEdgeAsync(Edge("SAMPLE-B", "SAMPLE-A"), "corr-cycle", TestContext.Current.CancellationToken));

        Assert.Equal(OperationsErrorCodes.LineageCycle, exception.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "operations.lineage_edge"));
        Assert.Equal(1, await CountAsync(connectionString, "operations.audit_attempt"));
    }

    [Fact]
    public async Task Work_plan_dependencies_reservations_and_queue_share_one_version_chain()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, allowed: true);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOperationsService>();
        var plan = await service.CreateWorkPlanAsync(PlanRequest("TASK-A", "TASK-B"), "corr-plan", TestContext.Current.CancellationToken);
        var started = await service.ChangeTaskStateAsync(
            plan.WorkPlanId, "TASK-A", new ChangeWorkTaskStateRequest(1, WorkTaskStates.InProgress, "started"),
            "corr-start", TestContext.Current.CancellationToken);
        var completed = await service.ChangeTaskStateAsync(
            plan.WorkPlanId, "TASK-A", new ChangeWorkTaskStateRequest(started.Version, WorkTaskStates.Completed, "done"),
            "corr-complete", TestContext.Current.CancellationToken);
        var reserved = await service.ReserveResourceAsync(
            plan.WorkPlanId,
            new ReserveResourceRequest(completed.Version, "TASK-B", ResourceKinds.Equipment, "EQUIP-1", Now, Now.AddHours(1)),
            "corr-reserve",
            TestContext.Current.CancellationToken);
        var queue = await service.GetWorkQueueAsync("WC-A", WorkTaskStates.Ready, "corr-queue", TestContext.Current.CancellationToken);

        Assert.Equal(WorkTaskStates.Ready, completed.Tasks.Single(task => task.TaskId == "TASK-B").State);
        Assert.Single(reserved.Reservations);
        Assert.Single(queue.Items);
        Assert.Equal("TASK-B", queue.Items[0].TaskId);
        Assert.Equal(4, await CountAsync(connectionString, "operations.work_plan_version"));
        Assert.Equal(1, await CountAsync(connectionString, "operations.resource_reservation"));
    }

    [Fact]
    public async Task Concurrent_overlapping_resource_reservations_allow_only_one_plan()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string firstPlanId;
        string secondPlanId;
        await using (var setup = BuildProvider(connectionString, allowed: true))
        {
            using var setupScope = setup.CreateScope();
            var service = setupScope.ServiceProvider.GetRequiredService<IOperationsService>();
            firstPlanId = (await service.CreateWorkPlanAsync(
                PlanRequest("TASK-A", null), "corr-plan-a", TestContext.Current.CancellationToken)).WorkPlanId;
            secondPlanId = (await service.CreateWorkPlanAsync(
                PlanRequest("TASK-C", null), "corr-plan-b", TestContext.Current.CancellationToken)).WorkPlanId;
        }

        await using var first = BuildProvider(connectionString, allowed: true, "planner-a");
        await using var second = BuildProvider(connectionString, allowed: true, "planner-b");
        using var firstScope = first.CreateScope();
        using var secondScope = second.CreateScope();
        var requestA = new ReserveResourceRequest(1, "TASK-A", ResourceKinds.Equipment, "EQUIP-1", Now, Now.AddHours(1));
        var requestB = new ReserveResourceRequest(1, "TASK-C", ResourceKinds.Equipment, "EQUIP-1", Now, Now.AddHours(1));
        var outcomes = await Task.WhenAll(
            CaptureAsync(firstScope.ServiceProvider.GetRequiredService<IOperationsService>()
                .ReserveResourceAsync(firstPlanId, requestA, "corr-a", TestContext.Current.CancellationToken)),
            CaptureAsync(secondScope.ServiceProvider.GetRequiredService<IOperationsService>()
                .ReserveResourceAsync(secondPlanId, requestB, "corr-b", TestContext.Current.CancellationToken)));

        Assert.Single(outcomes, outcome => outcome.Error is null);
        Assert.Equal(
            OperationsErrorCodes.ResourceConflict,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "operations.resource_reservation"));
        Assert.Equal(1, await CountAsync(connectionString, "operations.audit_attempt"));
    }

    [Fact]
    public async Task Append_only_and_authorization_controls_protect_work_plans()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using (var allowed = BuildProvider(connectionString, allowed: true))
        {
            using var allowedScope = allowed.CreateScope();
            await allowedScope.ServiceProvider.GetRequiredService<IOperationsService>()
                .CreateWorkPlanAsync(PlanRequest("TASK-A", null), "corr-plan", TestContext.Current.CancellationToken);
        }
        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update operations.work_plan_version set state = 'COMPLETED'"));

        await using var denied = BuildProvider(connectionString, allowed: false);
        using var deniedScope = denied.CreateScope();
        var exception = await Assert.ThrowsAsync<OperationsDomainException>(() =>
            deniedScope.ServiceProvider.GetRequiredService<IOperationsService>()
                .CreateWorkPlanAsync(PlanRequest("TASK-Z", null), "corr-denied", TestContext.Current.CancellationToken));

        Assert.Equal("55000", update.SqlState);
        Assert.Equal(OperationsErrorCodes.NotAuthorized, exception.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "operations.work_plan_version"));
        Assert.Equal(1, await CountAsync(connectionString, "operations.audit_attempt"));
    }

    private static async Task<(WorkPlanResult? Result, OperationsDomainException? Error)> CaptureAsync(Task<WorkPlanResult> task)
    {
        try
        {
            return (await task, null);
        }
        catch (OperationsDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(string connectionString, bool allowed, string actorId = "operator-a")
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
        new OperationsModule(connectionString).AddApiServices(services);
        services.RemoveAll<IOperationsAuthorizationPort>();
        services.AddSingleton<IOperationsAuthorizationPort>(new FixedAuthorizationPort(allowed));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateLineageEdgeRequest Edge(string source, string target) => new(
        source, target, LineageRelationKinds.DerivedFrom,
        new OperationsVersionedReference("BASIS", 1), ObjectScope());

    private static RecordCustodyEventRequest Custody(string kind, string? from, string to) => new(
        "SAMPLE-B", kind, from, to, "PERSON-A", "EVIDENCE-A", ObjectScope());

    private static CreateWorkPlanRequest PlanRequest(string firstTaskId, string? secondTaskId)
    {
        var tasks = new List<WorkTaskInput>
        {
            WorkTask(firstTaskId, 1, [])
        };
        if (secondTaskId is not null)
            tasks.Add(WorkTask(secondTaskId, 2, [firstTaskId]));
        return new CreateWorkPlanRequest(
            new OperationsVersionedReference("SCOPE", 1),
            new OperationsVersionedReference("IDENTITY", 1),
            tasks,
            ObjectScope());
    }

    private static WorkTaskInput WorkTask(string id, int sequence, IReadOnlyList<string> dependencies) => new(
        id, $"LINE-{sequence}", new OperationsVersionedReference("METHOD", 1), "WC-A",
        100 - sequence, sequence, sequence > 1, Now.AddHours(sequence), Now.AddHours(sequence + 1), dependencies);

    private static OperationsObjectContext ObjectScope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE");

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for operations integration tests.");

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
        await OperationsMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              operations.audit_attempt,
              operations.resource_reservation,
              operations.work_plan_version,
              operations.custody_event,
              operations.lineage_edge,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

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

    private sealed class FixedAuthorizationPort(bool allowed) : IOperationsAuthorizationPort
    {
        public ValueTask<OperationsAuthorizationDecision> AuthorizeAsync(
            OperationsAuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed
                ? OperationsAuthorizationDecision.Permit
                : OperationsAuthorizationDecision.Deny);
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
