using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Batch;

public interface IBatchService
{
    Task<BatchResult> CreateAsync(CreateBatchRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<BatchMemberResult> AddMemberAsync(string batchId, AddBatchMemberRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<BatchEvidenceResult> AddEvidenceAsync(string batchId, AddBatchEvidenceRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<BatchFreezeResult> FreezeAsync(string batchId, FreezeBatchRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<BatchResult> GetAsync(string batchId, string correlationId, CancellationToken cancellationToken = default);
}

internal sealed class BatchService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IBatchAuthorizationPort authorizationPort,
    IAllocationStatusPort allocationStatusPort,
    ITransactionCoordinator transactionCoordinator,
    BatchStore store,
    BatchAttemptAuditWriter attemptAuditWriter,
    ILogger<BatchService> logger) : IBatchService
{
    public async Task<BatchResult> CreateAsync(
        CreateBatchRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var batchId = Guid.Parse(idGenerator.NewId());
        var (organizationGroupId, actorId) = await RequireActorAsync(batchId.ToString("N"), correlationId, cancellationToken);
        try
        {
            BatchRules.RequireRuleSet(request?.RuleSetVersion);
            var objectScope = BatchRules.NormalizeObjectScope(request?.ObjectScope);
            var batchType = BatchRules.ValidateBatchType(request?.BatchType);
            BatchResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(organizationGroupId, actorId, objectScope, transactionToken);
                result = await store.InsertBatchAsync(
                    batchId, organizationGroupId, objectScope, batchType,
                    actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            BatchTelemetry.RecordCreated(batchType);
            return result ?? throw new InvalidOperationException("BAT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BatchDomainException or NpgsqlException)
        {
            throw await FailAsync("CreateBatch", actorId, organizationGroupId,
                batchId.ToString("N"), correlationId, exception, cancellationToken);
        }
    }

    public async Task<BatchMemberResult> AddMemberAsync(
        string batchId, AddBatchMemberRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(batchId, correlationId, cancellationToken);
        try
        {
            var id = ParseBatchId(batchId);
            var validated = BatchRules.ValidateMember(request);

            string? gateDecision = null;
            string? gateRuleSetVersion = null;
            if (string.Equals(validated.MemberType, BatchMemberTypes.Specimen, StringComparison.Ordinal))
            {
                var gate = await EvaluateAllocationGateAsync(organizationGroupId, validated, correlationId, cancellationToken);
                gateDecision = gate.Decision;
                gateRuleSetVersion = gate.RuleSetVersion;
            }

            BatchMemberResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireBatchLockAsync(id, transactionToken);
                var batch = await store.LoadBatchAsync(organizationGroupId, id, transactionToken)
                    ?? throw new BatchDomainException(BatchErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, batch.ObjectScope, transactionToken);
                BatchRules.RequireActive(batch);
                BatchRules.RequireVersion(validated.ExpectedCurrentVersion, batch.Version);
                if (validated.AllocationId is not null &&
                    batch.Members.Any(member => string.Equals(member.AllocationId, validated.AllocationId, StringComparison.Ordinal)))
                {
                    throw new BatchDomainException(BatchErrorCodes.ValidationFailed);
                }

                result = await store.InsertMemberAsync(
                    id, batch.Version + 1, organizationGroupId, validated,
                    gateDecision, gateRuleSetVersion, actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            BatchTelemetry.RecordMember(validated.MemberType);
            return result ?? throw new InvalidOperationException("BAT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BatchDomainException or NpgsqlException)
        {
            throw await FailAsync("AddBatchMember", actorId, organizationGroupId,
                batchId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<BatchEvidenceResult> AddEvidenceAsync(
        string batchId, AddBatchEvidenceRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(batchId, correlationId, cancellationToken);
        try
        {
            var id = ParseBatchId(batchId);
            var validated = BatchRules.ValidateEvidence(request);
            BatchEvidenceResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireBatchLockAsync(id, transactionToken);
                var batch = await store.LoadBatchAsync(organizationGroupId, id, transactionToken)
                    ?? throw new BatchDomainException(BatchErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, batch.ObjectScope, transactionToken);
                BatchRules.RequireActive(batch);
                BatchRules.RequireVersion(validated.ExpectedCurrentVersion, batch.Version);
                result = await store.InsertEvidenceAsync(
                    id, batch.Version + 1, organizationGroupId, validated,
                    actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("BAT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BatchDomainException or NpgsqlException)
        {
            throw await FailAsync("AddBatchEvidence", actorId, organizationGroupId,
                batchId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<BatchFreezeResult> FreezeAsync(
        string batchId, FreezeBatchRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(batchId, correlationId, cancellationToken);
        try
        {
            var id = ParseBatchId(batchId);
            var validated = BatchRules.ValidateFreeze(request);
            BatchFreezeResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireBatchLockAsync(id, transactionToken);
                var batch = await store.LoadBatchAsync(organizationGroupId, id, transactionToken)
                    ?? throw new BatchDomainException(BatchErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, batch.ObjectScope, transactionToken);
                BatchRules.RequireActive(batch);
                BatchRules.RequireVersion(validated.ExpectedCurrentVersion, batch.Version);
                result = await store.InsertFreezeAsync(
                    id, batch.Version + 1, organizationGroupId, validated,
                    batch.Members.Count, actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            BatchTelemetry.RecordFrozen(validated.Cause);
            return result ?? throw new InvalidOperationException("BAT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BatchDomainException or NpgsqlException)
        {
            throw await FailAsync("FreezeBatch", actorId, organizationGroupId,
                batchId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<BatchResult> GetAsync(
        string batchId, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(batchId, correlationId, cancellationToken);
        try
        {
            var id = ParseBatchId(batchId);
            BatchResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadBatchAsync(organizationGroupId, id, transactionToken)
                    ?? throw new BatchDomainException(BatchErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, result.ObjectScope, transactionToken);
                await store.WriteReadAuditAsync(
                    result.BatchId, result.Version, organizationGroupId, actorId,
                    "READ_BATCH", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("BAT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is BatchDomainException or NpgsqlException)
        {
            throw await FailAsync("GetBatch", actorId, organizationGroupId,
                batchId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<AllocationStatusResult> EvaluateAllocationGateAsync(
        string organizationGroupId,
        AddBatchMemberRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        AllocationStatusResult result;
        try
        {
            result = await allocationStatusPort.EvaluateAsync(new AllocationStatusRequest(
                organizationGroupId,
                request.AllocationId!,
                request.ExpectedSubjectAllocationVersion!.Value,
                AllocationContract.RuleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            BatchTelemetry.RecordGate("UNKNOWN");
            throw new BatchDomainException(BatchErrorCodes.ApplicabilityUnknown, "ALLOCATION");
        }

        BatchTelemetry.RecordGate(result.Decision);
        return result.Decision switch
        {
            AllocationStatusDecisions.Allowed => result,
            AllocationStatusDecisions.Blocked =>
                throw new BatchDomainException(BatchErrorCodes.EligibilityBlocked, "ALLOCATION"),
            _ => throw new BatchDomainException(BatchErrorCodes.ApplicabilityUnknown, "ALLOCATION")
        };
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
        string? target, string correlationId, CancellationToken cancellationToken)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is not null &&
            string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            return (organizationGroupId, actor.ActorId);
        }

        await WriteAttemptOrFailClosedAsync("BatchCommand", actor?.ActorId, organizationGroupId,
            target, correlationId, BatchErrorCodes.NotAuthorized, cancellationToken);
        throw new BatchDomainException(BatchErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId, string actorId, BatchObjectContext objectScope, CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new BatchAuthorizationRequest(
            organizationGroupId, actorId, objectScope, BatchCapabilities.Manage), cancellationToken);
        if (!decision.Allowed)
            throw new BatchDomainException(BatchErrorCodes.NotAuthorized);
    }

    private async Task<BatchDomainException> FailAsync(
        string commandType, string actorId, string organizationGroupId,
        string? target, string correlationId, Exception exception, CancellationToken cancellationToken)
    {
        var (code, gateSource) = exception switch
        {
            BatchDomainException domain => (domain.ErrorCode, domain.GateSource),
            PostgresException { SqlState: "23505" } => (BatchErrorCodes.ValidationFailed, null),
            _ => (BatchErrorCodes.PersistenceUnavailable, (string?)null)
        };
        BatchTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Batch command {CommandType} failed closed with {ErrorCode} (gate {GateSource}); correlation {CorrelationId}",
            commandType, code, gateSource ?? "-", correlationId);
        await WriteAttemptOrFailClosedAsync(commandType, actorId, organizationGroupId,
            target, correlationId, code, cancellationToken);
        return new BatchDomainException(code, gateSource);
    }

    private async Task WriteAttemptOrFailClosedAsync(
        string commandType, string? actorId, string organizationGroupId,
        string? target, string correlationId, string code, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(commandType, actorId, organizationGroupId,
                BatchRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new BatchDomainException(BatchErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseBatchId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new BatchDomainException(BatchErrorCodes.ObjectNotAccessible);
}

internal sealed class BatchStatusPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IBatchAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    BatchStore store,
    BatchAttemptAuditWriter attemptAuditWriter,
    ILogger<BatchStatusPort> logger) : IBatchStatusPort
{
    public async ValueTask<BatchStatusResult> EvaluateAsync(
        BatchStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;
        if (actor is null ||
            !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal) ||
            !string.Equals(request.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor?.ActorId, organizationGroupId, request.BatchId, correlationId, cancellationToken);
            throw new BatchDomainException(BatchErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.BatchId, "N", out var batchId) &&
            !Guid.TryParse(request.BatchId, out batchId))
        {
            return Record(BatchRules.EvaluateStatus(request, null));
        }

        try
        {
            BatchStatusResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var batch = await store.LoadBatchAsync(organizationGroupId, batchId, transactionToken);
                if (batch is null)
                {
                    result = BatchRules.EvaluateStatus(request, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new BatchAuthorizationRequest(
                    organizationGroupId, actor.ActorId, batch.ObjectScope, BatchCapabilities.Manage), transactionToken);
                if (!authorization.Allowed)
                    throw new BatchDomainException(BatchErrorCodes.NotAuthorized);

                result = BatchRules.EvaluateStatus(request, batch);
                await store.WriteReadAuditAsync(
                    batch.BatchId, batch.Version, organizationGroupId, actor.ActorId,
                    "EVALUATE_BATCH_STATUS", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return Record(result ?? BatchRules.EvaluateStatus(request, null));
        }
        catch (BatchDomainException exception)
            when (string.Equals(exception.ErrorCode, BatchErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor.ActorId, organizationGroupId, request.BatchId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Batch status failed closed because persistence is unavailable");
            return Record(new BatchStatusResult(
                BatchStatusDecisions.Unknown,
                [BatchStatusReasons.BatchUnavailable],
                request.BatchId, null, null, BatchContract.RuleSetVersion));
        }
    }

    private BatchStatusResult Record(BatchStatusResult result)
    {
        BatchTelemetry.RecordGate(result.Decision);
        if (string.Equals(result.Decision, BatchStatusDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Batch status failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId, string organizationGroupId, string target, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync("EvaluateBatchStatus", actorId, organizationGroupId,
                BatchRules.HashTarget(target), correlationId, BatchErrorCodes.NotAuthorized,
                clock.UtcNow, cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new BatchDomainException(BatchErrorCodes.PersistenceUnavailable);
        }
    }
}
