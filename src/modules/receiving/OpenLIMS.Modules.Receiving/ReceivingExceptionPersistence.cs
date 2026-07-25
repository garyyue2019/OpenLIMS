using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal sealed class ReceivingExceptionStore(
    IPostgresTransactionAccessor transactionAccessor,
    IdentityAssessmentStore identityStore)
{
    public Task<IdentityItemScope?> LoadItemAsync(
        string organizationGroupId,
        string receivedItemId,
        bool forUpdate,
        CancellationToken cancellationToken) =>
        identityStore.LoadItemAsync(organizationGroupId, receivedItemId, forUpdate, cancellationToken);

    public async Task<string?> LoadAssessmentStateAsync(Guid receivedItemId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select assessment_state from receiving.identity_assessment
            where received_item_id = @received_item_id
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<ReceivingExceptionScope?> LoadScopeAsync(
        string organizationGroupId,
        string exceptionId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(exceptionId, out var parsedId)) return null;
        var (connection, transaction) = RequireTransaction();
        var lockClause = forUpdate ? " for update of s, i" : string.Empty;
        await using var command = new NpgsqlCommand("""
            select e.exception_id, e.type, e.severity, e.created_by,
                   s.status, s.version,
                   i.id, i.received_item_number, i.version, i.state,
                   r.organization_group_id, r.legal_entity_id, r.laboratory_id,
                   r.customer_id, r.service_order_id, i.declared_description,
                   i.model, i.batch, i.serial_number, i.color
            from receiving.receiving_exception e
            join receiving.receiving_exception_state s on s.exception_id = e.exception_id
            join receiving.received_item i on i.id = e.received_item_id
            join receiving.container c on c.id = i.container_id
            join receiving.receipt r on r.id = c.receipt_id
            where r.organization_group_id = @organization_group_id
              and e.exception_id = @exception_id
            """ + lockClause, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("exception_id", parsedId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var item = new IdentityItemScope(
            reader.GetGuid(6), reader.GetString(7), reader.GetInt64(8), reader.GetString(9),
            reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13),
            reader.GetString(14), reader.GetString(15), reader.GetString(15), reader.GetString(16),
            reader.GetString(17), reader.IsDBNull(18) ? null : reader.GetString(18), reader.GetString(19));
        return new ReceivingExceptionScope(
            item, reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetInt64(5));
    }

    public async Task<ReceivingExceptionResult> InsertAsync(
        IdentityItemScope item,
        CreateReceivingExceptionRequest request,
        string severity,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var exceptionId = Guid.NewGuid();
        await using (var command = new NpgsqlCommand("""
            insert into receiving.receiving_exception (
                exception_id, received_item_id, item_version, type, severity, description,
                observed_at, evidence_refs, evidence_hashes, created_by, created_at
            ) values (
                @exception_id, @received_item_id, @item_version, @type, @severity, @description,
                @observed_at, @evidence_refs, @evidence_hashes, @created_by, @created_at
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("exception_id", exceptionId);
            command.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
            command.Parameters.AddWithValue("item_version", item.ItemVersion);
            command.Parameters.AddWithValue("type", request.Type);
            command.Parameters.AddWithValue("severity", severity);
            command.Parameters.AddWithValue("description", request.Description.Trim());
            command.Parameters.AddWithValue("observed_at", request.ObservedAt);
            AddJson(command, "evidence_refs", request.EvidenceRefs);
            AddJson(command, "evidence_hashes", NormalizeHashes(request.EvidenceHashes));
            command.Parameters.AddWithValue("created_by", actorId);
            command.Parameters.AddWithValue("created_at", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var state = new NpgsqlCommand("""
            insert into receiving.receiving_exception_state (
                exception_id, status, version, current_decision_version, updated_at, updated_by
            ) values (@exception_id, 'OPEN', 1, null, @updated_at, @updated_by)
            """, connection, transaction))
        {
            state.Parameters.AddWithValue("exception_id", exceptionId);
            state.Parameters.AddWithValue("updated_at", now);
            state.Parameters.AddWithValue("updated_by", actorId);
            await state.ExecuteNonQueryAsync(cancellationToken);
        }

        var newItemVersion = await AdvanceItemVersionAsync(item, actorId, now, cancellationToken);
        await WriteAuditAndOutboxAsync(
            item, actorId, "RECEIVING_EXCEPTION_RECORDED", correlationId, now,
            JsonSerializer.Serialize(new
            {
                exceptionId,
                request.Type,
                severity,
                exceptionVersion = 1,
                beforeItemVersion = item.ItemVersion,
                itemVersion = newItemVersion,
                state = "QUARANTINED",
                correlationId,
                evidenceHashes = NormalizeHashes(request.EvidenceHashes)
            }, ReceivingJson.Options),
            cancellationToken);

        var scope = new ReceivingExceptionScope(
            item with { ItemVersion = newItemVersion }, exceptionId, request.Type, severity,
            actorId, ReceivingExceptionStatuses.Open, 1);
        return await LoadResultAsync(scope, cancellationToken);
    }

    public async Task<ReceivingExceptionResult> InsertDecisionAsync(
        ReceivingExceptionScope scope,
        SubmitReceivingExceptionDecisionRequest request,
        string status,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var decisionVersion = await NextDecisionVersionAsync(scope.ExceptionId, cancellationToken);
        var decisionId = Guid.NewGuid();
        await using (var command = new NpgsqlCommand("""
            insert into receiving.receiving_exception_decision (
                decision_id, exception_id, version, expected_exception_version, decision_type,
                matrix_version, allowed_actions, prohibited_actions, valid_until,
                evidence_refs, evidence_hashes, technical_impact, rationale, decided_at, decided_by
            ) values (
                @decision_id, @exception_id, @version, @expected_exception_version, @decision_type,
                @matrix_version, @allowed_actions, @prohibited_actions, @valid_until,
                @evidence_refs, @evidence_hashes, @technical_impact, @rationale, @decided_at, @decided_by
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("decision_id", decisionId);
            command.Parameters.AddWithValue("exception_id", scope.ExceptionId);
            command.Parameters.AddWithValue("version", decisionVersion);
            command.Parameters.AddWithValue("expected_exception_version", request.ExpectedVersion);
            command.Parameters.AddWithValue("decision_type", request.DecisionType);
            command.Parameters.AddWithValue("matrix_version", request.MatrixVersion);
            AddJson(command, "allowed_actions", request.AllowedActions ?? []);
            AddJson(command, "prohibited_actions", request.ProhibitedActions ?? []);
            command.Parameters.AddWithValue("valid_until", (object?)request.ValidUntil ?? DBNull.Value);
            AddJson(command, "evidence_refs", request.EvidenceRefs);
            AddJson(command, "evidence_hashes", NormalizeHashes(request.EvidenceHashes));
            command.Parameters.AddWithValue("technical_impact", request.TechnicalImpact?.Trim() ?? string.Empty);
            command.Parameters.AddWithValue("rationale", request.Rationale.Trim());
            command.Parameters.AddWithValue("decided_at", now);
            command.Parameters.AddWithValue("decided_by", actorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var projection = new NpgsqlCommand("""
            update receiving.receiving_exception_state
            set status = @status, version = version + 1,
                current_decision_version = @decision_version,
                updated_at = @updated_at, updated_by = @updated_by
            where exception_id = @exception_id and version = @expected_version
            """, connection, transaction))
        {
            projection.Parameters.AddWithValue("status", status);
            projection.Parameters.AddWithValue("decision_version", decisionVersion);
            projection.Parameters.AddWithValue("updated_at", now);
            projection.Parameters.AddWithValue("updated_by", actorId);
            projection.Parameters.AddWithValue("exception_id", scope.ExceptionId);
            projection.Parameters.AddWithValue("expected_version", request.ExpectedVersion);
            if (await projection.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new ReceivingDomainException(ReceivingErrorCodes.ExpectedVersionConflict);
            }
        }

        var newItemVersion = await AdvanceItemVersionAsync(scope.Item, actorId, now, cancellationToken);
        await WriteAuditAndOutboxAsync(
            scope.Item, actorId, $"RECEIVING_EXCEPTION_{request.DecisionType}", correlationId, now,
            JsonSerializer.Serialize(new
            {
                exceptionId = scope.ExceptionId,
                decisionId,
                decisionVersion,
                request.DecisionType,
                request.MatrixVersion,
                status,
                beforeItemVersion = scope.Item.ItemVersion,
                itemVersion = newItemVersion,
                state = "QUARANTINED",
                correlationId,
                request.AllowedActions,
                request.ProhibitedActions,
                request.ValidUntil,
                evidenceHashes = NormalizeHashes(request.EvidenceHashes)
            }, ReceivingJson.Options),
            cancellationToken);

        return await LoadResultAsync(
            scope with
            {
                Item = scope.Item with { ItemVersion = newItemVersion },
                Status = status,
                Version = scope.Version + 1
            }, cancellationToken);
    }

    public async Task<ReceivingExceptionResult> LoadResultAsync(
        ReceivingExceptionScope scope,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        string description;
        DateTimeOffset observedAt;
        IReadOnlyList<string> evidenceRefs;
        IReadOnlyList<string> evidenceHashes;
        DateTimeOffset createdAt;
        await using (var command = new NpgsqlCommand("""
            select description, observed_at, evidence_refs, evidence_hashes, created_at
            from receiving.receiving_exception where exception_id = @exception_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("exception_id", scope.ExceptionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("REC.EXCEPTION_FACT_MISSING");
            description = reader.GetString(0);
            observedAt = reader.GetFieldValue<DateTimeOffset>(1);
            evidenceRefs = DeserializeArray(reader.GetString(2));
            evidenceHashes = DeserializeArray(reader.GetString(3));
            createdAt = reader.GetFieldValue<DateTimeOffset>(4);
        }

        var decisions = await LoadDecisionsAsync(scope.ExceptionId, cancellationToken);
        return new ReceivingExceptionResult(
            scope.ExceptionId.ToString("N"), scope.Item.ReceivedItemId.ToString("N"),
            scope.Item.ReceivedItemNumber, scope.Item.ItemVersion, scope.Item.CurrentState,
            scope.Type, scope.Severity, description, observedAt, evidenceRefs, evidenceHashes,
            scope.CreatedBy, createdAt, scope.Status, scope.Version, decisions);
    }

    public Task WriteReadAuditAsync(
        ReceivingExceptionScope scope,
        string actorId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        WriteAuditAsync(
            scope.Item, actorId, "RECEIVING_EXCEPTION_VIEWED", correlationId, now,
            JsonSerializer.Serialize(new
            {
                exceptionId = scope.ExceptionId,
                exceptionVersion = scope.Version,
                scope.Status,
                itemVersion = scope.Item.ItemVersion
            }, ReceivingJson.Options), null, cancellationToken);

    private async Task<IReadOnlyList<ReceivingExceptionDecisionResult>> LoadDecisionsAsync(
        Guid exceptionId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select decision_id, version, decision_type, allowed_actions, prohibited_actions,
                   valid_until, evidence_refs, evidence_hashes, technical_impact, rationale,
                   matrix_version, decided_at, decided_by
            from receiving.receiving_exception_decision
            where exception_id = @exception_id order by version
            """, connection, transaction);
        command.Parameters.AddWithValue("exception_id", exceptionId);
        var results = new List<ReceivingExceptionDecisionResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ReceivingExceptionDecisionResult(
                reader.GetGuid(0).ToString("N"), reader.GetInt64(1), reader.GetString(2),
                DeserializeArray(reader.GetString(3)), DeserializeArray(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                DeserializeArray(reader.GetString(6)), DeserializeArray(reader.GetString(7)),
                reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11), reader.GetString(12)));
        }
        return results;
    }

    private async Task<long> NextDecisionVersionAsync(Guid exceptionId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select coalesce(max(version), 0) + 1
            from receiving.receiving_exception_decision where exception_id = @exception_id
            """, connection, transaction);
        command.Parameters.AddWithValue("exception_id", exceptionId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<long> AdvanceItemVersionAsync(
        IdentityItemScope item,
        string actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            update receiving.received_item
            set version = version + 1, updated_at = @updated_at, updated_by = @updated_by
            where id = @received_item_id and version = @expected_version and state = 'QUARANTINED'
            returning version
            """, connection, transaction);
        command.Parameters.AddWithValue("updated_at", now);
        command.Parameters.AddWithValue("updated_by", actorId);
        command.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
        command.Parameters.AddWithValue("expected_version", item.ItemVersion);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null) throw new ReceivingDomainException(ReceivingErrorCodes.ExpectedVersionConflict);
        var version = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

        await using var identity = new NpgsqlCommand("""
            update receiving.label_identity set object_version = @object_version, object_state = 'QUARANTINED'
            where organization_group_id = @organization_group_id
              and object_type = 'RI' and object_id = @received_item_id
            """, connection, transaction);
        identity.Parameters.AddWithValue("object_version", version);
        identity.Parameters.AddWithValue("organization_group_id", item.OrganizationGroupId);
        identity.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
        if (await identity.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new ReceivingDomainException(ReceivingErrorCodes.ReceivingPortUnavailable);
        return version;
    }

    private async Task WriteAuditAndOutboxAsync(
        IdentityItemScope item,
        string actorId,
        string eventType,
        string correlationId,
        DateTimeOffset occurredAt,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.NewGuid();
        await WriteAuditAsync(item, actorId, eventType, correlationId, occurredAt, payloadJson, eventId, cancellationToken);
        var (connection, transaction) = RequireTransaction();
        await using var outbox = new NpgsqlCommand("""
            insert into receiving.outbox (id, event_type, aggregate_type, aggregate_id, occurred_at, payload_json)
            values (@id, @event_type, 'ReceivedItem', @aggregate_id, @occurred_at, @payload_json)
            """, connection, transaction);
        outbox.Parameters.AddWithValue("id", eventId);
        outbox.Parameters.AddWithValue("event_type", eventType);
        outbox.Parameters.AddWithValue("aggregate_id", item.ReceivedItemId);
        outbox.Parameters.AddWithValue("occurred_at", occurredAt);
        AddJson(outbox, "payload_json", payloadJson, alreadySerialized: true);
        await outbox.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task WriteAuditAsync(
        IdentityItemScope item,
        string actorId,
        string eventType,
        string correlationId,
        DateTimeOffset occurredAt,
        string payloadJson,
        Guid? eventId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var audit = new NpgsqlCommand("""
            insert into receiving.audit_pending (
                id, event_type, actor_id, organization_group_id, legal_entity_id,
                laboratory_id, customer_id, service_order_id, object_type, object_id,
                correlation_id, idempotency_key_hash, occurred_at, payload_json
            ) values (
                @id, @event_type, @actor_id, @organization_group_id, @legal_entity_id,
                @laboratory_id, @customer_id, @service_order_id, 'ReceivedItem', @object_id,
                @correlation_id, @key_hash, @occurred_at, @payload_json
            )
            """, connection, transaction);
        audit.Parameters.AddWithValue("id", eventId ?? Guid.NewGuid());
        audit.Parameters.AddWithValue("event_type", eventType);
        audit.Parameters.AddWithValue("actor_id", actorId);
        audit.Parameters.AddWithValue("organization_group_id", item.OrganizationGroupId);
        audit.Parameters.AddWithValue("legal_entity_id", item.LegalEntityId);
        audit.Parameters.AddWithValue("laboratory_id", item.LaboratoryId);
        audit.Parameters.AddWithValue("customer_id", item.CustomerId);
        audit.Parameters.AddWithValue("service_order_id", item.ServiceOrderId);
        audit.Parameters.AddWithValue("object_id", item.ReceivedItemId);
        audit.Parameters.AddWithValue("correlation_id", correlationId);
        audit.Parameters.AddWithValue("key_hash", ReceivingRules.Hash($"{eventType}:{correlationId}"));
        audit.Parameters.AddWithValue("occurred_at", occurredAt);
        AddJson(audit, "payload_json", payloadJson, alreadySerialized: true);
        await audit.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<string> NormalizeHashes(IEnumerable<string> hashes) =>
        hashes.Select(value => value.ToLowerInvariant()).ToArray();

    private static IReadOnlyList<string> DeserializeArray(string json) =>
        JsonSerializer.Deserialize<string[]>(json, ReceivingJson.Options)
        ?? throw new InvalidOperationException("REC.EXCEPTION_JSON_INVALID");

    private static void AddJson(NpgsqlCommand command, string name, object value, bool alreadySerialized = false) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = alreadySerialized ? value : JsonSerializer.Serialize(value, ReceivingJson.Options)
        });

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("REC.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}
