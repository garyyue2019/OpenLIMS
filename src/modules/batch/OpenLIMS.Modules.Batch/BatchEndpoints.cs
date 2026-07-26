using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Batch;

internal static class BatchEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(BatchContract.CreateBatchPath, CreateAsync)
            .WithName("createBatch").RequireAuthorization();
        endpoints.MapPost(BatchContract.AddMemberPath, AddMemberAsync)
            .WithName("addBatchMember").RequireAuthorization();
        endpoints.MapPost(BatchContract.AddEvidencePath, AddEvidenceAsync)
            .WithName("addBatchEvidence").RequireAuthorization();
        endpoints.MapPost(BatchContract.FreezePath, FreezeAsync)
            .WithName("freezeBatch").RequireAuthorization();
        endpoints.MapGet(BatchContract.GetBatchPath, GetAsync)
            .WithName("getBatch").RequireAuthorization();
        endpoints.MapGet(BatchContract.StatusPath, GetStatusAsync)
            .WithName("getBatchStatus").RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context, IBatchService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<CreateBatchRequest>(context, cancellationToken);
        if (request is null) return Problem(BatchErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.CreateAsync(request, correlationId, cancellationToken);
            return Results.Json(result, BatchJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (BatchDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> AddMemberAsync(
        string id, HttpContext context, IBatchService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<AddBatchMemberRequest>(context, cancellationToken);
        if (request is null) return Problem(BatchErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.AddMemberAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, BatchJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (BatchDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> AddEvidenceAsync(
        string id, HttpContext context, IBatchService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<AddBatchEvidenceRequest>(context, cancellationToken);
        if (request is null) return Problem(BatchErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.AddEvidenceAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, BatchJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (BatchDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> FreezeAsync(
        string id, HttpContext context, IBatchService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<FreezeBatchRequest>(context, cancellationToken);
        if (request is null) return Problem(BatchErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.FreezeAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, BatchJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (BatchDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> GetAsync(
        string id, HttpContext context, IBatchService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(await service.GetAsync(id, correlationId, cancellationToken), BatchJson.Options);
        }
        catch (BatchDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> GetStatusAsync(
        string id,
        HttpContext context,
        ICurrentOrganizationContext organizationContext,
        IBatchStatusPort port,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetPositiveLong(context.Request.Query["expectedVersion"], out var expectedVersion) ||
            !TryGetSingle(context.Request.Query["ruleSetVersion"], out var ruleSetVersion))
        {
            return Problem(BatchErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await port.EvaluateAsync(new BatchStatusRequest(
                organizationContext.Current.OrganizationGroupId, id, expectedVersion, ruleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return Results.Json(result, BatchJson.Options);
        }
        catch (BatchDomainException exception)
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
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, BatchJson.Options, cancellationToken);
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
            BatchErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            BatchErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            BatchErrorCodes.ExpectedVersionConflict or BatchErrorCodes.BatchFrozen => StatusCodes.Status409Conflict,
            BatchErrorCodes.EligibilityBlocked or
                BatchErrorCodes.ApplicabilityUnknown => StatusCodes.Status422UnprocessableEntity,
            BatchErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Batch request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId,
                ["gateSource"] = gateSource
            });
    }
}

internal static class BatchJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
