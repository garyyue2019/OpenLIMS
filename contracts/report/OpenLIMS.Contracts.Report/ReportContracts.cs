namespace OpenLIMS.Contracts.Report;

public static class ReportContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "RPT-ISSUANCE@1.0.0";
    public const string CreateReportPath = "/api/v1/reports";
    public const string AddLinePath = "/api/v1/reports/{id}/lines";
    public const string GateEvaluationPath = "/api/v1/reports/{id}/gate-evaluation";
    public const string SubmitForApprovalPath = "/api/v1/reports/{id}/submit-for-approval";
    public const string GetReportPath = "/api/v1/reports/{id}";
    public const string IssuanceGatePath = "/api/v1/reports/{id}/issuance-gate";
}

public static class ReportCapabilities
{
    public const string Manage = "report.manage";
}

public static class ReportClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ProductCategory = "product_category";
}

public static class ReportStates
{
    public const string Draft = "DRAFT";
    public const string PendingApproval = "PENDING_APPROVAL";
}

/// <summary>
/// RPT-SCOPE-001: a report must distinguish what was actually tested from what
/// is covered by approval, what was not evaluated, what the customer merely
/// declared, and what the laboratory concluded.
/// </summary>
public static class ReportScopePartitions
{
    public const string ActualTested = "ACTUAL_TESTED";
    public const string ApprovedCoverage = "APPROVED_COVERAGE";
    public const string NotEvaluated = "NOT_EVALUATED";
    public const string CustomerDeclared = "CUSTOMER_DECLARED";
    public const string LaboratoryConclusion = "LABORATORY_CONCLUSION";

    public static readonly IReadOnlyList<string> All =
    [
        ActualTested, ApprovedCoverage, NotEvaluated, CustomerDeclared, LaboratoryConclusion
    ];
}

public static class ReportAccreditationStatuses
{
    public const string Accredited = "ACCREDITED";
    public const string NotAccredited = "NOT_ACCREDITED";
    public const string Unknown = "UNKNOWN";
}

/// <summary>
/// OD-029@1.0.0: accreditation eligibility is computed per report line across
/// exactly these six dimensions. An organisation-level "accredited" flag can
/// never substitute for them.
/// </summary>
public static class ReportAccreditationDimensions
{
    public const string Site = "SITE";
    public const string MethodVersion = "METHOD_VERSION";
    public const string ProductMatrix = "PRODUCT_MATRIX";
    public const string ParameterRange = "PARAMETER_RANGE";
    public const string Validity = "VALIDITY";
    public const string Signatory = "SIGNATORY";

    public static readonly IReadOnlyList<string> All =
    [
        Site, MethodVersion, ProductMatrix, ParameterRange, Validity, Signatory
    ];
}

public static class ReportGateDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class ReportGateSources
{
    public const string ResultAdoption = "RESULT_ADOPTION";
    public const string QcReportability = "QC_REPORTABILITY";
    public const string ReceivingEligibility = "RECEIVING_ELIGIBILITY";
    public const string ScopeEligibility = "SCOPE_ELIGIBILITY";
    public const string AllocationStatus = "ALLOCATION_STATUS";
    public const string BatchStatus = "BATCH_STATUS";
    public const string InstrumentImport = "INSTRUMENT_IMPORT";
    public const string Accreditation = "ACCREDITATION";
    public const string SignatoryAuthority = "SIGNATORY_AUTHORITY";
    public const string ConformityDecision = "CONFORMITY_DECISION";
    public const string Traceability = "TRACEABILITY";
}

public static class ReportBlockerReasons
{
    public const string SourceBlocked = "SOURCE_BLOCKED";
    public const string SourceUnknown = "SOURCE_UNKNOWN";
    public const string AccreditationOutOfScope = "ACCREDITATION_OUT_OF_SCOPE";
    public const string AccreditationExpired = "ACCREDITATION_EXPIRED";
    public const string AccreditationReferenceMissing = "ACCREDITATION_REFERENCE_MISSING";
    public const string SignatoryNotAuthorized = "SIGNATORY_NOT_AUTHORIZED";
    public const string ConformityDecisionUnavailable = "CONFORMITY_DECISION_UNAVAILABLE";
    public const string TraceIncomplete = "TRACE_INCOMPLETE";
    public const string DuplicateAttribution = "DUPLICATE_ATTRIBUTION";
    public const string ReportRequired = "REPORT_REQUIRED";
    public const string LinesRequired = "LINES_REQUIRED";
    public const string GateEvaluationRequired = "GATE_EVALUATION_REQUIRED";
    public const string VersionMismatch = "VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string ReportUnavailable = "REPORT_UNAVAILABLE";
}

