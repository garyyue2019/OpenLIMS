using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Ai;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Ai;

internal sealed class AiDataSource : IAsyncDisposable
{
    public AiDataSource(AiPersistenceOptions options) => Value = NpgsqlDataSource.Create(options.ConnectionString);
    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed record StoredAiRunRequest(
    Guid RunId,
    string OrganizationGroupId,
    CreateAiRunRequest Request,
    string RequestHash,
    string RequestedBy,
    DateTimeOffset RequestedAt);

internal sealed class AiStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task AcquireKeyLockAsync(string category, string key, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtext(@lock_key))", connection, transaction);
        command.Parameters.AddWithValue("lock_key", $"ai.{category}:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredAiRunRequest?> LoadRequestByIdempotencyAsync(
        string organizationGroupId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select run_id from ai.run_request
            where organization_group_id = @organization_group_id and idempotency_key = @idempotency_key
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? await LoadRequestAsync(organizationGroupId, id, cancellationToken) : null;
    }

    public async Task<StoredAiRunRequest?> LoadRequestAsync(
        string organizationGroupId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                   envelope_json::text, validation_profile_ref, validation_profile_version,
                   allowed_fields, allowed_units, request_hash, idempotency_key,
                   requested_by, requested_at
            from ai.run_request
            where organization_group_id = @organization_group_id and run_id = @run_id
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("run_id", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var request = new CreateAiRunRequest(
            AiContract.RuntimeRuleSetVersion,
            new AiObjectContext(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4)),
            JsonSerializer.Deserialize<AiRunEnvelope>(reader.GetString(5), Json)
                ?? throw new InvalidOperationException("AIX.ENVELOPE_MISSING"),
            new AiVersionedReference(reader.GetString(6), reader.GetInt64(7)),
            reader.GetFieldValue<string[]>(8), reader.GetFieldValue<string[]>(9),
            reader.GetString(11));
        return new StoredAiRunRequest(
            runId, organizationGroupId, request, reader.GetString(10),
            reader.GetString(12), reader.GetFieldValue<DateTimeOffset>(13));
    }

    public async Task<StoredAiRunRequest> InsertRequestAsync(
        Guid runId,
        string organizationGroupId,
        CreateAiRunRequest request,
        string requestHash,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into ai.run_request (
                run_id, organization_group_id,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                envelope_json, validation_profile_ref, validation_profile_version,
                allowed_fields, allowed_units, request_hash, idempotency_key,
                requested_by, requested_at, event_id, correlation_id
            ) values (
                @run_id, @organization_group_id,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @envelope_json, @validation_profile_ref, @validation_profile_version,
                @allowed_fields, @allowed_units, @request_hash, @idempotency_key,
                @requested_by, @requested_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("legal_entity_id", request.ObjectScope.LegalEntityId);
        command.Parameters.AddWithValue("laboratory_id", request.ObjectScope.LaboratoryId);
        command.Parameters.AddWithValue("customer_id", request.ObjectScope.CustomerId);
        command.Parameters.AddWithValue("service_order_id", request.ObjectScope.ServiceOrderId);
        command.Parameters.AddWithValue("product_category", request.ObjectScope.ProductCategory);
        command.Parameters.AddWithValue("envelope_json", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(request.Envelope, Json));
        command.Parameters.AddWithValue("validation_profile_ref", request.ValidationProfile.Id);
        command.Parameters.AddWithValue("validation_profile_version", request.ValidationProfile.Version);
        command.Parameters.AddWithValue("allowed_fields", request.AllowedFields.ToArray());
        command.Parameters.AddWithValue("allowed_units", request.AllowedUnits.ToArray());
        command.Parameters.AddWithValue("request_hash", requestHash);
        command.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);
        command.Parameters.AddWithValue("requested_by", actorId);
        command.Parameters.AddWithValue("requested_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            "REQUEST_AI_RUN", runId.ToString("N"), organizationGroupId, actorId,
            eventId, "Ai.RunRequested.v1", correlationId, now, cancellationToken);
        return new StoredAiRunRequest(runId, organizationGroupId, request, requestHash, actorId, now);
    }

