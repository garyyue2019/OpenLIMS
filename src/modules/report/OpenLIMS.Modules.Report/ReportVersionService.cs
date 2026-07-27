using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

public interface IReportVersionService
{
    Task<PendingContentHashResult> GetPendingContentHashAsync(string reportId, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportVersionDetailResult> IssueAsync(string reportId, IssueReportRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportVerificationResult> PerformControlledActionAsync(string reportId, PerformControlledActionRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportVerificationResult> GetVerificationAsync(string reportId, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportVersionDetailResult> GetVersionAsync(string reportId, int versionNumber, string correlationId, CancellationToken cancellationToken = default);
}

internal sealed class ReportVersionService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IReportAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ReportStore store,
    ReportVersionStore versionStore,
    ReportAttemptAuditWriter attemptAuditWriter,
    ILogger<ReportVersionService> logger) : IReportVersionService
{
    public async Task<PendingContentHashResult> GetPendingContentHashAsync(
        string reportId, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(reportId);
            PendingContentHashResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var (report, snapshots, _, _) = await LoadChainAsync(organizationGroupId, id, transactionToken);
                await AuthorizeAsync(organizationGroupId, actorId, report.ObjectScope, transactionToken);
                var next = snapshots.Count + 1;
                var canonical = ReportVersionRules.Canonicalize(report, next);
                result = new PendingContentHashResult(
                    report.ReportId, next, ReportVersionRules.ComputeHash(canonical),
                    canonical, report.Lines.Count, ReportContract.RuleSetVersion);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("GetPendingContentHash", actorId, organizationGroupId,
                reportId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportVersionDetailResult> IssueAsync(
        string reportId, IssueReportRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(reportId);
            var validated = ReportVersionRules.ValidateIssuance(request);
            ReportVersionDetailResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireReportLockAsync(id, transactionToken);
                var (report, snapshots, actions, _) = await LoadChainAsync(organizationGroupId, id, transactionToken);
                await AuthorizeAsync(organizationGroupId, actorId, report.ObjectScope, transactionToken);
                if (validated.ExpectedCurrentVersion != report.Version)
                    throw new ReportDomainException(ReportErrorCodes.ExpectedVersionConflict);

                var chain = BuildChainState(snapshots, actions);
                if (string.Equals(chain.ChainState, ReportChainStates.Voided, StringComparison.Ordinal))
                    throw new ReportDomainException(ReportErrorCodes.VersionChainClosed);

                ReportVersionRules.RequireSatisfiedGate(report);

                var next = snapshots.Count + 1;
                // A version may only be issued once — a correction has to bump
                // the number rather than re-sign the same one.
                if (chain.IssuedVersions.Contains(next))
                    throw new ReportDomainException(ReportErrorCodes.VersionAlreadyIssued);
                if (next > 1 && !chain.SupersededVersions.Contains(next - 1))
                    throw new ReportDomainException(ReportErrorCodes.VersionAlreadyIssued);

                var canonical = ReportVersionRules.Canonicalize(report, next);
                var contentHash = ReportVersionRules.ComputeHash(canonical);
                ReportVersionRules.RequireMatchingHash(validated.ExpectedContentHash, contentHash);

                await versionStore.InsertVersionAsync(
                    id, next, canonical, contentHash, report.Lines.Count, validated,
                    organizationGroupId, actorId, clock.UtcNow, correlationId, transactionToken);
                result = await LoadVersionDetailAsync(organizationGroupId, id, next, transactionToken);
            }, cancellationToken);
            ReportTelemetry.RecordIssued();
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("IssueReport", actorId, organizationGroupId,
                reportId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportVerificationResult> PerformControlledActionAsync(
        string reportId, PerformControlledActionRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(reportId);
            var validated = ReportVersionRules.ValidateControlledAction(request);
            ReportVerificationResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireReportLockAsync(id, transactionToken);
                var (report, snapshots, actions, _) = await LoadChainAsync(organizationGroupId, id, transactionToken);
                await AuthorizeAsync(organizationGroupId, actorId, report.ObjectScope, transactionToken);
                if (validated.ExpectedCurrentVersion != report.Version)
                    throw new ReportDomainException(ReportErrorCodes.ExpectedVersionConflict);

                var chain = BuildChainState(snapshots, actions);
                ReportVersionRules.RequireActionable(chain, validated);

                await versionStore.InsertControlledActionAsync(
                    id, validated, organizationGroupId, actorId, clock.UtcNow, correlationId, transactionToken);
                result = await LoadVerificationAsync(organizationGroupId, id, transactionToken);
            }, cancellationToken);
            ReportTelemetry.RecordControlledAction(validated.Kind);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("PerformReportControlledAction", actorId, organizationGroupId,
                reportId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportVerificationResult> GetVerificationAsync(
        string reportId, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(reportId);
            ReportVerificationResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var (report, _, _, _) = await LoadChainAsync(organizationGroupId, id, transactionToken);
                await AuthorizeAsync(organizationGroupId, actorId, report.ObjectScope, transactionToken);
                result = await LoadVerificationAsync(organizationGroupId, id, transactionToken);
                await versionStore.WriteReadAuditAsync(
                    report.ReportId, organizationGroupId, actorId,
                    "READ_REPORT_VERIFICATION", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            ReportTelemetry.RecordVerification();
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("GetReportVerification", actorId, organizationGroupId,
                reportId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportVersionDetailResult> GetVersionAsync(
        string reportId, int versionNumber, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(reportId);
            if (versionNumber < 1)
                throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
            ReportVersionDetailResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var (report, _, _, _) = await LoadChainAsync(organizationGroupId, id, transactionToken);
                await AuthorizeAsync(organizationGroupId, actorId, report.ObjectScope, transactionToken);
                result = await LoadVersionDetailAsync(organizationGroupId, id, versionNumber, transactionToken);
                await versionStore.WriteReadAuditAsync(
                    report.ReportId, organizationGroupId, actorId,
                    "READ_REPORT_VERIFICATION", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("GetReportVersion", actorId, organizationGroupId,
                reportId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<(ReportResult Report,
        IReadOnlyList<ReportVersionSnapshotResult> Snapshots,
        IReadOnlyList<ReportControlledActionResult> Actions,
        IReadOnlyList<ReportSignatureResult> Signatures)>
        LoadChainAsync(string organizationGroupId, Guid reportId, CancellationToken cancellationToken)
    {
        var report = await store.LoadReportAsync(organizationGroupId, reportId, cancellationToken)
            ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
        var snapshots = await versionStore.LoadSnapshotsAsync(reportId, cancellationToken);
        var actions = await versionStore.LoadActionsAsync(reportId, cancellationToken);
        var signatures = await versionStore.LoadSignaturesAsync(reportId, cancellationToken);
        return (report, snapshots, actions, signatures);
    }

    /// <summary>
    /// Derives chain state from the append-only action log: a correction or
    /// supplement supersedes the version it names, a withdrawal marks it, and a
    /// void closes everything.
    /// </summary>
    private static ReportVersionChainState BuildChainState(
        IReadOnlyList<ReportVersionSnapshotResult> snapshots,
        IReadOnlyList<ReportControlledActionResult> actions)
    {
        var issued = new HashSet<int>(snapshots.Select(snapshot => snapshot.VersionNumber));
        var withdrawn = new HashSet<int>(actions
            .Where(action => string.Equals(action.Kind, ReportControlledActionKinds.Withdrawal, StringComparison.Ordinal))
            .Select(action => action.VersionNumber));
        var superseded = new HashSet<int>(actions
            .Where(action => ReportControlledActionKinds.ProduceNewVersion.Contains(action.Kind, StringComparer.Ordinal))
            .Select(action => action.VersionNumber));
        var voided = actions.Any(action =>
            string.Equals(action.Kind, ReportControlledActionKinds.Void, StringComparison.Ordinal));
        var superseding = actions
            .Where(action => string.Equals(action.Kind, ReportControlledActionKinds.Supersession, StringComparison.Ordinal))
            .Select(action => action.SupersedingReportNumber)
            .LastOrDefault();
        return new ReportVersionChainState(
            voided ? ReportChainStates.Voided : ReportChainStates.Active,
            issued, withdrawn, superseded, superseding);
    }

    private async Task<ReportVerificationResult> LoadVerificationAsync(
        string organizationGroupId, Guid reportId, CancellationToken cancellationToken)
    {
        var (report, snapshots, actions, signatures) =
            await LoadChainAsync(organizationGroupId, reportId, cancellationToken);
        var chain = BuildChainState(snapshots, actions);
        var versions = snapshots.Select(snapshot =>
        {
            var signature = signatures.FirstOrDefault(entry => entry.VersionNumber == snapshot.VersionNumber);
            var supersededBy = chain.SupersededVersions.Contains(snapshot.VersionNumber)
                ? snapshots
                    .Where(other => other.VersionNumber > snapshot.VersionNumber)
                    .Select(other => (int?)other.VersionNumber)
                    .DefaultIfEmpty(null)
                    .Min()
                : null;
            return new ReportVersionEntry(
                snapshot.VersionNumber,
                ReportVersionRules.ResolveVersionState(snapshot.VersionNumber, chain),
                snapshot.ContentHash,
                signature?.SignedAt ?? snapshot.CreatedAt,
                supersededBy);
        }).ToList();

        return new ReportVerificationResult(
            report.ReportId, report.ReportNumber,
            ReportVersionRules.ResolveCurrentVersion(chain),
            chain.ChainState, versions, chain.SupersedingReportNumber,
            ReportContract.RuleSetVersion);
    }

    private async Task<ReportVersionDetailResult> LoadVersionDetailAsync(
        string organizationGroupId, Guid reportId, int versionNumber, CancellationToken cancellationToken)
    {
        var (_, snapshots, actions, signatures) =
            await LoadChainAsync(organizationGroupId, reportId, cancellationToken);
        var snapshot = snapshots.FirstOrDefault(entry => entry.VersionNumber == versionNumber)
            ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
        var signature = signatures.FirstOrDefault(entry => entry.VersionNumber == versionNumber)
            ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
        var chain = BuildChainState(snapshots, actions);
        return new ReportVersionDetailResult(
            reportId.ToString("N"), versionNumber,
            ReportVersionRules.ResolveVersionState(versionNumber, chain),
            snapshot, signature,
            [.. actions.Where(action => action.VersionNumber == versionNumber)],
            ReportContract.RuleSetVersion);
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

        await WriteAttemptOrFailClosedAsync("ReportVersionCommand", actor?.ActorId, organizationGroupId,
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
        var code = exception switch
        {
            ReportDomainException domain => domain.ErrorCode,
            PostgresException { SqlState: "23505" } => ReportErrorCodes.ValidationFailed,
            _ => ReportErrorCodes.PersistenceUnavailable
        };
        ReportTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Report version command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType, code, correlationId);
        await WriteAttemptOrFailClosedAsync(commandType, actorId, organizationGroupId,
            target, correlationId, code, cancellationToken);
        return new ReportDomainException(code);
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

internal sealed class ReportVersionChainPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IReportAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ReportStore store,
    ReportVersionStore versionStore,
    ReportAttemptAuditWriter attemptAuditWriter,
    ILogger<ReportVersionChainPort> logger) : IReportVersionChainPort
{
    public async ValueTask<ReportVersionChainResult> EvaluateAsync(
        ReportVersionChainRequest request, CancellationToken cancellationToken = default)
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
            return Record(ReportVersionRules.EvaluateChain(request, null, null));
        }

        try
        {
            ReportVersionChainResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var report = await store.LoadReportAsync(organizationGroupId, reportId, transactionToken);
                if (report is null)
                {
                    result = ReportVersionRules.EvaluateChain(request, null, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new ReportAuthorizationRequest(
                    organizationGroupId, actor.ActorId, report.ObjectScope, ReportCapabilities.Manage), transactionToken);
                if (!authorization.Allowed)
                    throw new ReportDomainException(ReportErrorCodes.NotAuthorized);

                var snapshots = await versionStore.LoadSnapshotsAsync(reportId, transactionToken);
                var actions = await versionStore.LoadActionsAsync(reportId, transactionToken);
                var chain = BuildChainState(snapshots, actions);
                var current = ReportVersionRules.ResolveCurrentVersion(chain);
                var hash = current is null
                    ? null
                    : snapshots.FirstOrDefault(snapshot => snapshot.VersionNumber == current)?.ContentHash;
                result = ReportVersionRules.EvaluateChain(request, chain, hash);
                await versionStore.WriteReadAuditAsync(
                    report.ReportId, organizationGroupId, actor.ActorId,
                    "EVALUATE_REPORT_VERSION_CHAIN", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return Record(result ?? ReportVersionRules.EvaluateChain(request, null, null));
        }
        catch (ReportDomainException exception)
            when (string.Equals(exception.ErrorCode, ReportErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor.ActorId, organizationGroupId, request.ReportId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Report version chain failed closed because persistence is unavailable");
            return Record(new ReportVersionChainResult(
                ReportVersionChainDecisions.Unknown, [ReportVersionChainReasons.ReportUnavailable],
                request.ReportId, null, ReportChainStates.Active, null, ReportContract.RuleSetVersion));
        }
    }

    private static ReportVersionChainState BuildChainState(
        IReadOnlyList<ReportVersionSnapshotResult> snapshots,
        IReadOnlyList<ReportControlledActionResult> actions)
    {
        var issued = new HashSet<int>(snapshots.Select(snapshot => snapshot.VersionNumber));
        var withdrawn = new HashSet<int>(actions
            .Where(action => string.Equals(action.Kind, ReportControlledActionKinds.Withdrawal, StringComparison.Ordinal))
            .Select(action => action.VersionNumber));
        var superseded = new HashSet<int>(actions
            .Where(action => ReportControlledActionKinds.ProduceNewVersion.Contains(action.Kind, StringComparer.Ordinal))
            .Select(action => action.VersionNumber));
        var voided = actions.Any(action =>
            string.Equals(action.Kind, ReportControlledActionKinds.Void, StringComparison.Ordinal));
        var superseding = actions
            .Where(action => string.Equals(action.Kind, ReportControlledActionKinds.Supersession, StringComparison.Ordinal))
            .Select(action => action.SupersedingReportNumber)
            .LastOrDefault();
        return new ReportVersionChainState(
            voided ? ReportChainStates.Voided : ReportChainStates.Active,
            issued, withdrawn, superseded, superseding);
    }

    private ReportVersionChainResult Record(ReportVersionChainResult result)
    {
        ReportTelemetry.RecordVersionChain(result.Decision);
        if (string.Equals(result.Decision, ReportVersionChainDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Report version chain failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId, string organizationGroupId, string target, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync("EvaluateReportVersionChain", actorId, organizationGroupId,
                ReportRules.HashTarget(target), correlationId, ReportErrorCodes.NotAuthorized,
                clock.UtcNow, cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new ReportDomainException(ReportErrorCodes.PersistenceUnavailable);
        }
    }
}
