namespace OpenLIMS.Contracts.Toy;

public static class ToyContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "TOY-AGE-GRADE@1.0.0";
    public const string AgeDeclarationPath = "/api/v1/toy/products/{id}/age-declarations";
    public const string AgeGradeDecisionPath = "/api/v1/toy/products/{id}/age-grade-decisions";
    public const string FreezeDecisionPath = "/api/v1/toy/products/{id}/age-grade-decisions/{versionNumber}/freeze";
    public const string AccessibilityAssessmentPath = "/api/v1/toy/products/{id}/accessibility-assessments";
    public const string ResolveTriggerPath = "/api/v1/toy/products/{id}/reassessment-triggers/{triggerId}/resolution";
    public const string OverviewPath = "/api/v1/toy/products/{id}/overview";
}

public static class ToyCapabilities
{
    public const string Manage = "toy.manage";
}

public static class ToyClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
}

public static class ToyDecisionStates
{
    public const string Draft = "DRAFT";
    public const string Effective = "EFFECTIVE";
    public const string Superseded = "SUPERSEDED";
}

/// <summary>
/// OPS-TOY-003: accessibility is assessed as-received, after normal use, and
/// after each abuse event — three separate versioned records, never one
/// overwritten judgement.
/// </summary>
public static class ToyAssessmentStages
{
    public const string Initial = "INITIAL";
    public const string AfterNormalUse = "AFTER_NORMAL_USE";
    public const string AfterAbuse = "AFTER_ABUSE";

    public static readonly IReadOnlyList<string> All = [Initial, AfterNormalUse, AfterAbuse];
}

/// <summary>
/// A newly exposed part can pull in new mechanical, chemical and labeling
/// requirements at once, so one exposure raises one trigger per scope rather
/// than a single undifferentiated "please look again".
/// </summary>
public static class ToyReassessmentScopes
{
    public const string Mechanical = "MECHANICAL";
    public const string Chemical = "CHEMICAL";
    public const string Labeling = "LABELING";

    public static readonly IReadOnlyList<string> All = [Mechanical, Chemical, Labeling];
}

public static class ToyTriggerStates
{
    public const string Pending = "PENDING";
    public const string Resolved = "RESOLVED";
}

public static class ToyAccessibilityStatuses
{
    public const string Settled = "SETTLED";
    public const string ReassessmentPending = "REASSESSMENT_PENDING";
}

public static class ToyAgeGradeDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class ToyAgeGradeReasons
{
    public const string NoEffectiveDecision = "NO_EFFECTIVE_DECISION";
    public const string ReassessmentPending = "REASSESSMENT_PENDING";
    public const string VersionMismatch = "VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string ToyUnavailable = "TOY_UNAVAILABLE";
}

public static class ToyErrorCodes
{
    public const string ValidationFailed = "TOY.VALIDATION_FAILED";
    public const string DecisionFrozen = "TOY.DECISION_FROZEN";
    public const string DecisionNotFound = "TOY.DECISION_NOT_FOUND";
    public const string ReassessmentNotPending = "TOY.REASSESSMENT_NOT_PENDING";
    public const string ExpectedVersionConflict = "TOY.EXPECTED_VERSION_CONFLICT";
    public const string NotAuthorized = "TOY.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "TOY.OBJECT_NOT_ACCESSIBLE";
    public const string PersistenceUnavailable = "TOY.PERSISTENCE_UNAVAILABLE";
}

public sealed record ToyVersionedReference(string Id, long Version);

public sealed record ToyObjectContext(string LegalEntityId, string LaboratoryId);

public sealed record RecordAgeDeclarationRequest(
    string RuleSetVersion,
    ToyObjectContext ObjectScope,
    long ExpectedCurrentVersion,
    int DeclaredMinimumAgeMonths,
    string IntendedUse,
    string DeclarationSource);

public sealed record RecordAgeGradeDecisionRequest(
    string RuleSetVersion,
    ToyObjectContext ObjectScope,
    long ExpectedCurrentVersion,
    int MinimumAgeMonths,
    string Rationale,
    ToyVersionedReference StandardRef,
    string ApprovedBy);

public sealed record FreezeAgeGradeDecisionRequest(
    string RuleSetVersion,
    long ExpectedCurrentVersion);

