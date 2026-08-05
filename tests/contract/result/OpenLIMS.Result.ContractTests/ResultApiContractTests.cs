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
using OpenLIMS.Contracts.Result;
using OpenLIMS.Modules.Result;
using Xunit;

namespace OpenLIMS.Result.ContractTests;

[Trait("Profile", "result")]
public sealed class ResultApiContractTests
{
    private const string GroupId = "00000000000000000000000000000070";

    [Fact]
    public async Task Ten_result_operations_expose_versioned_contracts()
    {
        using var factory = new ResultApiFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync(
            ResultContract.CreateGroupPath, GroupRequest(), TestContext.Current.CancellationToken);
        using var observation = await client.PostAsJsonAsync(
            $"/api/v1/result-groups/{GroupId}/observations", ObservationRequest(), TestContext.Current.CancellationToken);
        using var derivation = await client.PostAsJsonAsync(
            $"/api/v1/result-groups/{GroupId}/derivations", DerivationRequest(), TestContext.Current.CancellationToken);
        using var calculation = await client.PostAsJsonAsync(
            $"/api/v1/result-groups/{GroupId}/calculations", CalculationRequest(), TestContext.Current.CancellationToken);
        using var rule = await client.PostAsJsonAsync(
            $"/api/v1/result-groups/{GroupId}/adoption-rule", RuleRequest(), TestContext.Current.CancellationToken);
        using var adoption = await client.PostAsJsonAsync(
            $"/api/v1/result-groups/{GroupId}/adoptions", AdoptRequest(), TestContext.Current.CancellationToken);
        using var executionAccreditation = await client.PostAsJsonAsync(
            $"/api/v1/result-groups/{GroupId}/accreditation-assessments",
            AccreditationRequest(ResultAccreditationStages.Execution, null, 6),
            TestContext.Current.CancellationToken);
        using var resultAccreditation = await client.PostAsJsonAsync(
            $"/api/v1/result-groups/{GroupId}/accreditation-assessments",
            AccreditationRequest(ResultAccreditationStages.Result, "00000000000000000000000000000071", 7),
            TestContext.Current.CancellationToken);
        using var read = await client.GetAsync(
            $"/api/v1/result-groups/{GroupId}", TestContext.Current.CancellationToken);
        using var status = await client.GetAsync(
            $"/api/v1/result-groups/{GroupId}/adoption-status?expectedVersion=6&ruleSetVersion={Uri.EscapeDataString(ResultContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);
        using var accreditation = await client.GetAsync(
            $"/api/v1/result-groups/{GroupId}/accreditation-eligibility?expectedVersion=8&ruleSetVersion={Uri.EscapeDataString(ResultContract.AccreditationRuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Created, observation.StatusCode);
        Assert.Equal(HttpStatusCode.Created, derivation.StatusCode);
        Assert.Equal(HttpStatusCode.Created, calculation.StatusCode);
        Assert.Equal(HttpStatusCode.Created, rule.StatusCode);
        Assert.Equal(HttpStatusCode.Created, adoption.StatusCode);
        Assert.Equal(HttpStatusCode.Created, executionAccreditation.StatusCode);
        Assert.Equal(HttpStatusCode.Created, resultAccreditation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accreditation.StatusCode);
        var gate = await status.Content.ReadFromJsonAsync<ResultAdoptionStatusResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(gate);
        Assert.Equal(ResultAdoptionDecisions.Allowed, gate.Decision);
        var accreditationGate = await accreditation.Content.ReadFromJsonAsync<ResultAccreditationEligibilityResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(accreditationGate);
        Assert.Equal(ResultAccreditationDecisions.Eligible, accreditationGate.Decision);
    }

    [Theory]
    [InlineData(ResultErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(ResultErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(ResultErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(ResultErrorCodes.EligibilityBlocked, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ResultErrorCodes.ApplicabilityUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ResultErrorCodes.AdoptionRuleRequired, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ResultErrorCodes.AdoptionStrategyViolation, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ResultErrorCodes.CalculationFailed, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ResultErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Result_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new ResultApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            ResultContract.CreateGroupPath, GroupRequest(), TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_adoption_status_query_is_rejected()
    {
        using var factory = new ResultApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/v1/result-groups/{GroupId}/adoption-status?expectedVersion=latest",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_accreditation_query_is_rejected()
    {
        using var factory = new ResultApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/v1/result-groups/{GroupId}/accreditation-eligibility?expectedVersion=latest",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_result_operations()
    {
        using var factory = new ResultApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(ResultContract.CreateGroupPath, content, StringComparison.Ordinal);
        foreach (var operation in new[]
        {
            "createResultGroup", "addResultObservation", "addResultDerivation",
            "executeResultCalculation", "recordAdoptionRule", "adoptResult",
            "recordResultAccreditationAssessment", "getResultGroup", "getResultAdoptionStatus",
            "getResultAccreditationEligibility"
        })
        {
            Assert.Contains(operation, content, StringComparison.Ordinal);
        }
    }

    private static ResultObjectContext ObjectScope() => new("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS");

    private static CreateResultGroupRequest GroupRequest() => new(
        ResultContract.RuleSetVersion, ObjectScope(),
        "00000000000000000000000000000050", 2, "MEMBER-1",
        new ResultVersionedReference("ITEM-PB", 1), new string('c', 64));

    private static AddResultObservationRequest ObservationRequest() => new(
        1, ResultContract.RuleSetVersion, ResultObservationKinds.Initial, "12.5", "MG-KG",
        new ResultEvidence(ResultEvidenceSources.Cds, new ResultVersionedReference("CDS-SEQ-1", 1), new string('a', 64), "PARSER-2.1"));

    private static AddResultDerivationRequest DerivationRequest() => new(
        2, ResultContract.RuleSetVersion, new ResultVersionedReference("AGG-MEAN", 1), "12.5", "MG-KG",
        [new ResultDerivationInput("00000000000000000000000000000071", true)]);

    private static ExecuteResultCalculationRequest CalculationRequest() => new(
        3,
        ResultContract.CalculationRuleSetVersion,
        [new ResultCalculationInput("00000000000000000000000000000071", 1m)],
        CalculationRule());

    private static ResultCalculationRule CalculationRule() => new(
        new ResultVersionedReference("CALC-1", 1),
        new ResultVersionedReference("UNIT-1", 1),
        "MG-KG", "MG-KG", 1m, 0m, 1m, 1m, 2,
        ResultRoundingModes.ToEven, 1m, 2m,
        ResultLimitOperators.LessThanOrEqual, ResultLimitEvaluationBases.Exact,
        null, 20m);

    private static RecordAdoptionRuleRequest RuleRequest() => new(
        4, ResultContract.RuleSetVersion, ResultAdoptionStrategies.TechnicalReviewSelects,
        new ResultVersionedReference("RULE-1", 1));

    private static AdoptResultRequest AdoptRequest() => new(
        5, ResultContract.RuleSetVersion, "00000000000000000000000000000071",
        new ResultVersionedReference("REVIEW-1", 1));

    private static RecordResultAccreditationAssessmentRequest AccreditationRequest(
        string stage,
        string? targetId,
        long expectedVersion) => new(
        expectedVersion,
        ResultContract.AccreditationRuleSetVersion,
        stage,
        targetId,
        new ResultVersionedReference("ACC-1", 1),
        new ResultVersionedReference("METHOD-1", 1),
        "LAB-A", "TOYS", "ITEM-PB", "MG-KG", 0m, 20m,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
        ["contract-actor"]);
}

internal sealed class ResultApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = ResultTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = ResultTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, ResultTestAuthenticationHandler>(
                    ResultTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IResultGroupService>();
            services.RemoveAll<IResultAdoptionPort>();
            services.RemoveAll<IResultAccreditationEligibilityPort>();
            services.AddSingleton<IResultGroupService>(new StubResultGroupService(errorCode));
            services.AddSingleton<IResultAdoptionPort>(new StubResultAdoptionPort(errorCode));
            services.AddSingleton<IResultAccreditationEligibilityPort>(new StubResultAccreditationPort(errorCode));
        });
    }
}

internal sealed class StubResultGroupService(string? errorCode) : IResultGroupService
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);

    public Task<ResultGroupResult> CreateGroupAsync(CreateResultGroupRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new ResultGroupResult(
            "00000000000000000000000000000070", 1, ResultContract.RuleSetVersion, request.ObjectScope,
            request.BatchId, request.ExpectedBatchVersion, "ALLOWED", "BATCH-EXECUTION@1.0.0",
            request.MemberId, request.TestItem, request.ScopeLineId, [], [], [], [], "contract-actor", Now));
    }

    public Task<ResultObservationResult> AddObservationAsync(string resultGroupId, AddResultObservationRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new ResultObservationResult(
            "00000000000000000000000000000071", resultGroupId, request.ExpectedCurrentVersion + 1,
            request.Kind, request.Value, request.Unit, request.Evidence,
            request.TriggerReason, request.ApprovalRef, "contract-actor", Now));
    }

    public Task<ResultDerivationResult> AddDerivationAsync(string resultGroupId, AddResultDerivationRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new ResultDerivationResult(
            "00000000000000000000000000000072", resultGroupId, request.ExpectedCurrentVersion + 1,
            request.AggregationRule, request.Value, request.Unit, request.Inputs, "contract-actor", Now));
    }

    public Task<ResultCalculationResult> ExecuteCalculationAsync(string resultGroupId, ExecuteResultCalculationRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new ResultCalculationResult(
            "00000000000000000000000000000073", resultGroupId, request.ExpectedCurrentVersion + 1,
            [new ResultCalculationResolvedInput(request.Inputs[0].TargetId, 12.5m, request.Rule.InputUnit, request.Inputs[0].Coefficient)],
            request.Rule, 12.5m, 12.5m, "12.5", request.Rule.OutputUnit,
            ResultDetectionQualifications.Quantified, ResultLimitDecisions.Pass, "contract-actor", Now));
    }

