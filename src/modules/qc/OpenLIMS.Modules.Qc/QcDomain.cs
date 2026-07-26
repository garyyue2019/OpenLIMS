using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Qc;

namespace OpenLIMS.Modules.Qc;

public sealed class QcDomainException(string errorCode, string? gateSource = null) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
    public string? GateSource { get; } = gateSource;
}

internal static partial class QcRules
{
    private const int MaximumImpactTargets = 10_000;
    private static readonly Regex StableIdentifier = StableIdentifierPattern();

    public static CreateQcRunRequest ValidateRun(CreateQcRunRequest? request)
    {
        if (request is null || request.ObjectScope is null)
            throw new QcDomainException(QcErrorCodes.ValidationFailed);
        if (!string.Equals(request.RuleSetVersion, QcContract.RuleSetVersion, StringComparison.Ordinal))
            throw new QcDomainException(QcErrorCodes.ValidationFailed);
        if (request.ExpectedBatchVersion < 1)
            throw new QcDomainException(QcErrorCodes.ValidationFailed);

        return request with
        {
            ObjectScope = new QcObjectContext(
                Identifier(request.ObjectScope.LegalEntityId),
                Identifier(request.ObjectScope.LaboratoryId)),
            BatchId = Identifier(request.BatchId),
            Method = Reference(request.Method),
            QcRuleSet = Reference(request.QcRuleSet)
        };
    }

