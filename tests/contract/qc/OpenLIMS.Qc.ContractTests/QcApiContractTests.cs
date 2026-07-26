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
using OpenLIMS.Contracts.Qc;
using OpenLIMS.Modules.Qc;
using Xunit;

namespace OpenLIMS.Qc.ContractTests;

[Trait("Profile", "qc")]
public sealed class QcApiContractTests
{
    private const string RunId = "00000000000000000000000000000100";

    [Fact]
    public async Task Nine_qc_operations_expose_versioned_contracts()
    {
        using var factory = new QcApiFactory();
        using var client = factory.CreateClient();
        using var opened = await client.PostAsJsonAsync(
            QcContract.CreateRunPath, Run(), TestContext.Current.CancellationToken);
        using var result = await client.PostAsJsonAsync(
            $"/api/v1/qc-runs/{RunId}/results",
            new AddQcResultRequest(1, QcContract.RuleSetVersion, new QcVersionedReference("RULE-BLANK", 1),
                QcControlTypes.Blank, "0.02", QcVerdicts.Fail, "blank exceeded tolerance"),
            TestContext.Current.CancellationToken);
        using var verdict = await client.PostAsJsonAsync(
            $"/api/v1/qc-runs/{RunId}/verdict",
            new RecordQcVerdictRequest(2, QcContract.RuleSetVersion), TestContext.Current.CancellationToken);
        using var impact = await client.PostAsJsonAsync(
            $"/api/v1/qc-runs/{RunId}/impact",
            new RecordQcImpactRequest(3, QcContract.RuleSetVersion,
                [new QcImpactTarget(QcImpactTargetTypes.ResultGroup, "GROUP-1", 3)]),
            TestContext.Current.CancellationToken);
        using var deviation = await client.PostAsJsonAsync(
            $"/api/v1/qc-runs/{RunId}/deviation-approval",
            new RecordQcDeviationApprovalRequest(4, QcContract.RuleSetVersion,
                new QcVersionedReference("DEV-1", 1), "approved"),
            TestContext.Current.CancellationToken);
        using var gate = await client.PostAsJsonAsync(
            $"/api/v1/qc-runs/{RunId}/gates",
            new SatisfyQcReleaseGateRequest(5, QcContract.RuleSetVersion,
                QcReleaseGateKinds.Investigation, new QcVersionedReference("INV-1", 1)),
            TestContext.Current.CancellationToken);
        using var release = await client.PostAsJsonAsync(
            $"/api/v1/qc-runs/{RunId}/release",
            new ReleaseQcBlockRequest(6, QcContract.RuleSetVersion), TestContext.Current.CancellationToken);
        using var read = await client.GetAsync($"/api/v1/qc-runs/{RunId}", TestContext.Current.CancellationToken);
        using var reportability = await client.GetAsync(
            $"/api/v1/qc-runs/{RunId}/reportability?expectedRunVersion=6&targetId=GROUP-1&ruleSetVersion={Uri.EscapeDataString(QcContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        foreach (var response in new[] { opened, result, verdict, impact, deviation, gate, release })
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reportability.StatusCode);
        var decision = await reportability.Content.ReadFromJsonAsync<QcReportabilityResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(decision);
        Assert.Equal(QcReportabilityDecisions.Blocked, decision.Decision);
        Assert.Equal(QcContract.RuleSetVersion, decision.RuleSetVersion);
        Assert.Equal(4, decision.OutstandingGates.Count);
    }

