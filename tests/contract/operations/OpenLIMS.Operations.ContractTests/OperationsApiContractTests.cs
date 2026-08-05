using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenLIMS.Contracts.Operations;
using OpenLIMS.Modules.Operations;
using Xunit;

namespace OpenLIMS.Operations.ContractTests;

[Trait("Profile", "operations")]
public sealed class OperationsApiContractTests
{
    private const string WorkPlanId = "00000000000000000000000000000041";

    [Fact]
    public async Task Operations_endpoints_expose_lineage_custody_planning_and_queue_contracts()
    {
        using var factory = new OperationsApiFactory();
        using var client = factory.CreateClient();
        using var edge = await client.PostAsJsonAsync(
            OperationsContract.CreateLineageEdgePath, EdgeRequest(), TestContext.Current.CancellationToken);
        using var lineage = await client.GetAsync(
            "/api/v1/sample-lineage/SAMPLE-B", TestContext.Current.CancellationToken);
        using var custody = await client.PostAsJsonAsync(
            OperationsContract.RecordCustodyEventPath, CustodyRequest(), TestContext.Current.CancellationToken);
        using var chain = await client.GetAsync(
            "/api/v1/samples/SAMPLE-B/custody", TestContext.Current.CancellationToken);
        using var plan = await client.PostAsJsonAsync(
            OperationsContract.CreateWorkPlanPath, PlanRequest(), TestContext.Current.CancellationToken);
        using var readPlan = await client.GetAsync(
            $"/api/v1/work-plans/{WorkPlanId}", TestContext.Current.CancellationToken);
        using var state = await client.PostAsJsonAsync(
            $"/api/v1/work-plans/{WorkPlanId}/tasks/TASK-A/state",
            new ChangeWorkTaskStateRequest(1, WorkTaskStates.InProgress, "started"),
            TestContext.Current.CancellationToken);
        using var reserve = await client.PostAsJsonAsync(
            $"/api/v1/work-plans/{WorkPlanId}/resource-reservations",
            new ReserveResourceRequest(1, "TASK-A", ResourceKinds.Equipment, "EQUIP-1", Now, Now.AddHours(1)),
            TestContext.Current.CancellationToken);
        using var queue = await client.GetAsync(
            "/api/v1/work-queues?workCenterId=WC-A&state=READY",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, edge.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lineage.StatusCode);
        Assert.Equal(HttpStatusCode.Created, custody.StatusCode);
        Assert.Equal(HttpStatusCode.OK, chain.StatusCode);
        Assert.Equal(HttpStatusCode.Created, plan.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readPlan.StatusCode);
        Assert.Equal(HttpStatusCode.Created, state.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reserve.StatusCode);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
    }

