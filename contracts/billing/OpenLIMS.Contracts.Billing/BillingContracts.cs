namespace OpenLIMS.Contracts.Billing;

public static class BillingContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "BILLING-EVIDENCE@1.0.0";
    public const string ExportRuleSetVersion = "BILLING-EXPORT@1.0.0";
    public const string HandoffRuleSetVersion = "BILLING-HANDOFF@1.0.0";
    public const string CreateEvidencePath = "/api/v1/billing-evidence";
    public const string AddAdjustmentPath = "/api/v1/billing-evidence/{id}/adjustments";
    public const string GetEvidencePath = "/api/v1/billing-evidence/{id}";
    public const string StatusPath = "/api/v1/billing-evidence/{id}/status";
    public const string CreateExportBatchPath = "/api/v1/billing-export-batches";
    public const string GetExportBatchPath = "/api/v1/billing-export-batches/{batchId}";
    public const string CreateHandoffPath = "/api/v1/billing-export-batches/{batchId}/handoffs";
    public const string GetHandoffPath = "/api/v1/billing-handoffs/{handoffId}";
    public const string RecordHandoffAttemptPath = "/api/v1/billing-handoffs/{handoffId}/attempts";
    public const string DifferenceQueuePath = "/api/v1/billing-handoffs/differences";
}

public static class BillingCapabilities
{
    public const string Record = "billing.record";
    public const string Integrate = "billing.integrate";
}

public static class BillingClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ProductCategory = "product_category";
}

public static class BillingStages
{
    public const string ServiceCompleted = "SERVICE_COMPLETED";
    public const string BillableCandidate = "BILLABLE_CANDIDATE";
}

public static class BillingStatusDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class BillingStatusReasons
{
    public const string EvidenceRequired = "EVIDENCE_REQUIRED";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string BillingUnavailable = "BILLING_UNAVAILABLE";
}

public static class BillingErrorCodes
{
    public const string ValidationFailed = "BIL.VALIDATION_FAILED";
    public const string DuplicateBilling = "BIL.DUPLICATE_BILLING";
    public const string EligibilityBlocked = "BIL.ELIGIBILITY_BLOCKED";
    public const string ApplicabilityUnknown = "BIL.APPLICABILITY_UNKNOWN";
    public const string NotAuthorized = "BIL.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "BIL.OBJECT_NOT_ACCESSIBLE";
    public const string PersistenceUnavailable = "BIL.PERSISTENCE_UNAVAILABLE";
    public const string ExportScopeMismatch = "BIL.EXPORT_SCOPE_MISMATCH";
    public const string IdempotencyConflict = "BIL.IDEMPOTENCY_CONFLICT";
    public const string HandoffConfirmationInvalid = "BIL.HANDOFF_CONFIRMATION_INVALID";
    public const string HandoffAlreadyCompleted = "BIL.HANDOFF_ALREADY_COMPLETED";
}

public sealed record BillingObjectContext(
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory);

public sealed record BillingVersionedReference(string Id, long Version);

public sealed record CreateBillingEvidenceRequest(
    string RuleSetVersion,
    BillingObjectContext ObjectScope,
    string ResultGroupId,
    long ExpectedGroupVersion,
    BillingVersionedReference ContractBaseline,
    string ChargeDimension,
    string BillingRuleVersion,
    decimal Amount,
    BillingVersionedReference Currency,
    string? ZeroAmountReason = null);

public sealed record AddBillingAdjustmentRequest(
    string RuleSetVersion,
    decimal Amount,
    string Reason);

