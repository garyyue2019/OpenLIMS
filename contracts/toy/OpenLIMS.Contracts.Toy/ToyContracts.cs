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

public static class ToyTestUnitPlanContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "TOY-TEST-UNIT-SAMPLE-DEMAND@1.0.0";
    public const string PlanPath = "/api/v1/toy/products/{id}/test-unit-plans";
    public const string ApprovalPath = "/api/v1/toy/products/{id}/test-unit-plans/{planVersion}/approval";
    public const string AllocationPath = "/api/v1/toy/products/{id}/test-unit-plans/{planVersion}/allocations";
    public const string DetailPath = "/api/v1/toy/products/{id}/test-unit-plans/{planVersion}";
}

public static class ToyCapabilities
{
    public const string Manage = "toy.manage";
    public const string SampleDemandApprove = "toy.sample-demand.approve";
    public const string LabelManage = "toy.label.manage";
    public const string LabelReview = "toy.label.review";
    // OD-034@1.0.0: Conclusion approval capabilities
    public const string ConclusionApproveItem = "toy.conclusion.approve-item";
    public const string ConclusionApproveScope = "toy.conclusion.approve-scope";
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
    public const string TestUnitPlanInvalid = "TOY.TEST_UNIT_PLAN_INVALID";
    public const string SampleRequirementUnknown = "TOY.SAMPLE_REQUIREMENT_UNKNOWN";
    public const string DestructiveTestUnitConflict = "TOY.DESTRUCTIVE_TEST_UNIT_CONFLICT";
    public const string SampleRequirementNotApproved = "TOY.SAMPLE_REQUIREMENT_NOT_APPROVED";
    public const string DownstreamEligibilityBlocked = "TOY.DOWNSTREAM_ELIGIBILITY_BLOCKED";
    public const string LabelArtifactInvalid = "TOY.LABEL_ARTIFACT_INVALID";
    public const string LabelReviewInvalid = "TOY.LABEL_REVIEW_INVALID";
    public const string LabelImpactUnknown = "TOY.LABEL_IMPACT_UNKNOWN";
    public const string LabelReviewNotValid = "TOY.LABEL_REVIEW_NOT_VALID";
    // OD-034@1.0.0: Conclusion error codes
    public const string ConclusionEvidenceIncomplete = "TOY.CONCLUSION_EVIDENCE_INCOMPLETE";
    public const string ConclusionPolicyUnknown = "TOY.CONCLUSION_POLICY_UNKNOWN";
    public const string FictitiousWholeItemConclusion = "TOY.FICTITIOUS_WHOLE_ITEM_CONCLUSION";
    public const string ConclusionSodViolation = "TOY.CONCLUSION_SOD_VIOLATION";
}

public static class ToyTestUnitPlanStates
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    public const string Superseded = "SUPERSEDED";
}

public static class ToySampleRequirementDecisions
{
    public const string PendingTechnicalApproval = "PENDING_TECHNICAL_APPROVAL";
    public const string Approved = "APPROVED";
    public const string Unknown = "UNKNOWN";
    public const string Superseded = "SUPERSEDED";
}

public static class ToySampleDemandKinds
{
    public const string Base = "BASE";
    public const string Parallel = "PARALLEL";
    public const string ExclusiveDestructive = "EXCLUSIVE_DESTRUCTIVE";
    public const string ChemicalMinimum = "CHEMICAL_MINIMUM";
    public const string RetestReserve = "RETEST_RESERVE";
    public const string Retention = "RETENTION";

    public static readonly IReadOnlyList<string> All =
        [Base, Parallel, ExclusiveDestructive, ChemicalMinimum, RetestReserve, Retention];
}

public static class ToyApplicabilityDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class ToyTestUnitPlanStatusDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class ToyTestUnitPlanStatusReasons
{
    public const string PlanRequired = "PLAN_REQUIRED";
    public const string PlanVersionMismatch = "PLAN_VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string RequirementNotApproved = "REQUIREMENT_NOT_APPROVED";
    public const string DownstreamAllocationRequired = "DOWNSTREAM_ALLOCATION_REQUIRED";
    public const string ToyUnavailable = "TOY_UNAVAILABLE";
}

public sealed record ToyVersionedReference(string Id, long Version);

public sealed record ToyObjectContext(string LegalEntityId, string LaboratoryId);

public sealed record CreateToySequenceStepInput(
    string StepId,
    int SequenceOrder,
    ToyVersionedReference TaskRef,
    bool Destructive,
    string? ExclusiveDestructiveGroupId,
    ToyVersionedReference? ShareRuleRef);

public sealed record CreateToyTestUnitInput(
    string TestUnitId,
    ToyVersionedReference PhysicalObjectRef,
    IReadOnlyList<ToyVersionedReference> HazardDomainRefs,
    int ParallelNumber,
    IReadOnlyList<CreateToySequenceStepInput> SequenceSteps);

