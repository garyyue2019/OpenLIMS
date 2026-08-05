namespace OpenLIMS.Contracts.Operations;

public static class OperationsContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "OPERATIONS@1.0.0";
    public const string CreateLineageEdgePath = "/api/v1/sample-lineage/edges";
    public const string GetLineagePath = "/api/v1/sample-lineage/{objectId}";
    public const string RecordCustodyEventPath = "/api/v1/custody-events";
    public const string GetCustodyPath = "/api/v1/samples/{objectId}/custody";
    public const string CreateWorkPlanPath = "/api/v1/work-plans";
    public const string GetWorkPlanPath = "/api/v1/work-plans/{id}";
    public const string ChangeTaskStatePath = "/api/v1/work-plans/{id}/tasks/{taskId}/state";
    public const string ReserveResourcePath = "/api/v1/work-plans/{id}/resource-reservations";
    public const string GetWorkQueuePath = "/api/v1/work-queues";
}

public static class LineageRelationKinds
{
    public const string DerivedFrom = "DERIVED_FROM";
    public const string SplitFrom = "SPLIT_FROM";
    public const string CompositeFrom = "COMPOSITE_FROM";
    public const string NonDestructiveUse = "NON_DESTRUCTIVE_USE";
}

public static class CustodyEventKinds
{
    public const string Received = "RECEIVED";
    public const string Transferred = "TRANSFERRED";
    public const string CheckedOut = "CHECKED_OUT";
    public const string Returned = "RETURNED";
    public const string Retained = "RETAINED";
    public const string Disposed = "DISPOSED";
}

public static class WorkPlanStates
{
    public const string Active = "ACTIVE";
    public const string Completed = "COMPLETED";
}

public static class WorkTaskStates
{
    public const string Planned = "PLANNED";
    public const string Ready = "READY";
    public const string InProgress = "IN_PROGRESS";
    public const string Blocked = "BLOCKED";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
}

public static class ResourceKinds
{
    public const string Person = "PERSON";
    public const string Equipment = "EQUIPMENT";
    public const string Location = "LOCATION";
    public const string Fixture = "FIXTURE";
}

public static class OperationsCapabilities
{
    public const string Read = "operations:read";
    public const string Write = "operations:write";
}

public static class OperationsClaimTypes
{
    public const string Capability = "openlims_capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ProductCategory = "product_category";
}

public static class OperationsErrorCodes
{
    public const string ValidationFailed = "OPS.VALIDATION_FAILED";
    public const string ExpectedVersionConflict = "OPS.EXPECTED_VERSION_CONFLICT";
    public const string ObjectNotAccessible = "OPS.OBJECT_NOT_ACCESSIBLE";
    public const string NotAuthorized = "OPS.NOT_AUTHORIZED";
    public const string LineageCycle = "OPS.LINEAGE_CYCLE";
    public const string LineageParentConflict = "OPS.LINEAGE_PARENT_CONFLICT";
    public const string CustodySequenceConflict = "OPS.CUSTODY_SEQUENCE_CONFLICT";
    public const string DependencyBlocked = "OPS.DEPENDENCY_BLOCKED";
    public const string InvalidTaskTransition = "OPS.INVALID_TASK_TRANSITION";
    public const string ResourceConflict = "OPS.RESOURCE_CONFLICT";
    public const string PersistenceUnavailable = "OPS.PERSISTENCE_UNAVAILABLE";
}

public sealed record OperationsVersionedReference(string Id, long Version);

public sealed record OperationsObjectContext(
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory);

public sealed record CreateLineageEdgeRequest(
    string SourceObjectId,
    string TargetObjectId,
    string RelationKind,
    OperationsVersionedReference Basis,
    OperationsObjectContext ObjectScope);

public sealed record LineageEdgeResult(
    string EdgeId,
    string SourceObjectId,
    string TargetObjectId,
    string RelationKind,
    OperationsVersionedReference Basis,
    OperationsObjectContext ObjectScope,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record LineageGraphResult(
    string ObjectId,
    string RuleSetVersion,
    IReadOnlyList<LineageEdgeResult> Edges);

public sealed record RecordCustodyEventRequest(
    string ObjectId,
    string EventKind,
    string? FromLocationId,
    string ToLocationId,
    string ResponsiblePartyId,
    string EvidenceRef,
    OperationsObjectContext ObjectScope);

public sealed record CustodyEventResult(
    string EventId,
    string ObjectId,
    long Sequence,
    string EventKind,
    string? FromLocationId,
    string ToLocationId,
    string ResponsiblePartyId,
    string EvidenceRef,
    OperationsObjectContext ObjectScope,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record CustodyChainResult(
    string ObjectId,
    string RuleSetVersion,
    IReadOnlyList<CustodyEventResult> Events);

public sealed record WorkTaskInput(
    string TaskId,
    string ScopeLineId,
    OperationsVersionedReference Method,
    string WorkCenterId,
    int Priority,
    int Sequence,
    bool Destructive,
    DateTimeOffset? PlannedStart,
    DateTimeOffset? PlannedEnd,
    IReadOnlyList<string> DependencyTaskIds);

public sealed record WorkTaskResult(
    string TaskId,
    string ScopeLineId,
    OperationsVersionedReference Method,
    string WorkCenterId,
    int Priority,
    int Sequence,
    bool Destructive,
    DateTimeOffset? PlannedStart,
    DateTimeOffset? PlannedEnd,
    IReadOnlyList<string> DependencyTaskIds,
    string State,
    string? StateReason,
    string? StateChangedBy,
    DateTimeOffset? StateChangedAt);

public sealed record CreateWorkPlanRequest(
    OperationsVersionedReference ScopeMatrix,
    OperationsVersionedReference SampleIdentity,
    IReadOnlyList<WorkTaskInput> Tasks,
    OperationsObjectContext ObjectScope);

public sealed record ChangeWorkTaskStateRequest(
    long ExpectedPlanVersion,
    string State,
    string Reason);

public sealed record ReserveResourceRequest(
    long ExpectedPlanVersion,
    string TaskId,
    string ResourceKind,
    string ResourceId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record ResourceReservationResult(
    string ReservationId,
    string TaskId,
    string ResourceKind,
    string ResourceId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record WorkPlanResult(
    string WorkPlanId,
    long Version,
    string RuleSetVersion,
    string State,
    OperationsVersionedReference ScopeMatrix,
    OperationsVersionedReference SampleIdentity,
    OperationsObjectContext ObjectScope,
    IReadOnlyList<WorkTaskResult> Tasks,
    IReadOnlyList<ResourceReservationResult> Reservations,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record WorkQueueItem(
    string WorkPlanId,
    long WorkPlanVersion,
    string TaskId,
    string WorkCenterId,
    string State,
    int Priority,
    int Sequence,
    DateTimeOffset? PlannedStart,
    DateTimeOffset? PlannedEnd,
    IReadOnlyList<string> DependencyTaskIds,
    OperationsObjectContext ObjectScope);

public sealed record WorkQueueResult(
    string WorkCenterId,
    string? State,
    string RuleSetVersion,
    IReadOnlyList<WorkQueueItem> Items);

public sealed record OperationsAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    OperationsObjectContext ObjectScope,
    string Capability);

public sealed record OperationsAuthorizationDecision(bool Allowed)
{
    public static OperationsAuthorizationDecision Permit { get; } = new(true);
    public static OperationsAuthorizationDecision Deny { get; } = new(false);
}

public interface IOperationsAuthorizationPort
{
    ValueTask<OperationsAuthorizationDecision> AuthorizeAsync(
        OperationsAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
