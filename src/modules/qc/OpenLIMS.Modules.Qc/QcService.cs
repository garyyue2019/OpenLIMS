using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Qc;

namespace OpenLIMS.Modules.Qc;

public interface IQcRunService
{
    Task<QcRunResult> OpenRunAsync(CreateQcRunRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<QcRunResult> AddResultAsync(string qcRunId, AddQcResultRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<QcRunResult> RecordVerdictAsync(string qcRunId, RecordQcVerdictRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<QcRunResult> RecordImpactAsync(string qcRunId, RecordQcImpactRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<QcRunResult> RecordDeviationApprovalAsync(string qcRunId, RecordQcDeviationApprovalRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<QcRunResult> SatisfyGateAsync(string qcRunId, SatisfyQcReleaseGateRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<QcRunResult> ReleaseAsync(string qcRunId, ReleaseQcBlockRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<QcRunResult> GetAsync(string qcRunId, string correlationId, CancellationToken cancellationToken = default);
}

internal sealed class QcRunService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IQcAuthorizationPort authorizationPort,
    IBatchStatusPort batchStatusPort,
    ITransactionCoordinator transactionCoordinator,
    QcStore store,
    QcAttemptAuditWriter attemptAuditWriter,
    ILogger<QcRunService> logger) : IQcRunService
{
    public async Task<QcRunResult> OpenRunAsync(
        CreateQcRunRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var runId = Guid.Parse(idGenerator.NewId());
        var (organizationGroupId, actorId) = await RequireActorAsync(runId.ToString("N"), correlationId, cancellationToken);
        try
        {
            var validated = QcRules.ValidateRun(request);
            // Gate-then-commit: the batch port opens its own transaction, so it
            // must be consulted before ours starts.
            var gate = await EvaluateBatchGateAsync(organizationGroupId, validated, correlationId, cancellationToken);
            QcRunResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(organizationGroupId, actorId, validated.ObjectScope, transactionToken);
                await store.InsertRunAsync(
                    runId, organizationGroupId, validated, gate, actorId, clock.UtcNow, correlationId, transactionToken);
                result = await store.LoadRunAsync(organizationGroupId, runId, transactionToken);
            }, cancellationToken);
            QcTelemetry.RecordRun();
            return result ?? throw new InvalidOperationException("QC.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is QcDomainException or NpgsqlException)
        {
            throw await FailAsync("OpenQcRun", actorId, organizationGroupId,
                runId.ToString("N"), correlationId, exception, cancellationToken);
        }
    }

    public Task<QcRunResult> AddResultAsync(
        string qcRunId, AddQcResultRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync("RecordQcResult", qcRunId, request?.ExpectedCurrentVersion, correlationId, cancellationToken,
            async (run, organizationGroupId, actorId, transactionToken) =>
            {
                if (!string.Equals(run.State, QcRunStates.Open, StringComparison.Ordinal))
                    throw new QcDomainException(QcErrorCodes.ValidationFailed);
                var validated = QcRules.ValidateResult(request);
                await store.InsertResultAsync(
                    Guid.Parse(run.QcRunId), run.Version + 1, validated, organizationGroupId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
                QcTelemetry.RecordResult(validated.Verdict);
            });

    public Task<QcRunResult> RecordVerdictAsync(
        string qcRunId, RecordQcVerdictRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync("RecordQcVerdict", qcRunId, request?.ExpectedCurrentVersion, correlationId, cancellationToken,
            async (run, organizationGroupId, actorId, transactionToken) =>
            {
                if (request is null ||
                    !string.Equals(request.RuleSetVersion, QcContract.RuleSetVersion, StringComparison.Ordinal) ||
                    !string.Equals(run.State, QcRunStates.Open, StringComparison.Ordinal))
                {
                    throw new QcDomainException(QcErrorCodes.ValidationFailed);
                }

                var state = QcRules.ResolveVerdict(run.Results);
                await store.InsertVerdictAsync(
                    Guid.Parse(run.QcRunId), run.Version + 1, state, organizationGroupId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
                QcTelemetry.RecordVerdict(state);
            });

    public Task<QcRunResult> RecordImpactAsync(
        string qcRunId, RecordQcImpactRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync("RecordQcImpact", qcRunId, request?.ExpectedCurrentVersion, correlationId, cancellationToken,
            async (run, organizationGroupId, actorId, transactionToken) =>
            {
                if (!string.Equals(run.State, QcRunStates.Failed, StringComparison.Ordinal))
                    throw new QcDomainException(QcErrorCodes.ValidationFailed);
                var recorded = new HashSet<string>(
                    run.Impact.Select(entry => QcRules.ImpactKey(entry.TargetType, entry.TargetId)),
                    StringComparer.Ordinal);
                var targets = QcRules.ValidateImpact(request, recorded);
                await store.InsertImpactAsync(
                    Guid.Parse(run.QcRunId), run.Version, targets, organizationGroupId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
                QcTelemetry.RecordImpact(targets.Count);
            });

    public Task<QcRunResult> RecordDeviationApprovalAsync(
        string qcRunId, RecordQcDeviationApprovalRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync("RecordQcDeviationApproval", qcRunId, request?.ExpectedCurrentVersion, correlationId, cancellationToken,
            async (run, organizationGroupId, actorId, transactionToken) =>
            {
                if (string.Equals(run.State, QcRunStates.Released, StringComparison.Ordinal))
                    throw new QcDomainException(QcErrorCodes.ValidationFailed);
                var validated = QcRules.ValidateDeviationApproval(request);
                await store.InsertDeviationApprovalAsync(
                    Guid.Parse(run.QcRunId), run.Version + 1, validated, organizationGroupId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
                QcTelemetry.RecordDeviationApproval();
            });

    public Task<QcRunResult> SatisfyGateAsync(
        string qcRunId, SatisfyQcReleaseGateRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync("SatisfyQcReleaseGate", qcRunId, request?.ExpectedCurrentVersion, correlationId, cancellationToken,
            async (run, organizationGroupId, actorId, transactionToken) =>
            {
                if (!string.Equals(run.State, QcRunStates.Failed, StringComparison.Ordinal))
                    throw new QcDomainException(QcErrorCodes.ValidationFailed);
                var satisfied = new HashSet<string>(run.Gates.Select(gate => gate.Kind), StringComparer.Ordinal);
                var validated = QcRules.ValidateGate(request, satisfied);
                await store.InsertGateAsync(
                    Guid.Parse(run.QcRunId), run.Version + 1, validated, organizationGroupId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
                QcTelemetry.RecordGateSatisfied(validated.Kind);
            });

    public Task<QcRunResult> ReleaseAsync(
        string qcRunId, ReleaseQcBlockRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync("ReleaseQcBlock", qcRunId, request?.ExpectedCurrentVersion, correlationId, cancellationToken,
            async (run, organizationGroupId, actorId, transactionToken) =>
            {
                if (request is null ||
                    !string.Equals(request.RuleSetVersion, QcContract.RuleSetVersion, StringComparison.Ordinal))
                {
                    throw new QcDomainException(QcErrorCodes.ValidationFailed);
                }

                QcRules.RequireReleasable(run);
                await store.InsertReleaseAsync(
                    Guid.Parse(run.QcRunId), run.Version + 1, organizationGroupId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
                QcTelemetry.RecordRelease();
            });

    public async Task<QcRunResult> GetAsync(
        string qcRunId, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(qcRunId, correlationId, cancellationToken);
        try
        {
            var runId = ParseId(qcRunId);
            QcRunResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadRunAsync(organizationGroupId, runId, transactionToken)
                    ?? throw new QcDomainException(QcErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, result.ObjectScope, transactionToken);
                await store.WriteReadAuditAsync(
                    result.QcRunId, result.Version, organizationGroupId, actorId,
                    "READ_QC_RUN", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("QC.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is QcDomainException or NpgsqlException)
        {
            throw await FailAsync("GetQcRun", actorId, organizationGroupId,
                qcRunId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<QcRunResult> MutateAsync(
        string commandType,
        string qcRunId,
        long? expectedCurrentVersion,
        string correlationId,
        CancellationToken cancellationToken,
        Func<QcRunResult, string, string, CancellationToken, Task> mutate)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(qcRunId, correlationId, cancellationToken);
        try
        {
            var runId = ParseId(qcRunId);
            QcRunResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireRunLockAsync(runId, transactionToken);
                var run = await store.LoadRunAsync(organizationGroupId, runId, transactionToken)
                    ?? throw new QcDomainException(QcErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, run.ObjectScope, transactionToken);
                if (expectedCurrentVersion is null || expectedCurrentVersion != run.Version)
                    throw new QcDomainException(QcErrorCodes.ExpectedVersionConflict);

                await mutate(run, organizationGroupId, actorId, transactionToken);
                result = await store.LoadRunAsync(organizationGroupId, runId, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("QC.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is QcDomainException or NpgsqlException)
        {
            throw await FailAsync(commandType, actorId, organizationGroupId,
                qcRunId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<QcGateFacts> EvaluateBatchGateAsync(
        string organizationGroupId,
        CreateQcRunRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        BatchStatusResult result;
        try
        {
            result = await batchStatusPort.EvaluateAsync(new BatchStatusRequest(
                organizationGroupId, request.BatchId, request.ExpectedBatchVersion, BatchContract.RuleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            QcTelemetry.RecordGate("UNKNOWN");
            throw new QcDomainException(QcErrorCodes.ApplicabilityUnknown, "BATCH");
        }

        QcTelemetry.RecordGate(result.Decision);
        return result.Decision switch
        {
            BatchStatusDecisions.Allowed when result.CurrentBatchVersion is { } version =>
                new QcGateFacts(result.Decision, version, result.RuleSetVersion),
            BatchStatusDecisions.Blocked => throw new QcDomainException(QcErrorCodes.EligibilityBlocked, "BATCH"),
            _ => throw new QcDomainException(QcErrorCodes.ApplicabilityUnknown, "BATCH")
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

        await WriteAttemptOrFailClosedAsync("QcCommand", actor?.ActorId, organizationGroupId,
            target, correlationId, QcErrorCodes.NotAuthorized, cancellationToken);
        throw new QcDomainException(QcErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId, string actorId, QcObjectContext objectScope, CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new QcAuthorizationRequest(
            organizationGroupId, actorId, objectScope, QcCapabilities.Manage), cancellationToken);
        if (!decision.Allowed)
            throw new QcDomainException(QcErrorCodes.NotAuthorized);
    }

    private async Task<QcDomainException> FailAsync(
        string commandType, string actorId, string organizationGroupId,
        string? target, string correlationId, Exception exception, CancellationToken cancellationToken)
    {
        var (code, gateSource) = exception switch
        {
            QcDomainException domain => (domain.ErrorCode, domain.GateSource),
            PostgresException { SqlState: "23505" } => (QcErrorCodes.ValidationFailed, (string?)null),
            _ => (QcErrorCodes.PersistenceUnavailable, (string?)null)
        };
        QcTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Qc command {CommandType} failed closed with {ErrorCode} (gate {GateSource}); correlation {CorrelationId}",
            commandType, code, gateSource ?? "-", correlationId);
        await WriteAttemptOrFailClosedAsync(commandType, actorId, organizationGroupId,
            target, correlationId, code, cancellationToken);
        return new QcDomainException(code, gateSource);
    }

    private async Task WriteAttemptOrFailClosedAsync(
        string commandType, string? actorId, string organizationGroupId,
        string? target, string correlationId, string code, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(commandType, actorId, organizationGroupId,
                QcRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new QcDomainException(QcErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new QcDomainException(QcErrorCodes.ObjectNotAccessible);
}

internal sealed class QcReportabilityPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IQcAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    QcStore store,
    QcAttemptAuditWriter attemptAuditWriter,
    ILogger<QcReportabilityPort> logger) : IQcReportabilityPort
{
    public async ValueTask<QcReportabilityResult> EvaluateAsync(
        QcReportabilityRequest request, CancellationToken cancellationToken = default)
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
            await WriteDeniedAsync(actor?.ActorId, organizationGroupId, request.QcRunId, correlationId, cancellationToken);
            throw new QcDomainException(QcErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.QcRunId, "N", out var runId) &&
            !Guid.TryParse(request.QcRunId, out runId))
        {
            return Record(QcRules.EvaluateReportability(request, null));
        }

        try
        {
            QcReportabilityResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var run = await store.LoadRunAsync(organizationGroupId, runId, transactionToken);
                if (run is null)
                {
                    result = QcRules.EvaluateReportability(request, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new QcAuthorizationRequest(
                    organizationGroupId, actor.ActorId, run.ObjectScope, QcCapabilities.Manage), transactionToken);
                if (!authorization.Allowed)
                    throw new QcDomainException(QcErrorCodes.NotAuthorized);

                result = QcRules.EvaluateReportability(request, run);
                await store.WriteReadAuditAsync(
                    run.QcRunId, run.Version, organizationGroupId, actor.ActorId,
                    "EVALUATE_QC_REPORTABILITY", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return Record(result ?? QcRules.EvaluateReportability(request, null));
        }
        catch (QcDomainException exception)
            when (string.Equals(exception.ErrorCode, QcErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor.ActorId, organizationGroupId, request.QcRunId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Qc reportability failed closed because persistence is unavailable");
            return Record(new QcReportabilityResult(
                QcReportabilityDecisions.Unknown, [QcReportabilityReasons.QcUnavailable],
                request.QcRunId, request.TargetId, null, [], QcContract.RuleSetVersion));
        }
    }

    private QcReportabilityResult Record(QcReportabilityResult result)
    {
        QcTelemetry.RecordReportability(result.Decision);
        if (string.Equals(result.Decision, QcReportabilityDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Qc reportability failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId, string organizationGroupId, string target, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync("EvaluateQcReportability", actorId, organizationGroupId,
                QcRules.HashTarget(target), correlationId, QcErrorCodes.NotAuthorized,
                clock.UtcNow, cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new QcDomainException(QcErrorCodes.PersistenceUnavailable);
        }
    }
}
