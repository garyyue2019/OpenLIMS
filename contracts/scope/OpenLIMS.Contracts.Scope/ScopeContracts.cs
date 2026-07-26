namespace OpenLIMS.Contracts.Scope;

public static class ScopeContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "SCOPE-LINE-GATE@1.0.0";
    public const string CreateMatrixPath = "/api/v1/scope-matrices";
    public const string CreateVersionPath = "/api/v1/scope-matrices/{id}/versions";
    public const string GetVersionPath = "/api/v1/scope-matrices/{id}/versions/{version:long}";
    public const string EligibilityPath = "/api/v1/scope-matrices/{id}/production-eligibility";
}

public static class ScopeCapabilities
{
    public const string Approve = "scope.approve";
}

public static class ScopeClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ProductCategory = "product_category";
}

public static class ScopeSubjectTypes
{
    public const string SubmissionItem = "SUBMISSION_ITEM";
    public const string ProductVariant = "PRODUCT_VARIANT";
    public const string FeatureNode = "FEATURE_NODE";
}

public static class ScopeEvaluationModes
{
    public const string MeasuredOnly = "MEASURED_ONLY";
    public const string Evaluated = "EVALUATED";
    public const string NotEvaluated = "NOT_EVALUATED";
    public const string Waived = "WAIVED";
}

public static class ScopeMatrixStates
{
    public const string Approved = "APPROVED";
}

public static class ScopeEligibilityDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class ScopeEligibilityReasons
{
    public const string ApprovedVersionRequired = "APPROVED_VERSION_REQUIRED";
    public const string MatrixVersionMismatch = "MATRIX_VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string ScopeIncomplete = "SCOPE_INCOMPLETE";
    public const string ScopeUnavailable = "SCOPE_UNAVAILABLE";
}

public static class ScopeErrorCodes
{
    public const string ValidationFailed = "SCP.VALIDATION_FAILED";
    public const string EvaluationIncomplete = "SCP.EVALUATION_INCOMPLETE";
    public const string EvaluationConflict = "SCP.EVALUATION_CONFLICT";
    public const string NotAuthorized = "SCP.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "SCP.OBJECT_NOT_ACCESSIBLE";
    public const string ExpectedVersionConflict = "SCP.EXPECTED_VERSION_CONFLICT";
    public const string ApplicabilityUnknown = "SCP.APPLICABILITY_UNKNOWN";
    public const string PersistenceUnavailable = "SCP.PERSISTENCE_UNAVAILABLE";
}

public sealed record ScopeObjectContext(
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory);

public sealed record ScopeVersionedReference(string Id, long Version);

public sealed record ScopeLineInput(
    string SubjectType,
    ScopeVersionedReference Subject,
    ScopeVersionedReference TargetMarket,
    ScopeVersionedReference RequirementClause,
    ScopeVersionedReference TestItem,
    ScopeVersionedReference Method,
    string MethodOption,
    ScopeVersionedReference SampleRequirement,
    string EvaluationMode,
    ScopeVersionedReference WorkCenter,
    string ReportPosition,
    ScopeVersionedReference? LimitRule = null,
    ScopeVersionedReference? DecisionRule = null,
    string? NonEvaluationReason = null,
    ScopeVersionedReference? WaiverApproval = null);

public sealed record SubmitScopeMatrixVersionRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    ScopeObjectContext ObjectScope,
    IReadOnlyList<ScopeLineInput> Lines);

public sealed record ScopeLineResult(
    string ScopeLineId,
    string SubjectType,
    ScopeVersionedReference Subject,
    ScopeVersionedReference TargetMarket,
    ScopeVersionedReference RequirementClause,
    ScopeVersionedReference TestItem,
    ScopeVersionedReference Method,
    string MethodOption,
    ScopeVersionedReference SampleRequirement,
    string EvaluationMode,
    ScopeVersionedReference WorkCenter,
    string ReportPosition,
    ScopeVersionedReference? LimitRule,
    ScopeVersionedReference? DecisionRule,
    string? NonEvaluationReason,
    ScopeVersionedReference? WaiverApproval);

public sealed record ScopeMatrixVersionResult(
    string ScopeMatrixId,
    long Version,
    string State,
    string RuleSetVersion,
    ScopeObjectContext ObjectScope,
    IReadOnlyList<ScopeLineResult> Lines,
    string ApprovedBy,
    DateTimeOffset ApprovedAt);

public sealed record ScopeProductionEligibilityRequest(
    string OrganizationGroupId,
    string ScopeMatrixId,
    long ExpectedMatrixVersion,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record ScopeProductionEligibilityResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string? ScopeMatrixId,
    long? CurrentMatrixVersion,
    string RuleSetVersion);

public interface IScopeProductionEligibilityPort
{
    ValueTask<ScopeProductionEligibilityResult> EvaluateAsync(
        ScopeProductionEligibilityRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ScopeAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    ScopeObjectContext ObjectScope,
    string Capability);

public sealed record ScopeAuthorizationDecision(bool Allowed)
{
    public static ScopeAuthorizationDecision Permit { get; } = new(true);
    public static ScopeAuthorizationDecision Deny { get; } = new(false);
}

public interface IScopeAuthorizationPort
{
    ValueTask<ScopeAuthorizationDecision> AuthorizeAsync(
        ScopeAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
