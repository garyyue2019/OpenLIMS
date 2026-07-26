using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Billing;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Billing;

internal static class BillingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(BillingContract.CreateEvidencePath, CreateAsync)
            .WithName("createBillingEvidence").RequireAuthorization();
        endpoints.MapPost(BillingContract.AddAdjustmentPath, AddAdjustmentAsync)
            .WithName("addBillingAdjustment").RequireAuthorization();
        endpoints.MapGet(BillingContract.GetEvidencePath, GetAsync)
            .WithName("getBillingEvidence").RequireAuthorization();
        endpoints.MapGet(BillingContract.StatusPath, GetStatusAsync)
            .WithName("getBillingEvidenceStatus").RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context, IBillingEvidenceService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<CreateBillingEvidenceRequest>(context, cancellationToken);
        if (request is null) return Problem(BillingErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.CreateAsync(request, correlationId, cancellationToken);
            return Results.Json(result, BillingJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (BillingDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> AddAdjustmentAsync(
        string id, HttpContext context, IBillingEvidenceService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<AddBillingAdjustmentRequest>(context, cancellationToken);
        if (request is null) return Problem(BillingErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.AddAdjustmentAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, BillingJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (BillingDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> GetAsync(
        string id, HttpContext context, IBillingEvidenceService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(await service.GetAsync(id, correlationId, cancellationToken), BillingJson.Options);
        }
        catch (BillingDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> GetStatusAsync(
        string id,
        HttpContext context,
        ICurrentOrganizationContext organizationContext,
        IBillingEvidencePort port,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetSingle(context.Request.Query["ruleSetVersion"], out var ruleSetVersion))
            return Problem(BillingErrorCodes.ValidationFailed, correlationId);

        try
        {
            var result = await port.EvaluateAsync(new BillingEvidenceStatusRequest(
                organizationContext.Current.OrganizationGroupId, id, ruleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return Results.Json(result, BillingJson.Options);
        }
        catch (BillingDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static bool TryGetSingle(Microsoft.Extensions.Primitives.StringValues values, out string value)
    {
        value = values.Count == 1 ? values[0]?.Trim() ?? string.Empty : string.Empty;
        return value.Length is > 0 and <= 200;
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, BillingJson.Options, cancellationToken);
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
            BillingErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            BillingErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            BillingErrorCodes.DuplicateBilling => StatusCodes.Status409Conflict,
            BillingErrorCodes.EligibilityBlocked or
                BillingErrorCodes.ApplicabilityUnknown => StatusCodes.Status422UnprocessableEntity,
            BillingErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Billing request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId,
                ["gateSource"] = gateSource
            });
    }
}

internal static class BillingJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
