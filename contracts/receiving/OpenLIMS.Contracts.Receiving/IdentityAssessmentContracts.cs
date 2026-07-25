namespace OpenLIMS.Contracts.Receiving;

public static class IdentityAssessmentContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "REC-ELIGIBILITY@1.0.0";
    public const string AssessmentPath = "/api/v1/received-items/{id}/identity-assessment";
    public const string ObservationsPath = "/api/v1/received-items/{id}/identity-observations";
    public const string DecisionsPath = "/api/v1/received-items/{id}/identity-decisions";
}

public static class IdentityAssessmentStates
{
    public const string NotStarted = "NOT_STARTED";
    public const string InProgress = "IN_PROGRESS";
    public const string Matched = "MATCHED";
    public const string Mismatched = "MISMATCHED";
    public const string Indeterminate = "INDETERMINATE";
}

public static class IdentityDecisionOutcomes
{
    public const string Matched = IdentityAssessmentStates.Matched;
    public const string Mismatched = IdentityAssessmentStates.Mismatched;
    public const string Indeterminate = IdentityAssessmentStates.Indeterminate;
}

public static class ReceivingEligibilityActions
{
    public const string Disassembly = "DISASSEMBLY";
    public const string SamplePreparation = "SAMPLE_PREPARATION";
    public const string TestAssignment = "TEST_ASSIGNMENT";
}

public static class ReceivingEligibilityDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class ReceivingEligibilityReasons
{
    public const string ReleaseDecisionRequired = "RELEASE_DECISION_REQUIRED";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string RequestedActionUnknown = "REQUESTED_ACTION_UNKNOWN";
    public const string ItemVersionMismatch = "ITEM_VERSION_MISMATCH";
    public const string ReceivingUnavailable = "RECEIVING_UNAVAILABLE";
}

public sealed record CreateIdentityObservationRequest(
    long ExpectedItemVersion,
    IReadOnlyList<string> ObservedLabels,
    string ObservedModel,
    string ObservedBatch,
    string Appearance,
    IReadOnlyList<string> AttachmentRefs,
    IReadOnlyList<string> AttachmentHashes);

public sealed record SubmitIdentityDecisionRequest(
    long ExpectedItemVersion,
    long ObservationVersion,
    long DeclarationSnapshotVersion,
    string Outcome,
    string ReasonCode,
    string Rationale,
    string RuleSetVersion);

public sealed record IdentityDeclarationSnapshotResult(
    string ReceivedItemId,
    long SnapshotVersion,
    long ItemVersion,
    string DeclaredDescription,
    string Model,
    string Batch,
    string? SerialNumber,
    string Color,
    DateTimeOffset CapturedAt);

public sealed record IdentityObservationResult(
    string ObservationId,
    long Version,
    long ExpectedItemVersion,
    IReadOnlyList<string> ObservedLabels,
    string ObservedModel,
    string ObservedBatch,
    string Appearance,
    IReadOnlyList<string> AttachmentRefs,
    IReadOnlyList<string> AttachmentHashes,
    DateTimeOffset ObservedAt,
    string ObservedBy);

public sealed record IdentityDecisionResult(
    string DecisionId,
    long Version,
    long ObservationVersion,
    long DeclarationSnapshotVersion,
    string Outcome,
    string ReasonCode,
    string Rationale,
    string RuleSetVersion,
    DateTimeOffset DecidedAt,
    string DecidedBy);

public sealed record IdentityAssessmentResult(
    string ReceivedItemId,
    string ReceivedItemNumber,
    string CurrentState,
    long ItemVersion,
    string AssessmentState,
    long AssessmentVersion,
    IdentityDeclarationSnapshotResult? DeclarationSnapshot,
    IReadOnlyList<IdentityObservationResult> Observations,
    IReadOnlyList<IdentityDecisionResult> Decisions);

public sealed record ReceivingEligibilityRequest(
    string LaboratoryId,
    string ReceivedItemId,
    string RequestedAction,
    long ExpectedItemVersion,
    string RuleSetVersion);

public sealed record ReceivingEligibilityResult(
    string Decision,
    string? CurrentState,
    string? AssessmentState,
    string? IdentityDecisionId,
    IReadOnlyList<string> ReasonCodes,
    long? ItemVersion,
    long? DecisionVersion,
    string RuleSetVersion);

public interface IReceivingEligibilityPort
{
    ValueTask<ReceivingEligibilityResult> EvaluateAsync(
        ReceivingEligibilityRequest request,
        CancellationToken cancellationToken = default);
}
