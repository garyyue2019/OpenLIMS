using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal static class ToyEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ToyContract.AgeDeclarationPath, RecordDeclarationAsync)
            .WithName("recordToyAgeDeclaration").RequireAuthorization();
        endpoints.MapPost(ToyContract.AgeGradeDecisionPath, RecordDecisionAsync)
            .WithName("recordToyAgeGradeDecision").RequireAuthorization();
        endpoints.MapPost(ToyContract.FreezeDecisionPath, FreezeDecisionAsync)
            .WithName("freezeToyAgeGradeDecision").RequireAuthorization();
        endpoints.MapPost(ToyContract.AccessibilityAssessmentPath, RecordAssessmentAsync)
            .WithName("recordToyAccessibilityAssessment").RequireAuthorization();
        endpoints.MapPost(ToyContract.ResolveTriggerPath, ResolveTriggerAsync)
            .WithName("resolveToyReassessmentTrigger").RequireAuthorization();
        endpoints.MapGet(ToyContract.OverviewPath, GetOverviewAsync)
            .WithName("getToyProductOverview").RequireAuthorization();
        endpoints.MapPost(ToyTestUnitPlanContract.PlanPath, CreateTestUnitPlanAsync)
            .WithName("createToyTestUnitPlan").RequireAuthorization();
        endpoints.MapPost(ToyTestUnitPlanContract.ApprovalPath, ApproveSampleRequirementAsync)
            .WithName("approveToySampleRequirement").RequireAuthorization();
        endpoints.MapPost(ToyTestUnitPlanContract.AllocationPath, RequestToyAllocationAsync)
            .WithName("requestToyAllocation").RequireAuthorization();
        endpoints.MapGet(ToyTestUnitPlanContract.DetailPath, GetTestUnitPlanAsync)
            .WithName("getToyTestUnitPlan").RequireAuthorization();
    }

    private static Task<IResult> RecordDeclarationAsync(
        string id, HttpContext context, IToyProductService service, CancellationToken cancellationToken) =>
        PostAsync<RecordAgeDeclarationRequest>(context, cancellationToken,
            (request, correlationId) => service.RecordDeclarationAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> RecordDecisionAsync(
        string id, HttpContext context, IToyProductService service, CancellationToken cancellationToken) =>
        PostAsync<RecordAgeGradeDecisionRequest>(context, cancellationToken,
            (request, correlationId) => service.RecordDecisionAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> FreezeDecisionAsync(
        string id,
        int versionNumber,
        HttpContext context,
        IToyProductService service,
        CancellationToken cancellationToken) =>
        PostAsync<FreezeAgeGradeDecisionRequest>(context, cancellationToken,
            (request, correlationId) =>
                service.FreezeDecisionAsync(id, versionNumber, request, correlationId, cancellationToken),
            StatusCodes.Status200OK);

    private static Task<IResult> RecordAssessmentAsync(
        string id, HttpContext context, IToyProductService service, CancellationToken cancellationToken) =>
        PostAsync<RecordAccessibilityAssessmentRequest>(context, cancellationToken,
            (request, correlationId) => service.RecordAssessmentAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> ResolveTriggerAsync(
        string id,
        string triggerId,
        HttpContext context,
        IToyProductService service,
        CancellationToken cancellationToken) =>
        PostAsync<ResolveReassessmentTriggerRequest>(context, cancellationToken,
            (request, correlationId) =>
                service.ResolveTriggerAsync(id, triggerId, request, correlationId, cancellationToken),
            StatusCodes.Status200OK);

    private static async Task<IResult> GetOverviewAsync(
        string id, HttpContext context, IToyProductService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetOverviewAsync(id, correlationId, cancellationToken), ToyJson.Options);
        }
        catch (ToyDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static Task<IResult> CreateTestUnitPlanAsync(
        string id,
        HttpContext context,
        IToyTestUnitPlanService service,
        CancellationToken cancellationToken) =>
        PostAsync<CreateToyTestUnitPlanRequest, ToyTestUnitPlanResult>(
            context,
            cancellationToken,
            (request, correlationId) =>
                service.CreatePlanAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> ApproveSampleRequirementAsync(
        string id,
        long planVersion,
        HttpContext context,
        IToyTestUnitPlanService service,
        CancellationToken cancellationToken) =>
        PostAsync<ApproveToySampleRequirementRequest, ToyTestUnitPlanResult>(
            context,
            cancellationToken,
            (request, correlationId) =>
                service.ApproveAsync(id, planVersion, request, correlationId, cancellationToken),
            StatusCodes.Status200OK);

    private static Task<IResult> RequestToyAllocationAsync(
        string id,
        long planVersion,
        HttpContext context,
        IToyTestUnitPlanService service,
        CancellationToken cancellationToken) =>
        PostAsync<RequestToyAllocationRequest, ToyTestUnitPlanResult>(
            context,
            cancellationToken,
            (request, correlationId) =>
                service.RequestAllocationAsync(id, planVersion, request, correlationId, cancellationToken));

    private static async Task<IResult> GetTestUnitPlanAsync(
        string id,
        long planVersion,
        HttpContext context,
        IToyTestUnitPlanService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetAsync(id, planVersion, correlationId, cancellationToken), ToyJson.Options);
        }
        catch (ToyDomainException exception)
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
        if (request is null) return Problem(ToyErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await handle(request, correlationId);
            return Results.Json(result, ToyJson.Options, statusCode: successStatusCode);
        }
        catch (ToyDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static Task<IResult> PostAsync<TRequest>(
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, string, Task<ToyProductOverview>> handle,
        int successStatusCode = StatusCodes.Status201Created) =>
        PostAsync<TRequest, ToyProductOverview>(
            context, cancellationToken, handle, successStatusCode);

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ToyJson.Options, cancellationToken);
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
            ToyErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            ToyErrorCodes.ObjectNotAccessible or
                ToyErrorCodes.DecisionNotFound => StatusCodes.Status404NotFound,
            ToyErrorCodes.ExpectedVersionConflict => StatusCodes.Status409Conflict,
            ToyErrorCodes.DecisionFrozen or
                ToyErrorCodes.ReassessmentNotPending or
                ToyErrorCodes.DestructiveTestUnitConflict or
                ToyErrorCodes.SampleRequirementNotApproved or
                ToyErrorCodes.DownstreamEligibilityBlocked => StatusCodes.Status422UnprocessableEntity,
            ToyErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Toy request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }
}

internal static class ToyJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
