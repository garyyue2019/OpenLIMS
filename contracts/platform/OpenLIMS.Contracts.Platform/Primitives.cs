namespace OpenLIMS.Contracts.Platform;

public sealed record OrganizationScope(string OrganizationGroupId);

public sealed record ActorContext(string ActorId, string OrganizationGroupId);

public sealed record CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";

    public CorrelationId(string value) => Value = value;

    public string Value { get; }
}

public sealed record IdempotencyKey(string Value);

public sealed record ObjectReference(string Bucket, string ObjectKey);

public sealed record OutboxEnvelope(string Id, string Type, DateTimeOffset OccurredAt);

public sealed record InboxReceipt(string MessageId, DateTimeOffset ReceivedAt);

public sealed record AuditIntent(
    string ActorId,
    string OrganizationGroupId,
    string ObjectId,
    string Action,
    string RuleVersion,
    string? BeforeVersion,
    string? AfterVersion,
    string CorrelationId,
    DateTimeOffset OccurredAt);
