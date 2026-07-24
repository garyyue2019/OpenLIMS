using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Labeling;

internal static class LabelingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(LabelingContract.CreateJobsPath, CreateAsync).RequireAuthorization();
        endpoints.MapGet(LabelingContract.JobPath, GetAsync).RequireAuthorization();
        endpoints.MapPost(LabelingContract.ReprintPath, ReprintAsync).RequireAuthorization();
        endpoints.MapPost(LabelingContract.ResolveScanPath, ResolveScanAsync).RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        ILabelingService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetIdempotencyKey(context.Request.Headers, out var idempotencyKey))
        {
            return Problem(StatusCodes.Status400BadRequest, LabelingErrorCodes.ValidationFailed, correlationId);
        }

        var request = await ReadAsync<CreateLabelJobsRequest>(context, cancellationToken);
        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, LabelingErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await service.CreateAsync(request, idempotencyKey, correlationId, cancellationToken);
            return Results.Json(result, LabelingJson.Options, statusCode: StatusCodes.Status202Accepted);
        }
        catch (LabelingDomainException exception)
        {
            return FromDomain(exception, correlationId);
        }
    }

    private static async Task<IResult> GetAsync(
        string printJobId,
        HttpContext context,
        ILabelingService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetAsync(printJobId, cancellationToken),
                LabelingJson.Options);
        }
        catch (LabelingDomainException exception)
        {
            return FromDomain(exception, correlationId);
        }
    }

    private static async Task<IResult> ReprintAsync(
        string printJobId,
        HttpContext context,
        ILabelingService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetIdempotencyKey(context.Request.Headers, out var idempotencyKey))
        {
            return Problem(StatusCodes.Status400BadRequest, LabelingErrorCodes.ValidationFailed, correlationId);
        }

        var request = await ReadAsync<ReprintLabelRequest>(context, cancellationToken);
        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, LabelingErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await service.ReprintAsync(
                printJobId,
                request,
                idempotencyKey,
                correlationId,
                cancellationToken);
            return Results.Json(result, LabelingJson.Options, statusCode: StatusCodes.Status202Accepted);
        }
        catch (LabelingDomainException exception)
        {
            return FromDomain(exception, correlationId);
        }
    }

    private static async Task<IResult> ResolveScanAsync(
        HttpContext context,
        ILabelingService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadAsync<ResolveLabelScanRequest>(context, cancellationToken);
        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, LabelingErrorCodes.BarcodeInvalid, correlationId);
        }

        try
        {
            return Results.Json(
                await service.ResolveScanAsync(request, correlationId, cancellationToken),
                LabelingJson.Options);
        }
        catch (LabelingDomainException exception)
        {
            return FromDomain(exception, correlationId);
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, LabelingJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static IResult FromDomain(LabelingDomainException exception, string correlationId) =>
        exception.ErrorCode switch
        {
            LabelingErrorCodes.ObjectNotAccessible => Problem(StatusCodes.Status403Forbidden, exception.ErrorCode, correlationId),
            LabelingErrorCodes.PrinterScopeMismatch => Problem(StatusCodes.Status403Forbidden, exception.ErrorCode, correlationId),
            LabelingErrorCodes.IdempotencyConflict => Problem(StatusCodes.Status409Conflict, exception.ErrorCode, correlationId),
            LabelingErrorCodes.ReprintLimitOverrideRequired => Problem(StatusCodes.Status409Conflict, exception.ErrorCode, correlationId),
            LabelingErrorCodes.DeliveryUnknown => Problem(StatusCodes.Status409Conflict, exception.ErrorCode, correlationId),
            LabelingErrorCodes.PersistenceUnavailable => Problem(StatusCodes.Status503ServiceUnavailable, exception.ErrorCode, correlationId),
            LabelingErrorCodes.BarcodeVersionUnsupported => Problem(StatusCodes.Status422UnprocessableEntity, exception.ErrorCode, correlationId),
            _ => Problem(StatusCodes.Status400BadRequest, exception.ErrorCode, correlationId)
        };

    private static bool TryGetIdempotencyKey(IHeaderDictionary headers, out string value)
    {
        value = string.Empty;
        if (!headers.TryGetValue(LabelingContract.IdempotencyHeader, out StringValues values) || values.Count != 1)
        {
            return false;
        }

        value = values[0] ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 200;
    }

    private static string Correlation(HttpContext context) =>
        context.Items[CorrelationId.HeaderName]?.ToString() ?? Guid.NewGuid().ToString("N");

    private static IResult Problem(int statusCode, string errorCode, string correlationId) =>
        Results.Problem(
            statusCode: statusCode,
            title: "Label operation could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
}
