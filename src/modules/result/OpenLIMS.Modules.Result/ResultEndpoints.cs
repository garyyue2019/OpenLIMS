using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Result;

namespace OpenLIMS.Modules.Result;

internal static class ResultEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ResultContract.CreateGroupPath, CreateGroupAsync)
            .WithName("createResultGroup").RequireAuthorization();
        endpoints.MapPost(ResultContract.AddObservationPath, AddObservationAsync)
            .WithName("addResultObservation").RequireAuthorization();
        endpoints.MapPost(ResultContract.AddDerivationPath, AddDerivationAsync)
            .WithName("addResultDerivation").RequireAuthorization();
        endpoints.MapPost(ResultContract.ExecuteCalculationPath, ExecuteCalculationAsync)
            .WithName("executeResultCalculation").RequireAuthorization();
        endpoints.MapPost(ResultContract.RecordAdoptionRulePath, RecordAdoptionRuleAsync)
            .WithName("recordAdoptionRule").RequireAuthorization();
        endpoints.MapPost(ResultContract.AdoptPath, AdoptAsync)
            .WithName("adoptResult").RequireAuthorization();
        endpoints.MapPost(ResultContract.RecordAccreditationAssessmentPath, RecordAccreditationAssessmentAsync)
            .WithName("recordResultAccreditationAssessment").RequireAuthorization();
        endpoints.MapGet(ResultContract.GetGroupPath, GetAsync)
            .WithName("getResultGroup").RequireAuthorization();
        endpoints.MapGet(ResultContract.AdoptionStatusPath, GetAdoptionStatusAsync)
            .WithName("getResultAdoptionStatus").RequireAuthorization();
        endpoints.MapGet(ResultContract.AccreditationEligibilityPath, GetAccreditationEligibilityAsync)
            .WithName("getResultAccreditationEligibility").RequireAuthorization();
    }

    private static Task<IResult> CreateGroupAsync(
        HttpContext context, IResultGroupService service, CancellationToken cancellationToken) =>
        HandleAsync<CreateResultGroupRequest, ResultGroupResult>(context, cancellationToken,
            (request, correlationId, token) => service.CreateGroupAsync(request, correlationId, token));

    private static Task<IResult> AddObservationAsync(
        string id, HttpContext context, IResultGroupService service, CancellationToken cancellationToken) =>
        HandleAsync<AddResultObservationRequest, ResultObservationResult>(context, cancellationToken,
            (request, correlationId, token) => service.AddObservationAsync(id, request, correlationId, token));

    private static Task<IResult> AddDerivationAsync(
        string id, HttpContext context, IResultGroupService service, CancellationToken cancellationToken) =>
        HandleAsync<AddResultDerivationRequest, ResultDerivationResult>(context, cancellationToken,
            (request, correlationId, token) => service.AddDerivationAsync(id, request, correlationId, token));

    private static Task<IResult> ExecuteCalculationAsync(
        string id, HttpContext context, IResultGroupService service, CancellationToken cancellationToken) =>
        HandleAsync<ExecuteResultCalculationRequest, ResultCalculationResult>(context, cancellationToken,
            (request, correlationId, token) => service.ExecuteCalculationAsync(id, request, correlationId, token));

    private static Task<IResult> RecordAdoptionRuleAsync(
        string id, HttpContext context, IResultGroupService service, CancellationToken cancellationToken) =>
        HandleAsync<RecordAdoptionRuleRequest, AdoptionRuleResult>(context, cancellationToken,
            (request, correlationId, token) => service.RecordAdoptionRuleAsync(id, request, correlationId, token));

    private static Task<IResult> AdoptAsync(
        string id, HttpContext context, IResultGroupService service, CancellationToken cancellationToken) =>
        HandleAsync<AdoptResultRequest, ResultAdoptionResult>(context, cancellationToken,
            (request, correlationId, token) => service.AdoptAsync(id, request, correlationId, token));

    private static Task<IResult> RecordAccreditationAssessmentAsync(
        string id, HttpContext context, IResultGroupService service, CancellationToken cancellationToken) =>
        HandleAsync<RecordResultAccreditationAssessmentRequest, ResultAccreditationAssessmentResult>(
            context, cancellationToken,
            (request, correlationId, token) =>
                service.RecordAccreditationAssessmentAsync(id, request, correlationId, token));

    private static async Task<IResult> GetAsync(
        string id, HttpContext context, IResultGroupService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(await service.GetAsync(id, correlationId, cancellationToken), ResultJson.Options);
        }
        catch (ResultDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> GetAdoptionStatusAsync(
        string id,
        HttpContext context,
        ICurrentOrganizationContext organizationContext,
        IResultAdoptionPort port,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetPositiveLong(context.Request.Query["expectedVersion"], out var expectedVersion) ||
            !TryGetSingle(context.Request.Query["ruleSetVersion"], out var ruleSetVersion))
        {
            return Problem(ResultErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await port.EvaluateAsync(new ResultAdoptionStatusRequest(
                organizationContext.Current.OrganizationGroupId, id, expectedVersion, ruleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return Results.Json(result, ResultJson.Options);
        }
        catch (ResultDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> GetAccreditationEligibilityAsync(
        string id,
        HttpContext context,
        ICurrentOrganizationContext organizationContext,
        IResultAccreditationEligibilityPort port,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetPositiveLong(context.Request.Query["expectedVersion"], out var expectedVersion) ||
            !TryGetSingle(context.Request.Query["ruleSetVersion"], out var ruleSetVersion))
        {
            return Problem(ResultErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await port.EvaluateAsync(new ResultAccreditationEligibilityRequest(
                organizationContext.Current.OrganizationGroupId, id, expectedVersion, ruleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return Results.Json(result, ResultJson.Options);
        }
        catch (ResultDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> HandleAsync<TRequest, TResponse>(
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, string, CancellationToken, Task<TResponse>> action)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<TRequest>(context, cancellationToken);
        if (request is null) return Problem(ResultErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await action(request, correlationId, cancellationToken);
            return Results.Json(result, ResultJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (ResultDomainException exception)
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
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ResultJson.Options, cancellationToken);
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
            ResultErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            ResultErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            ResultErrorCodes.ExpectedVersionConflict => StatusCodes.Status409Conflict,
            ResultErrorCodes.EligibilityBlocked or ResultErrorCodes.ApplicabilityUnknown or
                ResultErrorCodes.AdoptionRuleRequired or
                ResultErrorCodes.AdoptionStrategyViolation or
                ResultErrorCodes.CalculationFailed => StatusCodes.Status422UnprocessableEntity,
            ResultErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Result request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId,
                ["gateSource"] = gateSource
            });
    }
}

internal static class ResultJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
