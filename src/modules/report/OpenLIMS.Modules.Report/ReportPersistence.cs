using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

internal sealed class ReportDataSource : IAsyncDisposable
{
    public ReportDataSource(ReportPersistenceOptions options) =>
        Value = NpgsqlDataSource.Create(options.ConnectionString);

    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed record ReportAdoptionFacts(string TargetId, string RuleSetVersion, long GroupVersion);

internal sealed class ReportStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireReportLockAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
        command.Parameters.AddWithValue("key", $"openlims.report.{reportId:N}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertReportAsync(
        Guid reportId,
        string organizationGroupId,
        CreateReportRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into report.report (
                report_id, organization_group_id, legal_entity_id, laboratory_id,
                customer_id, service_order_id, product_category, report_number,
                rule_set_version, created_by, created_at, event_id, correlation_id
            ) values (
                @report_id, @organization_group_id, @legal_entity_id, @laboratory_id,
                @customer_id, @service_order_id, @product_category, @report_number,
                @rule_set_version, @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("report_id", reportId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("legal_entity_id", request.ObjectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", request.ObjectScope.LaboratoryId);
            command.Parameters.AddWithValue("customer_id", request.ObjectScope.CustomerId);
            command.Parameters.AddWithValue("service_order_id", request.ObjectScope.ServiceOrderId);
            command.Parameters.AddWithValue("product_category", request.ObjectScope.ProductCategory);
            command.Parameters.AddWithValue("report_number", request.ReportNumber);
            command.Parameters.AddWithValue("rule_set_version", ReportContract.RuleSetVersion);
            command.Parameters.AddWithValue("created_by", actorId);
            command.Parameters.AddWithValue("created_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "CREATE_REPORT", reportId.ToString("N"), organizationGroupId, actorId,
            eventId, "Report.Created.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertLineAsync(
        Guid reportId,
        long reportVersion,
        AddReportLineRequest request,
        ReportAdoptionFacts adoption,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        var claim = request.AccreditationClaim;
        var lineId = Guid.NewGuid();
        await using (var command = new NpgsqlCommand("""
            insert into report.report_line (
                line_id, report_id, report_version, line_number,
                result_group_id, group_version, adoption_target_id, adoption_rule_set_version,
                scope_line_id, scope_partition,
                batch_id, allocation_id, received_item_id,
                requirement_snapshot_ref, requirement_snapshot_version,
                accreditation_ref, accreditation_version, accreditation_sha256,
                site_id, method_ref, method_version, product_matrix, parameter_range,
                accreditation_valid_until, signatory_id, claims_accreditation,
                subcontracting_ref, subcontracting_version,
                instrument_file_id, instrument_file_version,
                scope_matrix_id, scope_matrix_version, received_item_version,
                allocation_version, batch_version,
                added_by, added_at, event_id, correlation_id
            ) values (
                @line_id, @report_id, @report_version, @line_number,
                @result_group_id, @group_version, @adoption_target_id, @adoption_rule_set_version,
                @scope_line_id, @scope_partition,
                @batch_id, @allocation_id, @received_item_id,
                @requirement_snapshot_ref, @requirement_snapshot_version,
                @accreditation_ref, @accreditation_version, @accreditation_sha256,
                @site_id, @method_ref, @method_version, @product_matrix, @parameter_range,
                @accreditation_valid_until, @signatory_id, @claims_accreditation,
                @subcontracting_ref, @subcontracting_version,
                @instrument_file_id, @instrument_file_version,
                @scope_matrix_id, @scope_matrix_version, @received_item_version,
                @allocation_version, @batch_version,
                @added_by, @added_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("line_id", lineId);
            command.Parameters.AddWithValue("report_id", reportId);
            command.Parameters.AddWithValue("report_version", reportVersion);
            command.Parameters.AddWithValue("line_number", request.LineNumber);
            command.Parameters.AddWithValue("result_group_id", request.ResultGroupId);
            command.Parameters.AddWithValue("group_version", adoption.GroupVersion);
            command.Parameters.AddWithValue("adoption_target_id", adoption.TargetId);
            command.Parameters.AddWithValue("adoption_rule_set_version", adoption.RuleSetVersion);
            command.Parameters.AddWithValue("scope_line_id", request.ScopeLineId);
            command.Parameters.AddWithValue("scope_partition", request.ScopePartition);
            command.Parameters.AddWithValue("batch_id", request.TraceRefs.BatchId);
            command.Parameters.AddWithValue("allocation_id", request.TraceRefs.AllocationId);
            command.Parameters.AddWithValue("received_item_id", request.TraceRefs.ReceivedItemId);
            command.Parameters.AddWithValue("requirement_snapshot_ref", request.TraceRefs.RequirementSnapshot.Id);
            command.Parameters.AddWithValue("requirement_snapshot_version", request.TraceRefs.RequirementSnapshot.Version);
            command.Parameters.AddWithValue("accreditation_ref", request.AccreditationRef.Id);
            command.Parameters.AddWithValue("accreditation_version", request.AccreditationRef.Version);
            command.Parameters.AddWithValue("accreditation_sha256", request.AccreditationRef.Sha256);
            command.Parameters.AddWithValue("site_id", claim.SiteId);
            command.Parameters.AddWithValue("method_ref", claim.Method.Id);
            command.Parameters.AddWithValue("method_version", claim.Method.Version);
            command.Parameters.AddWithValue("product_matrix", claim.ProductMatrix);
            command.Parameters.AddWithValue("parameter_range", claim.ParameterRange);
            command.Parameters.AddWithValue("accreditation_valid_until", claim.ValidUntil);
            command.Parameters.AddWithValue("signatory_id", claim.SignatoryId);
            command.Parameters.AddWithValue("claims_accreditation", request.ClaimsAccreditation);
            command.Parameters.AddWithValue("subcontracting_ref", (object?)request.SubcontractingDisclosure?.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("subcontracting_version", (object?)request.SubcontractingDisclosure?.Version ?? DBNull.Value);
            command.Parameters.AddWithValue("instrument_file_id", request.InstrumentFileId);
            command.Parameters.AddWithValue("instrument_file_version", request.ExpectedInstrumentFileVersion);
            command.Parameters.AddWithValue("scope_matrix_id", request.ScopeMatrixId);
            command.Parameters.AddWithValue("scope_matrix_version", request.ExpectedScopeMatrixVersion);
            command.Parameters.AddWithValue("received_item_version", request.ExpectedReceivedItemVersion);
            command.Parameters.AddWithValue("allocation_version", request.ExpectedAllocationVersion);
            command.Parameters.AddWithValue("batch_version", request.ExpectedBatchVersion);
            command.Parameters.AddWithValue("added_by", actorId);
            command.Parameters.AddWithValue("added_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var run in request.QcRuns)
        {
            await using var command = new NpgsqlCommand("""
                insert into report.report_line_qc_run (line_id, qc_run_id, qc_run_version)
                values (@line_id, @qc_run_id, @qc_run_version)
                """, connection, transaction);
            command.Parameters.AddWithValue("line_id", lineId);
            command.Parameters.AddWithValue("qc_run_id", run.Id);
            command.Parameters.AddWithValue("qc_run_version", run.Version);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "ADD_REPORT_LINE", reportId.ToString("N"), organizationGroupId, actorId,
            eventId, "Report.LineAdded.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertGateEvaluationAsync(
        Guid reportId,
        long reportVersion,
        string decision,
        IReadOnlyList<ReportBlocker> blockers,
        IReadOnlyList<ReportLineAccreditationVerdict> verdicts,
        string signatoryId,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var evaluationId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into report.gate_evaluation (
                evaluation_id, report_id, report_version, decision, blocker_count,
                signatory_id, evaluated_by, evaluated_at, event_id, correlation_id
            ) values (
                @evaluation_id, @report_id, @report_version, @decision, @blocker_count,
                @signatory_id, @evaluated_by, @evaluated_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            command.Parameters.AddWithValue("report_id", reportId);
            command.Parameters.AddWithValue("report_version", reportVersion);
            command.Parameters.AddWithValue("decision", decision);
            command.Parameters.AddWithValue("blocker_count", blockers.Count);
            command.Parameters.AddWithValue("signatory_id", signatoryId);
            command.Parameters.AddWithValue("evaluated_by", actorId);
            command.Parameters.AddWithValue("evaluated_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var blocker in blockers)
        {
            await using var command = new NpgsqlCommand("""
                insert into report.gate_blocker (
                    blocker_id, evaluation_id, object_ref, object_type, source,
                    rule_set_version, reason_code, allowed_next_steps, line_number
                ) values (
                    @blocker_id, @evaluation_id, @object_ref, @object_type, @source,
                    @rule_set_version, @reason_code, @allowed_next_steps, @line_number
                )
                """, connection, transaction);
            command.Parameters.AddWithValue("blocker_id", Guid.NewGuid());
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            command.Parameters.AddWithValue("object_ref", blocker.ObjectRef);
            command.Parameters.AddWithValue("object_type", blocker.ObjectType);
            command.Parameters.AddWithValue("source", blocker.Source);
            command.Parameters.AddWithValue("rule_set_version", blocker.RuleSetVersion);
            command.Parameters.AddWithValue("reason_code", blocker.ReasonCode);
            command.Parameters.AddWithValue("allowed_next_steps", string.Join(';', blocker.AllowedNextSteps));
            command.Parameters.AddWithValue("line_number", (object?)blocker.LineNumber ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var verdict in verdicts)
        {
            await using var command = new NpgsqlCommand("""
                insert into report.accreditation_verdict (
                    verdict_id, evaluation_id, line_number, status, failed_dimensions
                ) values (
                    @verdict_id, @evaluation_id, @line_number, @status, @failed_dimensions
                )
                """, connection, transaction);
            command.Parameters.AddWithValue("verdict_id", Guid.NewGuid());
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            command.Parameters.AddWithValue("line_number", verdict.LineNumber);
            command.Parameters.AddWithValue("status", verdict.Status);
            command.Parameters.AddWithValue("failed_dimensions", string.Join(';', verdict.FailedDimensions));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "EVALUATE_REPORT_GATE", reportId.ToString("N"), organizationGroupId, actorId,
            eventId, "Report.GateEvaluated.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertApprovalSubmissionAsync(
        Guid reportId,
        long reportVersion,
        Guid evaluationId,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into report.approval_submission (
                report_id, submission_id, report_version, evaluation_id,
                submitted_by, submitted_at, event_id, correlation_id
            ) values (
                @report_id, @submission_id, @report_version, @evaluation_id,
                @submitted_by, @submitted_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("report_id", reportId);
            command.Parameters.AddWithValue("submission_id", Guid.NewGuid());
            command.Parameters.AddWithValue("report_version", reportVersion);
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            command.Parameters.AddWithValue("submitted_by", actorId);
            command.Parameters.AddWithValue("submitted_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "SUBMIT_REPORT_FOR_APPROVAL", reportId.ToString("N"), organizationGroupId, actorId,
            eventId, "Report.SubmittedForApproval.v1", correlationId, now, cancellationToken);
    }

    public async Task<ReportResult?> LoadReportAsync(
        string organizationGroupId, Guid reportId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        ReportObjectContext? objectScope = null;
        string? reportNumber = null, createdBy = null;
        DateTimeOffset createdAt = default;
        await using (var command = new NpgsqlCommand("""
            select legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                   report_number, created_by, created_at
            from report.report
            where organization_group_id = @organization_group_id and report_id = @report_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("report_id", reportId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            objectScope = new ReportObjectContext(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4));
            reportNumber = reader.GetString(5);
            createdBy = reader.GetString(6);
            createdAt = reader.GetFieldValue<DateTimeOffset>(7);
        }

        var lines = new List<ReportLineResult>();
        await using (var command = new NpgsqlCommand("""
            select line_id, line_number, result_group_id, group_version, adoption_target_id,
                   adoption_rule_set_version, scope_line_id, scope_partition,
                   batch_id, allocation_id, received_item_id,
                   requirement_snapshot_ref, requirement_snapshot_version,
                   accreditation_ref, accreditation_version, accreditation_sha256,
                   site_id, method_ref, method_version, product_matrix, parameter_range,
                   accreditation_valid_until, signatory_id, claims_accreditation,
                   subcontracting_ref, subcontracting_version, added_by, added_at,
                   instrument_file_id, instrument_file_version,
                   scope_matrix_id, scope_matrix_version, received_item_version,
                   allocation_version, batch_version
            from report.report_line where report_id = @id order by line_number
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", reportId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(new ReportLineResult(
                    reader.GetGuid(0).ToString("N"),
                    reportId.ToString("N"),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    new ReportTraceReferences(
                        reader.GetString(8), reader.GetString(9), reader.GetString(10),
                        new ReportVersionedReference(reader.GetString(11), reader.GetInt64(12))),
                    new ReportLineGateReferences(
                        [],
                        reader.GetString(28), reader.GetInt64(29),
                        reader.GetString(30), reader.GetInt64(31),
                        reader.GetInt64(32), reader.GetInt64(33), reader.GetInt64(34)),
                    new AccreditationScopeReference(reader.GetString(13), reader.GetInt64(14), reader.GetString(15)),
                    new AccreditationClaim(
                        reader.GetString(16),
                        new ReportVersionedReference(reader.GetString(17), reader.GetInt64(18)),
                        reader.GetString(19),
                        reader.GetString(20),
                        reader.GetFieldValue<DateTimeOffset>(21),
                        reader.GetString(22)),
                    reader.GetBoolean(23),
                    reader.IsDBNull(24)
                        ? null
                        : new ReportVersionedReference(reader.GetString(24), reader.GetInt64(25)),
                    reader.GetString(26),
                    reader.GetFieldValue<DateTimeOffset>(27)));
            }
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var citedRuns = new List<ReportVersionedReference>();
            await using var command = new NpgsqlCommand("""
                select qc_run_id, qc_run_version from report.report_line_qc_run
                where line_id = @id order by qc_run_id
                """, connection, transaction);
            command.Parameters.AddWithValue("id", Guid.Parse(lines[index].LineId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                citedRuns.Add(new ReportVersionedReference(reader.GetString(0), reader.GetInt64(1)));
            lines[index] = lines[index] with
            {
                GateRefs = lines[index].GateRefs with { QcRuns = citedRuns }
            };
        }

        var evaluations = new List<ReportGateEvaluationResult>();
        await using (var command = new NpgsqlCommand("""
            select evaluation_id, report_version, decision, signatory_id, evaluated_by, evaluated_at
            from report.gate_evaluation where report_id = @id order by evaluated_at, evaluation_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", reportId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                evaluations.Add(new ReportGateEvaluationResult(
                    reader.GetGuid(0).ToString("N"), reportId.ToString("N"), reader.GetInt64(1),
                    reader.GetString(2), [], [], reader.GetString(3),
                    reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        for (var index = 0; index < evaluations.Count; index++)
        {
            var evaluationId = Guid.Parse(evaluations[index].EvaluationId);
            var blockers = new List<ReportBlocker>();
            await using (var command = new NpgsqlCommand("""
                select object_ref, object_type, source, rule_set_version, reason_code,
                       allowed_next_steps, line_number
                from report.gate_blocker where evaluation_id = @id order by line_number nulls first, blocker_id
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("id", evaluationId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    blockers.Add(new ReportBlocker(
                        reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.GetString(3), reader.GetString(4),
                        reader.GetString(5).Split(';', StringSplitOptions.RemoveEmptyEntries),
                        reader.IsDBNull(6) ? null : reader.GetInt32(6)));
                }
            }

            var verdicts = new List<ReportLineAccreditationVerdict>();
            await using (var command = new NpgsqlCommand("""
                select line_number, status, failed_dimensions
                from report.accreditation_verdict where evaluation_id = @id order by line_number
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("id", evaluationId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    verdicts.Add(new ReportLineAccreditationVerdict(
                        reader.GetInt32(0), reader.GetString(1),
                        reader.GetString(2).Split(';', StringSplitOptions.RemoveEmptyEntries)));
                }
            }

            evaluations[index] = evaluations[index] with { Blockers = blockers, AccreditationVerdicts = verdicts };
        }

        var submitted = false;
        await using (var command = new NpgsqlCommand(
            "select 1 from report.approval_submission where report_id = @id", connection, transaction))
        {
            command.Parameters.AddWithValue("id", reportId);
            submitted = await command.ExecuteScalarAsync(cancellationToken) is not null;
        }

        var version = 1L + lines.Count + evaluations.Count + (submitted ? 1 : 0);
        return new ReportResult(
            reportId.ToString("N"), version,
            submitted ? ReportStates.PendingApproval : ReportStates.Draft,
            ReportContract.RuleSetVersion, objectScope!, reportNumber!,
            lines, evaluations, createdBy!, createdAt);
    }

    public Task WriteReadAuditAsync(
        string reportId, long reportVersion, string organizationGroupId, string actorId,
        string action, string correlationId, DateTimeOffset now, CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, reportId, action, ReportContract.RuleSetVersion,
            reportVersion.ToString(), reportVersion.ToString(), correlationId, now), cancellationToken);

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

internal sealed class ReportAttemptAuditWriter(ReportDataSource dataSource)
{
    public async Task WriteAsync(
        string commandType, string? actorId, string organizationGroupId, string targetHash,
        string correlationId, string outcome, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.Value.CreateCommand("""
            insert into report.audit_attempt (
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