public sealed record ToySampleDemandInput(
    string ComponentId,
    string Kind,
    ToyVersionedReference? HazardDomainRef,
    string? TestUnitId,
    decimal Amount,
    string Dimension,
    string Unit,
    ToyVersionedReference SourceRuleRef,
    string Applicability);

public sealed record CreateToyTestUnitPlanRequest(
    string RuleSetVersion,
    ToyObjectContext ObjectScope,
    long ExpectedCurrentVersion,
    long ProductVersion,
    long AgeGradeDecisionVersion,
    long AccessibilityAssessmentVersion,
    string ScopeMatrixId,
    long ScopeMatrixVersion,
    IReadOnlyList<ToyVersionedReference> ScopeLineRefs,
    IReadOnlyList<ToyVersionedReference> SampleRuleRefs,
    IReadOnlyList<CreateToyTestUnitInput> TestUnits,
    IReadOnlyList<ToySampleDemandInput> DemandInputs);

public sealed record ApproveToySampleRequirementRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string InputHash,
    string ApprovalComment);

public sealed record ToyQuantityGateInput(
    string QuantityAccountId,
    long ExpectedAccountVersion,
    string RuleSetVersion,
    decimal Amount,
    string Dimension,
    string Unit,
    string ReservationRef);

public sealed record ToyAllocationGateInput(
    string AllocationId,
    long ExpectedSubjectAllocationVersion,
    string RuleSetVersion,
    string TestUnitId,
    string SequenceStepId);

public sealed record RequestToyAllocationRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    IReadOnlyList<ToyQuantityGateInput> QuantityChecks,
    IReadOnlyList<ToyAllocationGateInput> AllocationChecks);

public sealed record ToySequenceStepEntry(
    string StepId,
    int SequenceOrder,
    ToyVersionedReference TaskRef,
    bool Destructive,
    string? ExclusiveDestructiveGroupId,
    ToyVersionedReference? ShareRuleRef);

public sealed record ToyTestUnitEntry(
    string TestUnitId,
    ToyVersionedReference PhysicalObjectRef,
    IReadOnlyList<ToyVersionedReference> HazardDomainRefs,
    int ParallelNumber,
    IReadOnlyList<ToySequenceStepEntry> SequenceSteps);

public sealed record ToySampleDemandComponent(
    string ComponentId,
    string Kind,
    ToyVersionedReference? HazardDomainRef,
    string? TestUnitId,
    decimal Amount,
    string Dimension,
    string Unit,
    ToyVersionedReference SourceRuleRef);

public sealed record ToySampleDemandTotal(
    string Dimension,
    string Unit,
    decimal Amount);

public sealed record ToySampleDemandCalculation(
    IReadOnlyList<ToySampleDemandComponent> Components,
    IReadOnlyList<ToySampleDemandTotal> Totals,
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string InputHash,
    string RuleSetVersion);

public sealed record ToyTechnicalApprovalEntry(
    string RequirementId,
    long RequirementVersion,
    string ApprovedBy,
    DateTimeOffset ApprovedAt,
    string ApprovalComment,
    string InputHash,
    string RuleSetVersion);

public sealed record ToyQuantityDecisionEntry(
    string QuantityAccountId,
    long ExpectedAccountVersion,
    long CurrentAccountVersion,
    decimal RequestedAmount,
    decimal AvailableAmount,
    string Dimension,
    string Unit,
    string ReservationRef,
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string RuleSetVersion);

public sealed record ToyAllocationDecisionEntry(
    string AllocationId,
    long ExpectedSubjectAllocationVersion,
    long CurrentSubjectAllocationVersion,
    string State,
    string TestUnitId,
    string SequenceStepId,
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string RuleSetVersion);

public sealed record ToyDownstreamDecisionEntry(
    string RequestId,
    IReadOnlyList<ToyQuantityDecisionEntry> QuantityDecisions,
    IReadOnlyList<ToyAllocationDecisionEntry> AllocationDecisions,
    string RequestedBy,
    DateTimeOffset RequestedAt);

public sealed record ToySampleRequirementEntry(
    string RequirementId,
    long RequirementVersion,
    IReadOnlyList<ToySampleDemandComponent> Components,
    IReadOnlyList<ToySampleDemandTotal> Totals,
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string InputHash,
    string RuleSetVersion);

