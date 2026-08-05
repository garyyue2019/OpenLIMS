using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Ai;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Ai;

internal static class AiEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(AiContract.CreateRunPath, CreateAsync)
            .WithName("createAiRun").RequireAuthorization();
        endpoints.MapGet(AiContract.GetRunPath, GetAsync)
            .WithName("getAiRun").RequireAuthorization();
        endpoints.MapPost(AiContract.RecordDispositionPath, RecordDispositionAsync)
            .WithName("recordAiDisposition").RequireAuthorization();
        endpoints.MapGet(AiContract.ReviewQueuePath, GetReviewQueueAsync)
            .WithName("getAiReviewQueue").RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        IAiRunService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<CreateAiRunRequest>(context, cancellationToken);
        if (request is null)
            return Problem(AiErrorCodes.ValidationFailed, correlationId);
        try
        {
            return Results.Json(
                await service.CreateAsync(request, correlationId, cancellationToken),
                AiJson.Options,
                statusCode: StatusCodes.Status201Created);
        }
        catch (AiDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetAsync(
        string id,
        HttpContext context,
        IAiRunService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetAsync(id, correlationId, cancellationToken), AiJson.Options);
        }
        catch (AiDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> RecordDispositionAsync(
        string id,
        HttpContext context,
        IAiRunService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<RecordAiDispositionRequest>(context, cancellationToken);
        if (request is null)
            return Problem(AiErrorCodes.ValidationFailed, correlationId);
        try
        {
            return Results.Json(
                await service.RecordDispositionAsync(id, request, correlationId, cancellationToken),
                AiJson.Options,
                statusCode: StatusCodes.Status201Created);
        }
        catch (AiDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetReviewQueueAsync(
        HttpContext context,
        IAiRunService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var status = context.Request.Query["status"].Count switch
        {
            0 => null,
            1 => context.Request.Query["status"][0],
            _ => string.Empty
        };
        try
        {
            return Results.Json(
                await service.GetReviewQueueAsync(status, correlationId, cancellationToken), AiJson.Options);
        }
        catch (AiDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, AiJson.Options, cancellationToken);
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
            AiErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            AiErrorCodes.ObjectNotAccessible or AiErrorCodes.CandidateNotFound => StatusCodes.Status404NotFound,
            AiErrorCodes.ExpectedVersionConflict or AiErrorCodes.IdempotencyConflict => StatusCodes.Status409Conflict,
            AiErrorCodes.OutputQuarantined or AiErrorCodes.FactClassPromotionRejected or
                AiErrorCodes.ReviewNotAllowed => StatusCodes.Status422UnprocessableEntity,
            AiErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "AI request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }
}

internal static class AiJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
