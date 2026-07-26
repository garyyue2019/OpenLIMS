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
using OpenLIMS.Contracts.Report;
using OpenLIMS.Modules.Report;
using Xunit;

namespace OpenLIMS.Report.ContractTests;

[Trait("Profile", "report")]
public sealed class ReportApiContractTests
{
    private const string ReportId = "00000000000000000000000000000110";
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Six_report_operations_expose_versioned_contracts()
    {
        using var factory = new ReportApiFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync(
            ReportContract.CreateReportPath, Report(), TestContext.Current.CancellationToken);
        using var line = await client.PostAsJsonAsync(
            $"/api/v1/reports/{ReportId}/lines", Line(1, 1), TestContext.Current.CancellationToken);
        using var gate = await client.PostAsJsonAsync(
            $"/api/v1/reports/{ReportId}/gate-evaluation",
            new EvaluateReportGateRequest(2, ReportContract.RuleSetVersion, "signatory-a"),
            TestContext.Current.CancellationToken);
        using var submitted = await client.PostAsJsonAsync(
            $"/api/v1/reports/{ReportId}/submit-for-approval",
            new SubmitReportForApprovalRequest(3, ReportContract.RuleSetVersion),
            TestContext.Current.CancellationToken);
        using var read = await client.GetAsync($"/api/v1/reports/{ReportId}", TestContext.Current.CancellationToken);
        using var issuance = await client.GetAsync(
            $"/api/v1/reports/{ReportId}/issuance-gate?expectedReportVersion=3&ruleSetVersion={Uri.EscapeDataString(ReportContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        foreach (var response in new[] { created, line, gate, submitted })
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, issuance.StatusCode);
        var decision = await issuance.Content.ReadFromJsonAsync<ReportIssuanceGateResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(decision);
        Assert.Equal(ReportContract.RuleSetVersion, decision.RuleSetVersion);
    }

