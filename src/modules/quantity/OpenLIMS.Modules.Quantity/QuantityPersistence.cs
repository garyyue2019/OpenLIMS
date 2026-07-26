using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Quantity;

namespace OpenLIMS.Modules.Quantity;

internal sealed class QuantityDataSource : IAsyncDisposable
{
    public QuantityDataSource(QuantityPersistenceOptions options) => Value = NpgsqlDataSource.Create(options.ConnectionString);
    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed record QuantityAccountRow(
    Guid QuantityAccountId,
    string OrganizationGroupId,
    QuantityObjectContext ObjectScope,
    QuantitySubjectReference Subject,
    QuantityAccountConfiguration Configuration,
    string CreatedBy,
    DateTimeOffset CreatedAt);

internal sealed class QuantityStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireAccountLockAsync(Guid quantityAccountId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@quantity_account_id, 0))",
            connection,
            transaction);
        command.Parameters.AddWithValue("quantity_account_id", quantityAccountId.ToString("N"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<QuantityAccountRow?> LoadAccountAsync(
        string organizationGroupId,
        Guid quantityAccountId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select quantity_account_id, organization_group_id,
                   legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                   subject_type, subject_ref, subject_version,
                   dimension, unit, precision_scale, conservation_tolerance,
                   created_by, created_at
            from quantity.quantity_account
            where organization_group_id = @organization_group_id
              and quantity_account_id = @quantity_account_id
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("quantity_account_id", quantityAccountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new QuantityAccountRow(
            reader.GetGuid(0),
            reader.GetString(1),
            new QuantityObjectContext(
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)),
            new QuantitySubjectReference(
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt64(9)),
            new QuantityAccountConfiguration(
                reader.GetString(10),
                reader.GetString(11),
                reader.GetInt32(12),
                reader.GetDecimal(13)),
            reader.GetString(14),
            reader.GetFieldValue<DateTimeOffset>(15));
    }

    public async Task<QuantityBalances> LoadBalancesAsync(
        Guid quantityAccountId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select account_version, resulting_balance, resulting_reserved
            from quantity.quantity_entry
            where quantity_account_id = @quantity_account_id
            order by account_version desc
            limit 1
            """, connection, transaction);
        command.Parameters.AddWithValue("quantity_account_id", quantityAccountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new QuantityBalances(reader.GetInt64(0), reader.GetDecimal(1), reader.GetDecimal(2))
            : new QuantityBalances(1, 0m, 0m);
    }

    public async Task<QuantityEntrySnapshot?> LoadEntrySnapshotAsync(
        Guid quantityAccountId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select e.entry_id, e.entry_type, e.amount, e.referenced_entry_id, e.reservation_id,
                   exists (
                       select 1 from quantity.quantity_entry r
                       where r.quantity_account_id = e.quantity_account_id
                         and r.entry_type = 'REVERSAL'
                         and r.referenced_entry_id = e.entry_id
                   ) as reversed,
                   exists (
                       select 1 from quantity.quantity_entry c
                       where c.quantity_account_id = e.quantity_account_id
                         and c.reservation_id = e.entry_id
                   ) as reservation_closed,
                   exists (
                       select 1 from quantity.quantity_entry s
                       where s.quantity_account_id = e.quantity_account_id
                         and s.entry_type = 'RESTATE'
                         and s.referenced_entry_id = e.entry_id
                   ) as restated,
                   original.entry_type as original_entry_type
            from quantity.quantity_entry e
            left join quantity.quantity_entry original on original.entry_id = e.referenced_entry_id
            where e.quantity_account_id = @quantity_account_id
              and e.entry_id = @entry_id
            """, connection, transaction);
        command.Parameters.AddWithValue("quantity_account_id", quantityAccountId);
        command.Parameters.AddWithValue("entry_id", entryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new QuantityEntrySnapshot(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetDecimal(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetBoolean(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    public async Task<QuantityAccountResult> InsertAccountAsync(
        Guid quantityAccountId,
        string organizationGroupId,
        QuantityObjectContext objectScope,
        QuantitySubjectReference subject,
        QuantityAccountConfiguration configuration,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into quantity.quantity_account (
                quantity_account_id, organization_group_id,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                subject_type, subject_ref, subject_version,
                dimension, unit, precision_scale, conservation_tolerance,
                rule_set_version, created_by, created_at, event_id, correlation_id
            ) values (
                @quantity_account_id, @organization_group_id,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @subject_type, @subject_ref, @subject_version,
                @dimension, @unit, @precision_scale, @conservation_tolerance,
                @rule_set_version, @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("quantity_account_id", quantityAccountId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("legal_entity_id", objectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", objectScope.LaboratoryId);
            command.Parameters.AddWithValue("customer_id", objectScope.CustomerId);
            command.Parameters.AddWithValue("service_order_id", objectScope.ServiceOrderId);
            command.Parameters.AddWithValue("product_category", objectScope.ProductCategory);
            command.Parameters.AddWithValue("subject_type", subject.SubjectType);
            command.Parameters.AddWithValue("subject_ref", subject.Id);
            command.Parameters.AddWithValue("subject_version", subject.Version);
            command.Parameters.AddWithValue("dimension", configuration.Dimension);
            command.Parameters.AddWithValue("unit", configuration.Unit);
            command.Parameters.AddWithValue("precision_scale", configuration.PrecisionScale);
            command.Parameters.AddWithValue("conservation_tolerance", configuration.ConservationTolerance);
            command.Parameters.AddWithValue("rule_set_version", QuantityContract.RuleSetVersion);
            command.Parameters.AddWithValue("created_by", actorId);
            command.Parameters.AddWithValue("created_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var accountId = quantityAccountId.ToString("N");
        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            accountId,
            "CREATE_QUANTITY_ACCOUNT",
            QuantityContract.RuleSetVersion,
            null,
            "1",
            correlationId,
            now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(
            eventId,
            "QuantityAccountCreated.v1",
            now), cancellationToken);

        return new QuantityAccountResult(
            accountId,
            1,
            QuantityContract.RuleSetVersion,
            objectScope,
            subject,
            configuration.Dimension,
            configuration.Unit,
            configuration.PrecisionScale,
            configuration.ConservationTolerance,
            0m,
            0m,
            0m,
            actorId,
            now);
    }

    public async Task<QuantityEntryResult> InsertEntryAsync(
        Guid entryId,
        Guid quantityAccountId,
        long accountVersion,
        string organizationGroupId,
        QuantityPostingPlan plan,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into quantity.quantity_entry (
                entry_id, quantity_account_id, account_version, entry_type, amount,
                resulting_balance, resulting_reserved, referenced_entry_id, reservation_id,
                reason, posted_by, posted_at, event_id, correlation_id
            ) values (
                @entry_id, @quantity_account_id, @account_version, @entry_type, @amount,
                @resulting_balance, @resulting_reserved, @referenced_entry_id, @reservation_id,
                @reason, @posted_by, @posted_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("entry_id", entryId);
            command.Parameters.AddWithValue("quantity_account_id", quantityAccountId);
            command.Parameters.AddWithValue("account_version", accountVersion);
            command.Parameters.AddWithValue("entry_type", plan.EntryType);
            command.Parameters.AddWithValue("amount", plan.Amount);
            command.Parameters.AddWithValue("resulting_balance", plan.ResultingBalance);
            command.Parameters.AddWithValue("resulting_reserved", plan.ResultingReserved);
            command.Parameters.AddWithValue("referenced_entry_id", (object?)plan.ReferencedEntryId ?? DBNull.Value);
            command.Parameters.AddWithValue("reservation_id", (object?)plan.ReservationId ?? DBNull.Value);
            command.Parameters.AddWithValue("reason", (object?)plan.Reason ?? DBNull.Value);
            command.Parameters.AddWithValue("posted_by", actorId);
            command.Parameters.AddWithValue("posted_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var accountId = quantityAccountId.ToString("N");
        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            accountId,
            "POST_QUANTITY_ENTRY",
            QuantityContract.RuleSetVersion,
            (accountVersion - 1).ToString(),
            accountVersion.ToString(),
            correlationId,
            now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(
            eventId,
            "QuantityEntryPosted.v1",
            now), cancellationToken);

        return new QuantityEntryResult(
            entryId.ToString("N"),
            accountId,
            accountVersion,
            plan.EntryType,
            plan.Amount,
            plan.ResultingBalance,
            plan.ResultingReserved,
            plan.ResultingBalance - plan.ResultingReserved,
            plan.ReferencedEntryId?.ToString("N"),
            plan.ReservationId?.ToString("N"),
            plan.Reason,
            actorId,
            now);
    }

    public Task WriteReadAuditAsync(
        string quantityAccountId,
        long version,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            quantityAccountId,
            action,
            QuantityContract.RuleSetVersion,
            version.ToString(),
            version.ToString(),
            correlationId,
            now), cancellationToken);

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("QTY.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class QuantityAttemptAuditWriter(QuantityDataSource dataSource)
{
    public async Task WriteAsync(
        string commandType,
        string? actorId,
        string organizationGroupId,
        string targetHash,
        string correlationId,
        string outcome,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.Value.CreateCommand("""
            insert into quantity.audit_attempt (
                attempt_id, command_type, actor_id, organization_group_id,
                target_hash, correlation_id, outcome, occurred_at
            ) values (
                @attempt_id, @command_type, @actor_id, @organization_group_id,
                @target_hash, @correlation_id, @outcome, @occurred_at
            )
            """);
        command.Parameters.AddWithValue("attempt_id", Guid.NewGuid());
        command.Parameters.AddWithValue("command_type", commandType);
        command.Parameters.AddWithValue("actor_id", (object?)actorId ?? DBNull.Value);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("target_hash", targetHash);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
