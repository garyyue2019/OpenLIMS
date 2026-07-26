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
using OpenLIMS.Contracts.Scope;
using OpenLIMS.Modules.Scope;
using Xunit;

namespace OpenLIMS.Scope.ContractTests;

[Trait("Profile", "scope")]
public sealed class ScopeApiContractTests
{
    private const string MatrixId = "00000000000000000000000000000010";

    [Fact]
    public async Task Four_scope_operations_expose_versioned_contracts()
    {
        using var factory = new ScopeApiFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync(
            ScopeContract.CreateMatrixPath,
            Request(0),
            TestContext.Current.CancellationToken);
        using var revised = await client.PostAsJsonAsync(
            $"/api/v1/scope-matrices/{MatrixId}/versions",
            Request(1),
            TestContext.Current.CancellationToken);
        using var read = await client.GetAsync(
            $"/api/v1/scope-matrices/{MatrixId}/versions/1",
            TestContext.Current.CancellationToken);
        using var eligibility = await client.GetAsync(
            $"/api/v1/scope-matrices/{MatrixId}/production-eligibility?expectedVersion=1&ruleSetVersion={Uri.EscapeDataString(ScopeContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Created, revised.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, eligibility.StatusCode);
        var matrix = await created.Content.ReadFromJsonAsync<ScopeMatrixVersionResult>(
            TestContext.Current.CancellationToken);
        var gate = await eligibility.Content.ReadFromJsonAsync<ScopeProductionEligibilityResult>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(matrix);
        Assert.Equal(ScopeMatrixStates.Approved, matrix.State);
        Assert.Single(matrix.Lines);
        Assert.NotNull(gate);
        Assert.Equal(ScopeEligibilityDecisions.Allowed, gate.Decision);
    }

    [Theory]
    [InlineData(ScopeErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(ScopeErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(ScopeErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(ScopeErrorCodes.EvaluationIncomplete, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ScopeErrorCodes.EvaluationConflict, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ScopeErrorCodes.ApplicabilityUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ScopeErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Scope_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new ScopeApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            ScopeContract.CreateMatrixPath,
            Request(0),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_eligibility_query_is_rejected()
    {
        using var factory = new ScopeApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/v1/scope-matrices/{MatrixId}/production-eligibility?expectedVersion=latest",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_scope_operations()
    {
        using var factory = new ScopeApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(ScopeContract.CreateMatrixPath, content, StringComparison.Ordinal);
        Assert.Contains("createScopeMatrix", content, StringComparison.Ordinal);
        Assert.Contains("reviseScopeMatrix", content, StringComparison.Ordinal);
        Assert.Contains("getScopeMatrixVersion", content, StringComparison.Ordinal);
        Assert.Contains("getScopeProductionEligibility", content, StringComparison.Ordinal);
    }

    private static SubmitScopeMatrixVersionRequest Request(long expectedVersion) => new(
        expectedVersion,
        ScopeContract.RuleSetVersion,
        new ScopeObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        [new ScopeLineInput(
            ScopeSubjectTypes.FeatureNode,
            new ScopeVersionedReference("FEATURE-1", 1),
            new ScopeVersionedReference("MARKET-1", 1),
            new ScopeVersionedReference("REQ-1", 1),
            new ScopeVersionedReference("ITEM-1", 1),
            new ScopeVersionedReference("METHOD-1", 1),
            "OPTION-A",
            new ScopeVersionedReference("SAMPLE-REQ-1", 1),
            ScopeEvaluationModes.Evaluated,
            new ScopeVersionedReference("WC-1", 1),
            "REPORT-1",
            new ScopeVersionedReference("LIMIT-1", 1),
            new ScopeVersionedReference("DECISION-1", 1))]);
}

internal sealed class ScopeApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = ScopeTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = ScopeTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, ScopeTestAuthenticationHandler>(
                    ScopeTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IScopeMatrixService>();
            services.RemoveAll<IScopeProductionEligibilityPort>();
            services.AddSingleton<IScopeMatrixService>(new StubScopeMatrixService(errorCode));
            services.AddSingleton<IScopeProductionEligibilityPort>(new StubScopeEligibilityPort(errorCode));
        });
    }
}

internal sealed class StubScopeMatrixService(string? errorCode) : IScopeMatrixService
{
    public Task<ScopeMatrixVersionResult> CreateAsync(
        SubmitScopeMatrixVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ResultAsync(1, request, cancellationToken);

    public Task<ScopeMatrixVersionResult> ReviseAsync(
        string scopeMatrixId,
        SubmitScopeMatrixVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ResultAsync(request.ExpectedCurrentVersion + 1, request, cancellationToken);

    public Task<ScopeMatrixVersionResult> GetVersionAsync(
        string scopeMatrixId,
        long version,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ResultAsync(version, ScopeApiContractTestsAccessor.Request(0), cancellationToken);

    private Task<ScopeMatrixVersionResult> ResultAsync(
        long version,
        SubmitScopeMatrixVersionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new ScopeDomainException(errorCode);
        var input = request.Lines[0];
        return Task.FromResult(new ScopeMatrixVersionResult(
            "00000000000000000000000000000010",
            version,
            ScopeMatrixStates.Approved,
            ScopeContract.RuleSetVersion,
            request.ObjectScope,
            [new ScopeLineResult(
                new string('a', 64), input.SubjectType, input.Subject, input.TargetMarket,
                input.RequirementClause, input.TestItem, input.Method, input.MethodOption,
                input.SampleRequirement, input.EvaluationMode, input.WorkCenter,
                input.ReportPosition, input.LimitRule, input.DecisionRule,
                input.NonEvaluationReason, input.WaiverApproval)],
            "contract-actor",
            new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero)));
    }
}

internal sealed class StubScopeEligibilityPort(string? errorCode) : IScopeProductionEligibilityPort
{
    public ValueTask<ScopeProductionEligibilityResult> EvaluateAsync(
        ScopeProductionEligibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new ScopeDomainException(errorCode);
        return ValueTask.FromResult(new ScopeProductionEligibilityResult(
            ScopeEligibilityDecisions.Allowed,
            [],
            request.ScopeMatrixId,
            request.ExpectedMatrixVersion,
            ScopeContract.RuleSetVersion));
    }
}

internal static class ScopeApiContractTestsAccessor
{
    public static SubmitScopeMatrixVersionRequest Request(long expectedVersion) => new(
        expectedVersion,
        ScopeContract.RuleSetVersion,
        new ScopeObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        [new ScopeLineInput(
            ScopeSubjectTypes.FeatureNode,
            new ScopeVersionedReference("FEATURE-1", 1),
            new ScopeVersionedReference("MARKET-1", 1),
            new ScopeVersionedReference("REQ-1", 1),
            new ScopeVersionedReference("ITEM-1", 1),
            new ScopeVersionedReference("METHOD-1", 1),
            "OPTION-A",
            new ScopeVersionedReference("SAMPLE-REQ-1", 1),
            ScopeEvaluationModes.Evaluated,
            new ScopeVersionedReference("WC-1", 1),
            "REPORT-1",
            new ScopeVersionedReference("LIMIT-1", 1),
            new ScopeVersionedReference("DECISION-1", 1))]);
}

internal sealed class ScopeTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Scope.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
