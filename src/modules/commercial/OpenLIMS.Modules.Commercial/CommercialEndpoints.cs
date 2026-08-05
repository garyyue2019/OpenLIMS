using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenLIMS.Contracts.Commercial;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Commercial;

internal static class CommercialEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(CommercialContract.CreateCatalogRecordPath, CreateCatalogAsync)
            .WithName("createCatalogRecord").RequireAuthorization();
        endpoints.MapPost(CommercialContract.ReviseCatalogRecordPath, ReviseCatalogAsync)
            .WithName("reviseCatalogRecord").RequireAuthorization();
        endpoints.MapGet(CommercialContract.GetCatalogRecordPath, GetCatalogAsync)
            .WithName("getCatalogRecordVersion").RequireAuthorization();
        endpoints.MapPost(CommercialContract.CreateInquiryPath, CreateInquiryAsync)
            .WithName("createInquiry").RequireAuthorization();
        endpoints.MapGet(CommercialContract.GetInquiryPath, GetInquiryAsync)
            .WithName("getInquiry").RequireAuthorization();
        endpoints.MapPost(CommercialContract.ResolveInquiryGapPath, ResolveGapAsync)
            .WithName("resolveInquiryGap").RequireAuthorization();
        endpoints.MapPost(CommercialContract.RecordCapabilityReviewPath, RecordReviewAsync)
            .WithName("recordCapabilityReview").RequireAuthorization();
        endpoints.MapPost(CommercialContract.CreateQuoteVersionPath, CreateQuoteAsync)
            .WithName("createQuoteVersion").RequireAuthorization();
        endpoints.MapPost(CommercialContract.RecordChangeImpactPath, RecordChangeImpactAsync)
            .WithName("recordCommercialChangeImpact").RequireAuthorization();
    }

    private static async Task<IResult> CreateCatalogAsync(
        HttpContext context,
        ICommercialService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<SubmitCatalogRecordRequest>(context, cancellationToken);
        if (request is null)
            return Problem(CommercialErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.CreateCatalogAsync(request, correlationId, cancellationToken);
            return Results.Created($"/api/v1/catalog-records/{result.RecordId}/versions/{result.Version}", result);
        }
        catch (CommercialDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> ReviseCatalogAsync(
        string id,
        HttpContext context,
        ICommercialService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<SubmitCatalogRecordRequest>(context, cancellationToken);
        if (request is null)
            return Problem(CommercialErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.ReviseCatalogAsync(id, request, correlationId, cancellationToken);
            return Results.Created($"/api/v1/catalog-records/{result.RecordId}/versions/{result.Version}", result);
        }
        catch (CommercialDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetCatalogAsync(
        string id,
        long version,
        HttpContext context,
        ICommercialService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetCatalogAsync(id, version, correlationId, cancellationToken),
                CommercialJson.Options);
        }
        catch (CommercialDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> CreateInquiryAsync(
        HttpContext context,
        ICommercialService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<CreateInquiryRequest>(context, cancellationToken);
        if (request is null)
            return Problem(CommercialErrorCodes.ValidationFailed, correlationId);
        try
        {
            var result = await service.CreateInquiryAsync(request, correlationId, cancellationToken);
            return Results.Created($"/api/v1/inquiries/{result.InquiryId}", result);
        }
        catch (CommercialDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<IResult> GetInquiryAsync(
        string id,
        HttpContext context,
        ICommercialService service,
        CancellationToken cancellationToken)
    {
        var correlationId = Correlation(context);
        try
        {
            return Results.Json(
                await service.GetInquiryAsync(id, correlationId, cancellationToken),
                CommercialJson.Options);
        }
        catch (CommercialDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static Task<IResult> ResolveGapAsync(
        string id,
        string gapId,
        HttpContext context,
        ICommercialService service,
        CancellationToken cancellationToken) =>
        MutateInquiryAsync<ResolveInquiryGapRequest>(
            context,
            cancellationToken,
            body => service.ResolveGapAsync(id, gapId, body, Correlation(context), cancellationToken));

    private static Task<IResult> RecordReviewAsync(
        string id,
        HttpContext context,
        ICommercialService service,
        CancellationToken cancellationToken) =>
        MutateInquiryAsync<CapabilityReviewInput>(
            context,
            cancellationToken,
            body => service.RecordCapabilityReviewAsync(id, body, Correlation(context), cancellationToken));

    private static Task<IResult> CreateQuoteAsync(
        string id,
        HttpContext context,
        ICommercialService service,
        CancellationToken cancellationToken) =>
        MutateInquiryAsync<SubmitQuoteVersionRequest>(
            context,
            cancellationToken,
            body => service.CreateQuoteVersionAsync(id, body, Correlation(context), cancellationToken));

    private static Task<IResult> RecordChangeImpactAsync(
        string id,
        HttpContext context,
        ICommercialService service,
        CancellationToken cancellationToken) =>
        MutateInquiryAsync<RecordChangeImpactRequest>(
            context,
            cancellationToken,
            body => service.RecordChangeImpactAsync(id, body, Correlation(context), cancellationToken));

    private static async Task<IResult> MutateInquiryAsync<TRequest>(
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, Task<InquiryResult>> action)
        where TRequest : class
    {
        var correlationId = Correlation(context);
        var request = await ReadBodyAsync<TRequest>(context, cancellationToken);
        if (request is null)
            return Problem(CommercialErrorCodes.ValidationFailed, correlationId);
        try
        {
            return Results.Json(await action(request), CommercialJson.Options, statusCode: StatusCodes.Status201Created);
        }
        catch (CommercialDomainException exception)
        {
            return Problem(exception.ErrorCode, correlationId);
        }
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                context.Request.Body,
                CommercialJson.Options,
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
            CommercialErrorCodes.NotAuthorized => StatusCodes.Status403Forbidden,
            CommercialErrorCodes.ObjectNotAccessible => StatusCodes.Status404NotFound,
            CommercialErrorCodes.ExpectedVersionConflict => StatusCodes.Status409Conflict,
            CommercialErrorCodes.InquiryGapsOpen or CommercialErrorCodes.CapabilityReviewRequired or
                CommercialErrorCodes.CapabilityReviewBlocked => StatusCodes.Status422UnprocessableEntity,
            CommercialErrorCodes.PersistenceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            statusCode: statusCode,
            title: "Commercial request could not be processed",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = correlationId
            });
    }
}

internal static class CommercialJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
