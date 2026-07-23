using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenLIMS.Api;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
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
builder.Services.AddOpenTelemetry().WithTracing(tracing =>
    tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());

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
        ["/system/status"] = new { get = new { operationId = "getAuthenticatedSystemStatus", responses = new { ok = new { description = "Authenticated platform status" } } } }
    }
}));
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
