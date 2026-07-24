using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal static class ReceivingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ReceivingContract.RegisterReceiptPath, RegisterAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context,
        IReceiptRegistrationService service,
        CancellationToken cancellationToken)
    {
        var correlationId = context.Items[CorrelationId.HeaderName]?.ToString() ?? Guid.NewGuid().ToString("N");
        if (!TryGetIdempotencyKey(context.Request.Headers, out var idempotencyKey))
        {
            return Problem(StatusCodes.Status400BadRequest, ReceivingErrorCodes.ValidationFailed, correlationId);
        }

        RegisterReceiptRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<RegisterReceiptRequest>(
                context.Request.Body,
                ReceivingJson.Options,
                cancellationToken);
        }
        catch (JsonException)
        {
            return Problem(StatusCodes.Status400BadRequest, ReceivingErrorCodes.ValidationFailed, correlationId);
        }

        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, ReceivingErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await service.RegisterAsync(request, idempotencyKey, correlationId, cancellationToken);
            return Results.Json(result, ReceivingJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (ReceivingDomainException exception)
        {
            return exception.ErrorCode switch
            {
                ReceivingErrorCodes.AuthorizationDenied => Problem(StatusCodes.Status403Forbidden, exception.ErrorCode, correlationId),
                ReceivingErrorCodes.ServiceOrderNotReceivable => Problem(StatusCodes.Status409Conflict, exception.ErrorCode, correlationId),
                ReceivingErrorCodes.IdempotencyConflict => Problem(StatusCodes.Status409Conflict, exception.ErrorCode, correlationId),
                ReceivingErrorCodes.IdentityGranularityUnresolved => Problem(StatusCodes.Status422UnprocessableEntity, exception.ErrorCode, correlationId),
                ReceivingErrorCodes.PersistenceUnavailable => Problem(StatusCodes.Status503ServiceUnavailable, exception.ErrorCode, correlationId),
                _ => Problem(StatusCodes.Status400BadRequest, ReceivingErrorCodes.ValidationFailed, correlationId)
            };
        }
    }

    private static bool TryGetIdempotencyKey(IHeaderDictionary headers, out string value)
    {
        value = string.Empty;
        if (!headers.TryGetValue(ReceivingContract.IdempotencyHeader, out StringValues values) || values.Count != 1)
        {
            return false;
        }

        value = values[0] ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 200;
    }

    private static IResult Problem(int statusCode, string errorCode, string correlationId) =>
        Results.Problem(
            statusCode: statusCode,
            title: "Receipt registration could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
}

public interface IReceiptRegistrationService
{
    Task<ReceiptRegistrationResult> RegisterAsync(
        RegisterReceiptRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default);
}
