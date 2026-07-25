namespace OpenLIMS.Contracts.Receiving;

public static class ReceivingExceptionContract
{
    public const string Version = "1.0.0";
    public const string MatrixVersion = "OD-005@1.0.0";
    public const string CreatePath = "/api/v1/exceptions";
    public const string DetailPath = "/api/v1/exceptions/{id}";
    public const string DecisionsPath = "/api/v1/exceptions/{id}/decisions";
}

public static class ReceivingExceptionTypes
{
    public const string QuantityShortage = "QUANTITY_SHORTAGE";
    public const string TemperatureExcursion = "TEMPERATURE_EXCURSION";
    public const string Damaged = "DAMAGED";
    public const string Contamination = "CONTAMINATION";
    public const string LabelConflict = "LABEL_CONFLICT";
    public const string IdentityMismatch = "IDENTITY_MISMATCH";
    public const string IdentityIndeterminate = "IDENTITY_INDETERMINATE";
}

public static class ReceivingExceptionSeverities
{
    public const string Standard = "STANDARD";
    public const string SafetyCritical = "SAFETY_CRITICAL";
}

public static class ReceivingExceptionDecisionTypes
{
    public const string AwaitCustomer = "AWAIT_CUSTOMER";
    public const string ConditionalAccept = "CONDITIONAL_ACCEPT";
    public const string Reject = "REJECT";
    public const string SafetyHold = "SAFETY_HOLD";
}

public static class ReceivingExceptionStatuses
{
    public const string Open = "OPEN";
    public const string AwaitingCustomer = "AWAITING_CUSTOMER";
    public const string ConditionallyAccepted = "CONDITIONALLY_ACCEPTED";
    public const string Rejected = "REJECTED";
    public const string SafetyHold = "SAFETY_HOLD";
}

public sealed record CreateReceivingExceptionRequest(
    string ReceivedItemId,
    long ExpectedItemVersion,
    string Type,
    DateTimeOffset ObservedAt,
    string Description,
    IReadOnlyList<string> EvidenceRefs,
    IReadOnlyList<string> EvidenceHashes);

public sealed record SubmitReceivingExceptionDecisionRequest(
    long ExpectedVersion,
    string DecisionType,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<string> ProhibitedActions,
    DateTimeOffset? ValidUntil,
    IReadOnlyList<string> EvidenceRefs,
    IReadOnlyList<string> EvidenceHashes,
    string TechnicalImpact,
    string Rationale,
    string MatrixVersion);

public sealed record ReceivingExceptionDecisionResult(
    string DecisionId,
    long Version,
    string DecisionType,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<string> ProhibitedActions,
    DateTimeOffset? ValidUntil,
    IReadOnlyList<string> EvidenceRefs,
    IReadOnlyList<string> EvidenceHashes,
    string TechnicalImpact,
    string Rationale,
    string MatrixVersion,
    DateTimeOffset DecidedAt,
    string DecidedBy);

public sealed record ReceivingExceptionResult(
    string ExceptionId,
    string ReceivedItemId,
    string ReceivedItemNumber,
    long ItemVersion,
    string ItemState,
    string Type,
    string Severity,
    string Description,
    DateTimeOffset ObservedAt,
    IReadOnlyList<string> EvidenceRefs,
    IReadOnlyList<string> EvidenceHashes,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string Status,
    long Version,
    IReadOnlyList<ReceivingExceptionDecisionResult> Decisions);
