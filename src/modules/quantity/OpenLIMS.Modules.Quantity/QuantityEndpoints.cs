using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Quantity;

namespace OpenLIMS.Modules.Quantity;

internal static class QuantityEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(QuantityContract.CreateAccountPath, CreateAsync)
            .WithName("createQuantityAccount")
            .RequireAuthorization();
        endpoints.MapPost(QuantityContract.PostEntryPath, PostEntryAsync)
            .WithName("postQuantityEntry")
            .RequireAuthorization();
        endpoints.MapGet(QuantityContract.GetAccountPath, GetAccountAsync)
            .WithName("getQuantityAccount")
            .RequireAuthorization();
        endpoints.MapGet(QuantityContract.AvailabilityPath, GetAvailabilityAsync)
            .WithName("getQuantityAvailability")
            .RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        IQuantityAccountService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<CreateQuantityAccountRequest>(context, cancellationToken);
        if (request is null) return Problem(QuantityErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.CreateAsync(request, correlationId, cancellationToken);
            return Results.Json(result, QuantityJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (QuantityDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> PostEntryAsync(
        string id,
        HttpContext context,
        IQuantityAccountService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<PostQuantityEntryRequest>(context, cancellationToken);
        if (request is null) return Problem(QuantityErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.PostEntryAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, QuantityJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (QuantityDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetAccountAsync(
        string id,
        HttpContext context,
        IQuantityAccountService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetAccountAsync(id, correlationId, cancellationToken),
                QuantityJson.Options);
        }
        catch (QuantityDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetAvailabilityAsync(
        string id,
        HttpContext context,
        ICurrentOrganizationContext organizationContext,
        IQuantityAvailabilityPort port,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetPositiveLong(context.Request.Query["expectedVersion"], out var expectedVersion) ||
            !TryGetSingle(context.Request.Query["ruleSetVersion"], out var ruleSetVersion) ||
            !TryGetPositiveDecimal(context.Request.Query["requestedAmount"], out var requestedAmount))
        {
            return Problem(QuantityErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await port.EvaluateAsync(new QuantityAvailabilityRequest(
                organizationContext.Current.OrganizationGroupId,
                id,
                expectedVersion,
                ruleSetVersion,
                requestedAmount)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return Results.Json(result, QuantityJson.Options);
        }
        catch (QuantityDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static bool TryGetSingle(Microsoft.Extensions.Primitives.StringValues values, out string value)
    {
        value = values.Count == 1 ? values[0]?.Trim() ?? string.Empty : string.Empty;
        return value.Length is > 0 and <= 200;
    }

    private static bool TryGetPositiveLong(Microsoft.Extensions.Primitives.StringValues values, out long value)
    {
        value = 0;
        return TryGetSingle(values, out var text) && long.TryParse(text, out value) && value > 0;
    }

    private static bool TryGetPositiveDecimal(Microsoft.Extensions.Primitives.StringValues values, out decimal value)
    {
        value = 0;
        return TryGetSingle(values, out var text) &&
               decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) &&
               value > 0;
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, QuantityJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string Correlation(HttpContext context) =>
        context.Items[CorrelationId.HeaderName]?.ToString() ?? Guid.NewGuid().ToString("N");

    private static IResult Problem(string errorCode, string correlationId)
    {
        var statusCode = errorCode switch
        {
            QuantityErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            QuantityErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            QuantityErrorCodes.ExpectedVersionConflict => StatusCodes.Status409Conflict,
            QuantityErrorCodes.InsufficientBalance or QuantityErrorCodes.DimensionMismatch or
                QuantityErrorCodes.NotQuantifiable or
                QuantityErrorCodes.ApplicabilityUnknown => StatusCodes.Status422UnprocessableEntity,
            QuantityErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Quantity account request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }
}

internal static class QuantityJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
