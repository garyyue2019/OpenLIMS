namespace OpenLIMS.Contracts.Batch;

public static class BatchContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "BATCH-EXECUTION@1.0.0";
    public const string CreateBatchPath = "/api/v1/batches";
    public const string AddMemberPath = "/api/v1/batches/{id}/members";
    public const string AddEvidencePath = "/api/v1/batches/{id}/evidence";
    public const string FreezePath = "/api/v1/batches/{id}/freeze";
    public const string GetBatchPath = "/api/v1/batches/{id}";
    public const string StatusPath = "/api/v1/batches/{id}/status";
}

public static class BatchCapabilities
{
    public const string Manage = "batch.manage";
}

public static class BatchClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
}

public static class BatchTypes
{
    public const string Preparation = "PREPARATION";
    public const string Preconditioning = "PRECONDITIONING";
    public const string Analytical = "ANALYTICAL";
    public const string InstrumentRun = "INSTRUMENT_RUN";
}

public static class BatchMemberTypes
{
    public const string Specimen = "SPECIMEN";
    public const string QcSample = "QC_SAMPLE";
}

public static class BatchStates
{
    public const string Active = "ACTIVE";
    public const string Frozen = "FROZEN";
}

public static class BatchFreezeCauses
{
    public const string QcFailure = "QC_FAILURE";
    public const string EnvironmentOutOfTolerance = "ENVIRONMENT_OUT_OF_TOLERANCE";
    public const string CalibrationInvalid = "CALIBRATION_INVALID";
}

public static class BatchEvidenceSources
{
    public const string Cds = "CDS";
    public const string Eln = "ELN";
    public const string Instrument = "INSTRUMENT";
}

public static class BatchStatusDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class BatchStatusReasons
{
    public const string BatchRequired = "BATCH_REQUIRED";
    public const string BatchFrozen = "BATCH_FROZEN";
    public const string BatchVersionMismatch = "BATCH_VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string BatchUnavailable = "BATCH_UNAVAILABLE";
}

public static class BatchErrorCodes
{
    public const string ValidationFailed = "BAT.VALIDATION_FAILED";
    public const string EligibilityBlocked = "BAT.ELIGIBILITY_BLOCKED";
    public const string ApplicabilityUnknown = "BAT.APPLICABILITY_UNKNOWN";
    public const string BatchFrozen = "BAT.BATCH_FROZEN";
    public const string NotAuthorized = "BAT.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "BAT.OBJECT_NOT_ACCESSIBLE";
    public const string ExpectedVersionConflict = "BAT.EXPECTED_VERSION_CONFLICT";
    public const string PersistenceUnavailable = "BAT.PERSISTENCE_UNAVAILABLE";
}

public sealed record BatchObjectContext(string LegalEntityId, string LaboratoryId);

public sealed record BatchVersionedReference(string Id, long Version);

public sealed record CreateBatchRequest(
    string RuleSetVersion,
    BatchObjectContext ObjectScope,
    string BatchType);

public sealed record AddBatchMemberRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string MemberType,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory,
    string? AllocationId = null,
    long? ExpectedSubjectAllocationVersion = null,
    BatchVersionedReference? QcRef = null);

public sealed record AddBatchEvidenceRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string SourceSystem,
    BatchVersionedReference ExternalRef,
    string Sha256);

public sealed record FreezeBatchRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string Cause,
    BatchVersionedReference? ApprovedFollowUpRef = null);

public sealed record BatchMemberResult(
    string MemberId,
    string BatchId,
    long BatchVersion,
    string MemberType,
    string? AllocationId,
    long? SubjectAllocationVersion,
    string? AllocationGateDecision,
    string? AllocationGateRuleSetVersion,
    BatchVersionedReference? QcRef,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory,
    string AddedBy,
    DateTimeOffset AddedAt);

public sealed record BatchEvidenceResult(
    string EvidenceId,
    string BatchId,
    long BatchVersion,
    string SourceSystem,
    BatchVersionedReference ExternalRef,
    string Sha256,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record BatchFreezeResult(
    string FreezeId,
    string BatchId,
    long BatchVersion,
    string Cause,
    int AffectedMemberCount,
    BatchVersionedReference? ApprovedFollowUpRef,
    string FrozenBy,
    DateTimeOffset FrozenAt);

public sealed record BatchResult(
    string BatchId,
    string BatchType,
    string State,
    long Version,
    string RuleSetVersion,
    BatchObjectContext ObjectScope,
    IReadOnlyList<BatchMemberResult> Members,
    IReadOnlyList<BatchEvidenceResult> Evidence,
    BatchFreezeResult? Freeze,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record BatchStatusRequest(
    string OrganizationGroupId,
    string BatchId,
    long ExpectedBatchVersion,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record BatchStatusResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string? BatchId,
    string? State,
    long? CurrentBatchVersion,
    string RuleSetVersion);

public interface IBatchStatusPort
{
    ValueTask<BatchStatusResult> EvaluateAsync(
        BatchStatusRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record BatchAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    BatchObjectContext ObjectScope,
    string Capability);

public sealed record BatchAuthorizationDecision(bool Allowed)
{
    public static BatchAuthorizationDecision Permit { get; } = new(true);
    public static BatchAuthorizationDecision Deny { get; } = new(false);
}

public interface IBatchAuthorizationPort
{
    ValueTask<BatchAuthorizationDecision> AuthorizeAsync(
        BatchAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