    public Task<AdoptionRuleResult> RecordAdoptionRuleAsync(string resultGroupId, RecordAdoptionRuleRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new AdoptionRuleResult(
            resultGroupId, request.ExpectedCurrentVersion + 1, 1, request.Strategy, request.RuleRef, "contract-actor", Now));
    }

    public Task<ResultAdoptionResult> AdoptAsync(string resultGroupId, AdoptResultRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new ResultAdoptionResult(
            resultGroupId, request.ExpectedCurrentVersion + 1, 1, request.TargetId, 1,
            request.ReviewApprovalRef, "contract-actor", Now));
    }

    public Task<ResultAccreditationAssessmentResult> RecordAccreditationAssessmentAsync(string resultGroupId, RecordResultAccreditationAssessmentRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new ResultAccreditationAssessmentResult(
            Guid.NewGuid().ToString("N"), resultGroupId, request.ExpectedCurrentVersion + 1,
            request.Stage, request.TargetId, request.Accreditation, request.Method,
            request.SiteId, request.ProductOrMatrix, request.Parameter, request.RangeUnit,
            request.RangeLower, request.RangeUpper, request.ValidFrom, request.ValidTo,
            request.AuthorizedActorIds, ResultAccreditationDecisions.Eligible, [], "contract-actor", Now));
    }

    public Task<ResultGroupResult> GetAsync(string resultGroupId, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new ResultGroupResult(
            resultGroupId, 5, ResultContract.RuleSetVersion,
            new ResultObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
            "00000000000000000000000000000050", 2, "ALLOWED", "BATCH-EXECUTION@1.0.0",
            "MEMBER-1", new ResultVersionedReference("ITEM-PB", 1), new string('c', 64),
            [], [], [], [], "contract-actor", Now));
    }

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new ResultDomainException(errorCode);
    }
}

internal sealed class StubResultAdoptionPort(string? errorCode) : IResultAdoptionPort
{
    public ValueTask<ResultAdoptionStatusResult> EvaluateAsync(ResultAdoptionStatusRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new ResultDomainException(errorCode);
        return ValueTask.FromResult(new ResultAdoptionStatusResult(
            ResultAdoptionDecisions.Allowed, [], request.ResultGroupId, request.ExpectedGroupVersion,
            "00000000000000000000000000000071", 1, ResultContract.RuleSetVersion));
    }
}

internal sealed class StubResultAccreditationPort(string? errorCode) : IResultAccreditationEligibilityPort
{
    public ValueTask<ResultAccreditationEligibilityResult> EvaluateAsync(
        ResultAccreditationEligibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new ResultDomainException(errorCode);
        return ValueTask.FromResult(new ResultAccreditationEligibilityResult(
            ResultAccreditationDecisions.Eligible,
            [],
            request.ResultGroupId,
            request.ExpectedGroupVersion,
            "00000000000000000000000000000081",
            "00000000000000000000000000000082",
            "00000000000000000000000000000071",
            ResultContract.AccreditationRuleSetVersion));
    }
}

internal sealed class ResultTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Result.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
