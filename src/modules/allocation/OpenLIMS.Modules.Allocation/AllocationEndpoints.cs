using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Allocation;

internal static class AllocationEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(AllocationContract.CreateAllocationPath, CreateAsync)
            .WithName("createTestObjectAllocation")
            .RequireAuthorization();
        endpoints.MapPost(AllocationContract.ReleaseAllocationPath, ReleaseAsync)
            .WithName("releaseTestObjectAllocation")
            .RequireAuthorization();
        endpoints.MapGet(AllocationContract.GetAllocationPath, GetAsync)
            .WithName("getTestObjectAllocation")
            .RequireAuthorization();
        endpoints.MapGet(AllocationContract.StatusPath, GetStatusAsync)
            .WithName("getAllocationStatus")
            .RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        ITestObjectAllocationService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<CreateTestObjectAllocationRequest>(context, cancellationToken);
        if (request is null) return Problem(AllocationErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.CreateAsync(request, correlationId, cancellationToken);
            return Results.Json(result, AllocationJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (AllocationDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> ReleaseAsync(
        string id,
        HttpContext context,
        ITestObjectAllocationService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<ReleaseTestObjectAllocationRequest>(context, cancellationToken);
        if (request is null) return Problem(AllocationErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.ReleaseAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, AllocationJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (AllocationDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> GetAsync(
        string id,
        HttpContext context,
        ITestObjectAllocationService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetAsync(id, correlationId, cancellationToken),
                AllocationJson.Options);
        }
        catch (AllocationDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> GetStatusAsync(
        string id,
        HttpContext context,
        ICurrentOrganizationContext organizationContext,
        IAllocationStatusPort port,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetPositiveLong(context.Request.Query["expectedVersion"], out var expectedVersion) ||
            !TryGetSingle(context.Request.Query["ruleSetVersion"], out var ruleSetVersion))
        {
            return Problem(AllocationErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await port.EvaluateAsync(new AllocationStatusRequest(
                organizationContext.Current.OrganizationGroupId,
                id,
                expectedVersion,
                ruleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return Results.Json(result, AllocationJson.Options);
        }
        catch (AllocationDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
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
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, AllocationJson.Options, cancellationToken);
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
            AllocationErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            AllocationErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            AllocationErrorCodes.ExpectedVersionConflict or
                AllocationErrorCodes.DestructiveConflict => StatusCodes.Status409Conflict,
            AllocationErrorCodes.EligibilityBlocked or AllocationErrorCodes.ApplicabilityUnknown or
                AllocationErrorCodes.AllocationExpired => StatusCodes.Status422UnprocessableEntity,
            AllocationErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Test object allocation request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId,
                ["gateSource"] = gateSource
            });
    }
}

internal static class AllocationJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