    [Theory]
    [InlineData(ReportErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(ReportErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(ReportErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(ReportErrorCodes.DuplicateAttribution, HttpStatusCode.Conflict)]
    [InlineData(ReportErrorCodes.EligibilityBlocked, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReportErrorCodes.ApplicabilityUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReportErrorCodes.AccreditationBlocked, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReportErrorCodes.ConformityDecisionUnavailable, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReportErrorCodes.TraceIncomplete, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReportErrorCodes.ValidationFailed, HttpStatusCode.BadRequest)]
    [InlineData(ReportErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Report_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new ReportApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            ReportContract.CreateReportPath, Report(), TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_issuance_gate_query_is_rejected()
    {
        using var factory = new ReportApiFactory();
        using var client = factory.CreateClient();
        using var missingBoth = await client.GetAsync(
            $"/api/v1/reports/{ReportId}/issuance-gate", TestContext.Current.CancellationToken);
        using var missingVersion = await client.GetAsync(
            $"/api/v1/reports/{ReportId}/issuance-gate?ruleSetVersion={Uri.EscapeDataString(ReportContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);
        using var zeroVersion = await client.GetAsync(
            $"/api/v1/reports/{ReportId}/issuance-gate?expectedReportVersion=0&ruleSetVersion={Uri.EscapeDataString(ReportContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, missingBoth.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingVersion.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zeroVersion.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_report_operations()
    {
        using var factory = new ReportApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(ReportContract.CreateReportPath, content, StringComparison.Ordinal);
        foreach (var operation in new[]
        {
            "createReport", "addReportLine", "evaluateReportGate",
            "submitReportForApproval", "getReport", "getReportIssuanceGate"
        })
        {
            Assert.Contains(operation, content, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// RPT-CLAIM-001 at the contract surface: accreditation is expressed per
    /// line across six dimensions, and there is no report-level accredited flag
    /// anywhere in the contract for a caller to reach for.
    /// </summary>
    [Fact]
    public void Contract_has_no_report_level_accreditation_flag()
    {
        Assert.Equal(
            ["SITE", "METHOD_VERSION", "PRODUCT_MATRIX", "PARAMETER_RANGE", "VALIDITY", "SIGNATORY"],
            ReportAccreditationDimensions.All);

        var reportProperties = typeof(ReportResult).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(reportProperties, name =>
            name.Contains("Accredit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(typeof(ReportLineResult).GetProperties(), p => p.Name == "AccreditationClaim");
        Assert.Contains(typeof(ReportLineResult).GetProperties(), p => p.Name == "ClaimsAccreditation");
        Assert.Equal(5, ReportScopePartitions.All.Count);
    }

    /// <summary>
    /// RPT-GATE-002: each blocker carries its own object, rule version, reason
    /// and next steps — the shape itself forbids collapsing them into one flag.
    /// </summary>
    [Fact]
    public async Task Blockers_are_returned_itemised_over_http()
    {
        using var factory = new ReportApiFactory(blocked: true);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/v1/reports/{ReportId}/issuance-gate?expectedReportVersion=3&ruleSetVersion={Uri.EscapeDataString(ReportContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);
        var decision = await response.Content.ReadFromJsonAsync<ReportIssuanceGateResult>(TestContext.Current.CancellationToken);

        Assert.NotNull(decision);
        Assert.Equal(ReportGateDecisions.Blocked, decision.Decision);
        Assert.Equal(2, decision.Blockers.Count);
        Assert.All(decision.Blockers, blocker =>
        {
            Assert.False(string.IsNullOrWhiteSpace(blocker.ObjectRef));
            Assert.False(string.IsNullOrWhiteSpace(blocker.RuleSetVersion));
            Assert.False(string.IsNullOrWhiteSpace(blocker.ReasonCode));
            Assert.NotEmpty(blocker.AllowedNextSteps);
        });
        Assert.Contains(decision.Blockers, blocker => blocker.Source == ReportGateSources.QcReportability);
        Assert.Contains(decision.Blockers, blocker => blocker.Source == ReportGateSources.Accreditation);
    }

    private static CreateReportRequest Report() => new(
        ReportContract.RuleSetVersion,
        new ReportObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        "RPT-2026-0001");

    private static AddReportLineRequest Line(long expectedVersion, int lineNumber) => new(
        expectedVersion, ReportContract.RuleSetVersion, lineNumber, "GROUP-1", 4, "SCOPE-LINE-1",
        ReportScopePartitions.ActualTested,
        new ReportTraceReferences("BATCH-1", "ALLOC-1", "ITEM-1", new ReportVersionedReference("REQ-SNAPSHOT-1", 1)),
        new AccreditationScopeReference("ACC-CNAS-1", 2, new string('a', 64)),
        new AccreditationClaim(
            "LAB-A", new ReportVersionedReference("METHOD-TENSILE", 3), "RIGID-PLASTIC", "0-500N",
            Now.AddYears(1), "signatory-a"),
        [new ReportVersionedReference("QC-RUN-1", 6)], "INST-FILE-1", 3, 2, "SCOPE-MATRIX-1", 2, 3, 4);
}

internal sealed class ReportApiFactory(string? errorCode = null, bool blocked = false) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = ReportTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = ReportTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, ReportTestAuthenticationHandler>(
                    ReportTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IReportService>();
            services.RemoveAll<IReportIssuanceGatePort>();
            services.AddSingleton<IReportService>(new StubReportService(errorCode));
            services.AddSingleton<IReportIssuanceGatePort>(new StubReportIssuanceGatePort(errorCode, blocked));
        });
    }
}

internal sealed class StubReportService(string? errorCode) : IReportService
{
    private const string ReportId = "00000000000000000000000000000110";
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    public Task<ReportResult> CreateAsync(CreateReportRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Report(ReportStates.Draft));
    }

    public Task<ReportResult> AddLineAsync(string reportId, AddReportLineRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Report(ReportStates.Draft, lines: 1));
    }

    public Task<ReportResult> EvaluateGateAsync(string reportId, EvaluateReportGateRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Report(ReportStates.Draft, lines: 1, evaluations: 1));
    }

    public Task<ReportResult> SubmitForApprovalAsync(string reportId, SubmitReportForApprovalRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Report(ReportStates.PendingApproval, lines: 1, evaluations: 1, submitted: true));
    }

    public Task<ReportResult> GetAsync(string reportId, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(Report(ReportStates.PendingApproval, lines: 1, evaluations: 1, submitted: true));
    }

    private static ReportResult Report(
        string state, int lines = 0, int evaluations = 0, bool submitted = false) => new(
        ReportId, 1 + lines + evaluations + (submitted ? 1 : 0), state, ReportContract.RuleSetVersion,
        new ReportObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        "RPT-2026-0001",
        [.. Enumerable.Range(1, lines).Select(number => new ReportLineResult(
            $"line-{number}", ReportId, number, "GROUP-1", 4, "adopted-target-1", "RESULT@1.0.0",
            "SCOPE-LINE-1", ReportScopePartitions.ActualTested,
            new ReportTraceReferences("BATCH-1", "ALLOC-1", "ITEM-1", new ReportVersionedReference("REQ-SNAPSHOT-1", 1)),
            new ReportLineGateReferences([new ReportVersionedReference("QC-RUN-1", 6)], "INST-FILE-1", 3, "SCOPE-MATRIX-1", 2, 2, 3, 4),
            new AccreditationScopeReference("ACC-CNAS-1", 2, new string('a', 64)),
            new AccreditationClaim(
                "LAB-A", new ReportVersionedReference("METHOD-TENSILE", 3), "RIGID-PLASTIC", "0-500N",
                Now.AddYears(1), "signatory-a"),
            true, null, "contract-actor", Now))],
        [.. Enumerable.Range(1, evaluations).Select(_ => new ReportGateEvaluationResult(
            Guid.NewGuid().ToString("N"), ReportId, 2, ReportGateDecisions.Allowed, [], [],
            "signatory-a", "contract-actor", Now))],
        "contract-actor", Now);

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new ReportDomainException(errorCode);
    }
}

internal sealed class StubReportIssuanceGatePort(string? errorCode, bool blocked) : IReportIssuanceGatePort
{
    public ValueTask<ReportIssuanceGateResult> EvaluateAsync(
        ReportIssuanceGateRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new ReportDomainException(errorCode);
        if (!blocked)
        {
            return ValueTask.FromResult(new ReportIssuanceGateResult(
                ReportGateDecisions.Allowed, [], request.ReportId, request.ExpectedReportVersion,
                [], [], ReportContract.RuleSetVersion));
        }

        ReportBlocker[] blockers =
        [
            new("QC-RUN-1", "QcRun", ReportGateSources.QcReportability, "QC-IMPACT@1.0.0",
                ReportBlockerReasons.SourceBlocked, [ReportNextSteps.ReleaseQcBlock], 1),
            new("ACC-CNAS-1", "AccreditationScope", ReportGateSources.Accreditation, ReportContract.RuleSetVersion,
                ReportBlockerReasons.AccreditationOutOfScope, [ReportNextSteps.UpdateAccreditationReference], 2)
        ];
        return ValueTask.FromResult(new ReportIssuanceGateResult(
            ReportGateDecisions.Blocked,
            [.. blockers.Select(blocker => blocker.ReasonCode).Distinct(StringComparer.Ordinal)],
            request.ReportId, request.ExpectedReportVersion, blockers,
            [new ReportLineAccreditationVerdict(2, ReportAccreditationStatuses.NotAccredited, ["METHOD_VERSION"])],
            ReportContract.RuleSetVersion));
    }
}

internal sealed class ReportTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Report.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
