using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

/// <summary>
/// DEV-023 storage for the immutable version chain. Kept beside
/// <see cref="ReportStore"/> so the DEV-022 assembly paths stay untouched.
/// </summary>
internal sealed class ReportVersionStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task InsertVersionAsync(
        Guid reportId,
        int versionNumber,
        string canonicalContent,
        string contentHash,
        int lineCount,
        IssueReportRequest request,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into report.version_snapshot (
                snapshot_id, report_id, version_number, content_hash, canonical_content,
                line_count, created_by, created_at, event_id, correlation_id
            ) values (
                @snapshot_id, @report_id, @version_number, @content_hash, @canonical_content,
                @line_count, @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("snapshot_id", Guid.NewGuid());
            command.Parameters.AddWithValue("report_id", reportId);
            command.Parameters.AddWithValue("version_number", versionNumber);
            command.Parameters.AddWithValue("content_hash", contentHash);
            command.Parameters.AddWithValue("canonical_content", canonicalContent);
            command.Parameters.AddWithValue("line_count", lineCount);
            command.Parameters.AddWithValue("created_by", actorId);
            command.Parameters.AddWithValue("created_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand("""
            insert into report.version_signature (
                signature_id, report_id, version_number, content_hash,
                reauthentication_ref, reauthentication_version, signing_intent,
                signatory_id, signed_at, event_id, correlation_id
            ) values (
                @signature_id, @report_id, @version_number, @content_hash,
                @reauthentication_ref, @reauthentication_version, @signing_intent,
                @signatory_id, @signed_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("signature_id", Guid.NewGuid());
            command.Parameters.AddWithValue("report_id", reportId);
            command.Parameters.AddWithValue("version_number", versionNumber);
            command.Parameters.AddWithValue("content_hash", contentHash);
            command.Parameters.AddWithValue("reauthentication_ref", request.ReauthenticationRef.Id);
            command.Parameters.AddWithValue("reauthentication_version", request.ReauthenticationRef.Version);
            command.Parameters.AddWithValue("signing_intent", request.SigningIntent);
            command.Parameters.AddWithValue("signatory_id", request.SignatoryId);
            command.Parameters.AddWithValue("signed_at", now);
            command.Parameters.AddWithValue("event_id", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "ISSUE_REPORT_VERSION", reportId.ToString("N"), organizationGroupId, actorId,
            eventId, "Report.Issued.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertControlledActionAsync(
        Guid reportId,
        PerformControlledActionRequest request,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into report.controlled_action (
                action_id, report_id, version_number, kind,
                impact_assessment_ref, impact_assessment_version, superseding_report_number,
                reason, performed_by, performed_at, event_id, correlation_id
            ) values (
                @action_id, @report_id, @version_number, @kind,
                @impact_assessment_ref, @impact_assessment_version, @superseding_report_number,
                @reason, @performed_by, @performed_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("action_id", Guid.NewGuid());
            command.Parameters.AddWithValue("report_id", reportId);
            command.Parameters.AddWithValue("version_number", request.VersionNumber);
            command.Parameters.AddWithValue("kind", request.Kind);
            command.Parameters.AddWithValue("impact_assessment_ref", (object?)request.ImpactAssessmentRef?.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("impact_assessment_version", (object?)request.ImpactAssessmentRef?.Version ?? DBNull.Value);
            command.Parameters.AddWithValue("superseding_report_number", (object?)request.SupersedingReportNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("reason", request.Reason);
            command.Parameters.AddWithValue("performed_by", actorId);
            command.Parameters.AddWithValue("performed_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var messageType = request.Kind switch
        {
            ReportControlledActionKinds.Correction => "Report.Corrected.v1",
            ReportControlledActionKinds.Supplement => "Report.Supplemented.v1",
            ReportControlledActionKinds.Withdrawal => "Report.Withdrawn.v1",
            ReportControlledActionKinds.Void => "Report.Voided.v1",
            _ => "Report.Superseded.v1"
        };
        await WritePlatformEvidenceAsync(
            $"REPORT_ACTION_{request.Kind}", reportId.ToString("N"), organizationGroupId, actorId,
            eventId, messageType, correlationId, now, cancellationToken);
    }

    public async Task<IReadOnlyList<ReportVersionSnapshotResult>> LoadSnapshotsAsync(
        Guid reportId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var snapshots = new List<ReportVersionSnapshotResult>();
        await using var command = new NpgsqlCommand("""
            select snapshot_id, version_number, content_hash, canonical_content,
                   line_count, created_by, created_at
            from report.version_snapshot where report_id = @id order by version_number
            """, connection, transaction);
        command.Parameters.AddWithValue("id", reportId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            snapshots.Add(new ReportVersionSnapshotResult(
                reader.GetGuid(0).ToString("N"), reportId.ToString("N"), reader.GetInt32(1),
                reader.GetString(2), reader.GetString(3), reader.GetInt32(4),
                reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return snapshots;
    }

    public async Task<IReadOnlyList<ReportSignatureResult>> LoadSignaturesAsync(
        Guid reportId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var signatures = new List<ReportSignatureResult>();
        await using var command = new NpgsqlCommand("""
            select signature_id, version_number, content_hash, reauthentication_ref,
                   reauthentication_version, signing_intent, signatory_id, signed_at
            from report.version_signature where report_id = @id order by version_number
            """, connection, transaction);
        command.Parameters.AddWithValue("id", reportId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            signatures.Add(new ReportSignatureResult(
                reader.GetGuid(0).ToString("N"), reportId.ToString("N"), reader.GetInt32(1),
                reader.GetString(2),
                new ReportVersionedReference(reader.GetString(3), reader.GetInt64(4)),
                reader.GetString(5), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return signatures;
    }

    public async Task<IReadOnlyList<ReportControlledActionResult>> LoadActionsAsync(
        Guid reportId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var actions = new List<ReportControlledActionResult>();
        await using var command = new NpgsqlCommand("""
            select action_id, version_number, kind, impact_assessment_ref, impact_assessment_version,
                   superseding_report_number, reason, performed_by, performed_at
            from report.controlled_action where report_id = @id
            order by version_number, performed_at, action_id
            """, connection, transaction);
        command.Parameters.AddWithValue("id", reportId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            actions.Add(new ReportControlledActionResult(
                reader.GetGuid(0).ToString("N"), reportId.ToString("N"), reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : new ReportVersionedReference(reader.GetString(3), reader.GetInt64(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8)));
        }

        return actions;
    }

    public Task WriteReadAuditAsync(
        string reportId, string organizationGroupId, string actorId,
        string action, string correlationId, DateTimeOffset now, CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, reportId, action, ReportContract.RuleSetVersion,
            "1", "1", correlationId, now), cancellationToken);

    private async Task WritePlatformEvidenceAsync(
        string action, string objectId, string organizationGroupId, string actorId,
        string eventId, string messageType, string correlationId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, objectId, action, ReportContract.RuleSetVersion,
            null, "1", correlationId, now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("RPT.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}
