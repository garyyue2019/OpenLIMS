using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenLIMS.Api;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Modules.Allocation;
using OpenLIMS.Modules.Billing;
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
    new BillingModule(deploymentOptions.PostgresConnectionString!)
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
