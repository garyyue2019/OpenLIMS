using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenLIMS.Api;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Modules.Allocation;
using OpenLIMS.Modules.Billing;
using OpenLIMS.Modules.Instrument;
using OpenLIMS.Modules.Qc;
using OpenLIMS.Modules.Toy;
using OpenLIMS.Modules.Report;
using OpenLIMS.Modules.Batch;
using OpenLIMS.Modules.Labeling;
using OpenLIMS.Modules.Quantity;
using OpenLIMS.Modules.Receiving;
using OpenLIMS.Modules.Result;
using OpenLIMS.Modules.Scope;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var platformOptions = builder.Configuration.GetSection(PlatformOptions.SectionName).Get<PlatformOptions>();
if (!HasRequiredDeploymentConfiguration(platformOptions, builder.Environment))
{
    throw new InvalidOperationException(PlatformErrorCodes.ConfigurationInvalid);
}

var deploymentOptions = platformOptions!;
var labelPrinters = builder.Configuration.GetSection("Labeling:Printers").Get<LogicalLabelPrinter[]>() ?? [];
IOpenLimsServerModule[] modules =
[
    new ReceivingModule(deploymentOptions.PostgresConnectionString!),
    new LabelingModule(deploymentOptions.PostgresConnectionString!, labelPrinters),
    new ScopeModule(deploymentOptions.PostgresConnectionString!),
    new QuantityModule(deploymentOptions.PostgresConnectionString!),
    new AllocationModule(deploymentOptions.PostgresConnectionString!),
    new BatchModule(deploymentOptions.PostgresConnectionString!),
    new ResultModule(deploymentOptions.PostgresConnectionString!),
    new BillingModule(deploymentOptions.PostgresConnectionString!),
    new InstrumentModule(deploymentOptions.PostgresConnectionString!),
    new QcModule(deploymentOptions.PostgresConnectionString!),
    new ToyModule(deploymentOptions.PostgresConnectionString!),
    new ReportModule(deploymentOptions.PostgresConnectionString!)
];
var moduleCatalog = OpenLimsModuleCatalog.Create(modules);
var organizationScope = new OrganizationScope(deploymentOptions.OrganizationGroupId!);
var allowInsecureOidc = IsApprovedDevelopmentHttpEndpoint(
    deploymentOptions.OidcAuthority!,
    deploymentOptions.AllowInsecureDevelopmentOidc,
    builder.Environment);

builder.Services.AddSingleton<ICurrentOrganizationContext>(new DeploymentOrganizationContext(organizationScope));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActorContext, HttpCurrentActorContext>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IIdGenerator, GuidIdGenerator>();
builder.Services.AddPlatformDependencies(new PlatformDependencyOptions
{
    PostgresConnectionString = deploymentOptions.PostgresConnectionString!,
    OidcAuthority = deploymentOptions.OidcAuthority!,
    OidcAudience = deploymentOptions.OidcAudience!,
    ObjectStorageEndpoint = deploymentOptions.ObjectStorageEndpoint!,
    ObjectStorageBucket = deploymentOptions.ObjectStorageBucket!,
    ObjectStorageAccessKey = deploymentOptions.ObjectStorageAccessKey!,
    ObjectStorageSecretKey = deploymentOptions.ObjectStorageSecretKey!,
    PostgresCommandTimeoutSeconds = deploymentOptions.PostgresCommandTimeoutSeconds,
    OidcMetadataTimeoutSeconds = deploymentOptions.OidcMetadataTimeoutSeconds,
    ObjectStorageProbeTimeoutSeconds = deploymentOptions.ObjectStorageProbeTimeoutSeconds,
    DependencyProbeTimeoutSeconds = deploymentOptions.DependencyProbeTimeoutSeconds
});
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = deploymentOptions.OidcAuthority;
        options.Audience = deploymentOptions.OidcAudience;
        options.RequireHttpsMetadata = !allowInsecureOidc;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            NameClaimType = "sub"
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var correlationId = GetOrCreateCorrelationId(context.HttpContext);
                await WriteProblemAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    PlatformErrorCodes.AuthenticationRequired,
                    correlationId);
            },
            OnForbidden = async context =>
            {
                var correlationId = GetOrCreateCorrelationId(context.HttpContext);
                await WriteProblemAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    PlatformErrorCodes.AuthorizationForbidden,
                    correlationId);
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddOpenLimsModule(moduleCatalog);
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter("OpenLIMS.Receiving.IdentityAssessment")
        .AddMeter("OpenLIMS.Receiving.Exception"));

