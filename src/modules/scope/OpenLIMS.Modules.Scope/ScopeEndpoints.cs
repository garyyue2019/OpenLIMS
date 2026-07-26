using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Scope;

namespace OpenLIMS.Modules.Scope;

internal static class ScopeEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ScopeContract.CreateMatrixPath, CreateAsync)
            .WithName("createScopeMatrix")
            .RequireAuthorization();
        endpoints.MapPost(ScopeContract.CreateVersionPath, ReviseAsync)
            .WithName("reviseScopeMatrix")
            .RequireAuthorization();
        endpoints.MapGet(ScopeContract.GetVersionPath, GetVersionAsync)
            .WithName("getScopeMatrixVersion")
            .RequireAuthorization();
        endpoints.MapGet(ScopeContract.EligibilityPath, GetEligibilityAsync)
            .WithName("getScopeProductionEligibility")
            .RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        IScopeMatrixService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<SubmitScopeMatrixVersionRequest>(context, cancellationToken);
        if (request is null) return Problem(ScopeErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.CreateAsync(request, correlationId, cancellationToken);
            return Results.Json(result, ScopeJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (ScopeDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> ReviseAsync(
        string id,
        HttpContext context,
        IScopeMatrixService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<SubmitScopeMatrixVersionRequest>(context, cancellationToken);
        if (request is null) return Problem(ScopeErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.ReviseAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, ScopeJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (ScopeDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetVersionAsync(
        string id,
        long version,
        HttpContext context,
        IScopeMatrixService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetVersionAsync(id, version, correlationId, cancellationToken),
                ScopeJson.Options);
        }
        catch (ScopeDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetEligibilityAsync(
        string id,
        HttpContext context,
        ICurrentOrganizationContext organizationContext,
        IScopeProductionEligibilityPort port,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetPositiveLong(context.Request.Query["expectedVersion"], out var expectedVersion) ||
            !TryGetSingle(context.Request.Query["ruleSetVersion"], out var ruleSetVersion))
        {
            return Problem(ScopeErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await port.EvaluateAsync(new ScopeProductionEligibilityRequest(
                organizationContext.Current.OrganizationGroupId,
                id,
                expectedVersion,
                ruleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return Results.Json(result, ScopeJson.Options);
        }
        catch (ScopeDomainException exception)
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

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ScopeJson.Options, cancellationToken);
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
            ScopeErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            ScopeErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            ScopeErrorCodes.ExpectedVersionConflict => StatusCodes.Status409Conflict,
            ScopeErrorCodes.EvaluationIncomplete or ScopeErrorCodes.EvaluationConflict or
                ScopeErrorCodes.ApplicabilityUnknown => StatusCodes.Status422UnprocessableEntity,
            ScopeErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Scope matrix request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }
}

internal static class ScopeJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
