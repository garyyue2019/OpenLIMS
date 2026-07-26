using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

internal static class ReportEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(ReportContract.CreateReportPath, CreateAsync)
            .WithName("createReport").RequireAuthorization();
        endpoints.MapPost(ReportContract.AddLinePath, AddLineAsync)
            .WithName("addReportLine").RequireAuthorization();
        endpoints.MapPost(ReportContract.GateEvaluationPath, EvaluateGateAsync)
            .WithName("evaluateReportGate").RequireAuthorization();
        endpoints.MapPost(ReportContract.SubmitForApprovalPath, SubmitAsync)
            .WithName("submitReportForApproval").RequireAuthorization();
        endpoints.MapGet(ReportContract.GetReportPath, GetAsync)
            .WithName("getReport").RequireAuthorization();
        endpoints.MapGet(ReportContract.IssuanceGatePath, GetIssuanceGateAsync)
            .WithName("getReportIssuanceGate").RequireAuthorization();
    }

    private static Task<IResult> CreateAsync(
        HttpContext context, IReportService service, CancellationToken cancellationToken) =>
        PostAsync<CreateReportRequest>(context, cancellationToken,
            (request, correlationId) => service.CreateAsync(request, correlationId, cancellationToken));

    private static Task<IResult> AddLineAsync(
        string id, HttpContext context, IReportService service, CancellationToken cancellationToken) =>
        PostAsync<AddReportLineRequest>(context, cancellationToken,
            (request, correlationId) => service.AddLineAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> EvaluateGateAsync(
        string id, HttpContext context, IReportService service, CancellationToken cancellationToken) =>
        PostAsync<EvaluateReportGateRequest>(context, cancellationToken,
            (request, correlationId) => service.EvaluateGateAsync(id, request, correlationId, cancellationToken));

    private static Task<IResult> SubmitAsync(
        string id, HttpContext context, IReportService service, CancellationToken cancellationToken) =>
        PostAsync<SubmitReportForApprovalRequest>(context, cancellationToken,
            (request, correlationId) => service.SubmitForApprovalAsync(id, request, correlationId, cancellationToken));

    private static async Task<IResult> GetAsync(
        string id, HttpContext context, IReportService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(await service.GetAsync(id, correlationId, cancellationToken), ReportJson.Options);
        }
        catch (ReportDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> GetIssuanceGateAsync(
        string id,
        HttpContext context,
        ICurrentOrganizationContext organizationContext,
        IReportIssuanceGatePort port,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetSingle(context.Request.Query["ruleSetVersion"], out var ruleSetVersion) ||
            !TryGetVersion(context.Request.Query["expectedReportVersion"], out var expectedReportVersion))
        {
            return Problem(ReportErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await port.EvaluateAsync(new ReportIssuanceGateRequest(
                organizationContext.Current.OrganizationGroupId, id, expectedReportVersion, ruleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return Results.Json(result, ReportJson.Options);
        }
        catch (ReportDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static async Task<IResult> PostAsync<TRequest>(
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, string, Task<ReportResult>> handle)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<TRequest>(context, cancellationToken);
        if (request is null) return Problem(ReportErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await handle(request, correlationId);
            return Results.Json(result, ReportJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (ReportDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId, exception.GateSource);
        }
    }

    private static bool TryGetSingle(Microsoft.Extensions.Primitives.StringValues values, out string value)
    {
        value = values.Count == 1 ? values[0]?.Trim() ?? string.Empty : string.Empty;
        return value.Length is > 0 and <= 200;
    }

    private static bool TryGetVersion(Microsoft.Extensions.Primitives.StringValues values, out long value)
    {
        value = 0;
        return TryGetSingle(values, out var raw) && long.TryParse(raw, out value) && value > 0;
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ReportJson.Options, cancellationToken);
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
            ReportErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            ReportErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            ReportErrorCodes.ExpectedVersionConflict or
                ReportErrorCodes.DuplicateAttribution => StatusCodes.Status409Conflict,
            ReportErrorCodes.EligibilityBlocked or
                ReportErrorCodes.ApplicabilityUnknown or
                ReportErrorCodes.AccreditationBlocked or
                ReportErrorCodes.ConformityDecisionUnavailable or
                ReportErrorCodes.TraceIncomplete => StatusCodes.Status422UnprocessableEntity,
            ReportErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Report request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId,
                ["gateSource"] = gateSource
            });
    }
}

internal static class ReportJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