public sealed record BillingAdjustmentResult(
    string AdjustmentId,
    string BillingEvidenceId,
    decimal Amount,
    string Reason,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record BillingEvidenceResult(
    string BillingEvidenceId,
    string Stage,
    string RuleSetVersion,
    BillingObjectContext ObjectScope,
    string ResultGroupId,
    long GroupVersion,
    string AdoptionTargetId,
    BillingVersionedReference ContractBaseline,
    string ChargeDimension,
    string BillingRuleVersion,
    decimal Amount,
    BillingVersionedReference Currency,
    string? ZeroAmountReason,
    IReadOnlyList<BillingAdjustmentResult> Adjustments,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record BillingEvidenceStatusRequest(
    string OrganizationGroupId,
    string BillingEvidenceId,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record BillingEvidenceStatusResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string? BillingEvidenceId,
    string? Stage,
    decimal? Amount,
    int? AdjustmentCount,
    string RuleSetVersion);

public interface IBillingEvidencePort
{
    ValueTask<BillingEvidenceStatusResult> EvaluateAsync(
        BillingEvidenceStatusRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record BillingAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    BillingObjectContext ObjectScope,
    string Capability);

public sealed record BillingAuthorizationDecision(bool Allowed)
{
    public static BillingAuthorizationDecision Permit { get; } = new(true);
    public static BillingAuthorizationDecision Deny { get; } = new(false);
}

public interface IBillingAuthorizationPort
{
    ValueTask<BillingAuthorizationDecision> AuthorizeAsync(
        BillingAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

public static class BillingExternalSystems
{
    public const string Erp = "ERP";
    public const string Invoice = "INVOICE";

    public static readonly IReadOnlyList<string> All = [Erp, Invoice];
}

public static class BillingHandoffModes
{
    public const string Automated = "AUTOMATED";
    public const string Manual = "MANUAL";

    public static readonly IReadOnlyList<string> All = [Automated, Manual];
}

public static class BillingHandoffOutcomes
{
    public const string Pending = "PENDING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";
    public const string Different = "DIFFERENT";

    public static readonly IReadOnlyList<string> Attempts = [Succeeded, Failed, Unknown, Different];
}

public sealed record CreateBillingExportBatchRequest(
    string RuleSetVersion,
    IReadOnlyList<string> BillingEvidenceIds,
    string ExportSchemaVersion,
    string IdempotencyKey);

public sealed record BillingExportItemResult(
    string BillingEvidenceId,
    string ResultGroupId,
    long GroupVersion,
    decimal BaseAmount,
    decimal AdjustmentAmount,
    decimal NetAmount,
    BillingVersionedReference Currency);

public sealed record BillingExportBatchResult(
    string BatchId,
    BillingObjectContext ObjectScope,
    string ExportSchemaVersion,
    IReadOnlyList<BillingExportItemResult> Items,
    decimal TotalAmount,
    BillingVersionedReference Currency,
    string ContentHash,
    string CanonicalContent,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record CreateBillingHandoffRequest(
    string RuleSetVersion,
    string ExternalSystem,
    string Mode,
    BillingVersionedReference Endpoint,
    string IdempotencyKey);

public sealed record ErpPostingConfirmation(
    string VoucherNumber,
    string CompanyCode,
    int FiscalYear,
    int Period,
    DateOnly PostingDate);

public sealed record RecordBillingHandoffAttemptRequest(
    string RuleSetVersion,
    string IdempotencyKey,
    string Outcome,
    string? ExternalReference = null,
    string? DetailCode = null,
    ErpPostingConfirmation? ErpPosting = null);

public sealed record BillingHandoffAttemptResult(
    string AttemptId,
    string HandoffId,
    int AttemptNumber,
    string Outcome,
    string? ExternalReference,
    string? DetailCode,
    ErpPostingConfirmation? ErpPosting,
    string AttemptedBy,
    DateTimeOffset AttemptedAt);

public sealed record BillingHandoffResult(
    string HandoffId,
    string BatchId,
    string ExternalSystem,
    string Mode,
    BillingVersionedReference Endpoint,
    string Status,
    IReadOnlyList<BillingHandoffAttemptResult> Attempts,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record BillingDifferenceQueueResult(
    IReadOnlyList<BillingHandoffResult> Handoffs,
    string RuleSetVersion);
