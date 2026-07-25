namespace OpenLIMS.Contracts.Receiving;

public static class ReceivingContract
{
    public const string Version = "1.0.0";
    public const string RegisterReceiptPath = "/api/v1/receipts";
    public const string IdempotencyHeader = "Idempotency-Key";
}

public sealed record RegisterReceiptRequest(
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    DateTimeOffset ArrivalAt,
    IReadOnlyList<RegisterContainerRequest> Containers);

public sealed record RegisterContainerRequest(
    string? ExternalLabel,
    string PackageType,
    string Condition,
    string? SealObservation,
    IReadOnlyList<RegisterReceivedItemRequest> ReceivedItems);

public sealed record RegisterReceivedItemRequest(
    string DeclaredDescription,
    string Model,
    string Batch,
    string? SerialNumber,
    string Color,
    string PackageCondition,
    string SealCondition,
    string ItemCondition,
    decimal Quantity,
    string Unit);

public sealed record ReceiptRegistrationResult(
    string ReceiptId,
    string ReceiptNumber,
    long AggregateVersion,
    IReadOnlyList<ContainerRegistrationResult> Containers);

public sealed record ContainerRegistrationResult(
    string ContainerId,
    string ContainerNumber,
    IReadOnlyList<ReceivedItemRegistrationResult> ReceivedItems)
{
    public LabelIdentityResult? LabelIdentity { get; init; }
}

public sealed record ReceivedItemRegistrationResult(
    string ReceivedItemId,
    string ReceivedItemNumber,
    string State,
    long Version)
{
    public LabelIdentityResult? LabelIdentity { get; init; }
}

public sealed record LabelIdentityResult(
    string ObjectType,
    string BusinessNumber,
    string BarcodePayload,
    string TemplateVersion);

public sealed record ReceivingAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string Capability)
{
    public string? ProductCategory { get; init; }
}

public enum ReceivingAuthorizationOutcome
{
    Allowed,
    Denied,
    ServiceOrderNotReceivable
}

public sealed record ReceivingAuthorizationDecision(
    ReceivingAuthorizationOutcome Outcome,
    string? LaboratoryCode = null)
{
    public static ReceivingAuthorizationDecision AllowedFor(string laboratoryCode) =>
        new(ReceivingAuthorizationOutcome.Allowed, laboratoryCode);
    public static ReceivingAuthorizationDecision Denied { get; } = new(ReceivingAuthorizationOutcome.Denied);
    public static ReceivingAuthorizationDecision NotReceivable { get; } = new(ReceivingAuthorizationOutcome.ServiceOrderNotReceivable);
}

public interface IReceivingAuthorizationPort
{
    ValueTask<ReceivingAuthorizationDecision> AuthorizeAsync(
        ReceivingAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

public static class ReceivingCapabilities
{
    public const string Register = "receiving.register";
    public const string LabelPrint = "receiving.label.print";
    public const string LabelReprint = "receiving.label.reprint";
    public const string LabelReprintOverride = "receiving.label.reprint.override";
    public const string LabelScan = "receiving.label.scan";
    public const string IdentityEvaluate = "receiving.identity.evaluate";
    public const string EligibilityEvaluate = "receiving.eligibility.evaluate";
    public const string ExceptionCreate = "exception.create";
    public const string ExceptionRead = "exception.read";
    public const string ExceptionQualityApprove = "exception.quality.approve";
    public const string ExceptionEhsApprove = "exception.ehs.approve";
}

public static class ReceivingClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string LaboratoryCode = "laboratory_code";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ReceivableServiceOrder = "receivable_service_order";
    public const string ProductCategory = "product_category";
}

public static class ReceivingLabelObjectTypes
{
    public const string Container = "CT";
    public const string ReceivedItem = "RI";
}

public sealed record ReceivingLabelObjectSnapshot(
    string ObjectType,
    string ObjectId,
    long ObjectVersion,
    string OrganizationGroupId,
    string LegalEntityId,
    string LaboratoryId,
    string LaboratoryCode,
    string CustomerId,
    string ServiceOrderId,
    string BusinessNumber,
    string OpaqueReference,
    string FormatVersion,
    string State);

public interface IReceivingLabelObjectPort
{
    ValueTask<ReceivingLabelObjectSnapshot?> GetAsync(
        string organizationGroupId,
        string objectType,
        string objectId,
        CancellationToken cancellationToken = default);

    ValueTask<ReceivingLabelObjectSnapshot?> ResolveAsync(
        string organizationGroupId,
        string objectType,
        string opaqueReference,
        CancellationToken cancellationToken = default);
}

public static class ReceivingErrorCodes
{
    public const string AuthenticationRequired = "AUTH.AUTHENTICATION_REQUIRED";
    public const string AuthorizationDenied = "REC.AUTHORIZATION_DENIED";
    public const string ServiceOrderNotReceivable = "REC.SERVICE_ORDER_NOT_RECEIVABLE";
    public const string IdentityGranularityUnresolved = "REC.IDENTITY_GRANULARITY_UNRESOLVED";
    public const string IdempotencyConflict = "REC.IDEMPOTENCY_CONFLICT";
    public const string ValidationFailed = "REC.VALIDATION_FAILED";
    public const string PersistenceUnavailable = "REC.PERSISTENCE_UNAVAILABLE";
    public const string IdentityEvidenceIncomplete = "IDENTITY_EVIDENCE_INCOMPLETE";
    public const string IdentityConflict = "IDENTITY_CONFLICT";
    public const string IdentityAmbiguous = "IDENTITY_AMBIGUOUS";
    public const string ObjectNotAccessible = "OBJECT_NOT_ACCESSIBLE";
    public const string ExpectedVersionConflict = "EXPECTED_VERSION_CONFLICT";
    public const string ReceivingPortUnavailable = "RECEIVING_PORT_UNAVAILABLE";
    public const string ExceptionTypeUnknown = "EXCEPTION_TYPE_UNKNOWN";
    public const string DecisionNotAuthorized = "DECISION_NOT_AUTHORIZED";
    public const string DecisionEvidenceIncomplete = "DECISION_EVIDENCE_INCOMPLETE";
    public const string ConditionalAcceptConstraintsRequired = "CONDITIONAL_ACCEPT_CONSTRAINTS_REQUIRED";
    public const string ApplicabilityUnknown = "APPLICABILITY_UNKNOWN";
}
