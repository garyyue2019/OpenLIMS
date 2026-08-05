using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Commercial;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Commercial;

public interface ICommercialService
{
    Task<CatalogRecordResult> CreateCatalogAsync(
        SubmitCatalogRecordRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CatalogRecordResult> ReviseCatalogAsync(
        string recordId,
        SubmitCatalogRecordRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CatalogRecordResult> GetCatalogAsync(
        string recordId,
        long version,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InquiryResult> CreateInquiryAsync(
        CreateInquiryRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InquiryResult> GetInquiryAsync(
        string inquiryId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InquiryResult> ResolveGapAsync(
        string inquiryId,
        string gapId,
        ResolveInquiryGapRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InquiryResult> RecordCapabilityReviewAsync(
        string inquiryId,
        CapabilityReviewInput request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InquiryResult> CreateQuoteVersionAsync(
        string inquiryId,
        SubmitQuoteVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InquiryResult> RecordChangeImpactAsync(
        string inquiryId,
        RecordChangeImpactRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

internal sealed class CommercialService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    ICommercialAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    CommercialStore store,
    CommercialAttemptAuditWriter attemptAuditWriter,
    ILogger<CommercialService> logger) : ICommercialService
{
    public async Task<CatalogRecordResult> CreateCatalogAsync(
        SubmitCatalogRecordRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var recordId = ParseGeneratedId(idGenerator.NewId());
        var target = recordId.ToString("N");
        var actor = await RequireActorAsync("CreateCatalogRecord", target, correlationId, cancellationToken);
        try
        {
            var result = CommercialRules.CreateCatalog(target, request, actor.ActorId, clock.UtcNow);
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(actor, result.ObjectScope, CommercialCapabilities.Write, transactionToken);
                await store.InsertCatalogAsync(result, actor.OrganizationGroupId, correlationId, transactionToken);
            }, cancellationToken);
            CommercialTelemetry.RecordCatalog("create");
            return result;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("CreateCatalogRecord", actor, target, correlationId, exception, cancellationToken);
        }
    }

    public async Task<CatalogRecordResult> ReviseCatalogAsync(
        string recordId,
        SubmitCatalogRecordRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync("ReviseCatalogRecord", recordId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(recordId);
            CatalogRecordResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireLockAsync($"catalog:{id:N}", transactionToken);
                var current = await store.LoadCatalogAsync(actor.OrganizationGroupId, id, null, transactionToken)
                    ?? throw new CommercialDomainException(CommercialErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(actor, current.ObjectScope, CommercialCapabilities.Write, transactionToken);
                result = CommercialRules.ReviseCatalog(current, request, actor.ActorId, clock.UtcNow);
                await store.InsertCatalogAsync(result, actor.OrganizationGroupId, correlationId, transactionToken);
            }, cancellationToken);
            CommercialTelemetry.RecordCatalog("revise");
            return result ?? throw new InvalidOperationException("COM.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("ReviseCatalogRecord", actor, recordId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<CatalogRecordResult> GetCatalogAsync(
        string recordId,
        long version,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync("GetCatalogRecord", recordId, correlationId, cancellationToken);
        try
        {
            if (version < 1)
                throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
            var id = ParseId(recordId);
            CatalogRecordResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadCatalogAsync(actor.OrganizationGroupId, id, version, transactionToken)
                    ?? throw new CommercialDomainException(CommercialErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(actor, result.ObjectScope, CommercialCapabilities.Read, transactionToken);
                await store.WriteReadAuditAsync(
                    result.RecordId,
                    actor.OrganizationGroupId,
                    actor.ActorId,
                    "READ_CATALOG_VERSION",
                    result.Version.ToString(),
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("COM.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("GetCatalogRecord", actor, recordId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<InquiryResult> CreateInquiryAsync(
        CreateInquiryRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var inquiryId = ParseGeneratedId(idGenerator.NewId());
        var target = inquiryId.ToString("N");
        var actor = await RequireActorAsync("CreateInquiry", target, correlationId, cancellationToken);
        try
        {
            var result = CommercialRules.CreateInquiry(target, request, actor.ActorId, clock.UtcNow);
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(actor, result.ObjectScope, CommercialCapabilities.Write, transactionToken);
                await store.InsertInquiryAsync(
                    result,
                    actor.OrganizationGroupId,
                    correlationId,
                    "CommercialInquiryCreated",
                    transactionToken);
            }, cancellationToken);
            CommercialTelemetry.RecordInquiry("create");
            return result;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("CreateInquiry", actor, target, correlationId, exception, cancellationToken);
        }
    }

    public async Task<InquiryResult> GetInquiryAsync(
        string inquiryId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireActorAsync("GetInquiry", inquiryId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(inquiryId);
            InquiryResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadInquiryAsync(actor.OrganizationGroupId, id, transactionToken)
                    ?? throw new CommercialDomainException(CommercialErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(actor, result.ObjectScope, CommercialCapabilities.Read, transactionToken);
                await store.WriteReadAuditAsync(
                    result.InquiryId,
                    actor.OrganizationGroupId,
                    actor.ActorId,
                    "READ_INQUIRY",
                    result.Version.ToString(),
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("COM.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync("GetInquiry", actor, inquiryId, correlationId, exception, cancellationToken);
        }
    }

    public Task<InquiryResult> ResolveGapAsync(
        string inquiryId,
        string gapId,
        ResolveInquiryGapRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        MutateInquiryAsync(
            "ResolveInquiryGap",
            "CommercialInquiryGapResolved",
            inquiryId,
            correlationId,
            (current, actor, now) => CommercialRules.ResolveGap(current, gapId, request, actor, now),
            cancellationToken);

    public Task<InquiryResult> RecordCapabilityReviewAsync(
        string inquiryId,
        CapabilityReviewInput request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var reviewId = ParseGeneratedId(idGenerator.NewId()).ToString("N");
        return MutateInquiryAsync(
            "RecordCapabilityReview",
            "CommercialCapabilityReviewRecorded",
            inquiryId,
            correlationId,
            (current, actor, now) => CommercialRules.RecordReview(current, request, reviewId, actor, now),
            cancellationToken);
    }

    public Task<InquiryResult> CreateQuoteVersionAsync(
        string inquiryId,
        SubmitQuoteVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var quoteId = ParseGeneratedId(idGenerator.NewId()).ToString("N");
        return MutateInquiryAsync(
            "CreateQuoteVersion",
            "CommercialQuoteVersionRecorded",
            inquiryId,
            correlationId,
            (current, actor, now) => CommercialRules.AddQuote(current, request, quoteId, actor, now),
            cancellationToken);
    }

    public Task<InquiryResult> RecordChangeImpactAsync(
        string inquiryId,
        RecordChangeImpactRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var impactId = ParseGeneratedId(idGenerator.NewId()).ToString("N");
        return MutateInquiryAsync(
            "RecordChangeImpact",
            "CommercialChangeImpactRecorded",
            inquiryId,
            correlationId,
            (current, actor, now) => CommercialRules.AddChangeImpact(current, request, impactId, actor, now),
            cancellationToken);
    }

    private async Task<InquiryResult> MutateInquiryAsync(
        string commandType,
        string eventType,
        string inquiryId,
        string correlationId,
        Func<InquiryResult, string, DateTimeOffset, InquiryResult> mutate,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActorAsync(commandType, inquiryId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(inquiryId);
            InquiryResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireLockAsync($"inquiry:{id:N}", transactionToken);
                var current = await store.LoadInquiryAsync(actor.OrganizationGroupId, id, transactionToken)
                    ?? throw new CommercialDomainException(CommercialErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(actor, current.ObjectScope, CommercialCapabilities.Write, transactionToken);
                result = mutate(current, actor.ActorId, clock.UtcNow);
                await store.InsertInquiryAsync(
                    result,
                    actor.OrganizationGroupId,
                    correlationId,
                    eventType,
                    transactionToken);
            }, cancellationToken);
            CommercialTelemetry.RecordInquiry(commandType);
            return result ?? throw new InvalidOperationException("COM.RESULT_MISSING");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw await FailAsync(commandType, actor, inquiryId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<ActorScope> RequireActorAsync(
        string commandType,
        string? target,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is not null &&
            string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            return new ActorScope(organizationGroupId, actor.ActorId);
        }

        await WriteAttemptOrFailClosedAsync(
            commandType,
            actor?.ActorId,
            organizationGroupId,
            target,
            correlationId,
            CommercialErrorCodes.NotAuthorized,
            cancellationToken);
        throw new CommercialDomainException(CommercialErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        ActorScope actor,
        CommercialObjectContext scope,
        string capability,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new CommercialAuthorizationRequest(
            actor.OrganizationGroupId,
            actor.ActorId,
            scope,
            capability), cancellationToken);
        if (!decision.Allowed)
            throw new CommercialDomainException(CommercialErrorCodes.NotAuthorized);
    }

    private async Task<CommercialDomainException> FailAsync(
        string commandType,
        ActorScope actor,
        string? target,
        string correlationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var code = exception is CommercialDomainException domain
            ? domain.ErrorCode
            : CommercialErrorCodes.PersistenceUnavailable;
        CommercialTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Commercial command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType,
            code,
            correlationId);
        await WriteAttemptOrFailClosedAsync(
            commandType,
            actor.ActorId,
            actor.OrganizationGroupId,
            target,
            correlationId,
            code,
            cancellationToken);
        return new CommercialDomainException(code);
    }

    private async Task WriteAttemptOrFailClosedAsync(
        string commandType,
        string? actorId,
        string organizationGroupId,
        string? target,
        string correlationId,
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                commandType,
                actorId,
                organizationGroupId,
                CommercialRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId,
                code,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new CommercialDomainException(CommercialErrorCodes.PersistenceUnavailable);
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is CommercialDomainException or NpgsqlException or InvalidOperationException;

    private static Guid ParseGeneratedId(string value) =>
        Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("COM.ID_GENERATOR_INVALID");

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new CommercialDomainException(CommercialErrorCodes.ObjectNotAccessible);

    private sealed record ActorScope(string OrganizationGroupId, string ActorId);
}
