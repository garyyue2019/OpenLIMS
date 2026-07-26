namespace OpenLIMS.Contracts.Billing;

public static class BillingContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "BILLING-EVIDENCE@1.0.0";
    public const string CreateEvidencePath = "/api/v1/billing-evidence";
    public const string AddAdjustmentPath = "/api/v1/billing-evidence/{id}/adjustments";
    public const string GetEvidencePath = "/api/v1/billing-evidence/{id}";
    public const string StatusPath = "/api/v1/billing-evidence/{id}/status";
}

public static class BillingCapabilities
{
    public const string Record = "billing.record";
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
