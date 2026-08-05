namespace OpenLIMS.Contracts.Result;

public static class ResultContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "RESULT-ADOPTION@1.0.0";
    public const string CalculationRuleSetVersion = "RESULT-CALCULATION@1.0.0";
    public const string AccreditationRuleSetVersion = "RESULT-ACCREDITATION@1.0.0";
    public const string CreateGroupPath = "/api/v1/result-groups";
    public const string AddObservationPath = "/api/v1/result-groups/{id}/observations";
    public const string AddDerivationPath = "/api/v1/result-groups/{id}/derivations";
    public const string ExecuteCalculationPath = "/api/v1/result-groups/{id}/calculations";
    public const string RecordAdoptionRulePath = "/api/v1/result-groups/{id}/adoption-rule";
    public const string AdoptPath = "/api/v1/result-groups/{id}/adoptions";
    public const string RecordAccreditationAssessmentPath = "/api/v1/result-groups/{id}/accreditation-assessments";
    public const string AccreditationEligibilityPath = "/api/v1/result-groups/{id}/accreditation-eligibility";
    public const string GetGroupPath = "/api/v1/result-groups/{id}";
    public const string AdoptionStatusPath = "/api/v1/result-groups/{id}/adoption-status";
}

public static class ResultCapabilities
{
    public const string Record = "result.record";
}

public static class ResultClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ProductCategory = "product_category";
}

public static class ResultObservationKinds
{
    public const string Initial = "INITIAL";
    public const string Duplicate = "DUPLICATE";
    public const string Retest = "RETEST";
    public const string Supplement = "SUPPLEMENT";
    public const string RePreparation = "RE_PREPARATION";
    public const string ReSampling = "RE_SAMPLING";
}

public static class ResultAdoptionStrategies
{
    public const string RetestReplacesOriginal = "RETEST_REPLACES_ORIGINAL";
    public const string TechnicalReviewSelects = "TECHNICAL_REVIEW_SELECTS";
}

public static class ResultEvidenceSources
{
    public const string Cds = "CDS";
    public const string Eln = "ELN";
    public const string Instrument = "INSTRUMENT";
    public const string Manual = "MANUAL";
}

public static class ResultAdoptionDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class ResultRoundingModes
{
    public const string ToEven = "TO_EVEN";
    public const string AwayFromZero = "AWAY_FROM_ZERO";
    public const string TowardZero = "TOWARD_ZERO";
    public const string Floor = "FLOOR";
    public const string Ceiling = "CEILING";
}

public static class ResultDetectionQualifications
{
    public const string BelowLod = "BELOW_LOD";
    public const string BelowLoq = "BELOW_LOQ";
    public const string Quantified = "QUANTIFIED";
}

public static class ResultLimitOperators
{
    public const string None = "NONE";
    public const string LessThanOrEqual = "LESS_THAN_OR_EQUAL";
    public const string GreaterThanOrEqual = "GREATER_THAN_OR_EQUAL";
    public const string BetweenInclusive = "BETWEEN_INCLUSIVE";
}

public static class ResultLimitEvaluationBases
{
    public const string Exact = "EXACT";
    public const string Rounded = "ROUNDED";
}

public static class ResultLimitDecisions
{
    public const string NotEvaluated = "NOT_EVALUATED";
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string Unknown = "UNKNOWN";
}

public static class ResultAccreditationStages
{
    public const string Execution = "EXECUTION";
    public const string Result = "RESULT";
}

