using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal sealed class IdentityAssessmentStore(IPostgresTransactionAccessor transactionAccessor)
{
    public async Task<IdentityItemScope?> LoadItemAsync(
        string organizationGroupId,
        string receivedItemId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(receivedItemId, out var itemId))
        {
            return null;
        }

        var (connection, transaction) = RequireTransaction();
        var lockClause = forUpdate ? " for update of i" : string.Empty;
        await using var command = new NpgsqlCommand("""
            select i.id, i.received_item_number, i.version, i.state,
                   r.organization_group_id, r.legal_entity_id, r.laboratory_id,
                   r.customer_id, r.service_order_id, i.declared_description,
                   i.model, i.batch, i.serial_number, i.color
            from receiving.received_item i
            join receiving.container c on c.id = i.container_id
            join receiving.receipt r on r.id = c.receipt_id
            where r.organization_group_id = @organization_group_id and i.id = @item_id
            """ + lockClause, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new IdentityItemScope(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetString(13));
    }

    public async Task<IdentityAssessmentResult> LoadAssessmentAsync(
        IdentityItemScope item,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var assessmentState = IdentityAssessmentStates.NotStarted;
        long assessmentVersion = 0;
        await using (var assessment = new NpgsqlCommand("""
            select assessment_state, assessment_version
            from receiving.identity_assessment
            where received_item_id = @received_item_id
            """, connection, transaction))
        {
            assessment.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
            await using var reader = await assessment.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                assessmentState = reader.GetString(0);
                assessmentVersion = reader.GetInt64(1);
            }
        }

        var declaration = await LoadLatestDeclarationAsync(item.ReceivedItemId, cancellationToken);
        var observations = await LoadObservationsAsync(item.ReceivedItemId, cancellationToken);
        var decisions = await LoadDecisionsAsync(item.ReceivedItemId, cancellationToken);
        return new IdentityAssessmentResult(
            item.ReceivedItemId.ToString("N"),
            item.ReceivedItemNumber,
            item.CurrentState,
            item.ItemVersion,
            assessmentState,
            assessmentVersion,
            declaration,
            observations,
            decisions);
    }

    public async Task<IdentityAssessmentResult> InsertObservationAsync(
        IdentityItemScope item,
        CreateIdentityObservationRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var declaration = await EnsureDeclarationSnapshotAsync(item, actorId, now, cancellationToken);
        var observationVersion = await NextVersionAsync(
            connection,
            transaction,
            "receiving.identity_observation",
            item.ReceivedItemId,
            cancellationToken);
        var observationId = Guid.NewGuid();
        await using (var command = new NpgsqlCommand("""
            insert into receiving.identity_observation (
                observation_id, received_item_id, version, expected_item_version,
                observed_labels, observed_model, observed_batch, appearance,
                attachment_refs, attachment_hashes, observed_at, observed_by
            ) values (
                @observation_id, @received_item_id, @version, @expected_item_version,
                @observed_labels, @observed_model, @observed_batch, @appearance,
                @attachment_refs, @attachment_hashes, @observed_at, @observed_by
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("observation_id", observationId);
            command.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
            command.Parameters.AddWithValue("version", observationVersion);
            command.Parameters.AddWithValue("expected_item_version", request.ExpectedItemVersion);
            command.Parameters.Add(new NpgsqlParameter("observed_labels", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.ObservedLabels, ReceivingJson.Options) });
            command.Parameters.AddWithValue("observed_model", request.ObservedModel.Trim());
            command.Parameters.AddWithValue("observed_batch", request.ObservedBatch.Trim());
            command.Parameters.AddWithValue("appearance", request.Appearance.Trim());
            command.Parameters.Add(new NpgsqlParameter("attachment_refs", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.AttachmentRefs, ReceivingJson.Options) });
            command.Parameters.Add(new NpgsqlParameter("attachment_hashes", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.AttachmentHashes.Select(value => value.ToLowerInvariant()), ReceivingJson.Options) });
            command.Parameters.AddWithValue("observed_at", now);
            command.Parameters.AddWithValue("observed_by", actorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var assessmentVersion = await NextAssessmentVersionAsync(item.ReceivedItemId, cancellationToken);
        await using (var projection = new NpgsqlCommand("""
            insert into receiving.identity_assessment (
                received_item_id, assessment_state, assessment_version,
                declaration_snapshot_version, current_observation_version,
                current_decision_version, updated_at, updated_by
            ) values (
                @received_item_id, 'IN_PROGRESS', @assessment_version,
                @declaration_snapshot_version, @observation_version,
                null, @updated_at, @updated_by
            )
            on conflict (received_item_id) do update set
                assessment_state = excluded.assessment_state,
                assessment_version = excluded.assessment_version,
                declaration_snapshot_version = excluded.declaration_snapshot_version,
                current_observation_version = excluded.current_observation_version,
                current_decision_version = null,
                updated_at = excluded.updated_at,
                updated_by = excluded.updated_by
            """, connection, transaction))
        {
            projection.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
            projection.Parameters.AddWithValue("assessment_version", assessmentVersion);
            projection.Parameters.AddWithValue("declaration_snapshot_version", declaration.SnapshotVersion);
            projection.Parameters.AddWithValue("observation_version", observationVersion);
            projection.Parameters.AddWithValue("updated_at", now);
            projection.Parameters.AddWithValue("updated_by", actorId);
            await projection.ExecuteNonQueryAsync(cancellationToken);
        }

        var newItemVersion = await AdvanceItemVersionAsync(item, actorId, now, cancellationToken);
        await WriteAuditAndOutboxAsync(
            item,
            actorId,
            "IDENTITY_OBSERVATION_RECORDED",
            correlationId,
            now,
            JsonSerializer.Serialize(new
            {
                observationId,
                observationVersion,
                declarationSnapshotVersion = declaration.SnapshotVersion,
                beforeItemVersion = item.ItemVersion,
                itemVersion = newItemVersion,
                state = "QUARANTINED",
                correlationId,
                attachmentHashes = request.AttachmentHashes.Select(value => value.ToLowerInvariant())
            }, ReceivingJson.Options),
            cancellationToken);

        var updatedItem = item with { ItemVersion = newItemVersion };
        return await LoadAssessmentAsync(updatedItem, cancellationToken);
    }

    public async Task<(IdentityDeclarationSnapshotResult Declaration, IdentityObservationResult Observation)?>
        LoadDecisionEvidenceAsync(
            Guid receivedItemId,
            long snapshotVersion,
            long observationVersion,
            CancellationToken cancellationToken)
    {
        var declaration = await LoadDeclarationAsync(receivedItemId, snapshotVersion, cancellationToken);
        var observation = await LoadObservationAsync(receivedItemId, observationVersion, cancellationToken);
        return declaration is null || observation is null ? null : (declaration, observation);
    }

    public async Task<IdentityAssessmentResult> InsertDecisionAsync(
        IdentityItemScope item,
        SubmitIdentityDecisionRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var decisionVersion = await NextVersionAsync(
            connection,
            transaction,
            "receiving.identity_decision",
            item.ReceivedItemId,
            cancellationToken);
        var decisionId = Guid.NewGuid();
        await using (var command = new NpgsqlCommand("""
            insert into receiving.identity_decision (
                decision_id, received_item_id, version, observation_version,
                declaration_snapshot_version, outcome, reason_code, rationale,
                rule_set_version, decided_at, decided_by
            ) values (
                @decision_id, @received_item_id, @version, @observation_version,
                @declaration_snapshot_version, @outcome, @reason_code, @rationale,
                @rule_set_version, @decided_at, @decided_by
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("decision_id", decisionId);
            command.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
            command.Parameters.AddWithValue("version", decisionVersion);
            command.Parameters.AddWithValue("observation_version", request.ObservationVersion);
            command.Parameters.AddWithValue("declaration_snapshot_version", request.DeclarationSnapshotVersion);
            command.Parameters.AddWithValue("outcome", request.Outcome);
            command.Parameters.AddWithValue("reason_code", request.ReasonCode.Trim());
            command.Parameters.AddWithValue("rationale", request.Rationale.Trim());
            command.Parameters.AddWithValue("rule_set_version", request.RuleSetVersion);
            command.Parameters.AddWithValue("decided_at", now);
            command.Parameters.AddWithValue("decided_by", actorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var assessmentVersion = await NextAssessmentVersionAsync(item.ReceivedItemId, cancellationToken);
        await using (var projection = new NpgsqlCommand("""
            update receiving.identity_assessment
            set assessment_state = @assessment_state,
                assessment_version = @assessment_version,
                current_decision_version = @decision_version,
                updated_at = @updated_at,
                updated_by = @updated_by
            where received_item_id = @received_item_id
              and current_observation_version = @observation_version
              and declaration_snapshot_version = @declaration_snapshot_version
            """, connection, transaction))
        {
            projection.Parameters.AddWithValue("assessment_state", request.Outcome);
            projection.Parameters.AddWithValue("assessment_version", assessmentVersion);
            projection.Parameters.AddWithValue("decision_version", decisionVersion);
            projection.Parameters.AddWithValue("updated_at", now);
            projection.Parameters.AddWithValue("updated_by", actorId);
            projection.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
            projection.Parameters.AddWithValue("observation_version", request.ObservationVersion);
            projection.Parameters.AddWithValue("declaration_snapshot_version", request.DeclarationSnapshotVersion);
            if (await projection.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new ReceivingDomainException(ReceivingErrorCodes.ExpectedVersionConflict);
            }
        }

        var newItemVersion = await AdvanceItemVersionAsync(item, actorId, now, cancellationToken);
        var eventType = request.Outcome switch
        {
            IdentityDecisionOutcomes.Matched => "IDENTITY_MATCHED",
            IdentityDecisionOutcomes.Mismatched => "IDENTITY_MISMATCHED",
            _ => "IDENTITY_INDETERMINATE"
        };
        await WriteAuditAndOutboxAsync(
            item,
            actorId,
            eventType,
            correlationId,
            now,
            JsonSerializer.Serialize(new
            {
                decisionId,
                decisionVersion,
                observationVersion = request.ObservationVersion,
                declarationSnapshotVersion = request.DeclarationSnapshotVersion,
                request.Outcome,
                request.ReasonCode,
                request.RuleSetVersion,
                beforeItemVersion = item.ItemVersion,
                itemVersion = newItemVersion,
                state = "QUARANTINED",
                correlationId
            }, ReceivingJson.Options),
            cancellationToken);

        var updatedItem = item with { ItemVersion = newItemVersion };
        return await LoadAssessmentAsync(updatedItem, cancellationToken);
    }

    public Task WriteReadAuditAsync(
        IdentityItemScope item,
        string actorId,
        string eventType,
        string correlationId,
        DateTimeOffset now,
        string payloadJson,
        CancellationToken cancellationToken) =>
        WriteAuditAsync(item, actorId, eventType, correlationId, now, payloadJson, null, cancellationToken);

    private async Task<IdentityDeclarationSnapshotResult> EnsureDeclarationSnapshotAsync(
        IdentityItemScope item,
        string actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using (var command = new NpgsqlCommand("""
            insert into receiving.identity_declaration_snapshot (
                received_item_id, snapshot_version, item_version, declared_description,
                model, batch, serial_number, color, captured_at, captured_by
            ) values (
                @received_item_id, 1, @item_version, @declared_description,
                @model, @batch, @serial_number, @color, @captured_at, @captured_by
            )
            on conflict (received_item_id, snapshot_version) do nothing
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
            command.Parameters.AddWithValue("item_version", item.ItemVersion);
            command.Parameters.AddWithValue("declared_description", item.DeclaredDescription);
            command.Parameters.AddWithValue("model", item.Model);
            command.Parameters.AddWithValue("batch", item.Batch);
            command.Parameters.AddWithValue("serial_number", (object?)item.SerialNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("color", item.Color);
            command.Parameters.AddWithValue("captured_at", now);
            command.Parameters.AddWithValue("captured_by", actorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return await LoadDeclarationAsync(item.ReceivedItemId, 1, cancellationToken)
            ?? throw new InvalidOperationException("REC.IDENTITY_DECLARATION_MISSING");
    }

    private async Task<IdentityDeclarationSnapshotResult?> LoadLatestDeclarationAsync(
        Guid receivedItemId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select snapshot_version
            from receiving.identity_declaration_snapshot
            where received_item_id = @received_item_id
            order by snapshot_version desc
            limit 1
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is long version
            ? await LoadDeclarationAsync(receivedItemId, version, cancellationToken)
            : null;
    }

    private async Task<IdentityDeclarationSnapshotResult?> LoadDeclarationAsync(
        Guid receivedItemId,
        long snapshotVersion,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select snapshot_version, item_version, declared_description, model, batch,
                   serial_number, color, captured_at
            from receiving.identity_declaration_snapshot
            where received_item_id = @received_item_id and snapshot_version = @snapshot_version
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        command.Parameters.AddWithValue("snapshot_version", snapshotVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new IdentityDeclarationSnapshotResult(
            receivedItemId.ToString("N"),
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7));
    }

    private async Task<IReadOnlyList<IdentityObservationResult>> LoadObservationsAsync(
        Guid receivedItemId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select observation_id, version, expected_item_version, observed_labels,
                   observed_model, observed_batch, appearance, attachment_refs,
                   attachment_hashes, observed_at, observed_by
            from receiving.identity_observation
            where received_item_id = @received_item_id
            order by version
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        var results = new List<IdentityObservationResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadObservation(receivedItemId, reader));
        }

        return results;
    }

    private async Task<IdentityObservationResult?> LoadObservationAsync(
        Guid receivedItemId,
        long version,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select observation_id, version, expected_item_version, observed_labels,
                   observed_model, observed_batch, appearance, attachment_refs,
                   attachment_hashes, observed_at, observed_by
            from receiving.identity_observation
            where received_item_id = @received_item_id and version = @version
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        command.Parameters.AddWithValue("version", version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadObservation(receivedItemId, reader) : null;
    }

    private static IdentityObservationResult ReadObservation(Guid receivedItemId, NpgsqlDataReader reader) => new(
        reader.GetGuid(0).ToString("N"),
        reader.GetInt64(1),
        reader.GetInt64(2),
        DeserializeArray(reader.GetString(3)),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        DeserializeArray(reader.GetString(7)),
        DeserializeArray(reader.GetString(8)),
        reader.GetFieldValue<DateTimeOffset>(9),
        reader.GetString(10));

    private async Task<IReadOnlyList<IdentityDecisionResult>> LoadDecisionsAsync(
        Guid receivedItemId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select decision_id, version, observation_version, declaration_snapshot_version,
                   outcome, reason_code, rationale, rule_set_version, decided_at, decided_by
            from receiving.identity_decision
            where received_item_id = @received_item_id
            order by version
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        var results = new List<IdentityDecisionResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new IdentityDecisionResult(
                reader.GetGuid(0).ToString("N"),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetString(9)));
        }

        return results;
    }

    private async Task<long> NextAssessmentVersionAsync(Guid receivedItemId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select coalesce(assessment_version, 0) + 1
            from receiving.identity_assessment
            right join (select @received_item_id::uuid as received_item_id) source using (received_item_id)
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<long> NextVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tableName,
        Guid receivedItemId,
        CancellationToken cancellationToken)
    {
        var allowed = tableName is "receiving.identity_observation" or "receiving.identity_decision";
        if (!allowed) throw new ArgumentOutOfRangeException(nameof(tableName));
        await using var command = new NpgsqlCommand(
            $"select coalesce(max(version), 0) + 1 from {tableName} where received_item_id = @received_item_id",
            connection,
            transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<long> AdvanceItemVersionAsync(
        IdentityItemScope item,
        string actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        long newVersion;
        await using (var command = new NpgsqlCommand("""
            update receiving.received_item
            set version = version + 1, updated_at = @updated_at, updated_by = @updated_by
            where id = @received_item_id and version = @expected_version and state = 'QUARANTINED'
            returning version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("updated_at", now);
            command.Parameters.AddWithValue("updated_by", actorId);
            command.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
            command.Parameters.AddWithValue("expected_version", item.ItemVersion);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is null)
            {
                throw new ReceivingDomainException(ReceivingErrorCodes.ExpectedVersionConflict);
            }

            newVersion = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        await using var identity = new NpgsqlCommand("""
            update receiving.label_identity
            set object_version = @object_version, object_state = 'QUARANTINED'
            where organization_group_id = @organization_group_id
              and object_type = 'RI' and object_id = @received_item_id
            """, connection, transaction);
        identity.Parameters.AddWithValue("object_version", newVersion);
        identity.Parameters.AddWithValue("organization_group_id", item.OrganizationGroupId);
        identity.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
        if (await identity.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ReceivingPortUnavailable);
        }

        return newVersion;
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
            insert into receiving.outbox (
                id, event_type, aggregate_type, aggregate_id, occurred_at, payload_json
            ) values (
                @id, @event_type, 'ReceivedItem', @aggregate_id, @occurred_at, @payload_json
            )
            """, connection, transaction);
        outbox.Parameters.AddWithValue("id", eventId);
        outbox.Parameters.AddWithValue("event_type", eventType);
        outbox.Parameters.AddWithValue("aggregate_id", item.ReceivedItemId);
        outbox.Parameters.AddWithValue("occurred_at", occurredAt);
        outbox.Parameters.Add(new NpgsqlParameter("payload_json", NpgsqlDbType.Jsonb) { Value = payloadJson });
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
        audit.Parameters.Add(new NpgsqlParameter("payload_json", NpgsqlDbType.Jsonb) { Value = payloadJson });
        await audit.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<string> DeserializeArray(string json) =>
        JsonSerializer.Deserialize<string[]>(json, ReceivingJson.Options)
        ?? throw new InvalidOperationException("REC.IDENTITY_JSON_INVALID");

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
        {
            throw new InvalidOperationException("REC.TRANSACTION_REQUIRED");
        }

        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}