var app = builder.Build();
app.UseExceptionHandler(exceptionApp => exceptionApp.Run(async context =>
{
    var correlationId = GetOrCreateCorrelationId(context);
    await WriteProblemAsync(
        context,
        StatusCodes.Status500InternalServerError,
        PlatformErrorCodes.Unexpected,
        correlationId);
}));

app.Use(async (context, next) =>
{
    var requestedCorrelationId = context.Request.Headers[CorrelationId.HeaderName].SingleOrDefault();
    if (requestedCorrelationId is not null && !CorrelationIdPolicy.IsValid(requestedCorrelationId))
    {
        await WriteProblemAsync(
            context,
            StatusCodes.Status400BadRequest,
            PlatformErrorCodes.InvalidCorrelationId,
            CorrelationIdPolicy.Create().Value);
        return;
    }

    var correlationId = requestedCorrelationId ?? CorrelationIdPolicy.Create().Value;
    context.Items[CorrelationId.HeaderName] = correlationId;
    context.Response.Headers[CorrelationId.HeaderName] = correlationId;

    if (HasForbiddenGroupOverride(context.Request))
    {
        await WriteProblemAsync(
            context,
            StatusCodes.Status400BadRequest,
            PlatformErrorCodes.GroupContextOverrideForbidden,
            correlationId);
        return;
    }

    await next(context);
});

