using System.Threading;
using Npgsql;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.BuildingBlocks.Platform;

internal sealed record PostgresTransactionSession(NpgsqlConnection Connection, NpgsqlTransaction Transaction);

/// <summary>
/// Allows one module-owned PostgreSQL repository or DbContext to enlist its own
/// fact write in the transaction coordinated with platform Audit/Outbox ports.
/// It does not grant access to another module's schema.
/// </summary>
public interface IPostgresTransactionAccessor
{
    bool HasActiveTransaction { get; }
    NpgsqlConnection Connection { get; }
    NpgsqlTransaction Transaction { get; }
}

internal sealed class PostgresTransactionContext : IPostgresTransactionAccessor
{
    private readonly AsyncLocal<PostgresTransactionSession?> _session = new();

    public PostgresTransactionSession? Current => _session.Value;
    public bool HasActiveTransaction => Current is not null;
    public NpgsqlConnection Connection =>
        Current?.Connection ?? throw new InvalidOperationException("PLT.TRANSACTION_REQUIRED");
    public NpgsqlTransaction Transaction =>
        Current?.Transaction ?? throw new InvalidOperationException("PLT.TRANSACTION_REQUIRED");

    public IDisposable Push(PostgresTransactionSession session)
    {
        if (_session.Value is not null)
        {
            throw new InvalidOperationException("PLT.NESTED_TRANSACTION_NOT_SUPPORTED");
        }

        _session.Value = session;
        return new Scope(this);
    }

    private sealed class Scope(PostgresTransactionContext owner) : IDisposable
    {
        public void Dispose() => owner._session.Value = null;
    }
}

internal sealed class PostgresPlatformPersistence(
    NpgsqlDataSource dataSource,
    PostgresTransactionContext transactionContext) :
    ITransactionCoordinator,
    IOutboxWriter,
    IInboxDeduplicator,
    IAuditIntentWriter
{
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        using var scope = transactionContext.Push(new PostgresTransactionSession(connection, transaction));
        try
        {
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task WriteAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var session = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into platform.outbox (id, message_type, occurred_at)
            values (@id, @message_type, @occurred_at)
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("id", envelope.Id);
        command.Parameters.AddWithValue("message_type", envelope.Type);
        command.Parameters.AddWithValue("occurred_at", envelope.OccurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryRecordAsync(InboxReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var session = transactionContext.Current;
        if (session is not null)
        {
            return await InsertInboxAsync(session.Connection, session.Transaction, receipt, cancellationToken);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var recorded = await InsertInboxAsync(connection, transaction, receipt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return recorded;
    }

    public async Task WriteAsync(AuditIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var session = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into platform.audit_intent (
                actor_id,
                organization_group_id,
                object_id,
                action,
                rule_version,
                before_version,
                after_version,
                correlation_id,
                occurred_at
            ) values (
                @actor_id,
                @organization_group_id,
                @object_id,
                @action,
                @rule_version,
                @before_version,
                @after_version,
                @correlation_id,
                @occurred_at
            )
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("actor_id", intent.ActorId);
        command.Parameters.AddWithValue("organization_group_id", intent.OrganizationGroupId);
        command.Parameters.AddWithValue("object_id", intent.ObjectId);
        command.Parameters.AddWithValue("action", intent.Action);
        command.Parameters.AddWithValue("rule_version", intent.RuleVersion);
        command.Parameters.AddWithValue("before_version", (object?)intent.BeforeVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("after_version", (object?)intent.AfterVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", intent.CorrelationId);
        command.Parameters.AddWithValue("occurred_at", intent.OccurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private PostgresTransactionSession RequireTransaction() =>
        transactionContext.Current ?? throw new InvalidOperationException("PLT.TRANSACTION_REQUIRED");

    private static async Task<bool> InsertInboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InboxReceipt receipt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into platform.inbox (message_id, received_at)
            values (@message_id, @received_at)
            on conflict (message_id) do nothing
            """, connection, transaction);
        command.Parameters.AddWithValue("message_id", receipt.MessageId);
        command.Parameters.AddWithValue("received_at", receipt.ReceivedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
