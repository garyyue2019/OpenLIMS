using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Billing;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Billing;

public interface IBillingIntegrationService
{
    Task<BillingExportBatchResult> CreateExportBatchAsync(CreateBillingExportBatchRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<BillingExportBatchResult> GetExportBatchAsync(string batchId, string correlationId, CancellationToken cancellationToken = default);
    Task<BillingHandoffResult> CreateHandoffAsync(string batchId, CreateBillingHandoffRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<BillingHandoffResult> GetHandoffAsync(string handoffId, string correlationId, CancellationToken cancellationToken = default);
    Task<BillingHandoffAttemptResult> RecordHandoffAttemptAsync(string handoffId, RecordBillingHandoffAttemptRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<BillingDifferenceQueueResult> GetDifferenceQueueAsync(string? externalSystem, string correlationId, CancellationToken cancellationToken = default);
}

internal sealed class BillingIntegrationService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IBillingAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    BillingStore billingStore,
    BillingIntegrationStore integrationStore,
    BillingAttemptAuditWriter attemptAuditWriter,
    ILogger<BillingIntegrationService> logger) : IBillingIntegrationService
{
    public async Task<BillingExportBatchResult> CreateExportBatchAsync(
        CreateBillingExportBatchRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync("new-export-batch", correlationId, cancellationToken);
        try
        {
            var validated = BillingIntegrationRules.ValidateExport(request);
            var requestHash = BillingIntegrationRules.RequestHash(validated);
            BillingExportBatchResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await integrationStore.AcquireKeyLockAsync(
                    "export", $"{organizationGroupId}:{validated.IdempotencyKey}", transactionToken);
                var existing = await integrationStore.LoadBatchByIdempotencyAsync(
                    organizationGroupId, validated.IdempotencyKey, transactionToken);
                if (existing is not null)
                {
                    await AuthorizeAsync(
                        organizationGroupId, actorId, existing.Batch.ObjectScope,
                        BillingCapabilities.Integrate, transactionToken);
                    if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                        throw new BillingDomainException(BillingErrorCodes.IdempotencyConflict);
                    await integrationStore.WriteReadAuditAsync(
                        existing.Batch.BatchId, organizationGroupId, actorId,
                        "RETRY_BILLING_EXPORT_BATCH", BillingContract.ExportRuleSetVersion,
                        correlationId, clock.UtcNow, transactionToken);
                    result = existing.Batch;
                    return;
                }

                var evidence = new List<BillingEvidenceResult>();
                foreach (var evidenceId in validated.BillingEvidenceIds)
                {
                    var item = await billingStore.LoadEvidenceAsync(
                        organizationGroupId, Guid.Parse(evidenceId), transactionToken)
                        ?? throw new BillingDomainException(BillingErrorCodes.ObjectNotAccessible);
                    await AuthorizeAsync(
                        organizationGroupId, actorId, item.ObjectScope,
                        BillingCapabilities.Integrate, transactionToken);
                    evidence.Add(item);
                }

                var items = BillingIntegrationRules.BuildItems(evidence);
                var objectScope = evidence[0].ObjectScope;
                var canonical = BillingIntegrationRules.Canonicalize(
                    objectScope, validated.ExportSchemaVersion, items);
                result = await integrationStore.InsertBatchAsync(
                    Guid.Parse(idGenerator.NewId()), organizationGroupId, validated, objectScope,
                    items, canonical, BillingIntegrationRules.ComputeHash(canonical), requestHash,
                    actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("BIL.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BillingDomainException or NpgsqlException)
        {
            throw await FailAsync("CreateBillingExportBatch", actorId, organizationGroupId,
                "new-export-batch", correlationId, exception, cancellationToken);
        }
    }

    public async Task<BillingExportBatchResult> GetExportBatchAsync(
        string batchId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(batchId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(batchId);
            BillingExportBatchResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var batch = await integrationStore.LoadBatchAsync(organizationGroupId, id, transactionToken)
                    ?? throw new BillingDomainException(BillingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, batch.Batch.ObjectScope,
                    BillingCapabilities.Integrate, transactionToken);
                await integrationStore.WriteReadAuditAsync(
                    batch.Batch.BatchId, organizationGroupId, actorId, "READ_BILLING_EXPORT_BATCH",
                    BillingContract.ExportRuleSetVersion, correlationId, clock.UtcNow, transactionToken);
                result = batch.Batch;
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("BIL.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BillingDomainException or NpgsqlException)
        {
            throw await FailAsync("GetBillingExportBatch", actorId, organizationGroupId,
                batchId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<BillingHandoffResult> CreateHandoffAsync(
        string batchId,
        CreateBillingHandoffRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(batchId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(batchId);
            var validated = BillingIntegrationRules.ValidateHandoff(request);
            BillingHandoffResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await integrationStore.AcquireKeyLockAsync(
                    "handoff", $"{organizationGroupId}:{validated.IdempotencyKey}", transactionToken);
                var batch = await integrationStore.LoadBatchAsync(organizationGroupId, id, transactionToken)
                    ?? throw new BillingDomainException(BillingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, batch.Batch.ObjectScope,
                    BillingCapabilities.Integrate, transactionToken);
                var existing = await integrationStore.LoadHandoffByIdempotencyAsync(
                    organizationGroupId, validated.IdempotencyKey, transactionToken);
                if (existing is not null)
                {
                    if (!Matches(existing.Handoff, id, validated))
                        throw new BillingDomainException(BillingErrorCodes.IdempotencyConflict);
                    await integrationStore.WriteReadAuditAsync(
                        existing.Handoff.HandoffId, organizationGroupId, actorId,
                        "RETRY_BILLING_HANDOFF", BillingContract.HandoffRuleSetVersion,
                        correlationId, clock.UtcNow, transactionToken);
                    result = existing.Handoff;
                    return;
                }
                result = await integrationStore.InsertHandoffAsync(
                    Guid.Parse(idGenerator.NewId()), batch, validated, actorId,
                    clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("BIL.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BillingDomainException or NpgsqlException)
        {
            throw await FailAsync("CreateBillingHandoff", actorId, organizationGroupId,
                batchId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<BillingHandoffResult> GetHandoffAsync(
        string handoffId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(handoffId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(handoffId);
            BillingHandoffResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var handoff = await integrationStore.LoadHandoffAsync(organizationGroupId, id, transactionToken)
                    ?? throw new BillingDomainException(BillingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, handoff.ObjectScope,
                    BillingCapabilities.Integrate, transactionToken);
                await integrationStore.WriteReadAuditAsync(
                    handoff.Handoff.HandoffId, organizationGroupId, actorId, "READ_BILLING_HANDOFF",
                    BillingContract.HandoffRuleSetVersion, correlationId, clock.UtcNow, transactionToken);
                result = handoff.Handoff;
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("BIL.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BillingDomainException or NpgsqlException)
        {
            throw await FailAsync("GetBillingHandoff", actorId, organizationGroupId,
                handoffId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<BillingHandoffAttemptResult> RecordHandoffAttemptAsync(
        string handoffId,
        RecordBillingHandoffAttemptRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(handoffId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(handoffId);
            BillingHandoffAttemptResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await integrationStore.AcquireKeyLockAsync("handoff-attempt", id.ToString("N"), transactionToken);
                var handoff = await integrationStore.LoadHandoffAsync(organizationGroupId, id, transactionToken)
                    ?? throw new BillingDomainException(BillingErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, handoff.ObjectScope,
                    BillingCapabilities.Integrate, transactionToken);
                var validated = BillingIntegrationRules.ValidateAttempt(handoff.Handoff.ExternalSystem, request);
                var existing = await integrationStore.LoadHandoffAttemptByIdempotencyAsync(
                    id, validated.IdempotencyKey, transactionToken);
                if (existing is not null)
                {
                    if (!Matches(existing, validated))
                        throw new BillingDomainException(BillingErrorCodes.IdempotencyConflict);
                    await integrationStore.WriteReadAuditAsync(
                        existing.AttemptId, organizationGroupId, actorId,
                        "RETRY_BILLING_HANDOFF_ATTEMPT", BillingContract.HandoffRuleSetVersion,
                        correlationId, clock.UtcNow, transactionToken);
                    result = existing;
                    return;
                }
                if (string.Equals(handoff.Handoff.Status, BillingHandoffOutcomes.Succeeded, StringComparison.Ordinal))
                    throw new BillingDomainException(BillingErrorCodes.HandoffAlreadyCompleted);
                result = await integrationStore.InsertHandoffAttemptAsync(
                    Guid.Parse(idGenerator.NewId()), handoff, validated, actorId,
                    clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("BIL.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BillingDomainException or NpgsqlException)
        {
            throw await FailAsync("RecordBillingHandoffAttempt", actorId, organizationGroupId,
                handoffId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<BillingDifferenceQueueResult> GetDifferenceQueueAsync(
        string? externalSystem,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync("difference-queue", correlationId, cancellationToken);
        try
        {
            var normalizedSystem = string.IsNullOrWhiteSpace(externalSystem) ? null : externalSystem.Trim();
            if (normalizedSystem is not null &&
                !BillingExternalSystems.All.Contains(normalizedSystem, StringComparer.Ordinal))
            {
                throw new BillingDomainException(BillingErrorCodes.ValidationFailed);
            }
            BillingDifferenceQueueResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var candidates = await integrationStore.LoadDifferenceCandidatesAsync(
                    organizationGroupId, normalizedSystem, transactionToken);
                var visible = new List<BillingHandoffResult>();
                foreach (var candidate in candidates)
                {
                    var decision = await authorizationPort.AuthorizeAsync(new BillingAuthorizationRequest(
                        organizationGroupId, actorId, candidate.ObjectScope,
                        BillingCapabilities.Integrate), transactionToken);
                    if (decision.Allowed)
                        visible.Add(candidate.Handoff);
                }
                await integrationStore.WriteReadAuditAsync(
                    "difference-queue", organizationGroupId, actorId, "READ_BILLING_DIFFERENCE_QUEUE",
                    BillingContract.HandoffRuleSetVersion, correlationId, clock.UtcNow, transactionToken);
                result = new BillingDifferenceQueueResult(visible, BillingContract.HandoffRuleSetVersion);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("BIL.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BillingDomainException or NpgsqlException)
        {
            throw await FailAsync("GetBillingDifferenceQueue", actorId, organizationGroupId,
                "difference-queue", correlationId, exception, cancellationToken);
        }
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
        string? target,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is not null && string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
            return (organizationGroupId, actor.ActorId);
        await WriteAttemptOrFailClosedAsync(
            "BillingIntegrationCommand", actor?.ActorId, organizationGroupId, target,
            correlationId, BillingErrorCodes.NotAuthorized, cancellationToken);
        throw new BillingDomainException(BillingErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId,
        string actorId,
        BillingObjectContext objectScope,
        string capability,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new BillingAuthorizationRequest(
            organizationGroupId, actorId, objectScope, capability), cancellationToken);
        if (!decision.Allowed)
            throw new BillingDomainException(BillingErrorCodes.NotAuthorized);
    }

    private async Task<BillingDomainException> FailAsync(
        string commandType,
        string actorId,
        string organizationGroupId,
        string? target,
        string correlationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var code = exception switch
        {
            BillingDomainException domain => domain.ErrorCode,
            PostgresException { SqlState: "23505" } => BillingErrorCodes.IdempotencyConflict,
            _ => BillingErrorCodes.PersistenceUnavailable
        };
        BillingTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Billing integration command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType, code, correlationId);
        await WriteAttemptOrFailClosedAsync(
            commandType, actorId, organizationGroupId, target, correlationId, code, cancellationToken);
        return new BillingDomainException(code);
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
                commandType, actorId, organizationGroupId,
                BillingRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new BillingDomainException(BillingErrorCodes.PersistenceUnavailable);
        }
    }

    private static bool Matches(BillingHandoffResult handoff, Guid batchId, CreateBillingHandoffRequest request) =>
        string.Equals(handoff.BatchId, batchId.ToString("N"), StringComparison.Ordinal) &&
        string.Equals(handoff.ExternalSystem, request.ExternalSystem, StringComparison.Ordinal) &&
        string.Equals(handoff.Mode, request.Mode, StringComparison.Ordinal) &&
        handoff.Endpoint == request.Endpoint;

    private static bool Matches(
        BillingHandoffAttemptResult attempt,
        RecordBillingHandoffAttemptRequest request) =>
        string.Equals(attempt.Outcome, request.Outcome, StringComparison.Ordinal) &&
        string.Equals(attempt.ExternalReference, request.ExternalReference, StringComparison.Ordinal) &&
        string.Equals(attempt.DetailCode, request.DetailCode, StringComparison.Ordinal) &&
        attempt.ErpPosting == request.ErpPosting;

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new BillingDomainException(BillingErrorCodes.ObjectNotAccessible);
}
