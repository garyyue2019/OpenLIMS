namespace OpenLIMS.Contracts.Platform;

public interface ICurrentOrganizationContext
{
    OrganizationScope Current { get; }
}

public interface ICurrentActorContext
{
    ActorContext? Current { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IIdGenerator
{
    string NewId();
}

public interface ITransactionCoordinator
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}

public interface IOutboxWriter
{
    Task WriteAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default);
}

public interface IInboxDeduplicator
{
    Task<bool> TryRecordAsync(InboxReceipt receipt, CancellationToken cancellationToken = default);
}

public interface IAuditIntentWriter
{
    Task WriteAsync(AuditIntent intent, CancellationToken cancellationToken = default);
}

public interface IObjectStoragePort
{
    Task PutAsync(ObjectReference reference, Stream content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(ObjectReference reference, CancellationToken cancellationToken = default);
    Task DeleteAsync(ObjectReference reference, CancellationToken cancellationToken = default);
}
