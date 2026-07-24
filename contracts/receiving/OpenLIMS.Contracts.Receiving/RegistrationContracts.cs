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
    IReadOnlyList<ReceivedItemRegistrationResult> ReceivedItems);

public sealed record ReceivedItemRegistrationResult(
    string ReceivedItemId,
    string ReceivedItemNumber,
    string State,
    long Version);

public sealed record ReceivingAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string Capability);

public enum ReceivingAuthorizationOutcome
{
    Allowed,
    Denied,
    ServiceOrderNotReceivable
}

public sealed record ReceivingAuthorizationDecision(ReceivingAuthorizationOutcome Outcome)
{
    public static ReceivingAuthorizationDecision Allowed { get; } = new(ReceivingAuthorizationOutcome.Allowed);
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
}

public static class ReceivingClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ReceivableServiceOrder = "receivable_service_order";
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
}