public sealed record ToyTestUnitPlanResult(
    string PlanId,
    string ProductId,
    long ProductVersion,
    long PlanVersion,
    long AgeGradeDecisionVersion,
    long AccessibilityAssessmentVersion,
    string ScopeMatrixId,
    long ScopeMatrixVersion,
    IReadOnlyList<ToyVersionedReference> ScopeLineRefs,
    IReadOnlyList<ToyVersionedReference> SampleRuleRefs,
    string RuleSetVersion,
    string State,
    string InputHash,
    ToyObjectContext ObjectScope,
    IReadOnlyList<ToyTestUnitEntry> TestUnits,
    ToySampleRequirementEntry Requirement,
    ToyTechnicalApprovalEntry? TechnicalApproval,
    IReadOnlyList<ToyDownstreamDecisionEntry> DownstreamDecisions,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record ToyTestUnitPlanStatusRequest(
    string OrganizationGroupId,
    string ProductId,
    long PlanVersion,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record ToyTestUnitPlanStatusResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string ProductId,
    long? CurrentPlanVersion,
    string? RequirementId,
    long? RequirementVersion,
    IReadOnlyList<string> ReservationRefs,
    IReadOnlyList<string> AllocationIds,
    string RuleSetVersion);

public interface IToyTestUnitPlanStatusPort
{
    ValueTask<ToyTestUnitPlanStatusResult> EvaluateAsync(
        ToyTestUnitPlanStatusRequest request,
        CancellationToken cancellationToken = default);
}

public interface IToyTestUnitPlanService
{
    Task<ToyTestUnitPlanResult> CreatePlanAsync(
        string productId,
        CreateToyTestUnitPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyTestUnitPlanResult> ApproveAsync(
        string productId,
        long planVersion,
        ApproveToySampleRequirementRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyTestUnitPlanResult> RequestAllocationAsync(
        string productId,
        long planVersion,
        RequestToyAllocationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyTestUnitPlanResult> GetAsync(
        string productId,
        long planVersion,
        string correlationId,
        CancellationToken cancellationToken = default);
}

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

// OD-034@1.0.0: Two-level conclusion hierarchy
public static class ToyConclusionContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "TOY-CONCLUSION-COVERAGE@1.0.0";
    public const string ItemConformityPath = "/api/v1/toy/conclusions/item-conformity";
    public const string TestedScopeConformityPath = "/api/v1/toy/conclusions/tested-scope-conformity";
    public const string GetConclusionPath = "/api/v1/toy/conclusions/{id}";
    public const string GetConclusionsByProductPath = "/api/v1/toy/conclusions";
}

/// <summary>
/// OD-034@1.0.0: Exactly two conclusion levels.
/// WHOLE_PRODUCT_COMPLIANCE is permanently prohibited.
/// </summary>
public static class ToyConclusionLevels
{
    public const string ItemConformity = "ITEM_CONFORMITY";
    public const string TestedScopeConformity = "TESTED_SCOPE_CONFORMITY";
    // WHOLE_PRODUCT_COMPLIANCE: permanently prohibited, no enum value, no interface
}

/// <summary>
/// OD-034@1.0.0: Uncovered scope reasons for mandatory disclosure
/// </summary>
public static class ToyUncoveredReasons
{
    public const string NotTested = "NOT_TESTED";
    public const string Unknown = "UNKNOWN";
    public const string NotApplicable = "NOT_APPLICABLE";
}

public sealed record TestUnitEvidenceInput(
    string TestUnitId,
    string PhysicalObjectRef,
    long PhysicalObjectVersion,
    string HazardDomainRef,
    long HazardDomainVersion,
    string AdoptedResultRef,
    long AdoptedResultVersion,
    string ResultProvenanceGraphRef,
    long ResultProvenanceGraphVersion,
    string? CoverageDecisionRef,
    long CoverageDecisionVersion,
    IReadOnlyList<string>? RequirementRefs);

public sealed record UncoveredScopeInput(
    string Scope,
    string Reason,
    string Detail);

public sealed record ExternalReferenceInput(
    string Issuer,
    string Reference,
    string StatedScope,
    bool NotPartOfThisConclusion);

public sealed record CreateItemConformityConclusionRequest(
    string RuleSetVersion,
    string AdoptedResultRef,
    long AdoptedResultVersion,
    string RequirementRef,
    long RequirementVersion,
    string? CustomStatement);

public sealed record CreateTestedScopeConformityConclusionRequest(
    string RuleSetVersion,
    string ProductRef,
    long ProductVersion,
    string TestUnitPlanRef,
    long TestUnitPlanVersion,
    IReadOnlyList<TestUnitEvidenceInput> TestUnits,
    IReadOnlyList<UncoveredScopeInput> UncoveredScopes,
    IReadOnlyList<ExternalReferenceInput>? ExternalReferences,
    string? CustomStatement,
    bool? IsFictitiousWholeItemConclusion);

public sealed record ToyConclusionResult(
    string ConclusionId,
    string ConclusionLevel,
    string Statement,
    string ApprovedBy,
    DateTimeOffset ApprovedAt,
    long Version,
    string? SignatureRef,
    IReadOnlyList<string>? CoveredHazardDomains,
    IReadOnlyList<UncoveredScopeInput>? UncoveredScopes,
    IReadOnlyList<ExternalReferenceInput>? ExternalReferences);

public interface IToyConclusionService
{
    Task<ToyConclusionResult> CreateItemConformityConclusionAsync(
        CreateItemConformityConclusionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyConclusionResult> CreateTestedScopeConformityConclusionAsync(
        CreateTestedScopeConformityConclusionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyConclusionResult> GetConclusionAsync(
        string conclusionId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToyConclusionResult>> GetConclusionsByProductAsync(
        string productRef,
        long productVersion,
        string correlationId,
        CancellationToken cancellationToken = default);
}
