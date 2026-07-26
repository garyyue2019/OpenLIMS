namespace OpenLIMS.Contracts.Qc;

public static class QcContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "QC-IMPACT@1.0.0";
    public const string CreateRunPath = "/api/v1/qc-runs";
    public const string AddResultPath = "/api/v1/qc-runs/{id}/results";
    public const string VerdictPath = "/api/v1/qc-runs/{id}/verdict";
    public const string ImpactPath = "/api/v1/qc-runs/{id}/impact";
    public const string DeviationApprovalPath = "/api/v1/qc-runs/{id}/deviation-approval";
    public const string GatePath = "/api/v1/qc-runs/{id}/gates";
    public const string ReleasePath = "/api/v1/qc-runs/{id}/release";
    public const string GetRunPath = "/api/v1/qc-runs/{id}";
    public const string ReportabilityPath = "/api/v1/qc-runs/{id}/reportability";
}

public static class QcCapabilities
{
    public const string Manage = "qc.manage";
}

public static class QcClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
}

public static class QcRunStates
{
    public const string Open = "OPEN";
    public const string Passed = "PASSED";
    public const string Failed = "FAILED";
    public const string Released = "RELEASED";
}

public static class QcControlTypes
{
    public const string Blank = "BLANK";
    public const string Spike = "SPIKE";
    public const string Duplicate = "DUPLICATE";
    public const string ReferenceMaterial = "REFERENCE_MATERIAL";
    public const string CalibrationCheck = "CALIBRATION_CHECK";
}

public static class QcVerdicts
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
}

public static class QcImpactTargetTypes
{
    public const string ResultGroup = "RESULT_GROUP";
    public const string Task = "TASK";
}

/// <summary>
/// The five release gates of LAB-QC-003. Deviation approval is deliberately
/// NOT one of them (RULE-010: an approved deviation does not make a result
/// reportable).
/// </summary>
public static class QcReleaseGateKinds
{
    public const string Investigation = "INVESTIGATION";
    public const string ImpactScope = "IMPACT_SCOPE";
    public const string ValidityDecision = "VALIDITY_DECISION";
    public const string AdoptionRule = "ADOPTION_RULE";
    public const string TechnicalReview = "TECHNICAL_REVIEW";

    public static readonly IReadOnlyList<string> Required =
    [
        Investigation, ImpactScope, ValidityDecision, AdoptionRule, TechnicalReview
    ];
}

public static class QcReportabilityDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class QcReportabilityReasons
{
    public const string QcRunRequired = "QC_RUN_REQUIRED";
    public const string QcFailureUnreleased = "QC_FAILURE_UNRELEASED";
    public const string VerdictPending = "VERDICT_PENDING";
    public const string TargetNotInImpactScope = "TARGET_NOT_IN_IMPACT_SCOPE";
    public const string VersionMismatch = "VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string QcUnavailable = "QC_UNAVAILABLE";
}

public static class QcErrorCodes
{
    public const string ValidationFailed = "QC.VALIDATION_FAILED";
    public const string EligibilityBlocked = "QC.ELIGIBILITY_BLOCKED";
    public const string ApplicabilityUnknown = "QC.APPLICABILITY_UNKNOWN";
    public const string ReleaseGateIncomplete = "QC.RELEASE_GATE_INCOMPLETE";
    public const string ExpectedVersionConflict = "QC.EXPECTED_VERSION_CONFLICT";
    public const string NotAuthorized = "QC.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "QC.OBJECT_NOT_ACCESSIBLE";
    public const string PersistenceUnavailable = "QC.PERSISTENCE_UNAVAILABLE";
}

public sealed record QcVersionedReference(string Id, long Version);

public sealed record QcObjectContext(string LegalEntityId, string LaboratoryId);

public sealed record CreateQcRunRequest(
    string RuleSetVersion,
    QcObjectContext ObjectScope,
    string BatchId,
    long ExpectedBatchVersion,
    QcVersionedReference Method,
    QcVersionedReference QcRuleSet);

public sealed record AddQcResultRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    QcVersionedReference Rule,
    string ControlType,
    string ObservedValue,
    string Verdict,
    string VerdictBasis);

public sealed record RecordQcVerdictRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion);

public sealed record QcImpactTarget(string TargetType, string TargetId, long TargetVersion);

public sealed record RecordQcImpactRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    IReadOnlyList<QcImpactTarget> Targets);

public sealed record RecordQcDeviationApprovalRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    QcVersionedReference ApprovalRef,
    string Reason);

public sealed record SatisfyQcReleaseGateRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string Kind,
    QcVersionedReference EvidenceRef);

public sealed record ReleaseQcBlockRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion);

public sealed record QcResultEntry(
    string QcResultId,
    string QcRunId,
    QcVersionedReference Rule,
    string ControlType,
    string ObservedValue,
    string Verdict,
    string VerdictBasis,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record QcImpactEntry(
    string ImpactId,
    string QcRunId,
    string TargetType,
    string TargetId,
    long TargetVersion,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record QcReleaseGateEntry(
    string GateId,
    string QcRunId,
    string Kind,
    QcVersionedReference EvidenceRef,
    string SatisfiedBy,
    DateTimeOffset SatisfiedAt);

public sealed record QcDeviationApprovalEntry(
    string DeviationId,
    string QcRunId,
    QcVersionedReference ApprovalRef,
    string Reason,
    string ApprovedBy,
    DateTimeOffset ApprovedAt);

public sealed record QcRunResult(
    string QcRunId,
    long Version,
    string State,
    string RuleSetVersion,
    QcObjectContext ObjectScope,
    string BatchId,
    long BatchVersion,
    string BatchGateDecision,
    string BatchGateRuleSetVersion,
    QcVersionedReference Method,
    QcVersionedReference QcRuleSet,
    IReadOnlyList<QcResultEntry> Results,
    IReadOnlyList<QcImpactEntry> Impact,
    IReadOnlyList<QcReleaseGateEntry> Gates,
    IReadOnlyList<QcDeviationApprovalEntry> DeviationApprovals,
    string? ReleasedBy,
    DateTimeOffset? ReleasedAt,
    string OpenedBy,
    DateTimeOffset OpenedAt);

public sealed record QcReportabilityRequest(
    string OrganizationGroupId,
    string QcRunId,
    long ExpectedRunVersion,
    string RuleSetVersion,
    string TargetId)
{
    public string? CorrelationId { get; init; }
}

public sealed record QcReportabilityResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string QcRunId,
    string TargetId,
    long? CurrentVersion,
    IReadOnlyList<string> OutstandingGates,
    string RuleSetVersion);

/// <summary>
/// Answers reportability for one target <em>as judged by one QC run</em> —
/// the request pins both. A target touched by several runs is only reportable
/// when every one of those runs answers ALLOWED, so a consumer asking "may I
/// report this result?" must consult each run that names it. A PASSED run
/// therefore answers ALLOWED regardless of impact membership: a run that never
/// failed holds nothing back, and it cannot speak for a different run's block.
/// </summary>
public interface IQcReportabilityPort
{
    ValueTask<QcReportabilityResult> EvaluateAsync(
        QcReportabilityRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record QcAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    QcObjectContext ObjectScope,
    string Capability);

public sealed record QcAuthorizationDecision(bool Allowed)
{
    public static QcAuthorizationDecision Permit { get; } = new(true);
    public static QcAuthorizationDecision Deny { get; } = new(false);
}

public interface IQcAuthorizationPort
{
    ValueTask<QcAuthorizationDecision> AuthorizeAsync(
        QcAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
