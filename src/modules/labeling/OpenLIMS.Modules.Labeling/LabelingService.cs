using Npgsql;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Labeling;

internal interface ILabelingService
{
    Task<CreateLabelJobsResult> CreateAsync(
        CreateLabelJobsRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<LabelPrintJobResult> GetAsync(
        string printJobId,
        CancellationToken cancellationToken = default);

    Task<CreateLabelJobsResult> ReprintAsync(
        string printJobId,
        ReprintLabelRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<LabelScanResolution> ResolveScanAsync(
        ResolveLabelScanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

internal sealed class LabelingService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    ITransactionCoordinator transactionCoordinator,
    IReceivingLabelObjectPort receivingObjects,
    ILabelingAuthorization authorization,
    LabelPrinterRegistry printers,
    LabelingStore store) : ILabelingService
{
    public async Task<CreateLabelJobsResult> CreateAsync(
        CreateLabelJobsRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        LabelingRules.ValidateCreate(request, idempotencyKey);
        var (organizationGroupId, actorId) = RequireActor();
        var printer = printers.Require(request.PrinterId);
        var snapshots = new List<ReceivingLabelObjectSnapshot>(request.Targets.Count);
        try
        {
            foreach (var target in request.Targets)
            {
                var snapshot = await receivingObjects.GetAsync(organizationGroupId, target.ObjectType, target.ObjectId, cancellationToken)
                    ?? throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
                EnsureObjectAccess(snapshot, ReceivingCapabilities.LabelPrint, organizationGroupId);
                if (snapshot.ObjectVersion != target.ObjectVersion)
                {
                    throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
                }

                EnsurePrinterScope(printer, snapshot);
                snapshots.Add(snapshot);
            }
        }
        catch (NpgsqlException)
        {
            throw new LabelingDomainException(LabelingErrorCodes.PersistenceUnavailable);
        }

        var keyHash = LabelingRules.Hash(idempotencyKey);
        var requestHash = LabelingRules.RequestHash(request);
        CreateLabelJobsResult? result = null;
        try
        {
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var reservation = await store.ReserveIdempotencyAsync(
                    organizationGroupId,
                    actorId,
                    keyHash,
                    requestHash,
                    clock.UtcNow,
                    transactionToken);
                if (reservation.Kind == LabelIdempotencyKind.Conflict)
                {
                    throw new LabelingDomainException(LabelingErrorCodes.IdempotencyConflict);
                }

                if (reservation.Kind == LabelIdempotencyKind.Replay)
                {
                    result = reservation.Result;
                    return;
                }

                var jobs = new List<LabelPrintJobResult>(snapshots.Count);
                foreach (var snapshot in snapshots)
                {
                    jobs.Add(await store.InsertInitialJobAsync(
                        NextGuid(),
                        snapshot,
                        printer,
                        actorId,
                        keyHash,
                        correlationId,
                        clock.UtcNow,
                        transactionToken));
                }

                result = new CreateLabelJobsResult(jobs);
                await store.CompleteIdempotencyAsync(
                    organizationGroupId,
                    keyHash,
                    result,
                    transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("LABEL.CREATE_RESULT_MISSING");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new LabelingDomainException(LabelingErrorCodes.IdempotencyConflict);
        }
        catch (NpgsqlException)
        {
            throw new LabelingDomainException(LabelingErrorCodes.PersistenceUnavailable);
        }
    }

    public async Task<LabelPrintJobResult> GetAsync(
        string printJobId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, _) = RequireActor();
        if (!Guid.TryParse(printJobId, out var id))
        {
            throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
        }

        try
        {
            var record = await store.GetRecordAsync(id, organizationGroupId, cancellationToken)
                ?? throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
            var snapshot = await receivingObjects.GetAsync(organizationGroupId, record.ObjectType, record.ObjectId.ToString("N"), cancellationToken)
                ?? throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
            EnsureObjectAccess(snapshot, ReceivingCapabilities.LabelPrint, organizationGroupId);
            var reprints = await store.CountSuccessfulReprintsAsync(
                organizationGroupId,
                record.ObjectType,
                record.ObjectId,
                cancellationToken);
            var result = LabelingStore.ToResult(record);
            return result with { SuccessfulReprintCount = reprints };
        }
        catch (NpgsqlException)
        {
            throw new LabelingDomainException(LabelingErrorCodes.PersistenceUnavailable);
        }
    }

    public async Task<CreateLabelJobsResult> ReprintAsync(
        string printJobId,
        ReprintLabelRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        LabelingRules.ValidateIdempotencyKey(idempotencyKey);
        var reason = LabelingRules.ValidateReason(request.Reason);
        var (organizationGroupId, actorId) = RequireActor();
        if (!Guid.TryParse(printJobId, out var sourceId))
        {
            throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
        }

        LabelPrintJobRecord source;
        ReceivingLabelObjectSnapshot snapshot;
        try
        {
            source = await store.GetRecordAsync(sourceId, organizationGroupId, cancellationToken)
                ?? throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
            snapshot = await receivingObjects.GetAsync(organizationGroupId, source.ObjectType, source.ObjectId.ToString("N"), cancellationToken)
                ?? throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
        }
        catch (NpgsqlException)
        {
            throw new LabelingDomainException(LabelingErrorCodes.PersistenceUnavailable);
        }
        EnsureObjectAccess(snapshot, ReceivingCapabilities.LabelReprint, organizationGroupId);
        var printer = printers.Require(request.PrinterId);
        EnsurePrinterScope(printer, snapshot);
        var hasOverride = authorization.HasCapability(ReceivingCapabilities.LabelReprintOverride);
        var keyHash = LabelingRules.Hash(idempotencyKey);
        var requestHash = LabelingRules.RequestHash(new { printJobId, request.PrinterId, reason });
        CreateLabelJobsResult? result = null;
        try
        {
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var reservation = await store.ReserveIdempotencyAsync(
                    organizationGroupId,
                    actorId,
                    keyHash,
                    requestHash,
                    clock.UtcNow,
                    transactionToken);
                if (reservation.Kind == LabelIdempotencyKind.Conflict)
                {
                    throw new LabelingDomainException(LabelingErrorCodes.IdempotencyConflict);
                }

                if (reservation.Kind == LabelIdempotencyKind.Replay)
                {
                    result = reservation.Result;
                    return;
                }

                var job = await store.InsertReprintJobAsync(
                    NextGuid(),
                    sourceId,
                    snapshot,
                    printer,
                    actorId,
                    reason,
                    keyHash,
                    correlationId,
                    clock.UtcNow,
                    hasOverride,
                    transactionToken);
                result = new CreateLabelJobsResult([job]);
                await store.CompleteIdempotencyAsync(organizationGroupId, keyHash, result, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("LABEL.REPRINT_RESULT_MISSING");
        }
        catch (NpgsqlException)
        {
            throw new LabelingDomainException(LabelingErrorCodes.PersistenceUnavailable);
        }
    }

    public async Task<LabelScanResolution> ResolveScanAsync(
        ResolveLabelScanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (organizationGroupId, actorId) = RequireActor();
        var payload = request.BarcodePayload?.Trim() ?? string.Empty;
        if (!LabelBarcodeCodec.TryParse(payload, out var barcode, out var parseError))
        {
            await WriteScanAttemptOrFailClosedAsync(
                actorId,
                organizationGroupId,
                payload,
                parseError,
                correlationId,
                cancellationToken);
            throw new LabelingDomainException(parseError);
        }

        ReceivingLabelObjectSnapshot? snapshot;
        try
        {
            snapshot = await receivingObjects.ResolveAsync(
                organizationGroupId,
                barcode!.ObjectType,
                barcode.OpaqueReference.ToString("N"),
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new LabelingDomainException(LabelingErrorCodes.PersistenceUnavailable);
        }
        if (snapshot is null ||
            !string.Equals(snapshot.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal) ||
            !authorization.IsAuthorized(snapshot, ReceivingCapabilities.LabelScan))
        {
            await WriteScanAttemptOrFailClosedAsync(
                actorId,
                organizationGroupId,
                payload,
                LabelingErrorCodes.ObjectNotAccessible,
                correlationId,
                cancellationToken);
            throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
        }

        string verification;
        try
        {
            verification = await store.VerifyLatestAsync(
                snapshot,
                actorId,
                correlationId,
                clock.UtcNow,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new LabelingDomainException(LabelingErrorCodes.PersistenceUnavailable);
        }
        var actions = new List<string>();
        if (authorization.IsAuthorized(snapshot, ReceivingCapabilities.LabelPrint))
        {
            actions.Add("print");
        }

        if (authorization.IsAuthorized(snapshot, ReceivingCapabilities.LabelReprint))
        {
            actions.Add("reprint");
        }

        return new LabelScanResolution(
            snapshot.ObjectType,
            snapshot.ObjectId,
            snapshot.BusinessNumber,
            snapshot.State,
            verification,
            actions);
    }

    private (string OrganizationGroupId, string ActorId) RequireActor()
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is null || !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
        }

        return (organizationGroupId, actor.ActorId);
    }

    private void EnsureObjectAccess(
        ReceivingLabelObjectSnapshot snapshot,
        string capability,
        string organizationGroupId)
    {
        if (!string.Equals(snapshot.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal) ||
            !authorization.IsAuthorized(snapshot, capability))
        {
            throw new LabelingDomainException(LabelingErrorCodes.ObjectNotAccessible);
        }
    }

    private static void EnsurePrinterScope(
        LogicalLabelPrinter printer,
        ReceivingLabelObjectSnapshot snapshot)
    {
        if (!string.Equals(printer.LaboratoryId, snapshot.LaboratoryId, StringComparison.Ordinal))
        {
            throw new LabelingDomainException(LabelingErrorCodes.PrinterScopeMismatch);
        }
    }

    private Guid NextGuid()
    {
        var value = idGenerator.NewId();
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("LABEL.ID_GENERATOR_INVALID");
    }

    private async Task WriteScanAttemptOrFailClosedAsync(
        string actorId,
        string organizationGroupId,
        string payload,
        string decisionCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.WriteScanAttemptAsync(
                actorId,
                organizationGroupId,
                LabelingRules.Hash(string.IsNullOrWhiteSpace(payload) ? "empty" : payload),
                decisionCode,
                correlationId,
                clock.UtcNow,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new LabelingDomainException(LabelingErrorCodes.PersistenceUnavailable);
        }
    }
}
