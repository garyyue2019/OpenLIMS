using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal sealed class ReceivingReleaseStore(
    IPostgresTransactionAccessor transactionAccessor,
    IdentityAssessmentStore identityStore)
{
    public Task<IdentityItemScope?> LoadItemAsync(
        string organizationGroupId,
        string receivedItemId,
        bool forUpdate,
        CancellationToken cancellationToken) =>
        identityStore.LoadItemAsync(organizationGroupId, receivedItemId, forUpdate, cancellationToken);

    public async Task<(ReceivingReleaseIdentitySnapshot? Identity, IReadOnlyList<ReceivingReleaseExceptionSnapshot> Exceptions)>
        LoadInputsAsync(Guid receivedItemId, CancellationToken cancellationToken)
    {
        var identity = await LoadIdentityAsync(receivedItemId, cancellationToken);
        var exceptions = await LoadExceptionsAsync(receivedItemId, cancellationToken);
        return (identity, exceptions);
    }

    public async Task<ReceivingReleaseDecisionResult> InsertAsync(
        IdentityItemScope item,
        ReceivingReleaseIdentitySnapshot identity,
        IReadOnlyList<ReceivingReleaseExceptionSnapshot> exceptions,
        ReceivingReleaseEvaluation evaluation,
        SubmitReceivingReleaseDecisionRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var releaseDecisionId = Guid.NewGuid();
        var version = await NextVersionAsync(item.ReceivedItemId, cancellationToken);
        var exceptionReferences = exceptions.Select(exception =>
            new ReceivingReleaseExceptionReference(
                exception.ExceptionId,
                exception.Status,
                exception.ExceptionVersion,
                exception.DecisionId!,
                exception.DecisionVersion!.Value,
                exception.MatrixVersion!)).ToArray();

        await using (var command = new NpgsqlCommand("""
            insert into receiving.receiving_release_decision (
                release_decision_id, received_item_id, version, item_version,
                identity_decision_id, identity_decision_version, exception_decision_versions,
                release_rule_version, exception_matrix_version, outcome,
                allowed_actions, prohibited_actions, constraints_valid_until,
                rationale, approved_at, approved_by
            ) values (
                @release_decision_id, @received_item_id, @version, @item_version,
                @identity_decision_id, @identity_decision_version, @exception_decision_versions,
                @release_rule_version, @exception_matrix_version, @outcome,
                @allowed_actions, @prohibited_actions, @constraints_valid_until,
                @rationale, @approved_at, @approved_by
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("release_decision_id", releaseDecisionId);
            command.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("item_version", item.ItemVersion);
            command.Parameters.AddWithValue("identity_decision_id", Guid.Parse(identity.DecisionId));
            command.Parameters.AddWithValue("identity_decision_version", identity.DecisionVersion);
            AddJson(command, "exception_decision_versions", exceptionReferences);
            command.Parameters.AddWithValue("release_rule_version", request.RuleSetVersion);
            command.Parameters.AddWithValue("exception_matrix_version", ReceivingReleaseContract.ExceptionMatrixVersion);
            command.Parameters.AddWithValue("outcome", evaluation.Outcome);
            AddJson(command, "allowed_actions", evaluation.AllowedActions);
            AddJson(command, "prohibited_actions", evaluation.ProhibitedActions);
            command.Parameters.AddWithValue("constraints_valid_until", (object?)evaluation.ConstraintsValidUntil ?? DBNull.Value);
            command.Parameters.AddWithValue("rationale", request.Rationale.Trim());
            command.Parameters.AddWithValue("approved_at", now);
            command.Parameters.AddWithValue("approved_by", actorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var newItemVersion = await TransitionItemAsync(item, evaluation.State, actorId, now, cancellationToken);
        await AppendStateHistoryAsync(item, evaluation.State, actorId, now, cancellationToken);
        var payload = JsonSerializer.Serialize(new
        {
            releaseDecisionId,
            releaseDecisionVersion = version,
            beforeItemVersion = item.ItemVersion,
            itemVersion = newItemVersion,
            identityDecisionId = identity.DecisionId,
            identityDecisionVersion = identity.DecisionVersion,
            exceptionDecisionVersions = exceptionReferences,
            releaseRuleVersion = request.RuleSetVersion,
            exceptionMatrixVersion = ReceivingReleaseContract.ExceptionMatrixVersion,
            evaluation.Outcome,
            state = evaluation.State,
            evaluation.AllowedActions,
            evaluation.ProhibitedActions,
            evaluation.ConstraintsValidUntil,
            correlationId
        }, ReceivingJson.Options);
        await WriteAuditAndOutboxAsync(
            item,
            actorId,
            evaluation.Outcome == ReceivingReleaseOutcomes.Released
                ? "RECEIVING_RELEASED"
                : "RECEIVING_RELEASED_WITH_CONSTRAINTS",
            correlationId,
            now,
            payload,
            cancellationToken);

        return new ReceivingReleaseDecisionResult(
            releaseDecisionId.ToString("N"),
            version,
            item.ReceivedItemId.ToString("N"),
            item.ReceivedItemNumber,
            item.ItemVersion,
            newItemVersion,
            evaluation.State,
            identity.DecisionId,
            identity.DecisionVersion,
            exceptionReferences,
            request.RuleSetVersion,
            ReceivingReleaseContract.ExceptionMatrixVersion,
            evaluation.Outcome,
            evaluation.AllowedActions,
            evaluation.ProhibitedActions,
            evaluation.ConstraintsValidUntil,
            request.Rationale.Trim(),
            now,
            actorId);
    }

    public async Task<ReceivingReleaseSnapshot?> LoadCurrentReleaseAsync(
        IdentityItemScope item,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select rd.release_decision_id, rd.version, rd.item_version,
                   rd.identity_decision_id, rd.identity_decision_version,
                   rd.exception_decision_versions, rd.release_rule_version,
                   rd.exception_matrix_version, rd.outcome, rd.allowed_actions,
                   rd.prohibited_actions, rd.constraints_valid_until, rd.rationale,
                   rd.approved_at, rd.approved_by, ia.assessment_state,
                   current_identity.decision_id, ia.current_decision_version
            from receiving.receiving_release_decision rd
            join receiving.identity_assessment ia on ia.received_item_id = rd.received_item_id
            left join receiving.identity_decision current_identity
              on current_identity.received_item_id = ia.received_item_id
             and current_identity.version = ia.current_decision_version
            where rd.received_item_id = @received_item_id
            order by rd.version desc
            limit 1
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var result = new ReceivingReleaseDecisionResult(
            reader.GetGuid(0).ToString("N"),
            reader.GetInt64(1),
            item.ReceivedItemId.ToString("N"),
            item.ReceivedItemNumber,
            reader.GetInt64(2),
            item.ItemVersion,
            item.CurrentState,
            reader.GetGuid(3).ToString("N"),
            reader.GetInt64(4),
            DeserializeExceptionReferences(reader.GetString(5)),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            DeserializeArray(reader.GetString(9)),
            DeserializeArray(reader.GetString(10)),
            reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            reader.GetString(12),
            reader.GetFieldValue<DateTimeOffset>(13),
            reader.GetString(14));
        return new ReceivingReleaseSnapshot(
            result,
            reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetGuid(16).ToString("N"),
            reader.IsDBNull(17) ? null : reader.GetInt64(17));
    }

    public Task WriteReadAuditAsync(
        IdentityItemScope item,
        string actorId,
        string correlationId,
        DateTimeOffset now,
        string payloadJson,
        CancellationToken cancellationToken) =>
        WriteAuditAsync(
            item,
            actorId,
            "RECEIVING_ELIGIBILITY_V2_EVALUATED",
            correlationId,
            now,
            payloadJson,
            null,
            cancellationToken);

    private async Task<ReceivingReleaseIdentitySnapshot?> LoadIdentityAsync(
        Guid receivedItemId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select d.decision_id, d.version, d.outcome, d.rule_set_version
            from receiving.identity_assessment a
            join receiving.identity_decision d
              on d.received_item_id = a.received_item_id
             and d.version = a.current_decision_version
            where a.received_item_id = @received_item_id
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ReceivingReleaseIdentitySnapshot(
                reader.GetGuid(0).ToString("N"),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3))
            : null;
    }

    private async Task<IReadOnlyList<ReceivingReleaseExceptionSnapshot>> LoadExceptionsAsync(
        Guid receivedItemId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select e.exception_id, s.status, s.version,
                   d.decision_id, d.version, d.decision_type, d.matrix_version,
                   d.allowed_actions, d.prohibited_actions, d.valid_until
            from receiving.receiving_exception e
            join receiving.receiving_exception_state s on s.exception_id = e.exception_id
            left join receiving.receiving_exception_decision d
              on d.exception_id = e.exception_id
             and d.version = s.current_decision_version
            where e.received_item_id = @received_item_id
            order by e.created_at, e.exception_id
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        var results = new List<ReceivingReleaseExceptionSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ReceivingReleaseExceptionSnapshot(
                reader.GetGuid(0).ToString("N"),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3).ToString("N"),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? [] : DeserializeArray(reader.GetString(7)),
                reader.IsDBNull(8) ? [] : DeserializeArray(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)));
        }
        return results;
    }

    private async Task<long> NextVersionAsync(Guid receivedItemId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select coalesce(max(version), 0) + 1
            from receiving.receiving_release_decision where received_item_id = @received_item_id
            """, connection, transaction);
        command.Parameters.AddWithValue("received_item_id", receivedItemId);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<long> TransitionItemAsync(
        IdentityItemScope item,
        string newState,
        string actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            update receiving.received_item
            set state = @state, version = version + 1,
                updated_at = @updated_at, updated_by = @updated_by
            where id = @received_item_id and version = @expected_version and state = 'QUARANTINED'
            returning version
            """, connection, transaction);
        command.Parameters.AddWithValue("state", newState);
        command.Parameters.AddWithValue("updated_at", now);
        command.Parameters.AddWithValue("updated_by", actorId);
        command.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
        command.Parameters.AddWithValue("expected_version", item.ItemVersion);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null) throw new ReceivingDomainException(ReceivingErrorCodes.ExpectedVersionConflict);
        var version = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

        await using var identity = new NpgsqlCommand("""
            update receiving.label_identity
            set object_version = @object_version, object_state = @object_state
            where organization_group_id = @organization_group_id
              and object_type = 'RI' and object_id = @received_item_id
            """, connection, transaction);
        identity.Parameters.AddWithValue("object_version", version);
        identity.Parameters.AddWithValue("object_state", newState);
        identity.Parameters.AddWithValue("organization_group_id", item.OrganizationGroupId);
        identity.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
        if (await identity.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new ReceivingDomainException(ReceivingErrorCodes.ReceivingPortUnavailable);
        return version;
    }

    private async Task AppendStateHistoryAsync(
        IdentityItemScope item,
        string newState,
        string actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into receiving.received_item_state_history (
                id, received_item_id, sequence, from_state, to_state, occurred_at, actor_id
            )
            select @id, @received_item_id, coalesce(max(sequence), 0) + 1,
                   'QUARANTINED', @to_state, @occurred_at, @actor_id
            from receiving.received_item_state_history
            where received_item_id = @received_item_id
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("received_item_id", item.ReceivedItemId);
        command.Parameters.AddWithValue("to_state", newState);
        command.Parameters.AddWithValue("occurred_at", now);
        command.Parameters.AddWithValue("actor_id", actorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static IReadOnlyList<string> DeserializeArray(string json) =>
        JsonSerializer.Deserialize<string[]>(json, ReceivingJson.Options)
        ?? throw new InvalidOperationException("REC.RELEASE_JSON_INVALID");

    private static IReadOnlyList<ReceivingReleaseExceptionReference> DeserializeExceptionReferences(string json) =>
        JsonSerializer.Deserialize<ReceivingReleaseExceptionReference[]>(json, ReceivingJson.Options)
        ?? throw new InvalidOperationException("REC.RELEASE_REFERENCE_JSON_INVALID");

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