app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var groupClaim = context.User.FindFirst("organization_group")?.Value;
        if (string.IsNullOrWhiteSpace(groupClaim) ||
            !string.Equals(groupClaim, organizationScope.OrganizationGroupId, StringComparison.Ordinal))
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                PlatformErrorCodes.OrganizationGroupMismatch,
                GetOrCreateCorrelationId(context));
            return;
        }
    }

    await next(context);
});
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Json(new { status = "live" }));
app.MapGet("/health/ready", CheckReadinessAsync);
app.MapGet("/system/status", CheckReadinessAsync).RequireAuthorization();
app.MapGet("/openapi/v1.json", () => Results.Json(new
{
    openapi = "3.1.1",
    info = new { title = "OpenLIMS platform host", version = "1.0.0" },
    paths = new Dictionary<string, object>
    {
        ["/health/live"] = new { get = new { operationId = "getLiveness", responses = new { ok = new { description = "Process is alive" } } } },
        ["/health/ready"] = new { get = new { operationId = "getReadiness", responses = new { ok = new { description = "Platform host is ready" } } } },
        ["/system/status"] = new { get = new { operationId = "getAuthenticatedSystemStatus", responses = new { ok = new { description = "Authenticated platform status" } } } },
        ["/api/v1/receipts"] = new { post = new { operationId = "registerReceipt", responses = new { created = new { description = "Receipt, containers, and quarantined received items created" } } } },
        ["/api/v1/received-items/{id}/identity-assessment"] = new { get = new { operationId = "getIdentityAssessment", responses = new { ok = new { description = "Current declaration snapshot, observations, decisions, quarantine state, and versions" } } } },
        ["/api/v1/received-items/{id}/identity-observations"] = new { post = new { operationId = "createIdentityObservation", responses = new { created = new { description = "Append-only laboratory identity observation recorded" } } } },
        ["/api/v1/received-items/{id}/identity-decisions"] = new { post = new { operationId = "submitIdentityDecision", responses = new { created = new { description = "Append-only manual identity decision recorded without releasing quarantine" } } } },
        ["/api/v1/exceptions"] = new { post = new { operationId = "createReceivingException", responses = new { created = new { description = "Append-only receiving exception recorded without releasing quarantine" } } } },
        ["/api/v1/exceptions/{id}"] = new { get = new { operationId = "getReceivingException", responses = new { ok = new { description = "Receiving exception facts, state, versions, and decisions" } } } },
        ["/api/v1/exceptions/{id}/decisions"] = new { post = new { operationId = "submitReceivingExceptionDecision", responses = new { created = new { description = "Authorized exception decision recorded without releasing quarantine" } } } },
        ["/api/v1/received-items/{id}/release-decisions"] = new { post = new { operationId = "submitReceivingReleaseDecision", responses = new { created = new { description = "Immutable normal or constrained release decision recorded atomically" } } } },
        ["/api/v1/scope-matrices"] = new { post = new { operationId = "createScopeMatrix", responses = new { created = new { description = "Immutable approved scope matrix version created atomically" } } } },
        ["/api/v1/scope-matrices/{id}/versions"] = new { post = new { operationId = "reviseScopeMatrix", responses = new { created = new { description = "Append-only approved scope matrix revision created atomically" } } } },
        ["/api/v1/scope-matrices/{id}/versions/{version}"] = new { get = new { operationId = "getScopeMatrixVersion", responses = new { ok = new { description = "Immutable scope matrix version and complete scope lines" } } } },
        ["/api/v1/scope-matrices/{id}/production-eligibility"] = new { get = new { operationId = "getScopeProductionEligibility", responses = new { ok = new { description = "Version-pinned production eligibility decision" } } } },
        ["/api/v1/quantity-accounts"] = new { post = new { operationId = "createQuantityAccount", responses = new { created = new { description = "Immutable single-dimension quantity account created atomically" } } } },
        ["/api/v1/quantity-accounts/{id}/entries"] = new { post = new { operationId = "postQuantityEntry", responses = new { created = new { description = "Append-only quantity ledger entry posted atomically" } } } },
        ["/api/v1/quantity-accounts/{id}"] = new { get = new { operationId = "getQuantityAccount", responses = new { ok = new { description = "Quantity account configuration, balance, reserved, and available amounts" } } } },
        ["/api/v1/quantity-accounts/{id}/availability"] = new { get = new { operationId = "getQuantityAvailability", responses = new { ok = new { description = "Version-pinned availability decision" } } } },
        ["/api/v1/test-object-allocations"] = new { post = new { operationId = "createTestObjectAllocation", responses = new { created = new { description = "Immutable gate-pinned test object allocation created atomically" } } } },
        ["/api/v1/test-object-allocations/{id}/release"] = new { post = new { operationId = "releaseTestObjectAllocation", responses = new { created = new { description = "Append-only one-time allocation release recorded" } } } },
        ["/api/v1/test-object-allocations/{id}"] = new { get = new { operationId = "getTestObjectAllocation", responses = new { ok = new { description = "Immutable allocation fact with pinned gate decisions" } } } },
        ["/api/v1/test-object-allocations/{id}/status"] = new { get = new { operationId = "getAllocationStatus", responses = new { ok = new { description = "Version-pinned allocation status decision" } } } },
        ["/api/v1/batches"] = new { post = new { operationId = "createBatch", responses = new { created = new { description = "Immutable typed batch created atomically" } } } },
        ["/api/v1/batches/{id}/members"] = new { post = new { operationId = "addBatchMember", responses = new { created = new { description = "Allocation-gated specimen or approved QC member appended" } } } },
        ["/api/v1/batches/{id}/evidence"] = new { post = new { operationId = "addBatchEvidence", responses = new { created = new { description = "Immutable external evidence reference appended" } } } },
        ["/api/v1/batches/{id}/freeze"] = new { post = new { operationId = "freezeBatch", responses = new { created = new { description = "One-time whole-batch freeze event recorded" } } } },
        ["/api/v1/batches/{id}"] = new { get = new { operationId = "getBatch", responses = new { ok = new { description = "Immutable batch facts with members, evidence, and freeze state" } } } },
        ["/api/v1/batches/{id}/status"] = new { get = new { operationId = "getBatchStatus", responses = new { ok = new { description = "Version-pinned batch status decision" } } } },
        ["/api/v1/result-groups"] = new { post = new { operationId = "createResultGroup", responses = new { created = new { description = "Batch-gated immutable result group created atomically" } } } },
        ["/api/v1/result-groups/{id}/observations"] = new { post = new { operationId = "addResultObservation", responses = new { created = new { description = "Immutable typed observation with raw evidence reference appended" } } } },
        ["/api/v1/result-groups/{id}/derivations"] = new { post = new { operationId = "addResultDerivation", responses = new { created = new { description = "Append-only provenance derivation recorded" } } } },
        ["/api/v1/result-groups/{id}/adoption-rule"] = new { post = new { operationId = "recordAdoptionRule", responses = new { created = new { description = "Pre-retest adoption rule recorded append-only" } } } },
        ["/api/v1/result-groups/{id}/adoptions"] = new { post = new { operationId = "adoptResult", responses = new { created = new { description = "Strategy-checked adoption appended; latest version effective" } } } },
        ["/api/v1/result-groups/{id}"] = new { get = new { operationId = "getResultGroup", responses = new { ok = new { description = "Immutable result group with observations, provenance, rules, and adoptions" } } } },
        ["/api/v1/result-groups/{id}/adoption-status"] = new { get = new { operationId = "getResultAdoptionStatus", responses = new { ok = new { description = "Version-pinned adoption status decision" } } } },
        ["/api/v1/billing-evidence"] = new { post = new { operationId = "createBillingEvidence", responses = new { created = new { description = "Unique adoption-gated billing evidence created atomically" } } } },
        ["/api/v1/billing-evidence/{id}/adjustments"] = new { post = new { operationId = "addBillingAdjustment", responses = new { created = new { description = "Append-only signed adjustment recorded" } } } },
        ["/api/v1/billing-evidence/{id}"] = new { get = new { operationId = "getBillingEvidence", responses = new { ok = new { description = "Immutable billing evidence with adjustment chain" } } } },
        ["/api/v1/billing-evidence/{id}/status"] = new { get = new { operationId = "getBillingEvidenceStatus", responses = new { ok = new { description = "Rule-set-pinned billing evidence status decision" } } } },
        ["/api/v1/instrument-files"] = new { post = new { operationId = "registerInstrumentFile", responses = new { created = new { description = "Read-only instrument file registration with content hash and parser version" } } } },
        ["/api/v1/instrument-files/{id}/rows"] = new { post = new { operationId = "submitInstrumentRows", responses = new { created = new { description = "Parsed rows recorded with pre/post parse values; exceptions queued" } } } },
        ["/api/v1/instrument-files/{id}/exceptions/{exceptionId}/resolution"] = new { post = new { operationId = "resolveInstrumentImportException", responses = new { created = new { description = "Human confirmation recorded without altering raw values" } } } },
        ["/api/v1/instrument-files/{id}"] = new { get = new { operationId = "getInstrumentFile", responses = new { ok = new { description = "Immutable instrument file registration with rows and exceptions" } } } },
        ["/api/v1/instrument-files/{id}/import-status"] = new { get = new { operationId = "getInstrumentImportStatus", responses = new { ok = new { description = "Version-pinned instrument import status decision" } } } },
        ["/api/v1/qc-runs"] = new { post = new { operationId = "openQcRun", responses = new { created = new { description = "QC run opened with pinned method and QC rule set versions" } } } },
        ["/api/v1/qc-runs/{id}/results"] = new { post = new { operationId = "recordQcResult", responses = new { created = new { description = "Immutable QC rule result recorded" } } } },
        ["/api/v1/qc-runs/{id}/verdict"] = new { post = new { operationId = "recordQcVerdict", responses = new { created = new { description = "Run verdict derived from rule results" } } } },
        ["/api/v1/qc-runs/{id}/impact"] = new { post = new { operationId = "recordQcImpact", responses = new { created = new { description = "Full impact scope recorded for a failed run" } } } },
        ["/api/v1/qc-runs/{id}/deviation-approval"] = new { post = new { operationId = "recordQcDeviationApproval", responses = new { created = new { description = "Deviation approval recorded; it never lifts the block on its own" } } } },
        ["/api/v1/qc-runs/{id}/gates"] = new { post = new { operationId = "satisfyQcReleaseGate", responses = new { created = new { description = "One of the five release gates satisfied" } } } },
        ["/api/v1/qc-runs/{id}/release"] = new { post = new { operationId = "releaseQcBlock", responses = new { created = new { description = "Block released once all five gates are satisfied" } } } },
        ["/api/v1/qc-runs/{id}"] = new { get = new { operationId = "getQcRun", responses = new { ok = new { description = "QC run with results, impact scope, gates and deviations" } } } },
        ["/api/v1/qc-runs/{id}/reportability"] = new { get = new { operationId = "getQcReportability", responses = new { ok = new { description = "Version-pinned QC reportability decision for a target" } } } },
        ["/api/v1/toy/products/{id}/age-declarations"] = new { post = new { operationId = "recordToyAgeDeclaration", responses = new { created = new { description = "Customer age and intended-use claim recorded as its own fact" } } } },
        ["/api/v1/toy/products/{id}/age-grade-decisions"] = new { post = new { operationId = "recordToyAgeGradeDecision", responses = new { created = new { description = "Laboratory age grade determination appended as a new version" } } } },
        ["/api/v1/toy/products/{id}/age-grade-decisions/{versionNumber}/freeze"] = new { post = new { operationId = "freezeToyAgeGradeDecision", responses = new { ok = new { description = "Determination frozen into force; the previous one becomes superseded" } } } },
        ["/api/v1/toy/products/{id}/accessibility-assessments"] = new { post = new { operationId = "recordToyAccessibilityAssessment", responses = new { created = new { description = "Accessibility assessed for one stage; newly exposed parts raise scope reassessments" } } } },
        ["/api/v1/toy/products/{id}/reassessment-triggers/{triggerId}/resolution"] = new { post = new { operationId = "resolveToyReassessmentTrigger", responses = new { ok = new { description = "Reassessment trigger settled by an approved conclusion" } } } },
        ["/api/v1/toy/products/{id}/overview"] = new { get = new { operationId = "getToyProductOverview", responses = new { ok = new { description = "Effective determination, assessment history and outstanding reassessments" } } } },
        ["/api/v1/reports"] = new { post = new { operationId = "createReport", responses = new { created = new { description = "Report draft created" } } } },
        ["/api/v1/reports/{id}/lines"] = new { post = new { operationId = "addReportLine", responses = new { created = new { description = "Report line pinned to a current adoption with its full contribution chain" } } } },
        ["/api/v1/reports/{id}/gate-evaluation"] = new { post = new { operationId = "evaluateReportGate", responses = new { created = new { description = "Issuance gate evaluated across every upstream source with itemised blockers" } } } },
        ["/api/v1/reports/{id}/submit-for-approval"] = new { post = new { operationId = "submitReportForApproval", responses = new { created = new { description = "Report advanced to pending approval once the gate allowed it" } } } },
        ["/api/v1/reports/{id}"] = new { get = new { operationId = "getReport", responses = new { ok = new { description = "Immutable report with lines, contribution chain and gate evaluations" } } } },
        ["/api/v1/reports/{id}/issuance-gate"] = new { get = new { operationId = "getReportIssuanceGate", responses = new { ok = new { description = "Version-pinned issuance readiness with itemised blockers" } } } },
        ["/api/v1/reports/{id}/pending-content-hash"] = new { get = new { operationId = "getReportPendingContentHash", responses = new { ok = new { description = "Canonical content hash the next signature will bind to" } } } },
        ["/api/v1/reports/{id}/issuance"] = new { post = new { operationId = "issueReport", responses = new { created = new { description = "Controlled issuance binding re-authentication, signing intent and content hash" } } } },
        ["/api/v1/reports/{id}/controlled-actions"] = new { post = new { operationId = "performReportControlledAction", responses = new { created = new { description = "Correction, supplement, withdrawal, void or supersession recorded" } } } },
        ["/api/v1/reports/{id}/verification"] = new { get = new { operationId = "getReportVerification", responses = new { ok = new { description = "Current version, every historical state and supersession relationships" } } } },
        ["/api/v1/reports/{id}/versions/{versionNumber}"] = new { get = new { operationId = "getReportVersion", responses = new { ok = new { description = "That version own immutable snapshot and signature" } } } },
        ["/api/v1/label-jobs"] = new { post = new { operationId = "createLabelPrintJobs", responses = new { accepted = new { description = "Label print jobs accepted" } } } },
        ["/api/v1/label-jobs/{printJobId}"] = new { get = new { operationId = "getLabelPrintJob", responses = new { ok = new { description = "Label print job state" } } } },
        ["/api/v1/label-jobs/{printJobId}/reprint"] = new { post = new { operationId = "reprintLabel", responses = new { accepted = new { description = "Controlled reprint accepted" } } } },
        ["/api/v1/scans/resolve"] = new { post = new { operationId = "resolveLabelScan", responses = new { ok = new { description = "Authorized label scan resolution" } } } }
    }
}));
app.MapOpenLimsModuleEndpoints(moduleCatalog);
foreach (var module in moduleCatalog.Modules)
{
    app.Logger.LogInformation(
        "OpenLIMS module {ModuleId} contract {ContractVersion} registered for API host",
        module.Descriptor.ModuleId,
        module.Descriptor.ContractVersion);
}
app.Run();

