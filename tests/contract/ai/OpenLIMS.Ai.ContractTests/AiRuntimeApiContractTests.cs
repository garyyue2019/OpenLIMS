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
using OpenLIMS.Contracts.Ai;
using OpenLIMS.Modules.Ai;
using Xunit;

namespace OpenLIMS.Ai.ContractTests;

[Trait("Profile", "ai")]
public sealed class AiRuntimeApiContractTests
{
    private const string RunId = "00000000000000000000000000000041";

    [Fact]
    public async Task Ai_runtime_endpoints_return_stable_success_contracts()
    {
        using var factory = new AiApiFactory();
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync(
            AiContract.CreateRunPath, Request(), TestContext.Current.CancellationToken);
        using var read = await client.GetAsync(
            AiContract.GetRunPath.Replace("{id}", RunId, StringComparison.Ordinal),
            TestContext.Current.CancellationToken);
        using var disposition = await client.PostAsJsonAsync(
            AiContract.RecordDispositionPath.Replace("{id}", RunId, StringComparison.Ordinal),
            DispositionRequest(),
            TestContext.Current.CancellationToken);
        using var queue = await client.GetAsync(
            AiContract.ReviewQueuePath, TestContext.Current.CancellationToken);
        var createdBody = await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var dispositionBody = await disposition.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Created, disposition.StatusCode);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        Assert.Contains(AiRunStatuses.Accepted, createdBody, StringComparison.Ordinal);
        Assert.Contains("STY-1001", dispositionBody, StringComparison.Ordinal);
        Assert.Contains("STY-1002", dispositionBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AiErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(AiErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(AiErrorCodes.CandidateNotFound, HttpStatusCode.NotFound)]
    [InlineData(AiErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(AiErrorCodes.IdempotencyConflict, HttpStatusCode.Conflict)]
    [InlineData(AiErrorCodes.ReviewNotAllowed, HttpStatusCode.UnprocessableEntity)]
    [InlineData(AiErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(AiErrorCodes.ValidationFailed, HttpStatusCode.BadRequest)]
    public async Task Ai_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new AiApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            AiContract.CreateRunPath, Request(), TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_ai_body_is_rejected()
    {
        using var factory = new AiApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(
            AiContract.CreateRunPath,
            new StringContent("{"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_ai_runtime_operations()
    {
        using var factory = new AiApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        foreach (var operation in new[]
        {
            "createAiRun", "getAiRun", "recordAiDisposition", "getAiReviewQueue"
        })
        {
            Assert.Contains(operation, content, StringComparison.Ordinal);
        }
    }

    private static CreateAiRunRequest Request() => new(
        AiContract.RuntimeRuleSetVersion,
        Scope(),
        Envelope(),
        new AiVersionedReference("VALIDATION-PROFILE", 1),
        ["style-number"],
        [],
        "request-1");

    private static RecordAiDispositionRequest DispositionRequest() => new(
        1,
        AiContract.RuntimeRuleSetVersion,
        "candidate-1",
        AiDispositionKinds.Modify,
        "checked against source",
        "review-1",
        "STY-1002");

    private static AiObjectContext Scope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE");

    private static AiRunEnvelope Envelope() => new(
        new AiVersionedReference("MODEL-A", 1),
        "gateway-primary",
        new AiVersionedReference("PROMPT-A", 1),
        new AiVersionedReference("SCHEMA-A", 1),
        [new AiVersionedReference("DOC-A", 1)]);
}

internal sealed class AiApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = AiTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = AiTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, AiTestAuthenticationHandler>(
                    AiTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IAiRunService>();
            services.AddSingleton<IAiRunService>(new StubAiRunService(errorCode));
        });
    }
}

internal sealed class StubAiRunService(string? errorCode) : IAiRunService
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    public Task<AiRunResult> CreateAsync(
        CreateAiRunRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Run(request));
    }

    public Task<AiRunResult> GetAsync(
        string runId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Run(AiRuntimeApiContractTestsRequest.Request()));
    }

    public Task<AiReviewDispositionResult> RecordDispositionAsync(
        string runId,
        RecordAiDispositionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new AiReviewDispositionResult(
            new AiDisposition(
                "00000000000000000000000000000042", request.CandidateId, request.Kind,
                "STY-1001", request.Reason, "contract-actor", request.HumanValue),
            Now));
    }

    public Task<AiReviewQueueResult> GetReviewQueueAsync(
        string? status,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new AiReviewQueueResult(
            [Run(AiRuntimeApiContractTestsRequest.Request())], AiContract.RuntimeRuleSetVersion));
    }

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null)
            throw new AiDomainException(errorCode);
    }

    private static AiRunResult Run(CreateAiRunRequest request)
    {
        var candidate = new AiFieldCandidate(
            "candidate-1", "style-number", "STY-1001", AiFactClasses.AiInference, 0.94m,
            new AiSourceLocation(new AiVersionedReference("DOC-A", 1), 2, "top-right"));
        var output = new AiStructuredOutput(AiContract.RuleSetVersion, request.Envelope, [candidate], []);
        return new AiRunResult(
            "00000000000000000000000000000041", 1, AiRunStatuses.Accepted,
            request.ObjectScope, request.Envelope, request.ValidationProfile,
            request.AllowedFields, request.AllowedUnits, AiProviderStatuses.Completed,
            "provider-job-1", null, output,
            new AiValidationResult(AiValidationDecisions.Accepted, [], [candidate], [], AiContract.RuleSetVersion),
            [], true, false, "contract-actor", Now, Now, AiContract.RuntimeRuleSetVersion);
    }
}

internal static class AiRuntimeApiContractTestsRequest
{
    public static CreateAiRunRequest Request() => new(
        AiContract.RuntimeRuleSetVersion,
        new AiObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE"),
        new AiRunEnvelope(
            new AiVersionedReference("MODEL-A", 1),
            "gateway-primary",
            new AiVersionedReference("PROMPT-A", 1),
            new AiVersionedReference("SCHEMA-A", 1),
            [new AiVersionedReference("DOC-A", 1)]),
        new AiVersionedReference("VALIDATION-PROFILE", 1),
        ["style-number"],
        [],
        "request-1");
}

internal sealed class AiTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Ai.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