    public async Task InsertOutcomeAsync(
        StoredAiRunRequest run,
        EvaluatedAiOutcome outcome,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into ai.run_outcome (
                run_id, status, provider_status, provider_external_reference, provider_failure_code,
                original_output_json, validation_json, human_review_required, manual_fallback_required,
                completed_at, event_id, correlation_id
            ) values (
                @run_id, @status, @provider_status, @provider_external_reference, @provider_failure_code,
                @original_output_json, @validation_json, @human_review_required, @manual_fallback_required,
                @completed_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("run_id", run.RunId);
        command.Parameters.AddWithValue("status", outcome.Status);
        command.Parameters.AddWithValue("provider_status", outcome.ProviderStatus);
        command.Parameters.AddWithValue("provider_external_reference", (object?)outcome.ProviderExternalReference ?? DBNull.Value);
        command.Parameters.AddWithValue("provider_failure_code", (object?)outcome.ProviderFailureCode ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("original_output_json", NpgsqlDbType.Jsonb)
        {
            Value = outcome.OriginalOutput is null ? DBNull.Value : JsonSerializer.Serialize(outcome.OriginalOutput, Json)
        });
        command.Parameters.Add(new NpgsqlParameter("validation_json", NpgsqlDbType.Jsonb)
        {
            Value = outcome.Validation is null ? DBNull.Value : JsonSerializer.Serialize(outcome.Validation, Json)
        });
        command.Parameters.AddWithValue("human_review_required", outcome.HumanReviewRequired);
        command.Parameters.AddWithValue("manual_fallback_required", outcome.ManualFallbackRequired);
        command.Parameters.AddWithValue("completed_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            "COMPLETE_AI_RUN", run.RunId.ToString("N"), run.OrganizationGroupId, actorId,
            eventId, "Ai.RunCompleted.v1", correlationId, now, cancellationToken);
    }

    public async Task<AiRunResult?> LoadResultAsync(
        string organizationGroupId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await LoadRequestAsync(organizationGroupId, runId, cancellationToken);
        if (run is null)
            return null;
        var (connection, transaction) = RequireTransaction();
        string status = AiRunStatuses.Pending, providerStatus = AiProviderStatuses.Pending;
        string? externalReference = null, failureCode = null;
        AiStructuredOutput? output = null;
        AiValidationResult? validation = null;
        bool humanReviewRequired = false, manualFallbackRequired = false;
        DateTimeOffset? completedAt = null;
        await using (var command = new NpgsqlCommand("""
            select status, provider_status, provider_external_reference, provider_failure_code,
                   original_output_json::text, validation_json::text,
                   human_review_required, manual_fallback_required, completed_at
            from ai.run_outcome where run_id = @run_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("run_id", runId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                status = reader.GetString(0);
                providerStatus = reader.GetString(1);
                externalReference = reader.IsDBNull(2) ? null : reader.GetString(2);
                failureCode = reader.IsDBNull(3) ? null : reader.GetString(3);
                output = reader.IsDBNull(4) ? null : JsonSerializer.Deserialize<AiStructuredOutput>(reader.GetString(4), Json);
                validation = reader.IsDBNull(5) ? null : JsonSerializer.Deserialize<AiValidationResult>(reader.GetString(5), Json);
                humanReviewRequired = reader.GetBoolean(6);
                manualFallbackRequired = reader.GetBoolean(7);
                completedAt = reader.GetFieldValue<DateTimeOffset>(8);
            }
        }

        var dispositions = await LoadDispositionsAsync(runId, cancellationToken);
        return new AiRunResult(
            runId.ToString("N"), 1 + dispositions.Count, status, run.Request.ObjectScope,
            run.Request.Envelope, run.Request.ValidationProfile, run.Request.AllowedFields,
            run.Request.AllowedUnits, providerStatus, externalReference, failureCode,
            output, validation, dispositions, humanReviewRequired, manualFallbackRequired,
            run.RequestedBy, run.RequestedAt, completedAt, AiContract.RuntimeRuleSetVersion);
    }

    public async Task<AiReviewDispositionResult?> LoadDispositionByIdempotencyAsync(
        Guid runId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select disposition_id, candidate_id, kind, ai_original_value, reason,
                   responsible_actor, human_value, recorded_at
            from ai.disposition where run_id = @run_id and idempotency_key = @idempotency_key
            """, connection, transaction);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapDisposition(reader) : null;
    }

    public async Task<AiReviewDispositionResult> InsertDispositionAsync(
        Guid runId,
        long runVersion,
        AiDisposition disposition,
        string idempotencyKey,
        string organizationGroupId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into ai.disposition (
                disposition_id, run_id, run_version, candidate_id, kind,
                ai_original_value, human_value, reason, responsible_actor,
                idempotency_key, recorded_at, event_id, correlation_id
            ) values (
                @disposition_id, @run_id, @run_version, @candidate_id, @kind,
                @ai_original_value, @human_value, @reason, @responsible_actor,
                @idempotency_key, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("disposition_id", Guid.Parse(disposition.DispositionId));
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("run_version", runVersion);
        command.Parameters.AddWithValue("candidate_id", disposition.CandidateId);
        command.Parameters.AddWithValue("kind", disposition.Kind);
        command.Parameters.AddWithValue("ai_original_value", disposition.AiOriginalValue);
        command.Parameters.AddWithValue("human_value", (object?)disposition.HumanValue ?? DBNull.Value);
        command.Parameters.AddWithValue("reason", disposition.Reason);
        command.Parameters.AddWithValue("responsible_actor", disposition.ResponsibleActor);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("recorded_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            "RECORD_AI_DISPOSITION", disposition.DispositionId, organizationGroupId,
            disposition.ResponsibleActor, eventId, "Ai.DispositionRecorded.v1",
            correlationId, now, cancellationToken);
        return new AiReviewDispositionResult(disposition, now);
    }

    public async Task<IReadOnlyList<Guid>> LoadQueueIdsAsync(
        string organizationGroupId,
        string? status,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var ids = new List<Guid>();
        await using var command = new NpgsqlCommand("""
            select r.run_id
            from ai.run_request r
            left join ai.run_outcome o on o.run_id = r.run_id
            where r.organization_group_id = @organization_group_id
              and (@status is null or coalesce(o.status, 'PENDING') = @status)
            order by r.requested_at, r.run_id
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Text)
        {
            Value = (object?)status ?? DBNull.Value
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    public Task WriteReadAuditAsync(
        string objectId,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, objectId, action, AiContract.RuntimeRuleSetVersion,
            "1", "1", correlationId, now), cancellationToken);

    private async Task<IReadOnlyList<AiReviewDispositionResult>> LoadDispositionsAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var dispositions = new List<AiReviewDispositionResult>();
        await using var command = new NpgsqlCommand("""
            select disposition_id, candidate_id, kind, ai_original_value, reason,
                   responsible_actor, human_value, recorded_at
            from ai.disposition where run_id = @run_id order by run_version
            """, connection, transaction);
        command.Parameters.AddWithValue("run_id", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            dispositions.Add(MapDisposition(reader));
        return dispositions;
    }

    private static AiReviewDispositionResult MapDisposition(NpgsqlDataReader reader) => new(
        new AiDisposition(
            reader.GetGuid(0).ToString("N"), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6)),
        reader.GetFieldValue<DateTimeOffset>(7));

    private async Task WritePlatformEvidenceAsync(
        string action,
        string objectId,
        string organizationGroupId,
        string actorId,
        string eventId,
        string messageType,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, objectId, action, AiContract.RuntimeRuleSetVersion,
            null, "1", correlationId, now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("AIX.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class AiAttemptAuditWriter(AiDataSource dataSource)
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
            insert into ai.audit_attempt (
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
