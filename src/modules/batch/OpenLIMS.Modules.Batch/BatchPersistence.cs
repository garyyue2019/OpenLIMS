using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Batch;

internal sealed class BatchDataSource : IAsyncDisposable
{
    public BatchDataSource(BatchPersistenceOptions options) => Value = NpgsqlDataSource.Create(options.ConnectionString);
    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed class BatchStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireBatchLockAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@batch_id, 0))", connection, transaction);
        command.Parameters.AddWithValue("batch_id", batchId.ToString("N"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<BatchResult> InsertBatchAsync(
        Guid batchId,
        string organizationGroupId,
        BatchObjectContext objectScope,
        string batchType,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into batch.batch (
                batch_id, organization_group_id, legal_entity_id, laboratory_id,
                batch_type, rule_set_version, created_by, created_at, event_id, correlation_id
            ) values (
                @batch_id, @organization_group_id, @legal_entity_id, @laboratory_id,
                @batch_type, @rule_set_version, @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("batch_id", batchId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("legal_entity_id", objectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", objectScope.LaboratoryId);
            command.Parameters.AddWithValue("batch_type", batchType);
            command.Parameters.AddWithValue("rule_set_version", BatchContract.RuleSetVersion);
            command.Parameters.AddWithValue("created_by", actorId);
            command.Parameters.AddWithValue("created_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEvidenceAsync(
            "CREATE_BATCH", batchId.ToString("N"), organizationGroupId, actorId,
            null, "1", eventId, "BatchCreated.v1", correlationId, now, cancellationToken);
        return new BatchResult(
            batchId.ToString("N"), batchType, BatchStates.Active, 1,
            BatchContract.RuleSetVersion, objectScope, [], [], null, actorId, now);
    }

    public async Task<BatchResult?> LoadBatchAsync(
        string organizationGroupId,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        string batchType, legalEntity, laboratory, createdBy;
        DateTimeOffset createdAt;
        await using (var command = new NpgsqlCommand("""
            select batch_type, legal_entity_id, laboratory_id, created_by, created_at
            from batch.batch
            where organization_group_id = @organization_group_id and batch_id = @batch_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("batch_id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            batchType = reader.GetString(0);
            legalEntity = reader.GetString(1);
            laboratory = reader.GetString(2);
            createdBy = reader.GetString(3);
            createdAt = reader.GetFieldValue<DateTimeOffset>(4);
        }

        var members = new List<BatchMemberResult>();
        await using (var command = new NpgsqlCommand("""
            select member_id, batch_version, member_type, allocation_id, subject_allocation_version,
                   allocation_gate_decision, allocation_gate_rule_set_version, qc_ref, qc_version,
                   customer_id, service_order_id, product_category, added_by, added_at
            from batch.batch_member where batch_id = @batch_id order by batch_version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("batch_id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                members.Add(new BatchMemberResult(
                    reader.GetGuid(0).ToString("N"),
                    batchId.ToString("N"),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : new BatchVersionedReference(reader.GetString(7), reader.GetInt64(8)),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.GetFieldValue<DateTimeOffset>(13)));
            }
        }

        var evidence = new List<BatchEvidenceResult>();
        await using (var command = new NpgsqlCommand("""
            select evidence_id, batch_version, source_system, external_ref, external_version,
                   sha256, recorded_by, recorded_at
            from batch.batch_evidence where batch_id = @batch_id order by batch_version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("batch_id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                evidence.Add(new BatchEvidenceResult(
                    reader.GetGuid(0).ToString("N"),
                    batchId.ToString("N"),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    new BatchVersionedReference(reader.GetString(3), reader.GetInt64(4)),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7)));
            }
        }

        BatchFreezeResult? freeze = null;
        await using (var command = new NpgsqlCommand("""
            select freeze_id, batch_version, cause, affected_member_count,
                   approved_follow_up_ref, approved_follow_up_version, frozen_by, frozen_at
            from batch.batch_freeze where batch_id = @batch_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("batch_id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                freeze = new BatchFreezeResult(
                    reader.GetGuid(0).ToString("N"),
                    batchId.ToString("N"),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : new BatchVersionedReference(reader.GetString(4), reader.GetInt64(5)),
                    reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7));
            }
        }

        var version = Math.Max(1, Math.Max(
            members.Count > 0 ? members.Max(m => m.BatchVersion) : 1,
            Math.Max(
                evidence.Count > 0 ? evidence.Max(e => e.BatchVersion) : 1,
                freeze?.BatchVersion ?? 1)));
        return new BatchResult(
            batchId.ToString("N"),
            batchType,
            freeze is null ? BatchStates.Active : BatchStates.Frozen,
            version,
            BatchContract.RuleSetVersion,
            new BatchObjectContext(legalEntity, laboratory),
            members,
            evidence,
            freeze,
            createdBy,
            createdAt);
    }

    public async Task<BatchMemberResult> InsertMemberAsync(
        Guid batchId,
        long batchVersion,
        string organizationGroupId,
        AddBatchMemberRequest request,
        string? gateDecision,
        string? gateRuleSetVersion,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var memberId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into batch.batch_member (
                member_id, batch_id, batch_version, member_type,
                allocation_id, subject_allocation_version, allocation_gate_decision, allocation_gate_rule_set_version,
                qc_ref, qc_version, customer_id, service_order_id, product_category,
                added_by, added_at, event_id, correlation_id
            ) values (
                @member_id, @batch_id, @batch_version, @member_type,
                @allocation_id, @subject_allocation_version, @allocation_gate_decision, @allocation_gate_rule_set_version,
                @qc_ref, @qc_version, @customer_id, @service_order_id, @product_category,
                @added_by, @added_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("member_id", memberId);
            command.Parameters.AddWithValue("batch_id", batchId);
            command.Parameters.AddWithValue("batch_version", batchVersion);
            command.Parameters.AddWithValue("member_type", request.MemberType);
            command.Parameters.AddWithValue("allocation_id", (object?)request.AllocationId ?? DBNull.Value);
            command.Parameters.AddWithValue("subject_allocation_version", (object?)request.ExpectedSubjectAllocationVersion ?? DBNull.Value);
            command.Parameters.AddWithValue("allocation_gate_decision", (object?)gateDecision ?? DBNull.Value);
            command.Parameters.AddWithValue("allocation_gate_rule_set_version", (object?)gateRuleSetVersion ?? DBNull.Value);
            command.Parameters.AddWithValue("qc_ref", (object?)request.QcRef?.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("qc_version", (object?)request.QcRef?.Version ?? DBNull.Value);
            command.Parameters.AddWithValue("customer_id", request.CustomerId);
            command.Parameters.AddWithValue("service_order_id", request.ServiceOrderId);
            command.Parameters.AddWithValue("product_category", request.ProductCategory);
            command.Parameters.AddWithValue("added_by", actorId);
            command.Parameters.AddWithValue("added_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEvidenceAsync(
            "ADD_BATCH_MEMBER", batchId.ToString("N"), organizationGroupId, actorId,
            (batchVersion - 1).ToString(), batchVersion.ToString(),
            eventId, "BatchMemberAdded.v1", correlationId, now, cancellationToken);
        return new BatchMemberResult(
            memberId.ToString("N"), batchId.ToString("N"), batchVersion, request.MemberType,
            request.AllocationId, request.ExpectedSubjectAllocationVersion,
            gateDecision, gateRuleSetVersion, request.QcRef,
            request.CustomerId, request.ServiceOrderId, request.ProductCategory, actorId, now);
    }

    public async Task<BatchEvidenceResult> InsertEvidenceAsync(
        Guid batchId,
        long batchVersion,
        string organizationGroupId,
        AddBatchEvidenceRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var evidenceId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into batch.batch_evidence (
                evidence_id, batch_id, batch_version, source_system,
                external_ref, external_version, sha256, recorded_by, recorded_at, event_id, correlation_id
            ) values (
                @evidence_id, @batch_id, @batch_version, @source_system,
                @external_ref, @external_version, @sha256, @recorded_by, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("evidence_id", evidenceId);
            command.Parameters.AddWithValue("batch_id", batchId);
            command.Parameters.AddWithValue("batch_version", batchVersion);
            command.Parameters.AddWithValue("source_system", request.SourceSystem);
            command.Parameters.AddWithValue("external_ref", request.ExternalRef.Id);
            command.Parameters.AddWithValue("external_version", request.ExternalRef.Version);
            command.Parameters.AddWithValue("sha256", request.Sha256);
            command.Parameters.AddWithValue("recorded_by", actorId);
            command.Parameters.AddWithValue("recorded_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEvidenceAsync(
            "ADD_BATCH_EVIDENCE", batchId.ToString("N"), organizationGroupId, actorId,
            (batchVersion - 1).ToString(), batchVersion.ToString(),
            eventId, "BatchEvidenceRecorded.v1", correlationId, now, cancellationToken);
        return new BatchEvidenceResult(
            evidenceId.ToString("N"), batchId.ToString("N"), batchVersion,
            request.SourceSystem, request.ExternalRef, request.Sha256, actorId, now);
    }

    public async Task<BatchFreezeResult> InsertFreezeAsync(
        Guid batchId,
        long batchVersion,
        string organizationGroupId,
        FreezeBatchRequest request,
        int affectedMemberCount,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var freezeId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into batch.batch_freeze (
                batch_id, freeze_id, batch_version, cause, affected_member_count,
                approved_follow_up_ref, approved_follow_up_version, frozen_by, frozen_at, event_id, correlation_id
            ) values (
                @batch_id, @freeze_id, @batch_version, @cause, @affected_member_count,
                @approved_follow_up_ref, @approved_follow_up_version, @frozen_by, @frozen_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("batch_id", batchId);
            command.Parameters.AddWithValue("freeze_id", freezeId);
            command.Parameters.AddWithValue("batch_version", batchVersion);
            command.Parameters.AddWithValue("cause", request.Cause);
            command.Parameters.AddWithValue("affected_member_count", affectedMemberCount);
            command.Parameters.AddWithValue("approved_follow_up_ref", (object?)request.ApprovedFollowUpRef?.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("approved_follow_up_version", (object?)request.ApprovedFollowUpRef?.Version ?? DBNull.Value);
            command.Parameters.AddWithValue("frozen_by", actorId);
            command.Parameters.AddWithValue("frozen_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEvidenceAsync(
            "FREEZE_BATCH", batchId.ToString("N"), organizationGroupId, actorId,
            (batchVersion - 1).ToString(), batchVersion.ToString(),
            eventId, "BatchFrozen.v1", correlationId, now, cancellationToken);
        return new BatchFreezeResult(
            freezeId.ToString("N"), batchId.ToString("N"), batchVersion, request.Cause,
            affectedMemberCount, request.ApprovedFollowUpRef, actorId, now);
    }

    public Task WriteReadAuditAsync(
        string batchId,
        long version,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, batchId, action, BatchContract.RuleSetVersion,
            version.ToString(), version.ToString(), correlationId, now), cancellationToken);

    private async Task WriteEvidenceAsync(
        string action,
        string batchKey,
        string organizationGroupId,
        string actorId,
        string? beforeVersion,
        string afterVersion,
        string eventId,
        string messageType,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, batchKey, action, BatchContract.RuleSetVersion,
            beforeVersion, afterVersion, correlationId, now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("BAT.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class BatchAttemptAuditWriter(BatchDataSource dataSource)
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
            insert into batch.audit_attempt (
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
