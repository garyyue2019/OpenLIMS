using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Instrument;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Qc;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Contracts.Report;
using OpenLIMS.Contracts.Result;
using OpenLIMS.Contracts.Scope;

namespace OpenLIMS.Modules.Report;

public interface IReportService
{
    Task<ReportResult> CreateAsync(CreateReportRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportResult> AddLineAsync(string reportId, AddReportLineRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportResult> EvaluateGateAsync(string reportId, EvaluateReportGateRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportResult> SubmitForApprovalAsync(string reportId, SubmitReportForApprovalRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportResult> GetAsync(string reportId, string correlationId, CancellationToken cancellationToken = default);
}

internal sealed class ReportService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IReportAuthorizationPort authorizationPort,
    IResultAdoptionPort resultAdoptionPort,
    IQcReportabilityPort qcReportabilityPort,
    IReceivingEligibilityPortV2 receivingEligibilityPort,
    IScopeProductionEligibilityPort scopeEligibilityPort,
    IAllocationStatusPort allocationStatusPort,
    IBatchStatusPort batchStatusPort,
    IInstrumentImportPort instrumentImportPort,
    IAccreditationScopePort accreditationScopePort,
    ISignatoryAuthorityPort signatoryAuthorityPort,
    ITransactionCoordinator transactionCoordinator,
    ReportStore store,
    ReportAttemptAuditWriter attemptAuditWriter,
    ILogger<ReportService> logger) : IReportService
{
    public async Task<ReportResult> CreateAsync(
        CreateReportRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var reportId = Guid.Parse(idGenerator.NewId());
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId.ToString("N"), correlationId, cancellationToken);
        try
        {
            var validated = ReportRules.ValidateReport(request);
            ReportResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(organizationGroupId, actorId, validated.ObjectScope, transactionToken);
                await store.InsertReportAsync(
                    reportId, organizationGroupId, validated, actorId, clock.UtcNow, correlationId, transactionToken);
                result = await store.LoadReportAsync(organizationGroupId, reportId, transactionToken);
            }, cancellationToken);
            ReportTelemetry.RecordReport();
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("CreateReport", actorId, organizationGroupId,
                reportId.ToString("N"), correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportResult> AddLineAsync(
        string reportId, AddReportLineRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(reportId);
            // Gate-then-commit: the adoption port opens its own transaction, so
            // it is consulted before ours starts.
            var adoption = await EvaluateAdoptionAsync(
                organizationGroupId, request, correlationId, cancellationToken);
            ReportResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireReportLockAsync(id, transactionToken);
                var report = await store.LoadReportAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, report.ObjectScope, transactionToken);
                if (request is null || request.ExpectedCurrentVersion != report.Version)
                    throw new ReportDomainException(ReportErrorCodes.ExpectedVersionConflict);
                if (!string.Equals(report.State, ReportStates.Draft, StringComparison.Ordinal))
                    throw new ReportDomainException(ReportErrorCodes.ValidationFailed);

                var usedLineNumbers = new HashSet<int>(report.Lines.Select(line => line.LineNumber));
                var usedAttributions = new HashSet<string>(
                    report.Lines.Select(line => ReportRules.AttributionKey(line.ScopeLineId, line.ResultGroupId)),
                    StringComparer.Ordinal);
                var validated = ReportRules.ValidateLine(request, usedLineNumbers, usedAttributions);
                await store.InsertLineAsync(
                    id, report.Version + 1, validated, adoption, organizationGroupId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
                result = await store.LoadReportAsync(organizationGroupId, id, transactionToken);
            }, cancellationToken);
            ReportTelemetry.RecordLine();
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("AddReportLine", actorId, organizationGroupId,
                reportId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportResult> EvaluateGateAsync(
        string reportId, EvaluateReportGateRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(reportId);
            if (request is null ||
                !string.Equals(request.RuleSetVersion, ReportContract.RuleSetVersion, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(request.SignatoryId))
            {
                throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
            }

            // Every source port is consulted OUTSIDE our transaction, then the
            // verdicts are pinned verbatim into the evaluation fact.
            var snapshot = await LoadOutsideTransactionAsync(organizationGroupId, id, cancellationToken)
                ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);

            if (request.ExpectedCurrentVersion != snapshot.Version)
                throw new ReportDomainException(ReportErrorCodes.ExpectedVersionConflict);
            if (snapshot.Lines.Count == 0)
                throw new ReportDomainException(ReportErrorCodes.ValidationFailed);

            var (blockers, verdicts) = await EvaluateAllSourcesAsync(
                organizationGroupId, snapshot, request.SignatoryId, correlationId, cancellationToken);
            var decision = ReportRules.ResolveDecision(blockers);

            ReportResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireReportLockAsync(id, transactionToken);
                var report = await store.LoadReportAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, report.ObjectScope, transactionToken);
                if (request.ExpectedCurrentVersion != report.Version)
                    throw new ReportDomainException(ReportErrorCodes.ExpectedVersionConflict);

                await store.InsertGateEvaluationAsync(
                    id, report.Version + 1, decision, blockers, verdicts, request.SignatoryId,
                    organizationGroupId, actorId, clock.UtcNow, correlationId, transactionToken);
                result = await store.LoadReportAsync(organizationGroupId, id, transactionToken);
            }, cancellationToken);
            ReportTelemetry.RecordGate(decision, blockers.Count);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("EvaluateReportGate", actorId, organizationGroupId,
                reportId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportResult> SubmitForApprovalAsync(
        string reportId, SubmitReportForApprovalRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(reportId);
            ReportResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireReportLockAsync(id, transactionToken);
                var report = await store.LoadReportAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, report.ObjectScope, transactionToken);
                if (request is null ||
                    !string.Equals(request.RuleSetVersion, ReportContract.RuleSetVersion, StringComparison.Ordinal))
                {
                    throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
                }
                if (request.ExpectedCurrentVersion != report.Version)
                    throw new ReportDomainException(ReportErrorCodes.ExpectedVersionConflict);
                if (!string.Equals(report.State, ReportStates.Draft, StringComparison.Ordinal))
                    throw new ReportDomainException(ReportErrorCodes.ValidationFailed);

                var latest = report.GateEvaluations.LastOrDefault()
                    ?? throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
                if (latest.ReportVersion != report.Version)
                    throw new ReportDomainException(ReportErrorCodes.ExpectedVersionConflict);
                if (!string.Equals(latest.Decision, ReportGateDecisions.Allowed, StringComparison.Ordinal))
                {
                    throw new ReportDomainException(
                        latest.Blockers.Any(blocker => string.Equals(
                            blocker.Source, ReportGateSources.Accreditation, StringComparison.Ordinal))
                            ? ReportErrorCodes.AccreditationBlocked
                            : latest.Blockers.Any(blocker => string.Equals(
                                blocker.ReasonCode, ReportBlockerReasons.ConformityDecisionUnavailable, StringComparison.Ordinal))
                                ? ReportErrorCodes.ConformityDecisionUnavailable
                                : string.Equals(latest.Decision, ReportGateDecisions.Unknown, StringComparison.Ordinal)
                                    ? ReportErrorCodes.ApplicabilityUnknown
                                    : ReportErrorCodes.EligibilityBlocked);
                }

                await store.InsertApprovalSubmissionAsync(
                    id, report.Version + 1, Guid.Parse(latest.EvaluationId), organizationGroupId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
                result = await store.LoadReportAsync(organizationGroupId, id, transactionToken);
            }, cancellationToken);
            ReportTelemetry.RecordSubmission();
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("SubmitReportForApproval", actorId, organizationGroupId,
                reportId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportResult> GetAsync(
        string reportId, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(reportId);
            ReportResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadReportAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, result.ObjectScope, transactionToken);
                await store.WriteReadAuditAsync(
                    result.ReportId, result.Version, organizationGroupId, actorId,
                    "READ_REPORT", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("GetReport", actorId, organizationGroupId,
                reportId, correlationId, exception, cancellationToken);
        }
    }

    /// <summary>
    /// Reads the report in its own committed transaction so the source ports
    /// can then be consulted with no transaction open (gate-then-commit).
    /// </summary>
    private async Task<ReportResult?> LoadOutsideTransactionAsync(
        string organizationGroupId, Guid reportId, CancellationToken cancellationToken)
    {
        ReportResult? report = null;
        await transactionCoordinator.ExecuteAsync(async transactionToken =>
        {
            report = await store.LoadReportAsync(organizationGroupId, reportId, transactionToken);
        }, cancellationToken);
        return report;
    }

    private async Task<ReportAdoptionFacts> EvaluateAdoptionAsync(
        string organizationGroupId,
        AddReportLineRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ResultAdoptionStatusResult result;
        try
        {
            result = await resultAdoptionPort.EvaluateAsync(new ResultAdoptionStatusRequest(
                organizationGroupId, request.ResultGroupId, request.ExpectedGroupVersion,
                ResultContract.RuleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReportTelemetry.RecordSource(ReportGateSources.ResultAdoption, ReportGateDecisions.Unknown);
            throw new ReportDomainException(ReportErrorCodes.ApplicabilityUnknown, ReportGateSources.ResultAdoption);
        }

        ReportTelemetry.RecordSource(ReportGateSources.ResultAdoption, result.Decision);
        return result.Decision switch
        {
            ResultAdoptionDecisions.Allowed when result.EffectiveTargetId is not null =>
                new ReportAdoptionFacts(
                    result.EffectiveTargetId, result.RuleSetVersion,
                    result.CurrentGroupVersion ?? request.ExpectedGroupVersion),
            ResultAdoptionDecisions.Blocked =>
                throw new ReportDomainException(ReportErrorCodes.EligibilityBlocked, ReportGateSources.ResultAdoption),
            _ => throw new ReportDomainException(ReportErrorCodes.ApplicabilityUnknown, ReportGateSources.ResultAdoption)
        };
    }

    /// <summary>
    /// RPT-GATE-001: every source is consulted for every line, and each verdict
    /// becomes its own blocker entry rather than collapsing into one flag.
    /// </summary>
    private async Task<(IReadOnlyList<ReportBlocker> Blockers, IReadOnlyList<ReportLineAccreditationVerdict> Verdicts)>
        EvaluateAllSourcesAsync(
            string organizationGroupId,
            ReportResult report,
            string signatoryId,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var blockers = new List<ReportBlocker>();
        var verdicts = new List<ReportLineAccreditationVerdict>();

        foreach (var line in report.Lines)
        {
            if (ReportRules.TraceBlocker(line) is { } traceBlocker)
                blockers.Add(traceBlocker);
            if (ReportRules.ConformityBlocker(line) is { } conformityBlocker)
                blockers.Add(conformityBlocker);

            blockers.AddRange(await EvaluateLineSourcesAsync(
                organizationGroupId, report.ObjectScope.LaboratoryId, line, correlationId, cancellationToken));

            var scope = await ResolveAccreditationScopeAsync(organizationGroupId, line, cancellationToken);
            var signatory = await EvaluateSignatoryAsync(
                organizationGroupId, line, signatoryId, cancellationToken);
            var verdict = ReportRules.EvaluateAccreditation(line, scope, signatory, clock.UtcNow);
            verdicts.Add(verdict);
            ReportTelemetry.RecordAccreditation(verdict.Status);
            if (line.ClaimsAccreditation &&
                !string.Equals(verdict.Status, ReportAccreditationStatuses.Accredited, StringComparison.Ordinal))
            {
                blockers.Add(ReportRules.AccreditationBlocker(line, verdict));
            }
        }

        return (blockers, verdicts);
    }

    private async Task<IReadOnlyList<ReportBlocker>> EvaluateLineSourcesAsync(
        string organizationGroupId,
        string laboratoryId,
        ReportLineResult line,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var blockers = new List<ReportBlocker>();

        // RULE-005 / RPT-TRACE-001: the adoption the line pinned must still be
        // THE current effective one, so the gate replays the adoption port at
        // the pinned group version rather than trusting the append-time check.
        blockers.AddIfPresent(await EvaluateSourceAsync(
            ReportGateSources.ResultAdoption, line.ResultGroupId, "ResultGroup",
            ReportNextSteps.RefreshAdoption, line.LineNumber, ResultContract.RuleSetVersion, async () =>
            {
                var adoption = await resultAdoptionPort.EvaluateAsync(new ResultAdoptionStatusRequest(
                    organizationGroupId, line.ResultGroupId, line.GroupVersion, ResultContract.RuleSetVersion)
                {
                    CorrelationId = correlationId
                }, cancellationToken);
                return string.Equals(adoption.Decision, ResultAdoptionDecisions.Allowed, StringComparison.Ordinal) &&
                       !string.Equals(adoption.EffectiveTargetId, line.AdoptionTargetId, StringComparison.Ordinal)
                    ? ReportGateDecisions.Blocked
                    : adoption.Decision;
            }));

        // BUS-RPT-002: the QC port is run-scoped, so a target is only
        // reportable when EVERY run naming it says so — one blocker per run
        // that does not.
        foreach (var citedRun in line.GateRefs.QcRuns)
        {
            blockers.AddIfPresent(await EvaluateSourceAsync(
                ReportGateSources.QcReportability, citedRun.Id, "QcRun", ReportNextSteps.ReleaseQcBlock,
                line.LineNumber, QcContract.RuleSetVersion, async () =>
                {
                    var qc = await qcReportabilityPort.EvaluateAsync(new QcReportabilityRequest(
                        organizationGroupId, citedRun.Id, citedRun.Version,
                        QcContract.RuleSetVersion, line.ResultGroupId)
                    {
                        CorrelationId = correlationId
                    }, cancellationToken);
                    return qc.Decision;
                }));
        }

        blockers.AddIfPresent(await EvaluateSourceAsync(
            ReportGateSources.ReceivingEligibility, line.TraceRefs.ReceivedItemId, "ReceivedItem",
            ReportNextSteps.ResolveIdentityConflict, line.LineNumber, ReceivingEligibilityV2Contract.RuleSetVersion,
            async () =>
            {
                var receiving = await receivingEligibilityPort.EvaluateAsync(new ReceivingEligibilityV2Request(
                    laboratoryId, line.TraceRefs.ReceivedItemId,
                    ReceivingEligibilityActions.TestAssignment, line.GateRefs.ReceivedItemVersion,
                    ReceivingEligibilityV2Contract.RuleSetVersion), cancellationToken);
                return receiving.Decision;
            }));

        blockers.AddIfPresent(await EvaluateSourceAsync(
            ReportGateSources.ScopeEligibility, line.GateRefs.ScopeMatrixId, "ScopeMatrix", ReportNextSteps.ReviseScopeMatrix,
            line.LineNumber, ScopeContract.RuleSetVersion, async () =>
            {
                var scope = await scopeEligibilityPort.EvaluateAsync(new ScopeProductionEligibilityRequest(
                    organizationGroupId, line.GateRefs.ScopeMatrixId, line.GateRefs.ScopeMatrixVersion,
                    ScopeContract.RuleSetVersion)
                {
                    CorrelationId = correlationId
                }, cancellationToken);
                return scope.Decision;
            }));

        blockers.AddIfPresent(await EvaluateSourceAsync(
            ReportGateSources.AllocationStatus, line.TraceRefs.AllocationId, "TestObjectAllocation",
            ReportNextSteps.RestoreAllocation, line.LineNumber, AllocationContract.RuleSetVersion, async () =>
            {
                var allocation = await allocationStatusPort.EvaluateAsync(new AllocationStatusRequest(
                    organizationGroupId, line.TraceRefs.AllocationId, line.GateRefs.AllocationVersion,
                    AllocationContract.RuleSetVersion)
                {
                    CorrelationId = correlationId
                }, cancellationToken);
                return allocation.Decision;
            }));

        blockers.AddIfPresent(await EvaluateSourceAsync(
            ReportGateSources.BatchStatus, line.TraceRefs.BatchId, "Batch", ReportNextSteps.UnfreezeOrReplaceBatch,
            line.LineNumber, BatchContract.RuleSetVersion, async () =>
            {
                var batch = await batchStatusPort.EvaluateAsync(new BatchStatusRequest(
                    organizationGroupId, line.TraceRefs.BatchId, line.GateRefs.BatchVersion,
                    BatchContract.RuleSetVersion)
                {
                    CorrelationId = correlationId
                }, cancellationToken);
                return batch.Decision;
            }));

        blockers.AddIfPresent(await EvaluateSourceAsync(
            ReportGateSources.InstrumentImport, line.GateRefs.InstrumentFileId, "InstrumentFile",
            ReportNextSteps.CompleteInstrumentImport, line.LineNumber, InstrumentContract.RuleSetVersion, async () =>
            {
                var instrument = await instrumentImportPort.EvaluateAsync(new InstrumentImportStatusRequest(
                    organizationGroupId, line.GateRefs.InstrumentFileId, line.GateRefs.InstrumentFileVersion,
                    InstrumentContract.RuleSetVersion)
                {
                    CorrelationId = correlationId
                }, cancellationToken);
                return instrument.Decision;
            }));

        return blockers;
    }

    private async Task<ReportBlocker?> EvaluateSourceAsync(
        string source,
        string objectRef,
        string objectType,
        string nextStep,
        int lineNumber,
        string ruleSetVersion,
        Func<Task<string>> evaluate)
    {
        string decision;
        try
        {
            decision = await evaluate();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "Report gate source {Source} failed closed for {ObjectRef}", source, objectRef);
            decision = ReportGateDecisions.Unknown;
        }

        ReportTelemetry.RecordSource(source, decision);
        return ReportRules.SourceBlocker(
            source, objectRef, objectType, decision, ruleSetVersion, nextStep, lineNumber);
    }

    private async Task<AccreditationScopeSnapshot?> ResolveAccreditationScopeAsync(
        string organizationGroupId, ReportLineResult line, CancellationToken cancellationToken)
    {
        try
        {
            var scope = await accreditationScopePort.ResolveAsync(new AccreditationScopeLookupRequest(
                organizationGroupId, line.AccreditationRef), cancellationToken);
            return scope is null
                ? null
                : new AccreditationScopeSnapshot(
                    scope.SiteId, scope.Method, scope.ProductMatrix, scope.ParameterRange,
                    scope.ValidUntil, scope.AuthorizedSignatories);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Accreditation scope lookup failed closed for {Reference}", line.AccreditationRef.Id);
            return null;
        }
    }

    private async Task<SignatoryAuthorityOutcome> EvaluateSignatoryAsync(
        string organizationGroupId, ReportLineResult line, string signatoryId, CancellationToken cancellationToken)
    {
        try
        {
            var decision = await signatoryAuthorityPort.EvaluateAsync(new SignatoryAuthorityRequest(
                organizationGroupId, signatoryId, line.AccreditationClaim.SiteId,
                line.AccreditationClaim.Method, line.AccreditationClaim.ParameterRange), cancellationToken);
            return new SignatoryAuthorityOutcome(decision.Authorized, decision.ReasonCodes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Signatory authority lookup failed closed for {SignatoryId}", signatoryId);
            return new SignatoryAuthorityOutcome(false, [ReportBlockerReasons.SourceUnknown]);
        }
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

        await WriteAttemptOrFailClosedAsync("ReportCommand", actor?.ActorId, organizationGroupId,
            target, correlationId, ReportErrorCodes.NotAuthorized, cancellationToken);
        throw new ReportDomainException(ReportErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId, string actorId, ReportObjectContext objectScope, CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new ReportAuthorizationRequest(
            organizationGroupId, actorId, objectScope, ReportCapabilities.Manage), cancellationToken);
        if (!decision.Allowed)
            throw new ReportDomainException(ReportErrorCodes.NotAuthorized);
    }

    private async Task<ReportDomainException> FailAsync(
        string commandType, string actorId, string organizationGroupId,
        string? target, string correlationId, Exception exception, CancellationToken cancellationToken)
    {
        var (code, gateSource) = exception switch
        {
            ReportDomainException domain => (domain.ErrorCode, domain.GateSource),
            PostgresException { SqlState: "23505" } => (ReportErrorCodes.ValidationFailed, (string?)null),
            _ => (ReportErrorCodes.PersistenceUnavailable, (string?)null)
        };
        ReportTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Report command {CommandType} failed closed with {ErrorCode} (gate {GateSource}); correlation {CorrelationId}",
            commandType, code, gateSource ?? "-", correlationId);
        await WriteAttemptOrFailClosedAsync(commandType, actorId, organizationGroupId,
            target, correlationId, code, cancellationToken);
        return new ReportDomainException(code, gateSource);
    }

    private async Task WriteAttemptOrFailClosedAsync(
        string commandType, string? actorId, string organizationGroupId,
        string? target, string correlationId, string code, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(commandType, actorId, organizationGroupId,
                ReportRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new ReportDomainException(ReportErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
}

internal static class BlockerListExtensions
{
    public static void AddIfPresent(this List<ReportBlocker> blockers, ReportBlocker? blocker)
    {
        if (blocker is not null)
            blockers.Add(blocker);
    }
}

internal sealed class ReportIssuanceGatePort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IReportAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ReportStore store,
    ReportAttemptAuditWriter attemptAuditWriter,
    ILogger<ReportIssuanceGatePort> logger) : IReportIssuanceGatePort
{
    public async ValueTask<ReportIssuanceGateResult> EvaluateAsync(
        ReportIssuanceGateRequest request, CancellationToken cancellationToken = default)
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
            await WriteDeniedAsync(actor?.ActorId, organizationGroupId, request.ReportId, correlationId, cancellationToken);
            throw new ReportDomainException(ReportErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.ReportId, "N", out var reportId) &&
            !Guid.TryParse(request.ReportId, out reportId))
        {
            return Record(ReportRules.EvaluateIssuanceGate(request, null));
        }

        try
        {
            ReportIssuanceGateResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var report = await store.LoadReportAsync(organizationGroupId, reportId, transactionToken);
                if (report is null)
                {
                    result = ReportRules.EvaluateIssuanceGate(request, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new ReportAuthorizationRequest(
                    organizationGroupId, actor.ActorId, report.ObjectScope, ReportCapabilities.Manage), transactionToken);
                if (!authorization.Allowed)
                    throw new ReportDomainException(ReportErrorCodes.NotAuthorized);

                result = ReportRules.EvaluateIssuanceGate(request, report);
                await store.WriteReadAuditAsync(
                    report.ReportId, report.Version, organizationGroupId, actor.ActorId,
                    "EVALUATE_REPORT_ISSUANCE_GATE", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return Record(result ?? ReportRules.EvaluateIssuanceGate(request, null));
        }
        catch (ReportDomainException exception)
            when (string.Equals(exception.ErrorCode, ReportErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor.ActorId, organizationGroupId, request.ReportId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Report issuance gate failed closed because persistence is unavailable");
            return Record(new ReportIssuanceGateResult(
                ReportGateDecisions.Unknown, [ReportBlockerReasons.ReportUnavailable],
                request.ReportId, null, [], [], ReportContract.RuleSetVersion));
        }
    }

    private ReportIssuanceGateResult Record(ReportIssuanceGateResult result)
    {
        ReportTelemetry.RecordIssuanceGate(result.Decision);
        if (string.Equals(result.Decision, ReportGateDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Report issuance gate failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId, string organizationGroupId, string target, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync("EvaluateReportIssuanceGate", actorId, organizationGroupId,
                ReportRules.HashTarget(target), correlationId, ReportErrorCodes.NotAuthorized,
                clock.UtcNow, cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new ReportDomainException(ReportErrorCodes.PersistenceUnavailable);
        }
    }
}