static async Task<IResult> CheckReadinessAsync(
    HttpContext context,
    IPlatformDependencyProbe probe,
    CancellationToken cancellationToken)
{
    var ready = await probe.IsReadyAsync(cancellationToken);
    return ready
        ? Results.Json(new { status = "ready" })
        : Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Platform dependencies are not ready",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = PlatformErrorCodes.DependencyUnready,
                ["correlationId"] = GetOrCreateCorrelationId(context)
            });
}

static bool HasForbiddenGroupOverride(HttpRequest request) =>
    request.Headers.Keys.Any(key => string.Equals(key, "X-Organization-Group-Id", StringComparison.OrdinalIgnoreCase)) ||
    request.Headers.Keys.Any(key => string.Equals(key, "X-Organization-Group-Claim", StringComparison.OrdinalIgnoreCase)) ||
    request.Query.Keys.Any(key => string.Equals(key, "organizationGroupId", StringComparison.OrdinalIgnoreCase));

static string GetOrCreateCorrelationId(HttpContext context)
{
    var correlationId = context.Items[CorrelationId.HeaderName]?.ToString() ?? CorrelationIdPolicy.Create().Value;
    context.Items[CorrelationId.HeaderName] = correlationId;
    context.Response.Headers[CorrelationId.HeaderName] = correlationId;
    return correlationId;
}

