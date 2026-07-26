using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Qc;

namespace OpenLIMS.Modules.Qc;

internal sealed class QcDataSource : IAsyncDisposable
{
    public QcDataSource(QcPersistenceOptions options) => Value = NpgsqlDataSource.Create(options.ConnectionString);
    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed record QcGateFacts(string Decision, long BatchVersion, string RuleSetVersion);

internal sealed class QcStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireRunLockAsync(Guid qcRunId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
        command.Parameters.AddWithValue("key", $"openlims.qc.run.{qcRunId:N}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertRunAsync(
        Guid qcRunId,
        string organizationGroupId,
        CreateQcRunRequest request,
        QcGateFacts batchGate,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into qc.qc_run (
                qc_run_id, organization_group_id, legal_entity_id, laboratory_id,
                batch_id, batch_version, batch_gate_decision, batch_gate_rule_set_version,
                method_ref, method_version, qc_rule_set_ref, qc_rule_set_version,
                rule_set_version, opened_by, opened_at, event_id, correlation_id
            ) values (
                @qc_run_id, @organization_group_id, @legal_entity_id, @laboratory_id,
                @batch_id, @batch_version, @batch_gate_decision, @batch_gate_rule_set_version,
                @method_ref, @method_version, @qc_rule_set_ref, @qc_rule_set_version,
                @rule_set_version, @opened_by, @opened_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("qc_run_id", qcRunId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("legal_entity_id", request.ObjectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", request.ObjectScope.LaboratoryId);
            command.Parameters.AddWithValue("batch_id", request.BatchId);
            command.Parameters.AddWithValue("batch_version", batchGate.BatchVersion);
            command.Parameters.AddWithValue("batch_gate_decision", batchGate.Decision);
            command.Parameters.AddWithValue("batch_gate_rule_set_version", batchGate.RuleSetVersion);
            command.Parameters.AddWithValue("method_ref", request.Method.Id);
            command.Parameters.AddWithValue("method_version", request.Method.Version);
            command.Parameters.AddWithValue("qc_rule_set_ref", request.QcRuleSet.Id);
            command.Parameters.AddWithValue("qc_rule_set_version", request.QcRuleSet.Version);
            command.Parameters.AddWithValue("rule_set_version", QcContract.RuleSetVersion);
            command.Parameters.AddWithValue("opened_by", actorId);
            command.Parameters.AddWithValue("opened_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "OPEN_QC_RUN", qcRunId.ToString("N"), organizationGroupId, actorId,
            eventId, "Qc.RunOpened.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertResultAsync(
        Guid qcRunId, long runVersion, AddQcResultRequest request, string organizationGroupId,
        string actorId, DateTimeOffset now, string correlationId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into qc.qc_result (
                qc_result_id, qc_run_id, run_version, rule_ref, rule_version,
                control_type, observed_value, verdict, verdict_basis,
                recorded_by, recorded_at, event_id, correlation_id
            ) values (
                @qc_result_id, @qc_run_id, @run_version, @rule_ref, @rule_version,
                @control_type, @observed_value, @verdict, @verdict_basis,
                @recorded_by, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("qc_result_id", Guid.NewGuid());
            command.Parameters.AddWithValue("qc_run_id", qcRunId);
            command.Parameters.AddWithValue("run_version", runVersion);
            command.Parameters.AddWithValue("rule_ref", request.Rule.Id);
            command.Parameters.AddWithValue("rule_version", request.Rule.Version);
            command.Parameters.AddWithValue("control_type", request.ControlType);
            command.Parameters.AddWithValue("observed_value", request.ObservedValue);
            command.Parameters.AddWithValue("verdict", request.Verdict);
            command.Parameters.AddWithValue("verdict_basis", request.VerdictBasis);
            command.Parameters.AddWithValue("recorded_by", actorId);
            command.Parameters.AddWithValue("recorded_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RECORD_QC_RESULT", qcRunId.ToString("N"), organizationGroupId, actorId,
            eventId, "Qc.ResultRecorded.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertVerdictAsync(
        Guid qcRunId, long runVersion, string state, string organizationGroupId,
        string actorId, DateTimeOffset now, string correlationId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into qc.qc_verdict (
                qc_run_id, verdict_id, run_version, state,
                recorded_by, recorded_at, event_id, correlation_id
            ) values (
                @qc_run_id, @verdict_id, @run_version, @state,
                @recorded_by, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("qc_run_id", qcRunId);
            command.Parameters.AddWithValue("verdict_id", Guid.NewGuid());
            command.Parameters.AddWithValue("run_version", runVersion);
            command.Parameters.AddWithValue("state", state);
            command.Parameters.AddWithValue("recorded_by", actorId);
            command.Parameters.AddWithValue("recorded_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RECORD_QC_VERDICT", qcRunId.ToString("N"), organizationGroupId, actorId,
            eventId, "Qc.VerdictRecorded.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertImpactAsync(
        Guid qcRunId, long runVersion, IReadOnlyList<QcImpactTarget> targets, string organizationGroupId,
        string actorId, DateTimeOffset now, string correlationId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var version = runVersion;
        foreach (var target in targets)
        {
            await using var command = new NpgsqlCommand("""
                insert into qc.qc_impact (
                    impact_id, qc_run_id, run_version, target_type, target_id, target_version,
                    recorded_by, recorded_at, event_id, correlation_id
                ) values (
                    @impact_id, @qc_run_id, @run_version, @target_type, @target_id, @target_version,
                    @recorded_by, @recorded_at, @event_id, @correlation_id
                )
                """, connection, transaction);
            command.Parameters.AddWithValue("impact_id", Guid.NewGuid());
            command.Parameters.AddWithValue("qc_run_id", qcRunId);
            command.Parameters.AddWithValue("run_version", ++version);
            command.Parameters.AddWithValue("target_type", target.TargetType);
            command.Parameters.AddWithValue("target_id", target.TargetId);
            command.Parameters.AddWithValue("target_version", target.TargetVersion);
            command.Parameters.AddWithValue("recorded_by", actorId);
            command.Parameters.AddWithValue("recorded_at", now);
            command.Parameters.AddWithValue("event_id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RECORD_QC_IMPACT", qcRunId.ToString("N"), organizationGroupId, actorId,
            Guid.NewGuid().ToString("N"), "Qc.ImpactRecorded.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertGateAsync(
        Guid qcRunId, long runVersion, SatisfyQcReleaseGateRequest request, string organizationGroupId,
        string actorId, DateTimeOffset now, string correlationId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into qc.qc_release_gate (
                gate_id, qc_run_id, run_version, kind, evidence_ref, evidence_version,
                satisfied_by, satisfied_at, event_id, correlation_id
            ) values (
                @gate_id, @qc_run_id, @run_version, @kind, @evidence_ref, @evidence_version,
                @satisfied_by, @satisfied_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("gate_id", Guid.NewGuid());
            command.Parameters.AddWithValue("qc_run_id", qcRunId);
            command.Parameters.AddWithValue("run_version", runVersion);
            command.Parameters.AddWithValue("kind", request.Kind);
            command.Parameters.AddWithValue("evidence_ref", request.EvidenceRef.Id);
            command.Parameters.AddWithValue("evidence_version", request.EvidenceRef.Version);
            command.Parameters.AddWithValue("satisfied_by", actorId);
            command.Parameters.AddWithValue("satisfied_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "SATISFY_QC_RELEASE_GATE", qcRunId.ToString("N"), organizationGroupId, actorId,
            eventId, "Qc.GateSatisfied.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertDeviationApprovalAsync(
        Guid qcRunId, long runVersion, RecordQcDeviationApprovalRequest request, string organizationGroupId,
        string actorId, DateTimeOffset now, string correlationId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into qc.qc_deviation_approval (
                deviation_id, qc_run_id, run_version, approval_ref, approval_version, reason,
                approved_by, approved_at, event_id, correlation_id
            ) values (
                @deviation_id, @qc_run_id, @run_version, @approval_ref, @approval_version, @reason,
                @approved_by, @approved_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("deviation_id", Guid.NewGuid());
            command.Parameters.AddWithValue("qc_run_id", qcRunId);
            command.Parameters.AddWithValue("run_version", runVersion);
            command.Parameters.AddWithValue("approval_ref", request.ApprovalRef.Id);
            command.Parameters.AddWithValue("approval_version", request.ApprovalRef.Version);
            command.Parameters.AddWithValue("reason", request.Reason);
            command.Parameters.AddWithValue("approved_by", actorId);
            command.Parameters.AddWithValue("approved_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RECORD_QC_DEVIATION_APPROVAL", qcRunId.ToString("N"), organizationGroupId, actorId,
            eventId, "Qc.DeviationApprovalRecorded.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertReleaseAsync(
        Guid qcRunId, long runVersion, string organizationGroupId,
        string actorId, DateTimeOffset now, string correlationId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into qc.qc_release (
                qc_run_id, release_id, run_version, released_by, released_at, event_id, correlation_id
            ) values (
                @qc_run_id, @release_id, @run_version, @released_by, @released_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("qc_run_id", qcRunId);
            command.Parameters.AddWithValue("release_id", Guid.NewGuid());
            command.Parameters.AddWithValue("run_version", runVersion);
            command.Parameters.AddWithValue("released_by", actorId);
            command.Parameters.AddWithValue("released_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RELEASE_QC_BLOCK", qcRunId.ToString("N"), organizationGroupId, actorId,
            eventId, "Qc.Released.v1", correlationId, now, cancellationToken);
    }

    public async Task<QcRunResult?> LoadRunAsync(
        string organizationGroupId, Guid qcRunId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        QcObjectContext? objectScope = null;
        string? batchId = null, batchGateDecision = null, batchGateRuleSetVersion = null, openedBy = null;
        long batchVersion = 0;
        QcVersionedReference? method = null, qcRuleSet = null;
        DateTimeOffset openedAt = default;
        await using (var command = new NpgsqlCommand("""
            select legal_entity_id, laboratory_id, batch_id, batch_version,
                   batch_gate_decision, batch_gate_rule_set_version,
                   method_ref, method_version, qc_rule_set_ref, qc_rule_set_version,
                   opened_by, opened_at
            from qc.qc_run
            where organization_group_id = @organization_group_id and qc_run_id = @qc_run_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("qc_run_id", qcRunId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            objectScope = new QcObjectContext(reader.GetString(0), reader.GetString(1));
            batchId = reader.GetString(2);
            batchVersion = reader.GetInt64(3);
            batchGateDecision = reader.GetString(4);
            batchGateRuleSetVersion = reader.GetString(5);
            method = new QcVersionedReference(reader.GetString(6), reader.GetInt64(7));
            qcRuleSet = new QcVersionedReference(reader.GetString(8), reader.GetInt64(9));
            openedBy = reader.GetString(10);
            openedAt = reader.GetFieldValue<DateTimeOffset>(11);
        }

        var results = new List<QcResultEntry>();
        await using (var command = new NpgsqlCommand("""
            select qc_result_id, rule_ref, rule_version, control_type, observed_value,
                   verdict, verdict_basis, recorded_by, recorded_at
            from qc.qc_result where qc_run_id = @id order by recorded_at, qc_result_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", qcRunId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new QcResultEntry(
                    reader.GetGuid(0).ToString("N"), qcRunId.ToString("N"),
                    new QcVersionedReference(reader.GetString(1), reader.GetInt64(2)),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                    reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8)));
            }
        }

        string? verdictState = null;
        await using (var command = new NpgsqlCommand(
            "select state from qc.qc_verdict where qc_run_id = @id", connection, transaction))
        {
            command.Parameters.AddWithValue("id", qcRunId);
            verdictState = await command.ExecuteScalarAsync(cancellationToken) as string;
        }

        var impact = new List<QcImpactEntry>();
        await using (var command = new NpgsqlCommand("""
            select impact_id, target_type, target_id, target_version, recorded_by, recorded_at
            from qc.qc_impact where qc_run_id = @id order by recorded_at, impact_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", qcRunId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                impact.Add(new QcImpactEntry(
                    reader.GetGuid(0).ToString("N"), qcRunId.ToString("N"),
                    reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
                    reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        var gates = new List<QcReleaseGateEntry>();
        await using (var command = new NpgsqlCommand("""
            select gate_id, kind, evidence_ref, evidence_version, satisfied_by, satisfied_at
            from qc.qc_release_gate where qc_run_id = @id order by satisfied_at, gate_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", qcRunId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                gates.Add(new QcReleaseGateEntry(
                    reader.GetGuid(0).ToString("N"), qcRunId.ToString("N"), reader.GetString(1),
                    new QcVersionedReference(reader.GetString(2), reader.GetInt64(3)),
                    reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        var deviations = new List<QcDeviationApprovalEntry>();
        await using (var command = new NpgsqlCommand("""
            select deviation_id, approval_ref, approval_version, reason, approved_by, approved_at
            from qc.qc_deviation_approval where qc_run_id = @id order by approved_at, deviation_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", qcRunId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                deviations.Add(new QcDeviationApprovalEntry(
                    reader.GetGuid(0).ToString("N"), qcRunId.ToString("N"),
                    new QcVersionedReference(reader.GetString(1), reader.GetInt64(2)),
                    reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        string? releasedBy = null;
        DateTimeOffset? releasedAt = null;
        await using (var command = new NpgsqlCommand(
            "select released_by, released_at from qc.qc_release where qc_run_id = @id", connection, transaction))
        {
            command.Parameters.AddWithValue("id", qcRunId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                releasedBy = reader.GetString(0);
                releasedAt = reader.GetFieldValue<DateTimeOffset>(1);
            }
        }

        var state = releasedBy is not null
            ? QcRunStates.Released
            : verdictState ?? QcRunStates.Open;
        var version = 1L + results.Count + (verdictState is null ? 0 : 1) + impact.Count
                      + gates.Count + deviations.Count + (releasedBy is null ? 0 : 1);
        return new QcRunResult(
            qcRunId.ToString("N"), version, state, QcContract.RuleSetVersion, objectScope!,
            batchId!, batchVersion, batchGateDecision!, batchGateRuleSetVersion!,
            method!, qcRuleSet!, results, impact, gates, deviations,
            releasedBy, releasedAt, openedBy!, openedAt);
    }

    public Task WriteReadAuditAsync(
        string qcRunId, long runVersion, string organizationGroupId, string actorId,
        string action, string correlationId, DateTimeOffset now, CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, qcRunId, action, QcContract.RuleSetVersion,
            runVersion.ToString(), runVersion.ToString(), correlationId, now), cancellationToken);

    private async Task WritePlatformEvidenceAsync(
        string action, string objectId, string organizationGroupId, string actorId,
        string eventId, string messageType, string correlationId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, objectId, action, QcContract.RuleSetVersion,
            null, "1", correlationId, now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("QC.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class QcAttemptAuditWriter(QcDataSource dataSource)
{
    public async Task WriteAsync(
        string commandType, string? actorId, string organizationGroupId, string targetHash,
        string correlationId, string outcome, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.Value.CreateCommand("""
            insert into qc.audit_attempt (
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