public static class ReportNextSteps
{
    public const string ResolveIdentityConflict = "RESOLVE_IDENTITY_CONFLICT";
    public const string ReleaseQcBlock = "RELEASE_QC_BLOCK";
    public const string CompleteInstrumentImport = "COMPLETE_INSTRUMENT_IMPORT";
    public const string ReviseScopeMatrix = "REVISE_SCOPE_MATRIX";
    public const string RefreshAdoption = "REFRESH_ADOPTION";
    public const string UnfreezeOrReplaceBatch = "UNFREEZE_OR_REPLACE_BATCH";
    public const string RestoreAllocation = "RESTORE_ALLOCATION";
    public const string UpdateAccreditationReference = "UPDATE_ACCREDITATION_REFERENCE";
    public const string AssignAuthorizedSignatory = "ASSIGN_AUTHORIZED_SIGNATORY";
    public const string AwaitConformityDecisionCapability = "AWAIT_CONFORMITY_DECISION_CAPABILITY";
    public const string CompleteTraceReferences = "COMPLETE_TRACE_REFERENCES";
    public const string RemoveDuplicateLine = "REMOVE_DUPLICATE_LINE";
    public const string RetryWhenSourceAvailable = "RETRY_WHEN_SOURCE_AVAILABLE";
    public const string AddReportLine = "ADD_REPORT_LINE";
    public const string EvaluateIssuanceGate = "EVALUATE_ISSUANCE_GATE";
}

public static class ReportErrorCodes
{
    public const string ValidationFailed = "RPT.VALIDATION_FAILED";
    public const string EligibilityBlocked = "RPT.ELIGIBILITY_BLOCKED";
    public const string ApplicabilityUnknown = "RPT.APPLICABILITY_UNKNOWN";
    public const string AccreditationBlocked = "RPT.ACCREDITATION_BLOCKED";
    public const string ConformityDecisionUnavailable = "RPT.CONFORMITY_DECISION_UNAVAILABLE";
    public const string DuplicateAttribution = "RPT.DUPLICATE_ATTRIBUTION";
    public const string TraceIncomplete = "RPT.TRACE_INCOMPLETE";
    public const string ExpectedVersionConflict = "RPT.EXPECTED_VERSION_CONFLICT";
    public const string NotAuthorized = "RPT.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "RPT.OBJECT_NOT_ACCESSIBLE";
    public const string PersistenceUnavailable = "RPT.PERSISTENCE_UNAVAILABLE";
}

public sealed record ReportVersionedReference(string Id, long Version);

public sealed record ReportObjectContext(
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory);

/// <summary>
/// The controlled, versioned, content-hashed reference to an accreditation
/// scope record (OD-030 boundary: the certificate body itself stays outside).
/// </summary>
public sealed record AccreditationScopeReference(string Id, long Version, string Sha256);

/// <summary>
/// The six dimensions OD-029@1.0.0 pins, as declared for one report line.
/// </summary>
public sealed record AccreditationClaim(
    string SiteId,
    ReportVersionedReference Method,
    string ProductMatrix,
    string ParameterRange,
    DateTimeOffset ValidUntil,
    string SignatoryId);

public sealed record ReportTraceReferences(
    string BatchId,
    string AllocationId,
    string ReceivedItemId,
    ReportVersionedReference RequirementSnapshot);

public sealed record CreateReportRequest(
    string RuleSetVersion,
    ReportObjectContext ObjectScope,
    string ReportNumber);

public sealed record AddReportLineRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    int LineNumber,
    string ResultGroupId,
    long ExpectedGroupVersion,
    string ScopeLineId,
    string ScopePartition,
    ReportTraceReferences TraceRefs,
    AccreditationScopeReference AccreditationRef,
    AccreditationClaim AccreditationClaim,
    IReadOnlyList<ReportVersionedReference> QcRuns,
    string InstrumentFileId,
    long ExpectedInstrumentFileVersion,
    long ExpectedReceivedItemVersion,
    string ScopeMatrixId,
    long ExpectedScopeMatrixVersion,
    long ExpectedAllocationVersion,
    long ExpectedBatchVersion,
    ReportVersionedReference? SubcontractingDisclosure = null,
    bool ClaimsAccreditation = true);

public sealed record EvaluateReportGateRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string SignatoryId);

public sealed record SubmitReportForApprovalRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion);

/// <summary>
/// RPT-GATE-002: every blocker names its object, the rule-set version that
/// judged it, why, and what may be done next. Blockers are never collapsed
/// into a single boolean or summary message.
/// </summary>
public sealed record ReportBlocker(
    string ObjectRef,
    string ObjectType,
    string Source,
    string RuleSetVersion,
    string ReasonCode,
    IReadOnlyList<string> AllowedNextSteps,
    int? LineNumber = null);