public static class ResultAccreditationDecisions
{
    public const string Eligible = "ELIGIBLE";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class ResultAccreditationReasons
{
    public const string SiteMismatch = "SITE_MISMATCH";
    public const string ProductMatrixMismatch = "PRODUCT_MATRIX_MISMATCH";
    public const string ParameterMismatch = "PARAMETER_MISMATCH";
    public const string NotYetValid = "NOT_YET_VALID";
    public const string Expired = "EXPIRED";
    public const string ActorUnauthorized = "ACTOR_UNAUTHORIZED";
    public const string TargetRequired = "TARGET_REQUIRED";
    public const string TargetNotEffective = "TARGET_NOT_EFFECTIVE";
    public const string TargetNotNumeric = "TARGET_NOT_NUMERIC";
    public const string RangeUnitMismatch = "RANGE_UNIT_MISMATCH";
    public const string OutsideRange = "OUTSIDE_RANGE";
}

public static class ResultAccreditationEligibilityReasons
{
    public const string GroupRequired = "GROUP_REQUIRED";
    public const string GroupVersionMismatch = "GROUP_VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string ExecutionAssessmentRequired = "EXECUTION_ASSESSMENT_REQUIRED";
    public const string ResultAssessmentRequired = "RESULT_ASSESSMENT_REQUIRED";
    public const string AssessmentBlocked = "ASSESSMENT_BLOCKED";
    public const string AssessmentExpired = "ASSESSMENT_EXPIRED";
    public const string EvidenceMismatch = "EVIDENCE_MISMATCH";
    public const string EffectiveTargetMismatch = "EFFECTIVE_TARGET_MISMATCH";
    public const string CurrentActorUnauthorized = "CURRENT_ACTOR_UNAUTHORIZED";
    public const string EvidenceUnavailable = "EVIDENCE_UNAVAILABLE";
}

public static class ResultAdoptionReasons
{
    public const string GroupRequired = "GROUP_REQUIRED";
    public const string AdoptionRequired = "ADOPTION_REQUIRED";
    public const string GroupVersionMismatch = "GROUP_VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string ResultUnavailable = "RESULT_UNAVAILABLE";
}

public static class ResultErrorCodes
{
    public const string ValidationFailed = "RES.VALIDATION_FAILED";
    public const string EligibilityBlocked = "RES.ELIGIBILITY_BLOCKED";
    public const string ApplicabilityUnknown = "RES.APPLICABILITY_UNKNOWN";
    public const string AdoptionRuleRequired = "RES.ADOPTION_RULE_REQUIRED";
    public const string AdoptionStrategyViolation = "RES.ADOPTION_STRATEGY_VIOLATION";
    public const string CalculationFailed = "RES.CALCULATION_FAILED";
    public const string NotAuthorized = "RES.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "RES.OBJECT_NOT_ACCESSIBLE";
    public const string ExpectedVersionConflict = "RES.EXPECTED_VERSION_CONFLICT";
    public const string PersistenceUnavailable = "RES.PERSISTENCE_UNAVAILABLE";
}

public sealed record ResultObjectContext(
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory);

public sealed record ResultVersionedReference(string Id, long Version);

public sealed record ResultEvidence(
    string SourceSystem,
    ResultVersionedReference ExternalRef,
    string Sha256,
    string ParserVersion);

public sealed record CreateResultGroupRequest(
    string RuleSetVersion,
    ResultObjectContext ObjectScope,
    string BatchId,
    long ExpectedBatchVersion,
    string MemberId,
    ResultVersionedReference TestItem,
    string ScopeLineId);

public sealed record AddResultObservationRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string Kind,
    string Value,
    string Unit,
    ResultEvidence Evidence,
    string? TriggerReason = null,
    ResultVersionedReference? ApprovalRef = null);

public sealed record ResultDerivationInput(string TargetId, bool Included, string? Rationale = null);

public sealed record AddResultDerivationRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    ResultVersionedReference AggregationRule,
    string Value,
    string Unit,
    IReadOnlyList<ResultDerivationInput> Inputs);

public sealed record ResultCalculationInput(string TargetId, decimal Coefficient);

public sealed record ResultCalculationRule(
    ResultVersionedReference CalculationRule,
    ResultVersionedReference UnitConversionRule,
    string InputUnit,
    string OutputUnit,
    decimal UnitMultiplier,
    decimal UnitOffset,
    decimal DilutionFactor,
    decimal QuantityFactor,
    int DecimalPlaces,
    string RoundingMode,
    decimal? Lod,
    decimal? Loq,
    string LimitOperator,
    string LimitEvaluationBasis,
    decimal? LowerLimit,
    decimal? UpperLimit);

public sealed record ExecuteResultCalculationRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    IReadOnlyList<ResultCalculationInput> Inputs,
    ResultCalculationRule Rule);

public sealed record RecordAdoptionRuleRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string Strategy,
    ResultVersionedReference RuleRef);

public sealed record AdoptResultRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string TargetId,
    ResultVersionedReference? ReviewApprovalRef = null);

