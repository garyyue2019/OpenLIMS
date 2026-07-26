namespace OpenLIMS.Contracts.Quantity;

public static class QuantityContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "SAMPLE-QUANTITY@1.0.0";
    public const string CreateAccountPath = "/api/v1/quantity-accounts";
    public const string PostEntryPath = "/api/v1/quantity-accounts/{id}/entries";
    public const string GetAccountPath = "/api/v1/quantity-accounts/{id}";
    public const string AvailabilityPath = "/api/v1/quantity-accounts/{id}/availability";
}

public static class QuantityCapabilities
{
    public const string Post = "quantity.post";
}

public static class QuantityClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ProductCategory = "product_category";
}

public static class QuantitySubjectTypes
{
    public const string ReceivedItem = "RECEIVED_ITEM";
    public const string DerivedSample = "DERIVED_SAMPLE";
    public const string TestSpecimen = "TEST_SPECIMEN";
}

public static class QuantityDimensions
{
    public const string Count = "COUNT";
    public const string Mass = "MASS";
    public const string Length = "LENGTH";
    public const string Area = "AREA";
    public const string Volume = "VOLUME";
}

public static class QuantityEntryTypes
{
    public const string Receipt = "RECEIPT";
    public const string Output = "OUTPUT";
    public const string Reserve = "RESERVE";
    public const string ReserveRelease = "RESERVE_RELEASE";
    public const string Allocate = "ALLOCATE";
    public const string Consume = "CONSUME";
    public const string Return = "RETURN";
    public const string Loss = "LOSS";
    public const string Dispose = "DISPOSE";
    public const string Reversal = "REVERSAL";
    public const string Restate = "RESTATE";
}

public static class QuantityAvailabilityDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class QuantityAvailabilityReasons
{
    public const string AccountRequired = "ACCOUNT_REQUIRED";
    public const string AccountVersionMismatch = "ACCOUNT_VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string RequestInvalid = "REQUEST_INVALID";
    public const string InsufficientAvailable = "INSUFFICIENT_AVAILABLE";
    public const string QuantityUnavailable = "QUANTITY_UNAVAILABLE";
}

public static class QuantityErrorCodes
{
    public const string ValidationFailed = "QTY.VALIDATION_FAILED";
    public const string DimensionMismatch = "QTY.DIMENSION_MISMATCH";
    public const string NotQuantifiable = "QTY.NOT_QUANTIFIABLE";
    public const string InsufficientBalance = "QTY.INSUFFICIENT_BALANCE";
    public const string NotAuthorized = "QTY.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "QTY.OBJECT_NOT_ACCESSIBLE";
    public const string ExpectedVersionConflict = "QTY.EXPECTED_VERSION_CONFLICT";
    public const string ApplicabilityUnknown = "QTY.APPLICABILITY_UNKNOWN";
    public const string PersistenceUnavailable = "QTY.PERSISTENCE_UNAVAILABLE";
}

public sealed record QuantityObjectContext(
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory);

public sealed record QuantitySubjectReference(string SubjectType, string Id, long Version);

public sealed record CreateQuantityAccountRequest(
    string RuleSetVersion,
    QuantityObjectContext ObjectScope,
    QuantitySubjectReference Subject,
    bool SubjectQuantifiable,
    string Dimension,
    string Unit,
    int PrecisionScale,
    decimal ConservationTolerance);

public sealed record PostQuantityEntryRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    string EntryType,
    decimal Amount,
    string? Reason = null,
    string? ReferencedEntryId = null,
    string? ReservationId = null);

public sealed record QuantityAccountResult(
    string QuantityAccountId,
    long Version,
    string RuleSetVersion,
    QuantityObjectContext ObjectScope,
    QuantitySubjectReference Subject,
    string Dimension,
    string Unit,
    int PrecisionScale,
    decimal ConservationTolerance,
    decimal Balance,
    decimal Reserved,
    decimal Available,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record QuantityEntryResult(
    string EntryId,
    string QuantityAccountId,
    long AccountVersion,
    string EntryType,
    decimal Amount,
    decimal ResultingBalance,
    decimal ResultingReserved,
    decimal ResultingAvailable,
    string? ReferencedEntryId,
    string? ReservationId,
    string? Reason,
    string PostedBy,
    DateTimeOffset PostedAt);

public sealed record QuantityAvailabilityRequest(
    string OrganizationGroupId,
    string QuantityAccountId,
    long ExpectedAccountVersion,
    string RuleSetVersion,
    decimal RequestedAmount)
{
    public string? CorrelationId { get; init; }
}

public sealed record QuantityAvailabilityResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string? QuantityAccountId,
    long? CurrentAccountVersion,
    decimal? AvailableAmount,
    string RuleSetVersion);

public interface IQuantityAvailabilityPort
{
    ValueTask<QuantityAvailabilityResult> EvaluateAsync(
        QuantityAvailabilityRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record QuantityAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    QuantityObjectContext ObjectScope,
    string Capability);

public sealed record QuantityAuthorizationDecision(bool Allowed)
{
    public static QuantityAuthorizationDecision Permit { get; } = new(true);
    public static QuantityAuthorizationDecision Deny { get; } = new(false);
}

public interface IQuantityAuthorizationPort
{
    ValueTask<QuantityAuthorizationDecision> AuthorizeAsync(
        QuantityAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