static Task WriteProblemAsync(HttpContext context, int statusCode, string errorCode, string correlationId)
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers[CorrelationId.HeaderName] = correlationId;
    return Results.Problem(
        statusCode: statusCode,
        title: "Request could not be processed",
        extensions: new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode,
            ["correlationId"] = correlationId
        }).ExecuteAsync(context);
}

static bool HasRequiredDeploymentConfiguration(PlatformOptions? options, IHostEnvironment environment)
{
    if (options is null ||
        string.IsNullOrWhiteSpace(options.OrganizationGroupId) ||
        options.OrganizationGroupId.Length > 100 ||
        string.IsNullOrWhiteSpace(options.PostgresConnectionString) ||
        string.IsNullOrWhiteSpace(options.OidcAudience) ||
        string.IsNullOrWhiteSpace(options.ObjectStorageBucket) ||
        string.IsNullOrWhiteSpace(options.ObjectStorageAccessKey) ||
        string.IsNullOrWhiteSpace(options.ObjectStorageSecretKey) ||
        !IsValidTimeout(options.PostgresCommandTimeoutSeconds, 30) ||
        !IsValidTimeout(options.OidcMetadataTimeoutSeconds, 30) ||
        !IsValidTimeout(options.ObjectStorageProbeTimeoutSeconds, 30) ||
        !IsValidTimeout(options.DependencyProbeTimeoutSeconds, 60))
    {
        return false;
    }

    return IsApprovedEndpoint(options.OidcAuthority, options.AllowInsecureDevelopmentOidc, environment) &&
           IsApprovedEndpoint(options.ObjectStorageEndpoint, options.AllowInsecureDevelopmentObjectStorage, environment);
}

static bool IsValidTimeout(int value, int maximum) => value is > 0 && value <= maximum;

static bool IsApprovedEndpoint(string? value, bool allowDevelopmentHttp, IHostEnvironment environment)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
    {
        return false;
    }

    return uri.Scheme == Uri.UriSchemeHttps ||
           (uri.Scheme == Uri.UriSchemeHttp &&
            allowDevelopmentHttp &&
            environment.IsDevelopment() &&
            uri.IsLoopback);
}

static bool IsApprovedDevelopmentHttpEndpoint(
    string value,
    bool allowDevelopmentHttp,
    IHostEnvironment environment) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
    uri.Scheme == Uri.UriSchemeHttp &&
    allowDevelopmentHttp &&
    environment.IsDevelopment() &&
    uri.IsLoopback;

public partial class Program;