    [Theory]
    [InlineData(QcErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(QcErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(QcErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(QcErrorCodes.EligibilityBlocked, HttpStatusCode.UnprocessableEntity)]
    [InlineData(QcErrorCodes.ApplicabilityUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(QcErrorCodes.ReleaseGateIncomplete, HttpStatusCode.UnprocessableEntity)]
    [InlineData(QcErrorCodes.ValidationFailed, HttpStatusCode.BadRequest)]
    [InlineData(QcErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Qc_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new QcApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            QcContract.CreateRunPath, Run(), TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_reportability_query_is_rejected()
    {
        using var factory = new QcApiFactory();
        using var client = factory.CreateClient();
        using var missingAll = await client.GetAsync(
            $"/api/v1/qc-runs/{RunId}/reportability", TestContext.Current.CancellationToken);
        using var missingTarget = await client.GetAsync(
            $"/api/v1/qc-runs/{RunId}/reportability?expectedRunVersion=6&ruleSetVersion={Uri.EscapeDataString(QcContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);
        using var zeroVersion = await client.GetAsync(
            $"/api/v1/qc-runs/{RunId}/reportability?expectedRunVersion=0&targetId=GROUP-1&ruleSetVersion={Uri.EscapeDataString(QcContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, missingAll.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingTarget.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zeroVersion.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_qc_operations()
    {
        using var factory = new QcApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(QcContract.CreateRunPath, content, StringComparison.Ordinal);
        foreach (var operation in new[]
        {
            "openQcRun", "recordQcResult", "recordQcVerdict", "recordQcImpact",
            "recordQcDeviationApproval", "satisfyQcReleaseGate", "releaseQcBlock",
            "getQcRun", "getQcReportability"
        })
        {
            Assert.Contains(operation, content, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// AC-QC-001 at the contract surface: the five release gates are exactly the
    /// ones LAB-QC-003 names, and a deviation approval is not among them.
    /// </summary>
    [Fact]
    public void Release_gate_contract_excludes_deviation_approval()
    {
        Assert.Equal(
            [
                "INVESTIGATION", "IMPACT_SCOPE", "VALIDITY_DECISION", "ADOPTION_RULE", "TECHNICAL_REVIEW"
            ],
            QcReleaseGateKinds.Required);
        Assert.DoesNotContain("DEVIATION_APPROVAL", QcReleaseGateKinds.Required);
        Assert.Equal(5, QcReleaseGateKinds.Required.Distinct(StringComparer.Ordinal).Count());
    }

    private static CreateQcRunRequest Run() => new(
        QcContract.RuleSetVersion,
        new QcObjectContext("LEGAL-A", "LAB-A"),
        "00000000000000000000000000000040",
        2,
        new QcVersionedReference("METHOD-TENSILE", 3),
        new QcVersionedReference("QC-RULESET-TOY", 2));
}

internal sealed class QcApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = QcTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = QcTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, QcTestAuthenticationHandler>(
                    QcTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IQcRunService>();
            services.RemoveAll<IQcReportabilityPort>();
            services.AddSingleton<IQcRunService>(new StubQcRunService(errorCode));
            services.AddSingleton<IQcReportabilityPort>(new StubQcReportabilityPort(errorCode));
        });
    }
}

/// <summary>
/// Contract-level stub over the real rules: reportability and outstanding gates
/// are computed by production code so the HTTP surface reflects real semantics.
/// </summary>
internal sealed class StubQcRunService(string? errorCode) : IQcRunService
{
    private const string RunId = "00000000000000000000000000000100";
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    public Task<QcRunResult> OpenRunAsync(CreateQcRunRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Run(QcRunStates.Open));
    }

    public Task<QcRunResult> AddResultAsync(string qcRunId, AddQcResultRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Run(QcRunStates.Open, results: 1));
    }

    public Task<QcRunResult> RecordVerdictAsync(string qcRunId, RecordQcVerdictRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Run(QcRunStates.Failed, results: 1));
    }

    public Task<QcRunResult> RecordImpactAsync(string qcRunId, RecordQcImpactRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Run(QcRunStates.Failed, results: 1, impact: 1));
    }

    public Task<QcRunResult> RecordDeviationApprovalAsync(string qcRunId, RecordQcDeviationApprovalRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Run(QcRunStates.Failed, results: 1, impact: 1, deviations: 1));
    }

    public Task<QcRunResult> SatisfyGateAsync(string qcRunId, SatisfyQcReleaseGateRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Run(QcRunStates.Failed, results: 1, impact: 1, deviations: 1, gates: 1));
    }

    public Task<QcRunResult> ReleaseAsync(string qcRunId, ReleaseQcBlockRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Run(QcRunStates.Failed, results: 1, impact: 1, deviations: 1, gates: 1));
    }

    public Task<QcRunResult> GetAsync(string qcRunId, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Run(QcRunStates.Failed, results: 1, impact: 1, deviations: 1, gates: 1));
    }

    internal static QcRunResult Run(
        string state, int results = 0, int impact = 0, int deviations = 0, int gates = 0) => new(
        RunId,
        1 + results + (state == QcRunStates.Open ? 0 : 1) + impact + deviations + gates,
        state,
        QcContract.RuleSetVersion,
        new QcObjectContext("LEGAL-A", "LAB-A"),
        "00000000000000000000000000000040", 2,
        "ALLOWED", "BATCH-MANAGEMENT@1.0.0",
        new QcVersionedReference("METHOD-TENSILE", 3),
        new QcVersionedReference("QC-RULESET-TOY", 2),
        [.. Enumerable.Range(0, results).Select(_ => new QcResultEntry(
            "result-1", RunId, new QcVersionedReference("RULE-BLANK", 1), QcControlTypes.Blank,
            "0.02", QcVerdicts.Fail, "blank exceeded tolerance", "contract-actor", Now))],
        [.. Enumerable.Range(0, impact).Select(_ => new QcImpactEntry(
            "impact-1", RunId, QcImpactTargetTypes.ResultGroup, "GROUP-1", 3, "contract-actor", Now))],
        [.. Enumerable.Range(0, gates).Select(index => new QcReleaseGateEntry(
            $"gate-{index}", RunId, QcReleaseGateKinds.Required[index],
            new QcVersionedReference("INV-1", 1), "contract-actor", Now))],
        [.. Enumerable.Range(0, deviations).Select(_ => new QcDeviationApprovalEntry(
            "deviation-1", RunId, new QcVersionedReference("DEV-1", 1), "approved", "contract-actor", Now))],
        null, null, "contract-actor", Now);

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new QcDomainException(errorCode);
    }
}

internal sealed class StubQcReportabilityPort(string? errorCode) : IQcReportabilityPort
{
    public ValueTask<QcReportabilityResult> EvaluateAsync(
        QcReportabilityRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new QcDomainException(errorCode);
        var run = StubQcRunService.Run(QcRunStates.Failed, results: 1, impact: 1, deviations: 1, gates: 1);
        return ValueTask.FromResult(QcRules.EvaluateReportability(request with { ExpectedRunVersion = run.Version }, run));
    }
}

internal sealed class QcTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Qc.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
