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
using OpenLIMS.Contracts.Toy;
using OpenLIMS.Modules.Toy;
using Xunit;

namespace OpenLIMS.Toy.ContractTests;

[Trait("Profile", "toy")]
public sealed class ToyApiContractTests
{
    private const string ProductId = "00000000000000000000000000000200";
    private const string TriggerId = "00000000000000000000000000000201";

    [Fact]
    public async Task Six_toy_operations_expose_versioned_contracts()
    {
        using var factory = new ToyApiFactory();
        using var client = factory.CreateClient();
        using var declared = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/age-declarations", Declaration(),
            TestContext.Current.CancellationToken);
        using var decided = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/age-grade-decisions", Decision(),
            TestContext.Current.CancellationToken);
        using var frozen = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/age-grade-decisions/1/freeze",
            new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, 3),
            TestContext.Current.CancellationToken);
        using var assessed = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/accessibility-assessments", Assessment(),
            TestContext.Current.CancellationToken);
        using var resolved = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/reassessment-triggers/{TriggerId}/resolution",
            new ResolveReassessmentTriggerRequest(
                ToyContract.RuleSetVersion, 5, new ToyVersionedReference("REASSESS-1", 1)),
            TestContext.Current.CancellationToken);
        using var overview = await client.GetAsync(
            $"/api/v1/toy/products/{ProductId}/overview", TestContext.Current.CancellationToken);

        foreach (var response in new[] { declared, decided, assessed })
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Freeze and resolve settle an existing fact rather than creating a new
        // resource, so they answer 200 instead of 201.
        Assert.Equal(HttpStatusCode.OK, frozen.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);

        var body = await overview.Content.ReadFromJsonAsync<ToyProductOverview>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal(ToyContract.RuleSetVersion, body.RuleSetVersion);
        Assert.Equal(ToyAccessibilityStatuses.Settled, body.AccessibilityStatus);
    }

    [Theory]
    [InlineData(ToyErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(ToyErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(ToyErrorCodes.DecisionNotFound, HttpStatusCode.NotFound)]
    [InlineData(ToyErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(ToyErrorCodes.DecisionFrozen, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ToyErrorCodes.ReassessmentNotPending, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ToyErrorCodes.ValidationFailed, HttpStatusCode.BadRequest)]
    [InlineData(ToyErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Toy_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new ToyApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/age-declarations", Declaration(),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_bodies_are_rejected_before_the_service_is_reached()
    {
        using var factory = new ToyApiFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(
            $"/api/v1/toy/products/{ProductId}/age-declarations", content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_toy_operations()
    {
        using var factory = new ToyApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        foreach (var operation in new[]
        {
            "recordToyAgeDeclaration", "recordToyAgeGradeDecision", "freezeToyAgeGradeDecision",
            "recordToyAccessibilityAssessment", "resolveToyReassessmentTrigger", "getToyProductOverview"
        })
        {
            Assert.Contains(operation, content, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// OPS-TOY-003 at the contract surface: one exposure opens three scopes.
    /// Collapsing them into a single "look again" would let a labeling gap ride
    /// out on a closed mechanical review.
    /// </summary>
    [Fact]
    public void Reassessment_contract_names_three_independent_scopes()
    {
        Assert.Equal(["MECHANICAL", "CHEMICAL", "LABELING"], ToyReassessmentScopes.All);
        Assert.Equal(3, ToyReassessmentScopes.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["INITIAL", "AFTER_NORMAL_USE", "AFTER_ABUSE"], ToyAssessmentStages.All);
    }

    /// <summary>
    /// OPS-TOY-001: the customer's claim and the laboratory's determination are
    /// separate types. Nothing on the declaration carries a rationale, standard
    /// or approver, so a claim can never be mistaken for a determination.
    /// </summary>
    [Fact]
    public void Declaration_and_determination_are_structurally_distinct()
    {
        var declarationFields = typeof(ToyAgeDeclarationEntry)
            .GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("Rationale", declarationFields);
        Assert.DoesNotContain("StandardRef", declarationFields);
        Assert.DoesNotContain("ApprovedBy", declarationFields);
        Assert.DoesNotContain("State", declarationFields);

        var decisionFields = typeof(ToyAgeGradeDecisionEntry)
            .GetProperties().Select(property => property.Name).ToArray();
        Assert.Contains("Rationale", decisionFields);
        Assert.Contains("StandardRef", decisionFields);
        Assert.Contains("ApprovedBy", decisionFields);
    }

    private static RecordAgeDeclarationRequest Declaration() => new(
        ToyContract.RuleSetVersion, new ToyObjectContext("LEGAL-A", "LAB-A"), 1,
        36, "室内地板玩具车", "CUSTOMER_SUBMISSION");

    private static RecordAgeGradeDecisionRequest Decision() => new(
        ToyContract.RuleSetVersion, new ToyObjectContext("LEGAL-A", "LAB-A"), 2,
        36, "无可分离小零件", new ToyVersionedReference("GB6675.2", 2), "APPROVER-1");

    private static RecordAccessibilityAssessmentRequest Assessment() => new(
        ToyContract.RuleSetVersion, new ToyObjectContext("LEGAL-A", "LAB-A"), 4,
        ToyAssessmentStages.Initial, null, ["shell", "wheels"]);
}

internal sealed class ToyApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = ToyTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = ToyTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, ToyTestAuthenticationHandler>(
                    ToyTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IToyProductService>();
            services.AddSingleton<IToyProductService>(new StubToyProductService(errorCode));
        });
    }
}

/// <summary>
/// Contract-level stub: the overview it returns is assembled by the production
/// derivation helpers, so the HTTP surface reflects real state semantics.
/// </summary>
internal sealed class StubToyProductService(string? errorCode) : IToyProductService
{
    private const string ProductId = "00000000000000000000000000000200";
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    public Task<ToyProductOverview> RecordDeclarationAsync(
        string productId, RecordAgeDeclarationRequest request, string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Overview(declarations: 1));
    }

    public Task<ToyProductOverview> RecordDecisionAsync(
        string productId, RecordAgeGradeDecisionRequest request, string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Overview(declarations: 1, decisions: 1));
    }

    public Task<ToyProductOverview> FreezeDecisionAsync(
        string productId, int versionNumber, FreezeAgeGradeDecisionRequest request,
        string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Overview(declarations: 1, decisions: 1, frozen: true));
    }

    public Task<ToyProductOverview> RecordAssessmentAsync(
        string productId, RecordAccessibilityAssessmentRequest request, string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Overview(declarations: 1, decisions: 1, frozen: true, assessments: 1));
    }

    public Task<ToyProductOverview> ResolveTriggerAsync(
        string productId, string triggerId, ResolveReassessmentTriggerRequest request,
        string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Overview(declarations: 1, decisions: 1, frozen: true, assessments: 1));
    }

    public Task<ToyProductOverview> GetOverviewAsync(
        string productId, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Overview(declarations: 1, decisions: 1, frozen: true, assessments: 1));
    }

    private static ToyProductOverview Overview(
        int declarations = 0, int decisions = 0, bool frozen = false, int assessments = 0)
    {
        var decisionEntries = Enumerable.Range(1, decisions)
            .Select(version => new ToyAgeGradeDecisionEntry(
                $"decision-{version}", ProductId, version, 36, "无可分离小零件",
                new ToyVersionedReference("GB6675.2", 2), "APPROVER-1",
                frozen ? ToyDecisionStates.Effective : ToyDecisionStates.Draft,
                Now, frozen ? Now : null))
            .ToArray();
        var declarationEntries = Enumerable.Range(1, declarations)
            .Select(index => new ToyAgeDeclarationEntry(
                $"declaration-{index}", ProductId, 36, "室内地板玩具车",
                "CUSTOMER_SUBMISSION", "contract-actor", Now))
            .ToArray();
        var assessmentEntries = Enumerable.Range(1, assessments)
            .Select(version => new ToyAccessibilityAssessmentEntry(
                $"assessment-{version}", ProductId, version, ToyAssessmentStages.Initial, null,
                ["shell", "wheels"], "contract-actor", Now))
            .ToArray();
        return new ToyProductOverview(
            ProductId,
            1 + declarations + decisions + (frozen ? 1 : 0) + assessments,
            ToyContract.RuleSetVersion,
            new ToyObjectContext("LEGAL-A", "LAB-A"),
            ToyDomain.ResolveEffectiveDecision(decisionEntries),
            declarationEntries, decisionEntries, assessmentEntries, [],
            ToyDomain.ResolveAccessibilityStatus([]));
    }

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new ToyDomainException(errorCode);
    }
}

internal sealed class ToyTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Toy.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