public sealed record ResultObservationResult(
    string ObservationId,
    string ResultGroupId,
    long GroupVersion,
    string Kind,
    string Value,
    string Unit,
    ResultEvidence Evidence,
    string? TriggerReason,
    ResultVersionedReference? ApprovalRef,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record ResultDerivationResult(
    string DerivationId,
    string ResultGroupId,
    long GroupVersion,
    ResultVersionedReference AggregationRule,
    string Value,
    string Unit,
    IReadOnlyList<ResultDerivationInput> Inputs,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record ResultCalculationResolvedInput(
    string TargetId,
    decimal Value,
    string Unit,
    decimal Coefficient);

public sealed record ResultCalculationResult(
    string CalculationId,
    string ResultGroupId,
    long GroupVersion,
    IReadOnlyList<ResultCalculationResolvedInput> Inputs,
    ResultCalculationRule Rule,
    decimal ExactValue,
    decimal RoundedValue,
    string ReportedValue,
    string Unit,
    string Qualification,
    string LimitDecision,
    string ExecutedBy,
    DateTimeOffset ExecutedAt);

public sealed record AdoptionRuleResult(
    string ResultGroupId,
    long GroupVersion,
    long RuleVersion,
    string Strategy,
    ResultVersionedReference RuleRef,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record ResultAdoptionResult(
    string ResultGroupId,
    long GroupVersion,
    long AdoptionVersion,
    string TargetId,
    long RuleVersion,
    ResultVersionedReference? ReviewApprovalRef,
    string AdoptedBy,
    DateTimeOffset AdoptedAt);

public sealed record RecordResultAccreditationAssessmentRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string Stage,
    string? TargetId,
    ResultVersionedReference Accreditation,
    ResultVersionedReference Method,
    string SiteId,
    string ProductOrMatrix,
    string Parameter,
    string RangeUnit,
    decimal RangeLower,
    decimal RangeUpper,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    IReadOnlyList<string> AuthorizedActorIds);

public sealed record ResultAccreditationAssessmentResult(
    string AssessmentId,
    string ResultGroupId,
    long GroupVersion,
    string Stage,
    string? TargetId,
    ResultVersionedReference Accreditation,
    ResultVersionedReference Method,
    string SiteId,
    string ProductOrMatrix,
    string Parameter,
    string RangeUnit,
    decimal RangeLower,
    decimal RangeUpper,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    IReadOnlyList<string> AuthorizedActorIds,
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string AssessedBy,
    DateTimeOffset AssessedAt);

public sealed record ResultGroupResult(
    string ResultGroupId,
    long Version,
    string RuleSetVersion,
    ResultObjectContext ObjectScope,
    string BatchId,
    long BatchVersion,
    string BatchGateDecision,
    string BatchGateRuleSetVersion,
    string MemberId,
    ResultVersionedReference TestItem,
    string ScopeLineId,
    IReadOnlyList<ResultObservationResult> Observations,
    IReadOnlyList<ResultDerivationResult> Derivations,
    IReadOnlyList<AdoptionRuleResult> AdoptionRules,
    IReadOnlyList<ResultAdoptionResult> Adoptions,
    string CreatedBy,
    DateTimeOffset CreatedAt)
{
    public IReadOnlyList<ResultCalculationResult> Calculations { get; init; } = [];
    public IReadOnlyList<ResultAccreditationAssessmentResult> AccreditationAssessments { get; init; } = [];
}

public sealed record ResultAccreditationEligibilityRequest(
    string OrganizationGroupId,
    string ResultGroupId,
    long ExpectedGroupVersion,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record ResultAccreditationEligibilityResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string? ResultGroupId,
    long? CurrentGroupVersion,
    string? ExecutionAssessmentId,
    string? ResultAssessmentId,
    string? EffectiveTargetId,
    string RuleSetVersion);

public interface IResultAccreditationEligibilityPort
{
    ValueTask<ResultAccreditationEligibilityResult> EvaluateAsync(
        ResultAccreditationEligibilityRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ResultAdoptionStatusRequest(
    string OrganizationGroupId,
    string ResultGroupId,
    long ExpectedGroupVersion,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record ResultAdoptionStatusResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string? ResultGroupId,
    long? CurrentGroupVersion,
    string? EffectiveTargetId,
    long? EffectiveAdoptionVersion,
    string RuleSetVersion);

public interface IResultAdoptionPort
{
    ValueTask<ResultAdoptionStatusResult> EvaluateAsync(
        ResultAdoptionStatusRequest request,
        CancellationToken cancellationToken = default);
}

public static class ResultConclusionEvidenceDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class ResultConclusionEvidenceReasons
{
    public const string GroupUnavailable = "GROUP_UNAVAILABLE";
    public const string AdoptionVersionMissing = "ADOPTION_VERSION_MISSING";
    public const string TargetUnavailable = "TARGET_UNAVAILABLE";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string EvidenceUnavailable = "EVIDENCE_UNAVAILABLE";
}

public sealed record ResultConclusionEvidenceRequest(
    string OrganizationGroupId,
    string ResultGroupId,
    long AdoptionVersion,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record ResultConclusionEvidenceResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string? ResultGroupId,
    long? CurrentGroupVersion,
    long? AdoptionVersion,
    string? TargetId,
    string? TargetKind,
    string? RecordedBy,
    ResultObjectContext? ObjectScope,
    string RuleSetVersion);

public interface IResultConclusionEvidencePort
{
    ValueTask<ResultConclusionEvidenceResult> EvaluateAsync(
        ResultConclusionEvidenceRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ResultAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    ResultObjectContext ObjectScope,
    string Capability);

public sealed record ResultAuthorizationDecision(bool Allowed)
{
    public static ResultAuthorizationDecision Permit { get; } = new(true);
    public static ResultAuthorizationDecision Deny { get; } = new(false);
}

public interface IResultAuthorizationPort
{
    ValueTask<ResultAuthorizationDecision> AuthorizeAsync(
        ResultAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
