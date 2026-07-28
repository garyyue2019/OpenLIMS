using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenLIMS.Contracts.Textile;
using OpenLIMS.Modules.Textile;
using Xunit;

namespace OpenLIMS.Textile.ContractTests;

[Trait("Profile", "textile")]
public sealed class TextileRuntimeApiContractTests
{
    [Fact]
    public async Task Four_runtime_operations_expose_versioned_contracts()
    {
        using var factory = new TextileApiFactory();
        using var client = factory.CreateClient();

        using var requirement = await client.PostAsJsonAsync(
            TextileRuntimeContract.SampleRequirementPath,
            RequirementRequest(),
            TestContext.Current.CancellationToken);
        using var plan = await client.PostAsJsonAsync(
            TextileRuntimeContract.CuttingPlanPath,
            PlanRequest(),
            TestContext.Current.CancellationToken);
        using var approval = await client.PostAsJsonAsync(
            "/api/v1/textile/cutting-plans/PLAN-1/versions/1/approval",
            new ApproveTextileCuttingPlanRequest(
                1, "requirement-hash", TextileContract.RuleSetVersion, "reviewed"),
            TestContext.Current.CancellationToken);
        using var detail = await client.GetAsync(
            "/api/v1/textile/cutting-plans/PLAN-1/versions/1",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, requirement.StatusCode);
        Assert.Equal(HttpStatusCode.Created, plan.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.Content.ReadFromJsonAsync<TextileCuttingPlanResult>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(TextileCuttingPlanStates.Approved, body.State);
        Assert.Equal(TextileContract.RuleSetVersion, body.RuleSetVersion);
    }

    [Theory]
    [InlineData(TextileErrorCodes.ValidationFailed, HttpStatusCode.BadRequest)]
    [InlineData(TextileErrorCodes.DirectionUnknown, HttpStatusCode.BadRequest)]
    [InlineData(TextileErrorCodes.ExclusiveShareRejected, HttpStatusCode.BadRequest)]
    [InlineData(TextileErrorCodes.ApplicabilityUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(TextileErrorCodes.SampleRequirementNotApprovable, HttpStatusCode.UnprocessableEntity)]
    [InlineData(TextileErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(TextileErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(TextileErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(TextileErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Runtime_errors_map_to_stable_problem_contracts(
        string errorCode,
        HttpStatusCode expectedStatus)
    {
        using var factory = new TextileApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            TextileRuntimeContract.SampleRequirementPath,
            RequirementRequest(),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public void Endpoint_metadata_declares_all_four_textile_runtime_operations()
    {
        using var factory = new TextileApiFactory();
        var endpointNames = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var operation in new[]
        {
            "calculateTextileSampleRequirement",
            "createTextileCuttingPlan",
            "approveTextileCuttingPlan",
            "getTextileCuttingPlan"
        })
        {
            Assert.Contains(operation, endpointNames);
        }
    }

    internal static CreateTextileSampleRequirementRequest RequirementRequest() => new(
        "REQ-1", 0, Scope(),
        new TextileSampleRequirementCalculation(
            TextileContract.RuleSetVersion,
            [new TextileDemandLine(
                Ref("STYLE"), Ref("RED"), Ref("FRONT"), Ref("COTTON"), "BODY",
                TextileDirections.Warp, Ref("TENSILE"), 3, 1, 1, true, 10m, 12m,
                ExclusiveDestructiveGroupId: "GROUP-A")],
            [new TextileAvailableFabric(
                Ref("STYLE"), Ref("RED"), Ref("FRONT"), "BODY", 1_000m)]));

    internal static CreateTextileCuttingPlanRequest PlanRequest() => new(
        "PLAN-1", 0, "REQ-1", 1, "requirement-hash", TextileContract.RuleSetVersion,
        new TextileCuttingPlan(
            "PLAN-1", Ref("FABRIC-LOT"), "BODY", TextileDirections.Warp,
            10m, 12m, 5, 20m, "TPL-1", "operator",
            ["SPEC-1", "SPEC-2", "SPEC-3", "SPEC-4", "SPEC-5"]));

    internal static TextileObjectScope Scope() => new("LEGAL-A", "LAB-A");

    internal static TextileVersionedReference Ref(string id) => new(id, 1);
}

internal sealed class TextileApiFactory : IDisposable
{
    private readonly IHost _host;
    private readonly TestServer _server;

    public TextileApiFactory(string? errorCode = null)
    {
        _host = new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseEnvironment("Development")
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TextileTestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TextileTestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TextileTestAuthenticationHandler>(
                        TextileTestAuthenticationHandler.SchemeName, _ => { });
                    services.AddSingleton<ITextileRuntimeService>(new StubTextileRuntimeService(errorCode));
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(TextileEndpoints.Map);
                }))
            .Start();
        _server = _host.GetTestServer();
    }

    public IServiceProvider Services => _server.Services;

    public HttpClient CreateClient() => _server.CreateClient();

    public void Dispose()
    {
        _server.Dispose();
        _host.Dispose();
    }
}

internal sealed class StubTextileRuntimeService(string? errorCode) : ITextileRuntimeService
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    public Task<TextileSampleRequirementRecord> CalculateSampleRequirementAsync(
        CreateTextileSampleRequirementRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Requirement());
    }

    public Task<TextileCuttingPlanResult> CreateCuttingPlanAsync(
        CreateTextileCuttingPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Plan(approved: false));
    }

    public Task<TextileCuttingPlanResult> ApproveCuttingPlanAsync(
        string cuttingPlanId,
        long version,
        ApproveTextileCuttingPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Plan(approved: true));
    }

    public Task<TextileCuttingPlanResult> GetCuttingPlanAsync(
        string cuttingPlanId,
        long version,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Plan(approved: true));
    }

    private static TextileSampleRequirementRecord Requirement()
    {
        var request = TextileRuntimeApiContractTests.RequirementRequest();
        var result = TextileSampleRequirementRules.Instance.Calculate(request.Calculation);
        return new TextileSampleRequirementRecord(
            "REQ-1", 1, TextileRuntimeApiContractTests.Scope(), request.Calculation, result,
            "requirement-hash", "creator", Now);
    }

    private static TextileCuttingPlanResult Plan(bool approved)
    {
        var requirement = Requirement();
        return new TextileCuttingPlanResult(
            "PLAN-1", 1, TextileRuntimeApiContractTests.Scope(), requirement,
            TextileRuntimeApiContractTests.PlanRequest().Plan,
            approved ? TextileCuttingPlanStates.Approved : TextileCuttingPlanStates.Draft,
            "plan-hash", TextileContract.RuleSetVersion, "creator", Now,
            approved
                ? new TextileCuttingPlanApproval(
                    "PLAN-1", 1, "REQ-1", 1, requirement.InputHash,
                    TextileContract.RuleSetVersion, "approver", Now, "reviewed")
                : null);
    }

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null)
            throw new TextileOperationException(errorCode);
    }
}

internal sealed class TextileTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Textile.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
