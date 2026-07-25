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
        endpoints.MapGet(IdentityAssessmentContract.AssessmentPath, GetIdentityAssessmentAsync)
            .WithName("getIdentityAssessment")
            .RequireAuthorization();
        endpoints.MapPost(IdentityAssessmentContract.ObservationsPath, CreateIdentityObservationAsync)
            .WithName("createIdentityObservation")
            .RequireAuthorization();
        endpoints.MapPost(IdentityAssessmentContract.DecisionsPath, SubmitIdentityDecisionAsync)
            .WithName("submitIdentityDecision")
            .RequireAuthorization();
        endpoints.MapPost(ReceivingExceptionContract.CreatePath, CreateReceivingExceptionAsync)
            .WithName("createReceivingException")
            .RequireAuthorization();
        endpoints.MapGet(ReceivingExceptionContract.DetailPath, GetReceivingExceptionAsync)
            .WithName("getReceivingException")
            .RequireAuthorization();
        endpoints.MapPost(ReceivingExceptionContract.DecisionsPath, SubmitReceivingExceptionDecisionAsync)
            .WithName("submitReceivingExceptionDecision")
            .RequireAuthorization();
    }

    private static async Task<IResult> CreateReceivingExceptionAsync(
        HttpContext context,
        IReceivingExceptionService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<CreateReceivingExceptionRequest>(context, cancellationToken);
        if (request is null) return ExceptionProblem(ReceivingErrorCodes.DecisionEvidenceIncomplete, correlationId);
        try
        {
            var result = await service.CreateAsync(request, correlationId, cancellationToken);
            return Results.Json(result, ReceivingJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (ReceivingDomainException exception)
        {
            return ExceptionProblem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetReceivingExceptionAsync(
        string id,
        HttpContext context,
        IReceivingExceptionService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(await service.GetAsync(id, correlationId, cancellationToken), ReceivingJson.Options);
        }
        catch (ReceivingDomainException exception)
        {
            return ExceptionProblem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> SubmitReceivingExceptionDecisionAsync(
        string id,
        HttpContext context,
        IReceivingExceptionService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<SubmitReceivingExceptionDecisionRequest>(context, cancellationToken);
        if (request is null) return ExceptionProblem(ReceivingErrorCodes.DecisionEvidenceIncomplete, correlationId);
        try
        {
            var result = await service.SubmitDecisionAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, ReceivingJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (ReceivingDomainException exception)
        {
            return ExceptionProblem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetIdentityAssessmentAsync(
        string id,
        HttpContext context,
        IIdentityAssessmentService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            var result = await service.GetAsync(id, correlationId, cancellationToken);
            return Results.Json(result, ReceivingJson.Options);
        }
        catch (ReceivingDomainException exception)
        {
            return IdentityProblem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> CreateIdentityObservationAsync(
        string id,
        HttpContext context,
        IIdentityAssessmentService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<CreateIdentityObservationRequest>(context, cancellationToken);
        if (request is null)
        {
            return IdentityProblem(ReceivingErrorCodes.IdentityEvidenceIncomplete, correlationId);
        }

        try
        {
            var result = await service.AddObservationAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, ReceivingJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (ReceivingDomainException exception)
        {
            return IdentityProblem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> SubmitIdentityDecisionAsync(
        string id,
        HttpContext context,
        IIdentityAssessmentService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<SubmitIdentityDecisionRequest>(context, cancellationToken);
        if (request is null)
        {
            return IdentityProblem(ReceivingErrorCodes.IdentityEvidenceIncomplete, correlationId);
        }

        try
        {
            var result = await service.SubmitDecisionAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, ReceivingJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (ReceivingDomainException exception)
        {
            return IdentityProblem(exception.ErrorCode, correlationId);
        }
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

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ReceivingJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string Correlation(HttpContext context) =>
        context.Items[CorrelationId.HeaderName]?.ToString() ?? Guid.NewGuid().ToString("N");

    private static IResult IdentityProblem(string errorCode, string correlationId)
    {
        var statusCode = errorCode switch
        {
            ReceivingErrorCodes.AuthorizationDenied => StatusCodes.Status403Forbidden,
            ReceivingErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            ReceivingErrorCodes.ExpectedVersionConflict => StatusCodes.Status409Conflict,
            ReceivingErrorCodes.IdentityConflict => StatusCodes.Status422UnprocessableEntity,
            ReceivingErrorCodes.IdentityAmbiguous => StatusCodes.Status422UnprocessableEntity,
            ReceivingErrorCodes.IdentityEvidenceIncomplete => StatusCodes.Status422UnprocessableEntity,
            ReceivingErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            ReceivingErrorCodes.ReceivingPortUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Identity assessment could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }

    private static IResult ExceptionProblem(string errorCode, string correlationId)
    {
        var statusCode = errorCode switch
        {
            ReceivingErrorCodes.AuthorizationDenied or ReceivingErrorCodes.DecisionNotAuthorized => StatusCodes.Status403Forbidden,
            ReceivingErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            ReceivingErrorCodes.ExpectedVersionConflict => StatusCodes.Status409Conflict,
            ReceivingErrorCodes.PersistenceUnavailable or ReceivingErrorCodes.ReceivingPortUnavailable => StatusCodes.Status503ServiceUnavailable,
            ReceivingErrorCodes.ExceptionTypeUnknown or ReceivingErrorCodes.ApplicabilityUnknown or
            ReceivingErrorCodes.DecisionEvidenceIncomplete or ReceivingErrorCodes.ConditionalAcceptConstraintsRequired => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Receiving exception could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }
}

public interface IReceiptRegistrationService
{
    Task<ReceiptRegistrationResult> RegisterAsync(
        RegisterReceiptRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public interface IIdentityAssessmentService
{
    Task<IdentityAssessmentResult> GetAsync(
        string receivedItemId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<IdentityAssessmentResult> AddObservationAsync(
        string receivedItemId,
        CreateIdentityObservationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<IdentityAssessmentResult> SubmitDecisionAsync(
        string receivedItemId,
        SubmitIdentityDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public interface IReceivingExceptionService
{
    Task<ReceivingExceptionResult> CreateAsync(
        CreateReceivingExceptionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ReceivingExceptionResult> GetAsync(
        string exceptionId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ReceivingExceptionResult> SubmitDecisionAsync(
        string exceptionId,
        SubmitReceivingExceptionDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);
}