public sealed record RecordAccessibilityAssessmentRequest(
    string RuleSetVersion,
    ToyObjectContext ObjectScope,
    long ExpectedCurrentVersion,
    string Stage,
    string? AbuseEventRef,
    IReadOnlyList<string> AccessibleParts);

public sealed record ResolveReassessmentTriggerRequest(
    string RuleSetVersion,
    long ExpectedCurrentVersion,
    ToyVersionedReference ResolutionRef);

public sealed record ToyAgeDeclarationEntry(
    string DeclarationId,
    string ProductId,
    int DeclaredMinimumAgeMonths,
    string IntendedUse,
    string DeclarationSource,
    string DeclaredBy,
    DateTimeOffset DeclaredAt);

public sealed record ToyAgeGradeDecisionEntry(
    string DecisionId,
    string ProductId,
    int VersionNumber,
    int MinimumAgeMonths,
    string Rationale,
    ToyVersionedReference StandardRef,
    string ApprovedBy,
    string State,
    DateTimeOffset DecidedAt,
    DateTimeOffset? FrozenAt);

public sealed record ToyAccessibilityAssessmentEntry(
    string AssessmentId,
    string ProductId,
    int VersionNumber,
    string Stage,
    string? AbuseEventRef,
    IReadOnlyList<string> AccessibleParts,
    string AssessedBy,
    DateTimeOffset AssessedAt);

public sealed record ToyReassessmentTriggerEntry(
    string TriggerId,
    string ProductId,
    int AssessmentVersion,
    string Scope,
    IReadOnlyList<string> NewlyExposedParts,
    string State,
    ToyVersionedReference? ResolutionRef,
    string? ResolvedBy,
    DateTimeOffset? ResolvedAt);

public sealed record ToyProductOverview(
    string ProductId,
    long Version,
    string RuleSetVersion,
    ToyObjectContext ObjectScope,
    ToyAgeGradeDecisionEntry? EffectiveDecision,
    IReadOnlyList<ToyAgeDeclarationEntry> Declarations,
    IReadOnlyList<ToyAgeGradeDecisionEntry> Decisions,
    IReadOnlyList<ToyAccessibilityAssessmentEntry> Assessments,
    IReadOnlyList<ToyReassessmentTriggerEntry> Triggers,
    string AccessibilityStatus);

public sealed record ToyAgeGradeStatusRequest(
    string OrganizationGroupId,
    string ProductId,
    long ExpectedProductVersion,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record ToyAgeGradeStatusResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string ProductId,
    long? CurrentVersion,
    int? EffectiveDecisionVersion,
    int? MinimumAgeMonths,
    string AccessibilityStatus,
    string RuleSetVersion);

/// <summary>
/// Downstream sample-requirement and label-review chains ask this port whether
/// a product's age grading is settled enough to build on. UNKNOWN means the
/// answer could not be established and must be treated as a block — an
/// unanswered age question is not a permissive one.
/// </summary>
public interface IToyAgeGradeStatusPort
{
    ValueTask<ToyAgeGradeStatusResult> EvaluateAsync(
        ToyAgeGradeStatusRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ToyAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    ToyObjectContext ObjectScope,
    string Capability);

public sealed record ToyAuthorizationDecision(bool Allowed)
{
    public static ToyAuthorizationDecision Permit { get; } = new(true);
    public static ToyAuthorizationDecision Deny { get; } = new(false);
}

public interface IToyAuthorizationPort
{
    ValueTask<ToyAuthorizationDecision> AuthorizeAsync(
        ToyAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IToyProductService
{
    Task<ToyProductOverview> RecordDeclarationAsync(
        string productId,
        RecordAgeDeclarationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyProductOverview> RecordDecisionAsync(
        string productId,
        RecordAgeGradeDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyProductOverview> FreezeDecisionAsync(
        string productId,
        int versionNumber,
        FreezeAgeGradeDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyProductOverview> RecordAssessmentAsync(
        string productId,
        RecordAccessibilityAssessmentRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyProductOverview> ResolveTriggerAsync(
        string productId,
        string triggerId,
        ResolveReassessmentTriggerRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyProductOverview> GetOverviewAsync(
        string productId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
