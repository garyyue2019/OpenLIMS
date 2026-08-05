using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Ai;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Ai;

public interface IAiRunService
{
    Task<AiRunResult> CreateAsync(
        CreateAiRunRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<AiRunResult> GetAsync(
        string runId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<AiReviewDispositionResult> RecordDispositionAsync(
        string runId,
        RecordAiDispositionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<AiReviewQueueResult> GetReviewQueueAsync(
        string? status,
        string correlationId,
        CancellationToken cancellationToken = default);
}

internal sealed class AiRunService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    ITransactionCoordinator transactionCoordinator,
    IAiAuthorizationPort authorizationPort,
    IAiProviderPort providerPort,
    IAiOutputValidator outputValidator,
    AiStore store,
    AiAttemptAuditWriter attemptAuditWriter,
    ILogger<AiRunService> logger) : IAiRunService
{
    public async Task<AiRunResult> CreateAsync(
        CreateAiRunRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            request?.IdempotencyKey, correlationId, cancellationToken);
        StoredAiRunRequest? stored = null;
        AiRunResult? result = null;
        try
        {
            var validated = AiRuntimeRules.ValidateRun(request, outputValidator);
            var requestHash = AiRuntimeRules.RequestHash(validated);
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireKeyLockAsync(
                    "request", $"{organizationGroupId}:{validated.IdempotencyKey}", transactionToken);
                await AuthorizeAsync(
                    organizationGroupId, actorId, validated.ObjectScope, AiCapabilities.Run, transactionToken);

                var existing = await store.LoadRequestByIdempotencyAsync(
                    organizationGroupId, validated.IdempotencyKey, transactionToken);
                if (existing is not null)
                {
                    if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                        throw new AiDomainException(AiErrorCodes.IdempotencyConflict);
                    stored = existing;
                    result = await store.LoadResultAsync(organizationGroupId, existing.RunId, transactionToken);
                    await store.WriteReadAuditAsync(
                        existing.RunId.ToString("N"), organizationGroupId, actorId,
                        "RETRY_AI_RUN", correlationId, clock.UtcNow, transactionToken);
                    return;
                }

                stored = await store.InsertRequestAsync(
                    Guid.Parse(idGenerator.NewId()), organizationGroupId, validated, requestHash,
                    actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);

            if (result is not null && !string.Equals(result.Status, AiRunStatuses.Pending, StringComparison.Ordinal))
                return result;

            var run = stored ?? throw new InvalidOperationException("AIX.REQUEST_MISSING");
            var providerResponse = await ExecuteProviderAsync(run, cancellationToken);
            var outcome = AiRuntimeRules.EvaluateProviderResponse(run.Request, providerResponse, outputValidator);

            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireKeyLockAsync("run", run.RunId.ToString("N"), transactionToken);
                var current = await store.LoadRequestAsync(organizationGroupId, run.RunId, transactionToken)
                    ?? throw new AiDomainException(AiErrorCodes.ObjectNotAccessible);
                if (!string.Equals(current.RequestHash, run.RequestHash, StringComparison.Ordinal))
                    throw new AiDomainException(AiErrorCodes.IdempotencyConflict);
                await AuthorizeAsync(
                    organizationGroupId, actorId, current.Request.ObjectScope, AiCapabilities.Run, transactionToken);

                result = await store.LoadResultAsync(organizationGroupId, run.RunId, transactionToken);
                if (result is not null && !string.Equals(result.Status, AiRunStatuses.Pending, StringComparison.Ordinal))
                {
                    await store.WriteReadAuditAsync(
                        run.RunId.ToString("N"), organizationGroupId, actorId,
                        "RECOVER_AI_RUN_OUTCOME", correlationId, clock.UtcNow, transactionToken);
                    return;
                }

                await store.InsertOutcomeAsync(
                    current, outcome, actorId, clock.UtcNow, correlationId, transactionToken);
                result = await store.LoadResultAsync(organizationGroupId, run.RunId, transactionToken);
            }, cancellationToken);

            AiTelemetry.RecordRun(outcome.Status);
            return result ?? throw new InvalidOperationException("AIX.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is AiDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "CreateAiRun", actorId, organizationGroupId, request?.IdempotencyKey,
                correlationId, exception, cancellationToken);
        }
    }

    public async Task<AiRunResult> GetAsync(
        string runId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(runId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(runId);
            AiRunResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadResultAsync(organizationGroupId, id, transactionToken)
                    ?? throw new AiDomainException(AiErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, result.ObjectScope, AiCapabilities.Run, transactionToken);
                await store.WriteReadAuditAsync(
                    id.ToString("N"), organizationGroupId, actorId,
                    "READ_AI_RUN", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("AIX.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is AiDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "GetAiRun", actorId, organizationGroupId, runId,
                correlationId, exception, cancellationToken);
        }
    }

    public async Task<AiReviewDispositionResult> RecordDispositionAsync(
        string runId,
        RecordAiDispositionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(runId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(runId);
            var validated = AiRuntimeRules.ValidateDispositionRequest(request);
            AiReviewDispositionResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireKeyLockAsync("review", id.ToString("N"), transactionToken);
                var run = await store.LoadResultAsync(organizationGroupId, id, transactionToken)
                    ?? throw new AiDomainException(AiErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, run.ObjectScope, AiCapabilities.Review, transactionToken);

                var existing = await store.LoadDispositionByIdempotencyAsync(
                    id, validated.IdempotencyKey, transactionToken);
                if (existing is not null)
                {
                    if (!Matches(existing, validated, actorId))
                        throw new AiDomainException(AiErrorCodes.IdempotencyConflict);
                    await store.WriteReadAuditAsync(
                        existing.Disposition.DispositionId, organizationGroupId, actorId,
                        "RETRY_AI_DISPOSITION", correlationId, clock.UtcNow, transactionToken);
                    result = existing;
                    return;
                }

                if (!AiRunStatuses.Reviewable.Contains(run.Status, StringComparer.Ordinal) ||
                    run.OriginalOutput is null)
                {
                    throw new AiDomainException(AiErrorCodes.ReviewNotAllowed);
                }
                if (validated.ExpectedRunVersion != run.Version)
                    throw new AiDomainException(AiErrorCodes.ExpectedVersionConflict);
                var candidate = run.OriginalOutput.Candidates.SingleOrDefault(entry =>
                    string.Equals(entry.CandidateId, validated.CandidateId, StringComparison.Ordinal))
                    ?? throw new AiDomainException(AiErrorCodes.CandidateNotFound);
                var disposition = AiRuntimeRules.BuildDisposition(
                    Guid.Parse(idGenerator.NewId()), validated, candidate, actorId, outputValidator);
                result = await store.InsertDispositionAsync(
                    id, run.Version + 1, disposition, validated.IdempotencyKey,
                    organizationGroupId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            AiTelemetry.RecordDisposition(result?.Disposition.Kind ?? "UNKNOWN");
            return result ?? throw new InvalidOperationException("AIX.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is AiDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "RecordAiDisposition", actorId, organizationGroupId, runId,
                correlationId, exception, cancellationToken);
        }
    }

    public async Task<AiReviewQueueResult> GetReviewQueueAsync(
        string? status,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(status, correlationId, cancellationToken);
        try
        {
            var normalizedStatus = NormalizeQueueStatus(status);
            var runs = new List<AiRunResult>();
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var ids = await store.LoadQueueIdsAsync(
                    organizationGroupId, normalizedStatus, transactionToken);
                foreach (var id in ids)
                {
                    var run = await store.LoadResultAsync(organizationGroupId, id, transactionToken);
                    if (run is null ||
                        (normalizedStatus is null &&
                         !AiRunStatuses.Reviewable.Contains(run.Status, StringComparer.Ordinal)))
                    {
                        continue;
                    }
                    var decision = await authorizationPort.AuthorizeAsync(new AiAuthorizationRequest(
                        organizationGroupId, actorId, run.ObjectScope, AiCapabilities.Review), transactionToken);
                    if (!decision.Allowed)
                        continue;
                    await store.WriteReadAuditAsync(
                        id.ToString("N"), organizationGroupId, actorId,
                        "READ_AI_REVIEW_QUEUE", correlationId, clock.UtcNow, transactionToken);
                    runs.Add(run);
                }
            }, cancellationToken);
            AiTelemetry.RecordQueueRead(runs.Count);
            return new AiReviewQueueResult(runs, AiContract.RuntimeRuleSetVersion);
        }
        catch (Exception exception) when (exception is AiDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "GetAiReviewQueue", actorId, organizationGroupId, status,
                correlationId, exception, cancellationToken);
        }
    }

    private async Task<AiProviderResponse> ExecuteProviderAsync(
        StoredAiRunRequest run,
        CancellationToken cancellationToken)
    {
        try
        {
            return await providerPort.ExecuteAsync(new AiProviderRequest(
                run.RunId.ToString("N"), run.Request.Envelope, run.Request.ValidationProfile,
                run.Request.AllowedFields, run.Request.AllowedUnits), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "AI provider invocation failed closed for run {RunId}",
                run.RunId.ToString("N"));
            return new AiProviderResponse(AiProviderStatuses.Failed, FailureCode: "PROVIDER_INVOCATION_FAILED");
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
            "AiCommand", actor?.ActorId, organizationGroupId, target,
            correlationId, AiErrorCodes.NotAuthorized, cancellationToken);
        throw new AiDomainException(AiErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId,
        string actorId,
        AiObjectContext objectScope,
        string capability,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new AiAuthorizationRequest(
            organizationGroupId, actorId, objectScope, capability), cancellationToken);
        if (!decision.Allowed)
            throw new AiDomainException(AiErrorCodes.NotAuthorized);
    }

    private async Task<AiDomainException> FailAsync(
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
            AiDomainException domain => domain.ErrorCode,
            PostgresException { SqlState: "23505" } => AiErrorCodes.IdempotencyConflict,
            _ => AiErrorCodes.PersistenceUnavailable
        };
        AiTelemetry.RecordRejected(code);
        logger.LogWarning(
            "AI command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType, code, correlationId);
        await WriteAttemptOrFailClosedAsync(
            commandType, actorId, organizationGroupId, target,
            correlationId, code, cancellationToken);
        return new AiDomainException(code);
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
                commandType, actorId, organizationGroupId, AiRuntimeRules.TargetHash(target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new AiDomainException(AiErrorCodes.PersistenceUnavailable);
        }
    }

    private static bool Matches(
        AiReviewDispositionResult existing,
        RecordAiDispositionRequest request,
        string actorId) =>
        string.Equals(existing.Disposition.CandidateId, request.CandidateId, StringComparison.Ordinal) &&
        string.Equals(existing.Disposition.Kind, request.Kind, StringComparison.Ordinal) &&
        string.Equals(existing.Disposition.Reason, request.Reason, StringComparison.Ordinal) &&
        string.Equals(existing.Disposition.HumanValue, request.HumanValue, StringComparison.Ordinal) &&
        string.Equals(existing.Disposition.ResponsibleActor, actorId, StringComparison.Ordinal);

    private static string? NormalizeQueueStatus(string? status)
    {
        if (status is null)
            return null;
        if (string.IsNullOrWhiteSpace(status))
            throw new AiDomainException(AiErrorCodes.ValidationFailed);
        var normalized = status.Trim().ToUpperInvariant();
        return AiRunStatuses.Reviewable.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : throw new AiDomainException(AiErrorCodes.ValidationFailed);
    }

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new AiDomainException(AiErrorCodes.ObjectNotAccessible);
}
