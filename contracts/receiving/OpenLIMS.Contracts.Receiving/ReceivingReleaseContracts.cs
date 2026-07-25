namespace OpenLIMS.Contracts.Receiving;

public static class ReceivingReleaseContract
{
    public const string Version = "2.0.0";
    public const string RuleSetVersion = "REC-RELEASE@2.0.0";
    public const string ExceptionMatrixVersion = ReceivingExceptionContract.MatrixVersion;
    public const string DecisionsPath = "/api/v1/received-items/{id}/release-decisions";
}

public static class ReceivingReleaseOutcomes
{
    public const string Released = "RELEASED";
    public const string ReleasedWithConstraints = "RELEASED_WITH_CONSTRAINTS";
}

public static class ReceivingReleaseStates
{
    public const string Accepted = "ACCEPTED";
    public const string ConditionallyAccepted = "CONDITIONALLY_ACCEPTED";
}

public sealed record SubmitReceivingReleaseDecisionRequest(
    long ExpectedItemVersion,
    string RuleSetVersion,
    string Rationale);

public sealed record ReceivingReleaseExceptionReference(
    string ExceptionId,
    string Status,
    long ExceptionVersion,
    string DecisionId,
    long DecisionVersion,
    string MatrixVersion);

public sealed record ReceivingReleaseDecisionResult(
    string ReleaseDecisionId,
    long Version,
    string ReceivedItemId,
    string ReceivedItemNumber,
    long BoundItemVersion,
    long ItemVersion,
    string State,
    string IdentityDecisionId,
    long IdentityDecisionVersion,
    IReadOnlyList<ReceivingReleaseExceptionReference> ExceptionDecisionVersions,
    string ReleaseRuleVersion,
    string ExceptionMatrixVersion,
    string Outcome,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<string> ProhibitedActions,
    DateTimeOffset? ConstraintsValidUntil,
    string Rationale,
    DateTimeOffset ApprovedAt,
    string ApprovedBy);

public static class ReceivingEligibilityV2Contract
{
    public const string Version = "2.0.0";
    public const string RuleSetVersion = "REC-ELIGIBILITY@2.0.0";
}

public static class ReceivingEligibilityV2Reasons
{
    public const string ReleaseDecisionRequired = "RELEASE_DECISION_REQUIRED";
    public const string ReleaseDecisionNotCurrent = "RELEASE_DECISION_NOT_CURRENT";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string RequestedActionUnknown = "REQUESTED_ACTION_UNKNOWN";
    public const string ItemVersionMismatch = "ITEM_VERSION_MISMATCH";
    public const string ActionNotAllowed = "ACTION_NOT_ALLOWED";
    public const string ConstraintsExpired = "CONSTRAINTS_EXPIRED";
    public const string ReceivingUnavailable = "RECEIVING_UNAVAILABLE";
}

public sealed record ReceivingEligibilityV2Request(
    string LaboratoryId,
    string ReceivedItemId,
    string RequestedAction,
    long ExpectedItemVersion,
    string RuleSetVersion);

public sealed record ReceivingEligibilityV2Result(
    string Decision,
    string? CurrentState,
    string? AssessmentState,
    string? IdentityDecisionId,
    string? ReleaseDecisionId,
    IReadOnlyList<string> ReasonCodes,
    long? ItemVersion,
    long? ReleaseDecisionVersion,
    string RuleSetVersion,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<string> ProhibitedActions,
    DateTimeOffset? ConstraintsValidUntil);

public interface IReceivingEligibilityPortV2
{
    ValueTask<ReceivingEligibilityV2Result> EvaluateAsync(
        ReceivingEligibilityV2Request request,
        CancellationToken cancellationToken = default);
}
