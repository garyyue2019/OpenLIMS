using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal sealed class ReceivingDataSource : IAsyncDisposable
{
    public ReceivingDataSource(ReceivingPersistenceOptions options) => DataSource = NpgsqlDataSource.Create(options.ConnectionString);

    public NpgsqlDataSource DataSource { get; }

    public ValueTask DisposeAsync() => DataSource.DisposeAsync();
}

internal enum IdempotencyReservationKind
{
    New,
    Replay,
    Conflict
}

internal sealed record IdempotencyReservation(
    IdempotencyReservationKind Kind,
    ReceiptRegistrationResult? Result = null);

internal sealed class ReceivingRegistrationStore(
    IPostgresTransactionAccessor transactionAccessor,
    ReceivingLabelIdentityWriter labelIdentityWriter)
{
    public async Task<IdempotencyReservation> ReserveIdempotencyAsync(
        string organizationGroupId,
        string actorId,
        string keyHash,
        string requestHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var insert = new NpgsqlCommand("""
            insert into receiving.idempotency (
                organization_group_id, key_hash, request_hash, actor_id, created_at
            ) values (
                @organization_group_id, @key_hash, @request_hash, @actor_id, @created_at
            )
            on conflict (organization_group_id, key_hash) do nothing
            """, connection, transaction);
        insert.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        insert.Parameters.AddWithValue("key_hash", keyHash);
        insert.Parameters.AddWithValue("request_hash", requestHash);
        insert.Parameters.AddWithValue("actor_id", actorId);
        insert.Parameters.AddWithValue("created_at", now);
        if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return new IdempotencyReservation(IdempotencyReservationKind.New);
        }

        await using var select = new NpgsqlCommand("""
            select request_hash, response_json
            from receiving.idempotency
            where organization_group_id = @organization_group_id and key_hash = @key_hash
            for update
            """, connection, transaction);
        select.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        select.Parameters.AddWithValue("key_hash", keyHash);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("REC.IDEMPOTENCY_RESERVATION_MISSING");
        }

        var storedRequestHash = reader.GetString(0);
        if (!string.Equals(storedRequestHash, requestHash, StringComparison.Ordinal))
        {
            return new IdempotencyReservation(IdempotencyReservationKind.Conflict);
        }

        if (reader.IsDBNull(1))
        {
            throw new InvalidOperationException("REC.IDEMPOTENCY_RESULT_MISSING");
        }

        var result = JsonSerializer.Deserialize<ReceiptRegistrationResult>(reader.GetString(1), ReceivingJson.Options)
            ?? throw new InvalidOperationException("REC.IDEMPOTENCY_RESULT_INVALID");
        return new IdempotencyReservation(IdempotencyReservationKind.Replay, result);
    }

    public async Task<ReceiptRegistrationResult> InsertRegistrationAsync(
        ReceiptPlan plan,
        string idempotencyKeyHash,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await InsertReceiptAsync(connection, transaction, plan, cancellationToken);

        var containerResults = new List<ContainerRegistrationResult>(plan.Containers.Count);
        foreach (var container in plan.Containers)
        {
            await InsertContainerAsync(connection, transaction, plan, container, cancellationToken);
            var containerIdentity = await labelIdentityWriter.AllocateAsync(
                plan,
                ReceivingLabelObjectTypes.Container,
                container.Id,
                1,
                "REGISTERED",
                container.LabelOpaqueReference,
                idempotencyKeyHash,
                correlationId,
                cancellationToken);
            var itemResults = new List<ReceivedItemRegistrationResult>(container.Items.Count);
            foreach (var item in container.Items)
            {
                await InsertReceivedItemAsync(connection, transaction, plan, container, item, cancellationToken);
                await InsertStateHistoryAsync(connection, transaction, plan, item, cancellationToken);
                var itemIdentity = await labelIdentityWriter.AllocateAsync(
                    plan,
                    ReceivingLabelObjectTypes.ReceivedItem,
                    item.Id,
                    1,
                    "QUARANTINED",
                    item.LabelOpaqueReference,
                    idempotencyKeyHash,
                    correlationId,
                    cancellationToken);
                await InsertAuditAndOutboxAsync(
                    connection,
                    transaction,
                    plan,
                    item,
                    idempotencyKeyHash,
                    correlationId,
                    cancellationToken);
                itemResults.Add(new ReceivedItemRegistrationResult(
                    item.Id.ToString("N"),
                    item.Number,
                    "QUARANTINED",
                    1)
                {
                    LabelIdentity = ToResult(itemIdentity)
                });
            }

            containerResults.Add(new ContainerRegistrationResult(
                container.Id.ToString("N"),
                container.Number,
                itemResults)
            {
                LabelIdentity = ToResult(containerIdentity)
            });
        }

        await InsertReceiptAuditAndOutboxAsync(
            connection,
            transaction,
            plan,
            idempotencyKeyHash,
            correlationId,
            cancellationToken);
        return new ReceiptRegistrationResult(plan.Id.ToString("N"), plan.Number, 1, containerResults);
    }

    private static LabelIdentityResult ToResult(ReceivingLabelIdentity identity) => new(
        identity.ObjectType,
        identity.BusinessNumber,
        LabelBarcodeCodec.Create(identity.ObjectType, identity.OpaqueReference),
        identity.TemplateVersion);

    public async Task CompleteIdempotencyAsync(
        string organizationGroupId,
        string keyHash,
        Guid receiptId,
        ReceiptRegistrationResult result,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            update receiving.idempotency
            set receipt_id = @receipt_id, response_json = @response_json
            where organization_group_id = @organization_group_id and key_hash = @key_hash
            """, connection, transaction);
        command.Parameters.AddWithValue("receipt_id", receiptId);
        command.Parameters.Add(new NpgsqlParameter("response_json", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(result, ReceivingJson.Options)
        });
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("key_hash", keyHash);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("REC.IDEMPOTENCY_COMPLETION_FAILED");
        }
    }

    private async Task InsertReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReceiptPlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into receiving.receipt (
                id, organization_group_id, receipt_number, legal_entity_id, laboratory_id,
                customer_id, service_order_id, arrival_at, aggregate_version,
                created_at, created_by, updated_at, updated_by
            ) values (
                @id, @organization_group_id, @receipt_number, @legal_entity_id, @laboratory_id,
                @customer_id, @service_order_id, @arrival_at, 1,
                @created_at, @created_by, @created_at, @created_by
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("id", plan.Id);
        command.Parameters.AddWithValue("organization_group_id", plan.OrganizationGroupId);
        command.Parameters.AddWithValue("receipt_number", plan.Number);
        command.Parameters.AddWithValue("legal_entity_id", plan.Request.LegalEntityId);
        command.Parameters.AddWithValue("laboratory_id", plan.Request.LaboratoryId);
        command.Parameters.AddWithValue("customer_id", plan.Request.CustomerId);
        command.Parameters.AddWithValue("service_order_id", plan.Request.ServiceOrderId);
        command.Parameters.AddWithValue("arrival_at", plan.Request.ArrivalAt);
        command.Parameters.AddWithValue("created_at", plan.OccurredAt);
        command.Parameters.AddWithValue("created_by", plan.ActorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertContainerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReceiptPlan plan,
        ContainerPlan container,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into receiving.container (
                id, receipt_id, container_number, ordinal, external_label, package_type,
                condition, seal_observation, created_at, created_by, updated_at, updated_by
            ) values (
                @id, @receipt_id, @container_number, @ordinal, @external_label, @package_type,
                @condition, @seal_observation, @created_at, @created_by, @created_at, @created_by
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("id", container.Id);
        command.Parameters.AddWithValue("receipt_id", plan.Id);
        command.Parameters.AddWithValue("container_number", container.Number);
        command.Parameters.AddWithValue("ordinal", container.Index + 1);
        command.Parameters.AddWithValue("external_label", (object?)container.Request.ExternalLabel ?? DBNull.Value);
        command.Parameters.AddWithValue("package_type", container.Request.PackageType);
        command.Parameters.AddWithValue("condition", container.Request.Condition);
        command.Parameters.AddWithValue("seal_observation", (object?)container.Request.SealObservation ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", plan.OccurredAt);
        command.Parameters.AddWithValue("created_by", plan.ActorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertReceivedItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReceiptPlan plan,
        ContainerPlan container,
        ReceivedItemPlan item,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into receiving.received_item (
                id, container_id, received_item_number, ordinal, declared_description,
                model, batch, serial_number, color, package_condition, seal_condition,
                item_condition, quantity, unit, state, version,
                created_at, created_by, updated_at, updated_by
            ) values (
                @id, @container_id, @received_item_number, @ordinal, @declared_description,
                @model, @batch, @serial_number, @color, @package_condition, @seal_condition,
                @item_condition, @quantity, @unit, 'QUARANTINED', 1,
                @created_at, @created_by, @created_at, @created_by
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("container_id", container.Id);
        command.Parameters.AddWithValue("received_item_number", item.Number);
        command.Parameters.AddWithValue("ordinal", item.ItemIndex + 1);
        command.Parameters.AddWithValue("declared_description", item.Request.DeclaredDescription);
        command.Parameters.AddWithValue("model", item.Request.Model);
        command.Parameters.AddWithValue("batch", item.Request.Batch);
        command.Parameters.AddWithValue("serial_number", (object?)item.Request.SerialNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("color", item.Request.Color);
        command.Parameters.AddWithValue("package_condition", item.Request.PackageCondition);
        command.Parameters.AddWithValue("seal_condition", item.Request.SealCondition);
        command.Parameters.AddWithValue("item_condition", item.Request.ItemCondition);
        command.Parameters.AddWithValue("quantity", item.Request.Quantity);
        command.Parameters.AddWithValue("unit", item.Request.Unit);
        command.Parameters.AddWithValue("created_at", plan.OccurredAt);
        command.Parameters.AddWithValue("created_by", plan.ActorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertStateHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReceiptPlan plan,
        ReceivedItemPlan item,
        CancellationToken cancellationToken)
    {
        foreach (var transition in new[] { (From: (string?)null, To: "REGISTERED", Sequence: 1), (From: "REGISTERED", To: "QUARANTINED", Sequence: 2) })
        {
            await using var command = new NpgsqlCommand("""
                insert into receiving.received_item_state_history (
                    id, received_item_id, sequence, from_state, to_state, occurred_at, actor_id
                ) values (
                    @id, @received_item_id, @sequence, @from_state, @to_state, @occurred_at, @actor_id
                )
                """, connection, transaction);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("received_item_id", item.Id);
            command.Parameters.AddWithValue("sequence", transition.Sequence);
            command.Parameters.AddWithValue("from_state", (object?)transition.From ?? DBNull.Value);
            command.Parameters.AddWithValue("to_state", transition.To);
            command.Parameters.AddWithValue("occurred_at", plan.OccurredAt);
            command.Parameters.AddWithValue("actor_id", plan.ActorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static Task InsertReceiptAuditAndOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReceiptPlan plan,
        string keyHash,
        string correlationId,
        CancellationToken cancellationToken) =>
        InsertAuditAndOutboxPairAsync(
            connection,
            transaction,
            plan,
            "Receipt",
            plan.Id,
            "RECEIPT_REGISTERED",
            keyHash,
            correlationId,
            JsonSerializer.Serialize(new { plan.Number, aggregateVersion = 1 }, ReceivingJson.Options),
            cancellationToken);

    private static Task InsertAuditAndOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReceiptPlan plan,
        ReceivedItemPlan item,
        string keyHash,
        string correlationId,
        CancellationToken cancellationToken) =>
        InsertAuditAndOutboxPairAsync(
            connection,
            transaction,
            plan,
            "ReceivedItem",
            item.Id,
            "RECEIVED_ITEM_QUARANTINED",
            keyHash,
            correlationId,
            JsonSerializer.Serialize(new { item.Number, state = "QUARANTINED", version = 1 }, ReceivingJson.Options),
            cancellationToken);

    internal static async Task InsertAuditAndOutboxPairAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReceiptPlan plan,
        string objectType,
        Guid objectId,
        string eventType,
        string keyHash,
        string correlationId,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.NewGuid();
        await using var audit = new NpgsqlCommand("""
            insert into receiving.audit_pending (
                id, event_type, actor_id, organization_group_id, legal_entity_id,
                laboratory_id, customer_id, service_order_id, object_type, object_id,
                correlation_id, idempotency_key_hash, occurred_at, payload_json
            ) values (
                @id, @event_type, @actor_id, @organization_group_id, @legal_entity_id,
                @laboratory_id, @customer_id, @service_order_id, @object_type, @object_id,
                @correlation_id, @idempotency_key_hash, @occurred_at, @payload_json
            )
            """, connection, transaction);
        audit.Parameters.AddWithValue("id", eventId);
        audit.Parameters.AddWithValue("event_type", eventType);
        audit.Parameters.AddWithValue("actor_id", plan.ActorId);
        audit.Parameters.AddWithValue("organization_group_id", plan.OrganizationGroupId);
        audit.Parameters.AddWithValue("legal_entity_id", plan.Request.LegalEntityId);
        audit.Parameters.AddWithValue("laboratory_id", plan.Request.LaboratoryId);
        audit.Parameters.AddWithValue("customer_id", plan.Request.CustomerId);
        audit.Parameters.AddWithValue("service_order_id", plan.Request.ServiceOrderId);
        audit.Parameters.AddWithValue("object_type", objectType);
        audit.Parameters.AddWithValue("object_id", objectId);
        audit.Parameters.AddWithValue("correlation_id", correlationId);
        audit.Parameters.AddWithValue("idempotency_key_hash", keyHash);
        audit.Parameters.AddWithValue("occurred_at", plan.OccurredAt);
        audit.Parameters.Add(new NpgsqlParameter("payload_json", NpgsqlDbType.Jsonb) { Value = payloadJson });
        await audit.ExecuteNonQueryAsync(cancellationToken);

        await using var outbox = new NpgsqlCommand("""
            insert into receiving.outbox (
                id, event_type, aggregate_type, aggregate_id, occurred_at, payload_json
            ) values (
                @id, @event_type, @aggregate_type, @aggregate_id, @occurred_at, @payload_json
            )
            """, connection, transaction);
        outbox.Parameters.AddWithValue("id", eventId);
        outbox.Parameters.AddWithValue("event_type", eventType);
        outbox.Parameters.AddWithValue("aggregate_type", objectType);
        outbox.Parameters.AddWithValue("aggregate_id", objectId);
        outbox.Parameters.AddWithValue("occurred_at", plan.OccurredAt);
        outbox.Parameters.Add(new NpgsqlParameter("payload_json", NpgsqlDbType.Jsonb) { Value = payloadJson });
        await outbox.ExecuteNonQueryAsync(cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
        {
            throw new InvalidOperationException("REC.TRANSACTION_REQUIRED");
        }

        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class ReceivingAttemptAuditWriter(ReceivingDataSource dataSource)
{
    public async Task WriteAsync(
        string actorId,
        string organizationGroupId,
        string targetHash,
        string correlationId,
        string decisionCode,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await WriteAsync(
            "RegisterReceipt",
            actorId,
            organizationGroupId,
            targetHash,
            correlationId,
            decisionCode,
            occurredAt,
            cancellationToken);
    }

    public async Task WriteAsync(
        string commandType,
        string? actorId,
        string organizationGroupId,
        string targetHash,
        string correlationId,
        string decisionCode,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.DataSource.CreateCommand("""
            insert into receiving.audit_attempt (
                attempt_id, actor_id, organization_group_id, command_type, target_hash,
                decision_code, correlation_id, occurred_at
            ) values (
                @attempt_id, @actor_id, @organization_group_id, @command_type, @target_hash,
                @decision_code, @correlation_id, @occurred_at
            )
            """);
        command.Parameters.AddWithValue("attempt_id", Guid.NewGuid());
        command.Parameters.AddWithValue("actor_id", (object?)actorId ?? DBNull.Value);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("command_type", commandType);
        command.Parameters.AddWithValue("target_hash", targetHash);
        command.Parameters.AddWithValue("decision_code", decisionCode);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
