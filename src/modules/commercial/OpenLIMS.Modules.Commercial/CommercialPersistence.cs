using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Commercial;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Commercial;

internal sealed class CommercialDataSource : IAsyncDisposable
{
    public CommercialDataSource(CommercialPersistenceOptions options) =>
        Value = NpgsqlDataSource.Create(options.ConnectionString);

    public NpgsqlDataSource Value { get; }

    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed class CommercialStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task AcquireLockAsync(string key, CancellationToken cancellationToken) =>
        ExecuteLockAsync($"commercial:{key}", cancellationToken);

    public async Task InsertCatalogAsync(
        CatalogRecordResult result,
        string organizationGroupId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into commercial.catalog_record_version (
                record_id, version, organization_group_id,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                kind, code, payload, recorded_by, recorded_at, correlation_id
            ) values (
                @record_id, @version, @organization_group_id,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @kind, @code, @payload, @recorded_by, @recorded_at, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("record_id", Guid.Parse(result.RecordId));
        command.Parameters.AddWithValue("version", result.Version);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        AddScope(command, result.ObjectScope);
        command.Parameters.AddWithValue("kind", result.Kind);
        command.Parameters.AddWithValue("code", result.Code);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("recorded_by", result.RecordedBy);
        command.Parameters.AddWithValue("recorded_at", result.RecordedAt);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteEvidenceAsync(
            result.RecordId,
            organizationGroupId,
            result.RecordedBy,
            "RECORD_CATALOG_VERSION",
            result.Version > 1 ? (result.Version - 1).ToString() : null,
            result.Version.ToString(),
            "CommercialCatalogVersionRecorded.v1",
            correlationId,
            result.RecordedAt,
            cancellationToken);
    }

    public async Task<CatalogRecordResult?> LoadCatalogAsync(
        string organizationGroupId,
        Guid recordId,
        long? version,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var sql = version is null
            ? """
              select payload::text from commercial.catalog_record_version
              where organization_group_id = @organization_group_id and record_id = @record_id
              order by version desc limit 1
              """
            : """
              select payload::text from commercial.catalog_record_version
              where organization_group_id = @organization_group_id and record_id = @record_id and version = @version
              """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("record_id", recordId);
        if (version is not null)
            command.Parameters.AddWithValue("version", version.Value);
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : Deserialize<CatalogRecordResult>(payload);
    }

    public async Task InsertInquiryAsync(
        InquiryResult result,
        string organizationGroupId,
        string correlationId,
        string eventType,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into commercial.inquiry_version (
                inquiry_id, version, organization_group_id,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                inquiry_number, state, payload, recorded_by, recorded_at, correlation_id
            ) values (
                @inquiry_id, @version, @organization_group_id,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @inquiry_number, @state, @payload, @recorded_by, @recorded_at, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("inquiry_id", Guid.Parse(result.InquiryId));
        command.Parameters.AddWithValue("version", result.Version);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        AddScope(command, result.ObjectScope);
        command.Parameters.AddWithValue("inquiry_number", result.InquiryNumber);
        command.Parameters.AddWithValue("state", result.State);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("recorded_by", result.RecordedBy);
        command.Parameters.AddWithValue("recorded_at", result.RecordedAt);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteEvidenceAsync(
            result.InquiryId,
            organizationGroupId,
            result.RecordedBy,
            eventType,
            result.Version > 1 ? (result.Version - 1).ToString() : null,
            result.Version.ToString(),
            $"{eventType}.v1",
            correlationId,
            result.RecordedAt,
            cancellationToken);
    }

    public async Task<InquiryResult?> LoadInquiryAsync(
        string organizationGroupId,
        Guid inquiryId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select payload::text from commercial.inquiry_version
            where organization_group_id = @organization_group_id and inquiry_id = @inquiry_id
            order by version desc limit 1
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("inquiry_id", inquiryId);
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : Deserialize<InquiryResult>(payload);
    }

    public Task WriteReadAuditAsync(
        string objectId,
        string organizationGroupId,
        string actorId,
        string action,
        string version,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            objectId,
            action,
            CommercialContract.RuleSetVersion,
            version,
            version,
            correlationId,
            now), cancellationToken);

    private async Task ExecuteLockAsync(string key, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtext(@key))",
            connection,
            transaction);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task WriteEvidenceAsync(
        string objectId,
        string organizationGroupId,
        string actorId,
        string action,
        string? beforeVersion,
        string afterVersion,
        string messageType,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.NewGuid().ToString("N");
        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            objectId,
            action,
            CommercialContract.RuleSetVersion,
            beforeVersion,
            afterVersion,
            correlationId,
            now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private static void AddScope(NpgsqlCommand command, CommercialObjectContext scope)
    {
        command.Parameters.AddWithValue("legal_entity_id", scope.LegalEntityId);
        command.Parameters.AddWithValue("laboratory_id", scope.LaboratoryId);
        command.Parameters.AddWithValue("customer_id", scope.CustomerId);
        command.Parameters.AddWithValue("service_order_id", scope.ServiceOrderId);
        command.Parameters.AddWithValue("product_category", scope.ProductCategory);
    }

    private static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, JsonOptions)
        ?? throw new InvalidOperationException("COM.PERSISTED_PAYLOAD_INVALID");

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("COM.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class CommercialAttemptAuditWriter(CommercialDataSource dataSource)
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
            insert into commercial.audit_attempt (
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