    public static AddQcResultRequest ValidateResult(AddQcResultRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, QcContract.RuleSetVersion, StringComparison.Ordinal) ||
            request.ControlType is not (QcControlTypes.Blank or QcControlTypes.Spike or QcControlTypes.Duplicate
                or QcControlTypes.ReferenceMaterial or QcControlTypes.CalibrationCheck) ||
            request.Verdict is not (QcVerdicts.Pass or QcVerdicts.Fail) ||
            string.IsNullOrWhiteSpace(request.ObservedValue) ||
            string.IsNullOrWhiteSpace(request.VerdictBasis))
        {
            throw new QcDomainException(QcErrorCodes.ValidationFailed);
        }

        return request with { Rule = Reference(request.Rule) };
    }

    /// <summary>
    /// LAB-QC-001: any failing rule fails the whole run; a verdict may only be
    /// taken once at least one result exists.
    /// </summary>
    public static string ResolveVerdict(IReadOnlyList<QcResultEntry> results)
    {
        if (results.Count == 0)
            throw new QcDomainException(QcErrorCodes.ValidationFailed);
        return results.Any(entry => string.Equals(entry.Verdict, QcVerdicts.Fail, StringComparison.Ordinal))
            ? QcRunStates.Failed
            : QcRunStates.Passed;
    }

    /// <summary>
    /// RULE-022: the impact set must name every affected target, so an empty
    /// set — the "only fix the result that tripped" shortcut — is rejected.
    /// </summary>
    public static IReadOnlyList<QcImpactTarget> ValidateImpact(
        RecordQcImpactRequest? request,
        IReadOnlySet<string> alreadyRecorded)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, QcContract.RuleSetVersion, StringComparison.Ordinal) ||
            request.Targets is null || request.Targets.Count is < 1 or > MaximumImpactTargets)
        {
            throw new QcDomainException(QcErrorCodes.ValidationFailed);
        }

        var seen = new HashSet<string>(alreadyRecorded, StringComparer.Ordinal);
        var targets = new List<QcImpactTarget>();
        foreach (var target in request.Targets)
        {
            if (target is null ||
                target.TargetType is not (QcImpactTargetTypes.ResultGroup or QcImpactTargetTypes.Task) ||
                target.TargetVersion < 1)
            {
                throw new QcDomainException(QcErrorCodes.ValidationFailed);
            }

            var normalized = target with { TargetId = Identifier(target.TargetId) };
            if (!seen.Add(ImpactKey(normalized.TargetType, normalized.TargetId)))
                throw new QcDomainException(QcErrorCodes.ValidationFailed);
            targets.Add(normalized);
        }

        return targets;
    }

    public static SatisfyQcReleaseGateRequest ValidateGate(
        SatisfyQcReleaseGateRequest? request,
        IReadOnlySet<string> satisfiedKinds)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, QcContract.RuleSetVersion, StringComparison.Ordinal) ||
            !QcReleaseGateKinds.Required.Contains(request.Kind, StringComparer.Ordinal) ||
            satisfiedKinds.Contains(request.Kind))
        {
            throw new QcDomainException(QcErrorCodes.ValidationFailed);
        }

        return request with { EvidenceRef = Reference(request.EvidenceRef) };
    }

    public static RecordQcDeviationApprovalRequest ValidateDeviationApproval(RecordQcDeviationApprovalRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, QcContract.RuleSetVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new QcDomainException(QcErrorCodes.ValidationFailed);
        }

        return request with { ApprovalRef = Reference(request.ApprovalRef) };
    }

    /// <summary>
    /// LAB-QC-003 + RULE-010: release requires all five gates. Deviation
    /// approvals are counted nowhere here — by construction they cannot lift a
    /// block on their own.
    /// </summary>
    public static IReadOnlyList<string> OutstandingGates(QcRunResult run) =>
        [.. QcReleaseGateKinds.Required.Where(kind =>
            !run.Gates.Any(gate => string.Equals(gate.Kind, kind, StringComparison.Ordinal)))];

    public static void RequireReleasable(QcRunResult run)
    {
        if (!string.Equals(run.State, QcRunStates.Failed, StringComparison.Ordinal))
            throw new QcDomainException(QcErrorCodes.ValidationFailed);
        if (run.Impact.Count == 0 || OutstandingGates(run).Count > 0)
            throw new QcDomainException(QcErrorCodes.ReleaseGateIncomplete);
    }

    public static QcReportabilityResult EvaluateReportability(QcReportabilityRequest request, QcRunResult? run)
    {
        if (!string.Equals(request.RuleSetVersion, QcContract.RuleSetVersion, StringComparison.Ordinal))
            return Unknown(request, run, QcReportabilityReasons.RuleSetVersionUnknown);
        if (run is null)
            return Blocked(request, null, QcReportabilityReasons.QcRunRequired, []);
        if (request.ExpectedRunVersion != run.Version)
            return Unknown(request, run, QcReportabilityReasons.VersionMismatch);

        var targeted = run.Impact.Any(entry =>
            string.Equals(entry.TargetId, request.TargetId, StringComparison.Ordinal));
        return run.State switch
        {
            QcRunStates.Open => Blocked(request, run, QcReportabilityReasons.VerdictPending, []),
            QcRunStates.Passed => Allowed(request, run),
            QcRunStates.Released when targeted => Allowed(request, run),
            QcRunStates.Failed when targeted =>
                Blocked(request, run, QcReportabilityReasons.QcFailureUnreleased, OutstandingGates(run)),
            _ => Blocked(request, run, QcReportabilityReasons.TargetNotInImpactScope, [])
        };
    }

    public static string ImpactKey(string targetType, string targetId) => $"{targetType}|{targetId}";

    public static string HashTarget(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static QcReportabilityResult Allowed(QcReportabilityRequest request, QcRunResult run) => new(
        QcReportabilityDecisions.Allowed, [], run.QcRunId, request.TargetId,
        run.Version, [], QcContract.RuleSetVersion);

    private static QcReportabilityResult Blocked(
        QcReportabilityRequest request, QcRunResult? run, string reason, IReadOnlyList<string> outstanding) => new(
        QcReportabilityDecisions.Blocked, [reason], run?.QcRunId ?? request.QcRunId, request.TargetId,
        run?.Version, outstanding, QcContract.RuleSetVersion);

    private static QcReportabilityResult Unknown(
        QcReportabilityRequest request, QcRunResult? run, string reason) => new(
        QcReportabilityDecisions.Unknown, [reason], run?.QcRunId ?? request.QcRunId, request.TargetId,
        run?.Version, [], QcContract.RuleSetVersion);

    private static QcVersionedReference Reference(QcVersionedReference? value)
    {
        if (value is null || value.Version < 1)
            throw new QcDomainException(QcErrorCodes.ValidationFailed);
        return new QcVersionedReference(Identifier(value.Id), value.Version);
    }

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!StableIdentifier.IsMatch(trimmed))
            throw new QcDomainException(QcErrorCodes.ValidationFailed);
        return trimmed;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierPattern();
}
