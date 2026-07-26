using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Instrument;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Instrument;

internal static class InstrumentEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(InstrumentContract.RegisterFilePath, RegisterAsync)
            .WithName("registerInstrumentFile").RequireAuthorization();
        endpoints.MapPost(InstrumentContract.SubmitRowsPath, SubmitRowsAsync)
            .WithName("submitInstrumentRows").RequireAuthorization();
        endpoints.MapPost(InstrumentContract.ResolveExceptionPath, ResolveExceptionAsync)
            .WithName("resolveInstrumentImportException").RequireAuthorization();
        endpoints.MapGet(InstrumentContract.GetFilePath, GetAsync)
            .WithName("getInstrumentFile").RequireAuthorization();
        endpoints.MapGet(InstrumentContract.StatusPath, GetStatusAsync)
            .WithName("getInstrumentImportStatus").RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context, IInstrumentImportService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<RegisterInstrumentFileRequest>(context, cancellationToken);
        if (request is null) return Problem(InstrumentErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.RegisterFileAsync(request, correlationId, cancellationToken);
            return Results.Json(result, InstrumentJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (InstrumentDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> SubmitRowsAsync(
        string id, HttpContext context, IInstrumentImportService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<SubmitInstrumentRowsRequest>(context, cancellationToken);
        if (request is null) return Problem(InstrumentErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.SubmitRowsAsync(id, request, correlationId, cancellationToken);
            return Results.Json(result, InstrumentJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (InstrumentDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> ResolveExceptionAsync(
        string id, string exceptionId, HttpContext context, IInstrumentImportService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<ResolveImportExceptionRequest>(context, cancellationToken);
        if (request is null) return Problem(InstrumentErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.ResolveExceptionAsync(id, exceptionId, request, correlationId, cancellationToken);
            return Results.Json(result, InstrumentJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (InstrumentDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetAsync(
        string id, HttpContext context, IInstrumentImportService service, CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(await service.GetAsync(id, correlationId, cancellationToken), InstrumentJson.Options);
        }
        catch (InstrumentDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetStatusAsync(
        string id,
        HttpContext context,
        ICurrentOrganizationContext organizationContext,
        IInstrumentImportPort port,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        if (!TryGetSingle(context.Request.Query["ruleSetVersion"], out var ruleSetVersion) ||
            !TryGetVersion(context.Request.Query["expectedFileVersion"], out var expectedFileVersion))
        {
            return Problem(InstrumentErrorCodes.ValidationFailed, correlationId);
        }

        try
        {
            var result = await port.EvaluateAsync(new InstrumentImportStatusRequest(
                organizationContext.Current.OrganizationGroupId, id, expectedFileVersion, ruleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
            return Results.Json(result, InstrumentJson.Options);
        }
        catch (InstrumentDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
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
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, InstrumentJson.Options, cancellationToken);
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
            InstrumentErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            InstrumentErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            InstrumentErrorCodes.DuplicateFile or
                InstrumentErrorCodes.ExceptionAlreadyResolved or
                InstrumentErrorCodes.ExpectedVersionConflict => StatusCodes.Status409Conflict,
            InstrumentErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Instrument import request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }
}

internal static class InstrumentJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
