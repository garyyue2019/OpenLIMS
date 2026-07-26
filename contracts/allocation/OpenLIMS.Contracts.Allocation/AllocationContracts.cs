namespace OpenLIMS.Contracts.Allocation;

public static class AllocationContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "TASK-ALLOCATION@1.0.0";
    public const string CreateAllocationPath = "/api/v1/test-object-allocations";
    public const string ReleaseAllocationPath = "/api/v1/test-object-allocations/{id}/release";
    public const string GetAllocationPath = "/api/v1/test-object-allocations/{id}";
    public const string StatusPath = "/api/v1/test-object-allocations/{id}/status";
}

public static class AllocationCapabilities
{
    public const string Assign = "allocation.assign";
}

public static class AllocationClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ProductCategory = "product_category";
}

public static class AllocationSubjectTypes
{
    public const string ReceivedItem = "RECEIVED_ITEM";
    public const string TestSpecimen = "TEST_SPECIMEN";
    public const string TestPortion = "TEST_PORTION";
}

public static class AllocationStates
{
    public const string Active = "ACTIVE";
    public const string Released = "RELEASED";
}

public static class AllocationStatusDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class AllocationStatusReasons
{
    public const string AllocationRequired = "ALLOCATION_REQUIRED";
    public const string AllocationReleased = "ALLOCATION_RELEASED";
    public const string SubjectVersionMismatch = "SUBJECT_ALLOCATION_VERSION_MISMATCH";
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string AllocationExpired = "ALLOCATION_EXPIRED";
    public const string AllocationUnavailable = "ALLOCATION_UNAVAILABLE";
}

public static class AllocationErrorCodes
{
    public const string ValidationFailed = "ALC.VALIDATION_FAILED";
    public const string AllocationExpired = "ALC.ALLOCATION_EXPIRED";
    public const string EligibilityBlocked = "ALC.ELIGIBILITY_BLOCKED";
    public const string ApplicabilityUnknown = "ALC.APPLICABILITY_UNKNOWN";
    public const string DestructiveConflict = "ALC.DESTRUCTIVE_CONFLICT";
    public const string NotAuthorized = "ALC.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "ALC.OBJECT_NOT_ACCESSIBLE";
    public const string ExpectedVersionConflict = "ALC.EXPECTED_VERSION_CONFLICT";
    public const string PersistenceUnavailable = "ALC.PERSISTENCE_UNAVAILABLE";
}

public static class AllocationGateSources
{
    public const string Receiving = "RECEIVING";
    public const string Scope = "SCOPE";
    public const string Quantity = "QUANTITY";
}

public sealed record AllocationObjectContext(
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory);

public sealed record AllocationVersionedReference(string Id, long Version);

public sealed record AllocationSubjectReference(string SubjectType, string Id, long Version);

public sealed record AllocationGateResult(
    string Source,
    string Decision,
    long? PinnedVersion,
    string RuleSetVersion,
    IReadOnlyList<string> ReasonCodes);

public sealed record CreateTestObjectAllocationRequest(
    long ExpectedCurrentVersion,
    string RuleSetVersion,
    AllocationObjectContext ObjectScope,
    AllocationSubjectReference Subject,
    AllocationVersionedReference IdentityAssignment,
    string ReceivedItemId,
    long ExpectedReceivedItemVersion,
    string ScopeMatrixId,
    long ExpectedScopeMatrixVersion,
    string ScopeLineId,
    AllocationVersionedReference PlanStep,
    string Purpose,
    int SequenceOrder,
    bool Destructive,
    string QuantityAccountId,
    long ExpectedQuantityAccountVersion,
    decimal RequestedAmount,
    string Dimension,
    string Unit,
    AllocationVersionedReference StorageCondition,
    DateTimeOffset ValidUntil,
    string? ReservationEntryId = null);

public sealed record ReleaseTestObjectAllocationRequest(string Reason);

public sealed record TestObjectAllocationResult(
    string AllocationId,
    string State,
    long SubjectAllocationVersion,
    string RuleSetVersion,
    AllocationObjectContext ObjectScope,
    AllocationSubjectReference Subject,
    AllocationVersionedReference IdentityAssignment,
    string ScopeMatrixId,
    string ScopeLineId,
    AllocationVersionedReference PlanStep,
    string Purpose,
    int SequenceOrder,
    bool Destructive,
    string QuantityAccountId,
    decimal RequestedAmount,
    string Dimension,
    string Unit,
    AllocationVersionedReference StorageCondition,
    DateTimeOffset ValidUntil,
    string? ReservationEntryId,
    AllocationGateResult ReceivingGate,
    AllocationGateResult ScopeGate,
    AllocationGateResult QuantityGate,
    string AssignedBy,
    DateTimeOffset AssignedAt,
    string? ReleaseReason,
    string? ReleasedBy,
    DateTimeOffset? ReleasedAt);

public sealed record AllocationReleaseResult(
    string AllocationId,
    string State,
    string Reason,
    string ReleasedBy,
    DateTimeOffset ReleasedAt);

public sealed record AllocationStatusRequest(
    string OrganizationGroupId,
    string AllocationId,
    long ExpectedSubjectAllocationVersion,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record AllocationStatusResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string? AllocationId,
    string? State,
    long? CurrentSubjectAllocationVersion,
    string RuleSetVersion);

public interface IAllocationStatusPort
{
    ValueTask<AllocationStatusResult> EvaluateAsync(
        AllocationStatusRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AllocationAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    AllocationObjectContext ObjectScope,
    string Capability);

public sealed record AllocationAuthorizationDecision(bool Allowed)
{
    public static AllocationAuthorizationDecision Permit { get; } = new(true);
    public static AllocationAuthorizationDecision Deny { get; } = new(false);
}

public interface IAllocationAuthorizationPort
{
    ValueTask<AllocationAuthorizationDecision> AuthorizeAsync(
        AllocationAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
