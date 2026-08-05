using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Operations;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Operations;

internal static class OperationsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(OperationsContract.CreateLineageEdgePath, CreateLineageEdgeAsync)
            .WithName("createSampleLineageEdge").RequireAuthorization();
        endpoints.MapGet(OperationsContract.GetLineagePath, GetLineageAsync)
            .WithName("getSampleLineage").RequireAuthorization();
        endpoints.MapPost(OperationsContract.RecordCustodyEventPath, RecordCustodyEventAsync)
            .WithName("recordCustodyEvent").RequireAuthorization();
        endpoints.MapGet(OperationsContract.GetCustodyPath, GetCustodyAsync)
            .WithName("getCustodyChain").RequireAuthorization();
        endpoints.MapPost(OperationsContract.CreateWorkPlanPath, CreateWorkPlanAsync)
            .WithName("createWorkPlan").RequireAuthorization();
        endpoints.MapGet(OperationsContract.GetWorkPlanPath, GetWorkPlanAsync)
            .WithName("getWorkPlan").RequireAuthorization();
        endpoints.MapPost(OperationsContract.ChangeTaskStatePath, ChangeTaskStateAsync)
            .WithName("changeWorkTaskState").RequireAuthorization();
        endpoints.MapPost(OperationsContract.ReserveResourcePath, ReserveResourceAsync)
            .WithName("reserveWorkResource").RequireAuthorization();
        endpoints.MapGet(OperationsContract.GetWorkQueuePath, GetWorkQueueAsync)
            .WithName("getWorkQueue").RequireAuthorization();
    }

    private static Task<IResult> CreateLineageEdgeAsync(
        HttpContext context,
        IOperationsService service,
        CancellationToken cancellationToken) =>
        CommandAsync<CreateLineageEdgeRequest, LineageEdgeResult>(
            context,
            cancellationToken,
            request => service.CreateLineageEdgeAsync(request, Correlation(context), cancellationToken));

    private static async Task<IResult> GetLineageAsync(
        string objectId,
        HttpContext context,
        IOperationsService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetLineageAsync(objectId, correlationId, cancellationToken),
                OperationsJson.Options);
        }
        catch (OperationsDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static Task<IResult> RecordCustodyEventAsync(
        HttpContext context,
        IOperationsService service,
        CancellationToken cancellationToken) =>
        CommandAsync<RecordCustodyEventRequest, CustodyEventResult>(
            context,
            cancellationToken,
            request => service.RecordCustodyEventAsync(request, Correlation(context), cancellationToken));

    private static async Task<IResult> GetCustodyAsync(
        string objectId,
        HttpContext context,
        IOperationsService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetCustodyAsync(objectId, correlationId, cancellationToken),
                OperationsJson.Options);
        }
        catch (OperationsDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static Task<IResult> CreateWorkPlanAsync(
        HttpContext context,
        IOperationsService service,
        CancellationToken cancellationToken) =>
        CommandAsync<CreateWorkPlanRequest, WorkPlanResult>(
            context,
            cancellationToken,
            request => service.CreateWorkPlanAsync(request, Correlation(context), cancellationToken));

    private static async Task<IResult> GetWorkPlanAsync(
        string id,
        HttpContext context,
        IOperationsService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetWorkPlanAsync(id, correlationId, cancellationToken),
                OperationsJson.Options);
        }
        catch (OperationsDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static Task<IResult> ChangeTaskStateAsync(
        string id,
        string taskId,
        HttpContext context,
        IOperationsService service,
        CancellationToken cancellationToken) =>
        CommandAsync<ChangeWorkTaskStateRequest, WorkPlanResult>(
            context,
            cancellationToken,
            request => service.ChangeTaskStateAsync(id, taskId, request, Correlation(context), cancellationToken));

    private static Task<IResult> ReserveResourceAsync(
        string id,
        HttpContext context,
        IOperationsService service,
        CancellationToken cancellationToken) =>
        CommandAsync<ReserveResourceRequest, WorkPlanResult>(
            context,
            cancellationToken,
            request => service.ReserveResourceAsync(id, request, Correlation(context), cancellationToken));

    private static async Task<IResult> GetWorkQueueAsync(
        HttpContext context,
        IOperationsService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetSingle(context.Request.Query["workCenterId"], out var workCenterId) ||
            !TryGetOptionalSingle(context.Request.Query["state"], out var state))
        {
            return Problem(OperationsErrorCodes.ValidationFailed, correlationId);
        }
        try
        {
            return Results.Json(
                await service.GetWorkQueueAsync(workCenterId, state, correlationId, cancellationToken),
                OperationsJson.Options);
        }
        catch (OperationsDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> CommandAsync<TRequest, TResult>(
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, Task<TResult>> action)
        where TRequest : class
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<TRequest>(context, cancellationToken);
        if (request is null)
            return Problem(OperationsErrorCodes.ValidationFailed, correlationId);
        try
        {
            return Results.Json(await action(request), OperationsJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (OperationsDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static bool TryGetSingle(Microsoft.Extensions.Primitives.StringValues values, out string value)
    {
        value = values.Count == 1 ? values[0]?.Trim() ?? string.Empty : string.Empty;
        return value.Length is > 0 and <= 128;
    }

    private static bool TryGetOptionalSingle(
        Microsoft.Extensions.Primitives.StringValues values,
        out string? value)
    {
        value = null;
        if (values.Count == 0)
            return true;
        if (values.Count != 1)
            return false;
        var normalized = values[0]?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 128)
            return false;
        value = normalized;
        return true;
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                context.Request.Body,
                OperationsJson.Options,
                cancellationToken);
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
            OperationsErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            OperationsErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            OperationsErrorCodes.ExpectedVersionConflict or OperationsErrorCodes.ResourceConflict =>
                StatusCodes.Status409Conflict,
            OperationsErrorCodes.LineageCycle or OperationsErrorCodes.LineageParentConflict or
                OperationsErrorCodes.CustodySequenceConflict or OperationsErrorCodes.DependencyBlocked or
                OperationsErrorCodes.InvalidTaskTransition => StatusCodes.Status422UnprocessableEntity,
            OperationsErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Operations request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }
}

internal static class OperationsJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
