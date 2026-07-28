using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Textile;

namespace OpenLIMS.Modules.Textile;

internal static class TextileEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                TextileRuntimeContract.SampleRequirementPath,
                CalculateSampleRequirementAsync)
            .WithName("calculateTextileSampleRequirement")
            .RequireAuthorization();
        endpoints.MapPost(
                TextileRuntimeContract.CuttingPlanPath,
                CreateCuttingPlanAsync)
            .WithName("createTextileCuttingPlan")
            .RequireAuthorization();
        endpoints.MapPost(
                TextileRuntimeContract.CuttingPlanApprovalPath,
                ApproveCuttingPlanAsync)
            .WithName("approveTextileCuttingPlan")
            .RequireAuthorization();
        endpoints.MapGet(
                TextileRuntimeContract.CuttingPlanDetailPath,
                GetCuttingPlanAsync)
            .WithName("getTextileCuttingPlan")
            .RequireAuthorization();
    }

    private static Task<IResult> CalculateSampleRequirementAsync(
        HttpContext context,
        ITextileRuntimeService service,
        CancellationToken cancellationToken) =>
        PostAsync<CreateTextileSampleRequirementRequest, TextileSampleRequirementRecord>(
            context,
            cancellationToken,
            (request, correlationId) => service.CalculateSampleRequirementAsync(
                request,
                correlationId,
                cancellationToken));

    private static Task<IResult> CreateCuttingPlanAsync(
        HttpContext context,
        ITextileRuntimeService service,
        CancellationToken cancellationToken) =>
        PostAsync<CreateTextileCuttingPlanRequest, TextileCuttingPlanResult>(
            context,
            cancellationToken,
            (request, correlationId) => service.CreateCuttingPlanAsync(
                request,
                correlationId,
                cancellationToken));

    private static Task<IResult> ApproveCuttingPlanAsync(
        string id,
        long version,
        HttpContext context,
        ITextileRuntimeService service,
        CancellationToken cancellationToken) =>
        PostAsync<ApproveTextileCuttingPlanRequest, TextileCuttingPlanResult>(
            context,
            cancellationToken,
            (request, correlationId) => service.ApproveCuttingPlanAsync(
                id,
                version,
                request,
                correlationId,
                cancellationToken),
            StatusCodes.Status200OK);

    private static async Task<IResult> GetCuttingPlanAsync(
        string id,
        long version,
        HttpContext context,
        ITextileRuntimeService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            var result = await service.GetCuttingPlanAsync(
                id,
                version,
                correlationId,
                cancellationToken);
            return Results.Json(result, TextileJson.Options);
        }
        catch (TextileOperationException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
        catch (TextileContractException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> PostAsync<TRequest, TResult>(
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, string, Task<TResult>> handle,
        int successStatusCode = StatusCodes.Status201Created)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<TRequest>(context, cancellationToken);
        if (request is null)
            return Problem(TextileErrorCodes.ValidationFailed, correlationId);

        try
        {
            var result = await handle(request, correlationId);
            return Results.Json(result, TextileJson.Options, statusCode: successStatusCode);
        }
        catch (TextileOperationException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
        catch (TextileContractException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<T?> ReadBodyAsync<T>(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                context.Request.Body,
                TextileJson.Options,
                cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string Correlation(HttpContext context)
    {
        var correlationId = context.Items[CorrelationId.HeaderName]?.ToString();
        if (string.IsNullOrWhiteSpace(correlationId) &&
            context.Request.Headers.TryGetValue(CorrelationId.HeaderName, out var headerValue))
        {
            correlationId = headerValue.ToString();
        }

        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = Guid.NewGuid().ToString("N");
        context.Response.Headers[CorrelationId.HeaderName] = correlationId;
        return correlationId;
    }

    private static IResult Problem(string errorCode, string correlationId)
    {
        var statusCode = errorCode switch
        {
            TextileErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            TextileErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            TextileErrorCodes.ExpectedVersionConflict => StatusCodes.Status409Conflict,
            TextileErrorCodes.ApplicabilityUnknown or
                TextileErrorCodes.SampleRequirementNotApprovable =>
                StatusCodes.Status422UnprocessableEntity,
            TextileErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Textile request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }
}

internal static class TextileJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