public sealed record ReportLineAccreditationVerdict(
    int LineNumber,
    string Status,
    IReadOnlyList<string> FailedDimensions);

/// <summary>
/// The upstream object versions this line was built against. The gate replays
/// each source port at exactly these versions, so a line can never be judged
/// against a newer state than the one it cited.
/// </summary>
public sealed record ReportLineGateReferences(
    IReadOnlyList<ReportVersionedReference> QcRuns,
    string InstrumentFileId,
    long InstrumentFileVersion,
    string ScopeMatrixId,
    long ScopeMatrixVersion,
    long ReceivedItemVersion,
    long AllocationVersion,
    long BatchVersion);

public sealed record ReportLineResult(
    string LineId,
    string ReportId,
    int LineNumber,
    string ResultGroupId,
    long GroupVersion,
    string AdoptionTargetId,
    string AdoptionRuleSetVersion,
    string ScopeLineId,
    string ScopePartition,
    ReportTraceReferences TraceRefs,
    ReportLineGateReferences GateRefs,
    AccreditationScopeReference AccreditationRef,
    AccreditationClaim AccreditationClaim,
    bool ClaimsAccreditation,
    ReportVersionedReference? SubcontractingDisclosure,
    string AddedBy,
    DateTimeOffset AddedAt);

public sealed record ReportGateEvaluationResult(
    string EvaluationId,
    string ReportId,
    long ReportVersion,
    string Decision,
    IReadOnlyList<ReportBlocker> Blockers,
    IReadOnlyList<ReportLineAccreditationVerdict> AccreditationVerdicts,
    string SignatoryId,
    string EvaluatedBy,
    DateTimeOffset EvaluatedAt);

public sealed record ReportResult(
    string ReportId,
    long Version,
    string State,
    string RuleSetVersion,
    ReportObjectContext ObjectScope,
    string ReportNumber,
    IReadOnlyList<ReportLineResult> Lines,
    IReadOnlyList<ReportGateEvaluationResult> GateEvaluations,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record ReportIssuanceGateRequest(
    string OrganizationGroupId,
    string ReportId,
    long ExpectedReportVersion,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record ReportIssuanceGateResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string ReportId,
    long? CurrentVersion,
    IReadOnlyList<ReportBlocker> Blockers,
    IReadOnlyList<ReportLineAccreditationVerdict> AccreditationVerdicts,
    string RuleSetVersion);

/// <summary>
/// Version-pinned issuance readiness for one report. UNKNOWN always counts as
/// blocked; consumers (the signing card, billing) must not read a missing
/// evaluation as permission.
/// </summary>
public interface IReportIssuanceGatePort
{
    ValueTask<ReportIssuanceGateResult> EvaluateAsync(
        ReportIssuanceGateRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ReportAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    ReportObjectContext ObjectScope,
    string Capability);

public sealed record ReportAuthorizationDecision(bool Allowed)
{
    public static ReportAuthorizationDecision Permit { get; } = new(true);
    public static ReportAuthorizationDecision Deny { get; } = new(false);
}

public interface IReportAuthorizationPort
{
    ValueTask<ReportAuthorizationDecision> AuthorizeAsync(
        ReportAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads back the accreditation-scope facts behind one controlled reference.
/// The certificate body itself stays in the authoritative external system
/// (OD-030 boundary) — this returns only the six dimensions the gate judges.
/// A null result means the reference could not be resolved, which fails closed.
/// </summary>
public sealed record AccreditationScopeLookupRequest(
    string OrganizationGroupId,
    AccreditationScopeReference Reference);

public sealed record AccreditationScopeLookupResult(
    string SiteId,
    ReportVersionedReference Method,
    string ProductMatrix,
    string ParameterRange,
    DateTimeOffset ValidUntil,
    IReadOnlyList<string> AuthorizedSignatories);

public interface IAccreditationScopePort
{
    ValueTask<AccreditationScopeLookupResult?> ResolveAsync(
        AccreditationScopeLookupRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Signatory authority for one accreditation claim. Kept as a port so the
/// authoritative personnel-qualification source (OD-012, still open) can be
/// plugged in later without touching the gate.
/// </summary>
public sealed record SignatoryAuthorityRequest(
    string OrganizationGroupId,
    string SignatoryId,
    string SiteId,
    ReportVersionedReference Method,
    string ParameterRange);

public sealed record SignatoryAuthorityDecision(bool Authorized, IReadOnlyList<string> ReasonCodes);

public interface ISignatoryAuthorityPort
{
    ValueTask<SignatoryAuthorityDecision> EvaluateAsync(
        SignatoryAuthorityRequest request,
        CancellationToken cancellationToken = default);
}
