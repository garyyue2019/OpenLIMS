using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Operations;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Operations;

internal sealed class OperationsDataSource : IAsyncDisposable
{
    public OperationsDataSource(OperationsPersistenceOptions options) =>
        Value = NpgsqlDataSource.Create(options.ConnectionString);

    public NpgsqlDataSource Value { get; }

    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed class OperationsStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AcquireLockAsync(string key, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtext(@key))",
            connection,
            transaction);
        command.Parameters.AddWithValue("key", $"operations:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LineageEdgeResult>> LoadAllEdgesAsync(
        string organizationGroupId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select payload::text from operations.lineage_edge
            where organization_group_id = @organization_group_id
            order by recorded_at, edge_id
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        var results = new List<LineageEdgeResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(Deserialize<LineageEdgeResult>(reader.GetString(0)));
        return results;
    }

    public async Task InsertLineageEdgeAsync(
        LineageEdgeResult result,
        string organizationGroupId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into operations.lineage_edge (
                edge_id, organization_group_id, source_object_id, target_object_id, relation_kind,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                payload, recorded_by, recorded_at, correlation_id
            ) values (
                @edge_id, @organization_group_id, @source_object_id, @target_object_id, @relation_kind,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @payload, @recorded_by, @recorded_at, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("edge_id", Guid.Parse(result.EdgeId));
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("source_object_id", result.SourceObjectId);
        command.Parameters.AddWithValue("target_object_id", result.TargetObjectId);
        command.Parameters.AddWithValue("relation_kind", result.RelationKind);
        AddScope(command, result.ObjectScope);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("recorded_by", result.RecordedBy);
        command.Parameters.AddWithValue("recorded_at", result.RecordedAt);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteEvidenceAsync(
            result.EdgeId,
            organizationGroupId,
            result.RecordedBy,
            "CREATE_LINEAGE_EDGE",
            null,
            "1",
            "OperationsLineageEdgeCreated.v1",
            correlationId,
            result.RecordedAt,
            cancellationToken);
    }

    public async Task<CustodyEventResult?> LoadCurrentCustodyAsync(
        string organizationGroupId,
        string objectId,
        CancellationToken cancellationToken)
    {
        var chain = await LoadCustodyChainAsync(organizationGroupId, objectId, cancellationToken);
        return chain.LastOrDefault();
    }

    public async Task<IReadOnlyList<CustodyEventResult>> LoadCustodyChainAsync(
        string organizationGroupId,
        string objectId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select payload::text from operations.custody_event
            where organization_group_id = @organization_group_id and object_id = @object_id
            order by sequence
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("object_id", objectId);
        var results = new List<CustodyEventResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(Deserialize<CustodyEventResult>(reader.GetString(0)));
        return results;
    }

    public async Task InsertCustodyEventAsync(
        CustodyEventResult result,
        string organizationGroupId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into operations.custody_event (
                event_id, organization_group_id, object_id, sequence,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                payload, recorded_by, recorded_at, correlation_id
            ) values (
                @event_id, @organization_group_id, @object_id, @sequence,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @payload, @recorded_by, @recorded_at, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("event_id", Guid.Parse(result.EventId));
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("object_id", result.ObjectId);
        command.Parameters.AddWithValue("sequence", result.Sequence);
        AddScope(command, result.ObjectScope);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("recorded_by", result.RecordedBy);
        command.Parameters.AddWithValue("recorded_at", result.RecordedAt);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteEvidenceAsync(
            result.ObjectId,
            organizationGroupId,
            result.RecordedBy,
            "RECORD_CUSTODY_EVENT",
            result.Sequence > 1 ? (result.Sequence - 1).ToString() : null,
            result.Sequence.ToString(),
            "OperationsCustodyEventRecorded.v1",
            correlationId,
            result.RecordedAt,
            cancellationToken);
    }

    public async Task InsertWorkPlanAsync(
        WorkPlanResult result,
        string organizationGroupId,
        string correlationId,
        string eventType,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into operations.work_plan_version (
                work_plan_id, version, organization_group_id,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                state, payload, recorded_by, recorded_at, correlation_id
            ) values (
                @work_plan_id, @version, @organization_group_id,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @state, @payload, @recorded_by, @recorded_at, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("work_plan_id", Guid.Parse(result.WorkPlanId));
        command.Parameters.AddWithValue("version", result.Version);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        AddScope(command, result.ObjectScope);
        command.Parameters.AddWithValue("state", result.State);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("recorded_by", result.RecordedBy);
        command.Parameters.AddWithValue("recorded_at", result.RecordedAt);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteEvidenceAsync(
            result.WorkPlanId,
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

    public async Task<WorkPlanResult?> LoadWorkPlanAsync(
        string organizationGroupId,
        Guid workPlanId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select payload::text from operations.work_plan_version
            where organization_group_id = @organization_group_id and work_plan_id = @work_plan_id
            order by version desc limit 1
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("work_plan_id", workPlanId);
        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return payload is null ? null : Deserialize<WorkPlanResult>(payload);
    }

    public async Task<IReadOnlyList<WorkPlanResult>> LoadCurrentWorkPlansAsync(
        string organizationGroupId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select distinct on (work_plan_id) payload::text
            from operations.work_plan_version
            where organization_group_id = @organization_group_id
            order by work_plan_id, version desc
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        var results = new List<WorkPlanResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(Deserialize<WorkPlanResult>(reader.GetString(0)));
        return results;
    }

    public async Task<bool> HasResourceConflictAsync(
        string organizationGroupId,
        string resourceKind,
        string resourceId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select exists (
                select 1 from operations.resource_reservation
                where organization_group_id = @organization_group_id
                  and resource_kind = @resource_kind
                  and resource_id = @resource_id
                  and starts_at < @ends_at
                  and ends_at > @starts_at
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("resource_kind", resourceKind);
        command.Parameters.AddWithValue("resource_id", resourceId);
        command.Parameters.AddWithValue("starts_at", startsAt);
        command.Parameters.AddWithValue("ends_at", endsAt);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task InsertReservationAsync(
        Guid workPlanId,
        long workPlanVersion,
        ResourceReservationResult reservation,
        string organizationGroupId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into operations.resource_reservation (
                reservation_id, organization_group_id, work_plan_id, work_plan_version,
                task_id, resource_kind, resource_id, starts_at, ends_at,
                recorded_by, recorded_at, correlation_id
            ) values (
                @reservation_id, @organization_group_id, @work_plan_id, @work_plan_version,
                @task_id, @resource_kind, @resource_id, @starts_at, @ends_at,
                @recorded_by, @recorded_at, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("reservation_id", Guid.Parse(reservation.ReservationId));
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("work_plan_id", workPlanId);
        command.Parameters.AddWithValue("work_plan_version", workPlanVersion);
        command.Parameters.AddWithValue("task_id", reservation.TaskId);
        command.Parameters.AddWithValue("resource_kind", reservation.ResourceKind);
        command.Parameters.AddWithValue("resource_id", reservation.ResourceId);
        command.Parameters.AddWithValue("starts_at", reservation.StartsAt);
        command.Parameters.AddWithValue("ends_at", reservation.EndsAt);
        command.Parameters.AddWithValue("recorded_by", reservation.RecordedBy);
        command.Parameters.AddWithValue("recorded_at", reservation.RecordedAt);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            OperationsContract.RuleSetVersion,
            version,
            version,
            correlationId,
            now), cancellationToken);

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
            OperationsContract.RuleSetVersion,
            beforeVersion,
            afterVersion,
            correlationId,
            now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private static void AddScope(NpgsqlCommand command, OperationsObjectContext scope)
    {
        command.Parameters.AddWithValue("legal_entity_id", scope.LegalEntityId);
        command.Parameters.AddWithValue("laboratory_id", scope.LaboratoryId);
        command.Parameters.AddWithValue("customer_id", scope.CustomerId);
        command.Parameters.AddWithValue("service_order_id", scope.ServiceOrderId);
        command.Parameters.AddWithValue("product_category", scope.ProductCategory);
    }

    private static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, JsonOptions)
        ?? throw new InvalidOperationException("OPS.PERSISTED_PAYLOAD_INVALID");

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("OPS.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class OperationsAttemptAuditWriter(OperationsDataSource dataSource)
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
            insert into operations.audit_attempt (
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
