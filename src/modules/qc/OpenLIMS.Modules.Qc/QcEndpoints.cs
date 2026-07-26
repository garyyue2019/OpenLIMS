using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Qc;

namespace OpenLIMS.Modules.Qc;

internal static class QcEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(QcContract.CreateRunPath, CreateAsync)
            .WithName("openQcRun").RequireAuthorization();
        endpoints.MapPost(QcContract.AddResultPath, AddResultAsync)
            .WithName("recordQcResult").RequireAuthorization();
        endpoints.MapPost(QcContract.VerdictPath, RecordVerdictAsync)
            .WithName("recordQcVerdict").RequireAuthorization();
        endpoints.MapPost(QcContract.ImpactPath, RecordImpactAsync)
            .WithName("recordQcImpact").RequireAuthorization();
        endpoints.MapPost(QcContract.DeviationApprovalPath, RecordDeviationApprovalAsync)
            .WithName("recordQcDeviationApproval").RequireAuthorization();
        endpoints.MapPost(QcContract.GatePath, SatisfyGateAsync)
            .WithName("satisfyQcReleaseGate").RequireAuthorization();
        endpoints.MapPost(QcContract.ReleasePath, ReleaseAsync)
            .WithName("releaseQcBlock").RequireAuthorization();
        endpoints.MapGet(QcContract.GetRunPath, GetAsync)
            .WithName("getQcRun").RequireAuthorization();
        endpoints.MapGet(QcContract.ReportabilityPath, GetReportabilityAsync)
            .WithName("getQcReportability").RequireAuthorization();
    }

    private static Task<IResult> CreateAsync(
        HttpContext context, IQcRunService service, CancellationToken cancellationToken) =>
        PostAsync<CreateQcRunRequest>(context, cancellationToken,
            (request, correlationId) => service.OpenRunAsync(request, correlationId, cancellationToken));

    private static Task<IResult> AddResultAsync(
        string id, HttpContext context, IQcRunService service, CancellationToken cancellationToken) =>
        PostAsync<AddQcResultRequest>(context, cancellationToken,
            (request, correlationId) => service.AddResultAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> RecordVerdictAsync(
        string id, HttpContext context, IQcRunService service, CancellationToken cancellationToken) =>
        PostAsync<RecordQcVerdictRequest>(context, cancellationToken,
            (request, correlationId) => service.RecordVerdictAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> RecordImpactAsync(
        string id, HttpContext context, IQcRunService service, CancellationToken cancellationToken) =>
        PostAsync<RecordQcImpactRequest>(context, cancellationToken,
            (request, correlationId) => service.RecordImpactAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> RecordDeviationApprovalAsync(
        string id, HttpContext context, IQcRunService service, CancellationToken cancellationToken) =>
        PostAsync<RecordQcDeviationApprovalRequest>(context, cancellationToken,
            (request, correlationId) => service.RecordDeviationApprovalAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> SatisfyGateAsync(
        string id, HttpContext context, IQcRunService service, CancellationToken cancellationToken) =>
        PostAsync<SatisfyQcReleaseGateRequest>(context, cancellationToken,
            (request, correlationId) => service.SatisfyGateAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> ReleaseAsync(
        string id, HttpContext context, IQcRunService service, CancellationToken cancellationToken) =>
        PostAsync<ReleaseQcBlockRequest>(context, cancellationToken,
            (request, correlationId) => service.ReleaseAsync(id, request, correlationId, cancellationToken));

    private static async Task<IResult> GetAsync(
        string id, HttpContext context, IQcRunService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(await service.GetAsync(id, correlationId, cancellationToken), QcJson.Options);
        }
        catch (QcDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> GetReportabilityAsync(
        string id,
        HttpContext context,
        ICurrentOrganizationContext organizationContext,
        IQcReportabilityPort port,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetSingle(context.Request.Query["ruleSetVersion"], out var ruleSetVersion) ||
            !TryGetSingle(context.Request.Query["targetId"], out var targetId) ||
            !TryGetVersion(context.Request.Query["expectedRunVersion"], out var expectedRunVersion))
        {
            return Problem(QcErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await port.EvaluateAsync(new QcReportabilityRequest(
                organizationContext.Current.OrganizationGroupId, id, expectedRunVersion, ruleSetVersion, targetId)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return Results.Json(result, QcJson.Options);
        }
        catch (QcDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> PostAsync<TRequest>(
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, string, Task<QcRunResult>> handle)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<TRequest>(context, cancellationToken);
        if (request is null) return Problem(QcErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await handle(request, correlationId);
            return Results.Json(result, QcJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (QcDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static bool TryGetSingle(Microsoft.Extensions.Primitives.StringValues values, out string value)
    {
        value = values.Count == 1 ? values[0]?.Trim() ?? string.Empty : string.Empty;
        return value.Length is > 0 and <= 200;
    }

    private static bool TryGetVersion(Microsoft.Extensions.Primitives.StringValues values, out long value)
    {
        value = 0;
        return TryGetSingle(values, out var raw) && long.TryParse(raw, out value) && value > 0;
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, QcJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string Correlation(HttpContext context) =>
        context.Items[CorrelationId.HeaderName]?.ToString() ?? Guid.NewGuid().ToString("N");

    private static IResult Problem(string errorCode, string correlationId, string? gateSource = null)
    {
        var statusCode = errorCode switch
        {
            QcErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            QcErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            QcErrorCodes.ExpectedVersionConflict => StatusCodes.Status409Conflict,
            QcErrorCodes.EligibilityBlocked or
                QcErrorCodes.ApplicabilityUnknown or
                QcErrorCodes.ReleaseGateIncomplete => StatusCodes.Status422UnprocessableEntity,
            QcErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "QC request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId,
                ["gateSource"] = gateSource
            });
    }
}

internal static class QcJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