    [Theory]
    [InlineData(OperationsErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(OperationsErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(OperationsErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(OperationsErrorCodes.ResourceConflict, HttpStatusCode.Conflict)]
    [InlineData(OperationsErrorCodes.LineageCycle, HttpStatusCode.UnprocessableEntity)]
    [InlineData(OperationsErrorCodes.CustodySequenceConflict, HttpStatusCode.UnprocessableEntity)]
    [InlineData(OperationsErrorCodes.DependencyBlocked, HttpStatusCode.UnprocessableEntity)]
    [InlineData(OperationsErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Operations_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new OperationsApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            OperationsContract.CreateLineageEdgePath, EdgeRequest(), TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Work_queue_requires_single_work_center()
    {
        using var factory = new OperationsApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/v1/work-queues", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_operations_module_actions()
    {
        using var factory = new OperationsApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        foreach (var operation in new[]
        {
            "createSampleLineageEdge", "getSampleLineage", "recordCustodyEvent", "getCustodyChain",
            "createWorkPlan", "getWorkPlan", "changeWorkTaskState", "reserveWorkResource", "getWorkQueue"
        })
        {
            Assert.Contains(operation, content, StringComparison.Ordinal);
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

    private static CreateLineageEdgeRequest EdgeRequest() => new(
        "SAMPLE-A", "SAMPLE-B", LineageRelationKinds.DerivedFrom, Ref("BASIS", 1), Scope());

    private static RecordCustodyEventRequest CustodyRequest() => new(
        "SAMPLE-B", CustodyEventKinds.Received, null, "DOCK", "PERSON-A", "EVIDENCE-A", Scope());

    private static CreateWorkPlanRequest PlanRequest() => new(
        Ref("SCOPE", 1), Ref("IDENTITY", 1),
        [new WorkTaskInput("TASK-A", "LINE-A", Ref("METHOD", 1), "WC-A", 50, 1, false, Now, Now.AddHours(1), [])],
        Scope());

    private static OperationsVersionedReference Ref(string id, long version) => new(id, version);

    private static OperationsObjectContext Scope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE");
}

internal sealed class OperationsApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Platform:OrganizationGroupId", "test-group");
        builder.UseSetting("Platform:PostgresConnectionString", "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test;Timeout=1");
        builder.UseSetting("Platform:OidcAuthority", "https://issuer.invalid/");
        builder.UseSetting("Platform:OidcAudience", "openlims-api");
        builder.UseSetting("Platform:ObjectStorageEndpoint", "http://127.0.0.1:1");
        builder.UseSetting("Platform:ObjectStorageBucket", "test");
        builder.UseSetting("Platform:ObjectStorageAccessKey", "test-access");
        builder.UseSetting("Platform:ObjectStorageSecretKey", "test-secret");
        builder.UseSetting("Platform:PostgresCommandTimeoutSeconds", "1");
        builder.UseSetting("Platform:OidcMetadataTimeoutSeconds", "1");
        builder.UseSetting("Platform:ObjectStorageProbeTimeoutSeconds", "1");
        builder.UseSetting("Platform:DependencyProbeTimeoutSeconds", "2");
        builder.UseSetting("Platform:AllowInsecureDevelopmentObjectStorage", "true");
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = OperationsTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = OperationsTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, OperationsTestAuthenticationHandler>(
                    OperationsTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IOperationsService>();
            services.AddSingleton<IOperationsService>(new StubOperationsService(errorCode));
        });
    }
}

internal sealed class StubOperationsService(string? errorCode) : IOperationsService
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly OperationsObjectContext Scope = new("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE");

    public Task<LineageEdgeResult> CreateLineageEdgeAsync(CreateLineageEdgeRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new LineageEdgeResult(
            "00000000000000000000000000000042", request.SourceObjectId, request.TargetObjectId,
            request.RelationKind, request.Basis, request.ObjectScope, "contract-actor", Now));
    }

    public Task<LineageGraphResult> GetLineageAsync(string objectId, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new LineageGraphResult(objectId, OperationsContract.RuleSetVersion, []));
    }

    public Task<CustodyEventResult> RecordCustodyEventAsync(RecordCustodyEventRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new CustodyEventResult(
            "00000000000000000000000000000043", request.ObjectId, 1, request.EventKind,
            request.FromLocationId, request.ToLocationId, request.ResponsiblePartyId, request.EvidenceRef,
            request.ObjectScope, "contract-actor", Now));
    }

    public Task<CustodyChainResult> GetCustodyAsync(string objectId, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new CustodyChainResult(objectId, OperationsContract.RuleSetVersion, []));
    }

    public Task<WorkPlanResult> CreateWorkPlanAsync(CreateWorkPlanRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        Plan(request, cancellationToken);

    public Task<WorkPlanResult> GetWorkPlanAsync(string workPlanId, string correlationId, CancellationToken cancellationToken = default) =>
        Plan(PlanRequest(), cancellationToken);

    public Task<WorkPlanResult> ChangeTaskStateAsync(string workPlanId, string taskId, ChangeWorkTaskStateRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        Plan(PlanRequest(), cancellationToken);

    public Task<WorkPlanResult> ReserveResourceAsync(string workPlanId, ReserveResourceRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        Plan(PlanRequest(), cancellationToken);

    public Task<WorkQueueResult> GetWorkQueueAsync(string workCenterId, string? state, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new WorkQueueResult(workCenterId, state, OperationsContract.RuleSetVersion, []));
    }

    private Task<WorkPlanResult> Plan(CreateWorkPlanRequest request, CancellationToken cancellationToken)
    {
        Throw(cancellationToken);
        var tasks = request.Tasks.Select(task => new WorkTaskResult(
            task.TaskId, task.ScopeLineId, task.Method, task.WorkCenterId, task.Priority, task.Sequence,
            task.Destructive, task.PlannedStart, task.PlannedEnd, task.DependencyTaskIds,
            WorkTaskStates.Ready, null, null, null)).ToArray();
        return Task.FromResult(new WorkPlanResult(
            "00000000000000000000000000000041", 1, OperationsContract.RuleSetVersion,
            WorkPlanStates.Active, request.ScopeMatrix, request.SampleIdentity, request.ObjectScope,
            tasks, [], "contract-actor", Now));
    }

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null)
            throw new OperationsDomainException(errorCode);
    }

    private static CreateWorkPlanRequest PlanRequest() => new(
        new OperationsVersionedReference("SCOPE", 1), new OperationsVersionedReference("IDENTITY", 1),
        [new WorkTaskInput("TASK-A", "LINE-A", new OperationsVersionedReference("METHOD", 1), "WC-A", 50, 1, false, Now, Now.AddHours(1), [])],
        Scope);
}

internal sealed class OperationsTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Operations.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
