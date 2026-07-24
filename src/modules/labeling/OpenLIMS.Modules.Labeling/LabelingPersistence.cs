using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Labeling;

internal sealed class LabelingDataSource : IAsyncDisposable
{
    public LabelingDataSource(LabelingPersistenceOptions options) =>
        DataSource = NpgsqlDataSource.Create(options.ConnectionString);

    public NpgsqlDataSource DataSource { get; }

    public ValueTask DisposeAsync() => DataSource.DisposeAsync();
}

internal enum LabelIdempotencyKind
{
    New,
    Replay,
    Conflict
}

internal sealed record LabelIdempotencyReservation(
    LabelIdempotencyKind Kind,
    CreateLabelJobsResult? Result = null);

internal sealed record LabelPrintJobRecord(
    Guid Id,
    string OrganizationGroupId,
    string ActorId,
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ObjectType,
    Guid ObjectId,
    long ObjectVersion,
    string BusinessNumber,
    string BarcodePayload,
    string TemplateVersion,
    string PrinterId,
    string PrinterConfigurationVersion,
    string PrinterHost,
    int PrinterPort,
    byte[] RenderedPayload,
    bool IsReprint,
    string? Reason,
    string Status,
    int AttemptCount,
    string CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal enum LabelDispatchOutcome
{
    Dispatched,
    DefiniteFailure,
    Unknown
}

internal sealed class LabelingStore(
    IPostgresTransactionAccessor transactionAccessor,
    LabelingDataSource dataSource)
{
    public async Task<LabelIdempotencyReservation> ReserveIdempotencyAsync(
        string organizationGroupId,
        string actorId,
        string keyHash,
        string requestHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var insert = new NpgsqlCommand("""
            insert into labeling.idempotency (
              organization_group_id, key_hash, request_hash, actor_id, created_at
            ) values (
              @organization_group_id, @key_hash, @request_hash, @actor_id, @created_at
            ) on conflict (organization_group_id, key_hash) do nothing
            """, connection, transaction);
        insert.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        insert.Parameters.AddWithValue("key_hash", keyHash);
        insert.Parameters.AddWithValue("request_hash", requestHash);
        insert.Parameters.AddWithValue("actor_id", actorId);
        insert.Parameters.AddWithValue("created_at", now);
        if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return new LabelIdempotencyReservation(LabelIdempotencyKind.New);
        }

        await using var select = new NpgsqlCommand("""
            select request_hash, response_json
            from labeling.idempotency
            where organization_group_id = @organization_group_id and key_hash = @key_hash
            for update
            """, connection, transaction);
        select.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        select.Parameters.AddWithValue("key_hash", keyHash);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("LABEL.IDEMPOTENCY_RESERVATION_MISSING");
        }

        if (!string.Equals(reader.GetString(0), requestHash, StringComparison.Ordinal))
        {
            return new LabelIdempotencyReservation(LabelIdempotencyKind.Conflict);
        }

        if (reader.IsDBNull(1))
        {
            throw new InvalidOperationException("LABEL.IDEMPOTENCY_RESULT_MISSING");
        }

        return new LabelIdempotencyReservation(
            LabelIdempotencyKind.Replay,
            JsonSerializer.Deserialize<CreateLabelJobsResult>(reader.GetString(1), LabelingJson.Options)
                ?? throw new InvalidOperationException("LABEL.IDEMPOTENCY_RESULT_INVALID"));
    }

    public async Task CompleteIdempotencyAsync(
        string organizationGroupId,
        string keyHash,
        CreateLabelJobsResult result,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            update labeling.idempotency
            set response_json = @response_json
            where organization_group_id = @organization_group_id and key_hash = @key_hash
            """, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("response_json", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(result, LabelingJson.Options)
        });
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("key_hash", keyHash);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("LABEL.IDEMPOTENCY_COMPLETION_FAILED");
        }
    }

    public async Task<LabelPrintJobResult> InsertInitialJobAsync(
        Guid printJobId,
        ReceivingLabelObjectSnapshot snapshot,
        LogicalLabelPrinter printer,
        string actorId,
        string keyHash,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rendered = TsplLabelRenderer.Render(snapshot);
        return await InsertJobAsync(
            printJobId,
            snapshot,
            printer,
            actorId,
            keyHash,
            correlationId,
            now,
            rendered,
            isReprint: false,
            reason: null,
            sourcePrintJobId: null,
            successfulReprintCount: 0,
            cancellationToken);
    }

    public async Task<LabelPrintJobResult> InsertReprintJobAsync(
        Guid printJobId,
        Guid sourcePrintJobId,
        ReceivingLabelObjectSnapshot snapshot,
        LogicalLabelPrinter printer,
        string actorId,
        string reason,
        string keyHash,
        string correlationId,
        DateTimeOffset now,
        bool hasOverrideCapability,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using (var objectLock = new NpgsqlCommand(
                         "select pg_advisory_xact_lock(hashtext(@lock_key))",
                         connection,
                         transaction))
        {
            objectLock.Parameters.AddWithValue(
                "lock_key",
                $"{snapshot.OrganizationGroupId}:{snapshot.ObjectType}:{snapshot.ObjectId}");
            await objectLock.ExecuteNonQueryAsync(cancellationToken);
        }

        var successfulReprintCount = await CountSuccessfulReprintsAsync(
            connection,
            transaction,
            snapshot.OrganizationGroupId,
            snapshot.ObjectType,
            Guid.Parse(snapshot.ObjectId),
            cancellationToken);
        if (successfulReprintCount >= 3 && !hasOverrideCapability)
        {
            throw new LabelingDomainException(LabelingErrorCodes.ReprintLimitOverrideRequired);
        }
        return await InsertJobAsync(
            printJobId,
            snapshot,
            printer,
            actorId,
            keyHash,
            correlationId,
            now,
            TsplLabelRenderer.Render(snapshot),
            isReprint: true,
            reason,
            sourcePrintJobId,
            successfulReprintCount,
            cancellationToken);
    }

    public async Task<int> CountSuccessfulReprintsAsync(
        string organizationGroupId,
        string objectType,
        Guid objectId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.DataSource.CreateCommand("""
            select count(*)
            from labeling.print_job
            where organization_group_id = @organization_group_id
              and object_type = @object_type
              and object_id = @object_id
              and is_reprint = true
              and status in ('DISPATCHED', 'VERIFIED')
            """);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("object_type", objectType);
        command.Parameters.AddWithValue("object_id", objectId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<LabelPrintJobRecord?> GetRecordAsync(
        Guid printJobId,
        string organizationGroupId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.DataSource.CreateCommand(JobSelectSql + "\n" + """
            where id = @id and organization_group_id = @organization_group_id
            """);
        command.Parameters.AddWithValue("id", printJobId);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    public async Task<LabelPrintJobRecord?> ClaimNextAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var staleSelect = new NpgsqlCommand(JobSelectSql + "\n" + """
            where status = 'DISPATCHING' and dispatch_lease_expires_at <= @now
            order by dispatch_lease_expires_at
            limit 1
            for update skip locked
            """, connection, transaction);
        staleSelect.Parameters.AddWithValue("now", now);
        LabelPrintJobRecord? staleJob;
        await using (var staleReader = await staleSelect.ExecuteReaderAsync(cancellationToken))
        {
            staleJob = await staleReader.ReadAsync(cancellationToken) ? ReadJob(staleReader) : null;
        }

        if (staleJob is not null)
        {
            await using var recover = new NpgsqlCommand("""
                update labeling.print_job
                set status = 'UNKNOWN', updated_at = @now,
                    dispatch_lease_expires_at = null,
                    last_error_code = 'LABEL.WORKER_INTERRUPTED'
                where id = @id and status = 'DISPATCHING'
                """, connection, transaction);
            recover.Parameters.AddWithValue("id", staleJob.Id);
            recover.Parameters.AddWithValue("now", now);
            if (await recover.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("LABEL.INTERRUPTED_DISPATCH_RECOVERY_FAILED");
            }

            await InsertEventAsync(
                connection,
                transaction,
                staleJob.Id,
                "PRINT_DELIVERY_UNKNOWN",
                staleJob.ActorId,
                staleJob.Reason,
                now,
                new { errorCode = "LABEL.WORKER_INTERRUPTED", staleJob.AttemptCount },
                cancellationToken);
            await InsertAuditAndOutboxAsync(
                connection,
                transaction,
                staleJob,
                "PRINT_DELIVERY_UNKNOWN",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await using var select = new NpgsqlCommand(JobSelectSql + "\n" + """
            where status = 'REQUESTED'
              and (next_attempt_at is null or next_attempt_at <= @now)
            order by created_at
            limit 1
            for update skip locked
            """, connection, transaction);
        select.Parameters.AddWithValue("now", now);
        LabelPrintJobRecord? job;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            job = await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
        }

        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await using var update = new NpgsqlCommand("""
            update labeling.print_job
            set status = 'DISPATCHING', attempt_count = attempt_count + 1,
                updated_at = @now, next_attempt_at = null,
                dispatch_lease_expires_at = @lease_expires_at
            where id = @id and status = 'REQUESTED'
            """, connection, transaction);
        update.Parameters.AddWithValue("id", job.Id);
        update.Parameters.AddWithValue("now", now);
        update.Parameters.AddWithValue("lease_expires_at", now.AddSeconds(30));
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("LABEL.PRINT_JOB_CLAIM_FAILED");
        }

        await InsertEventAsync(
            connection,
            transaction,
            job.Id,
            "PRINT_DISPATCHING",
            job.ActorId,
            null,
            now,
            new { attempt = job.AttemptCount + 1 },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job with
        {
            Status = LabelPrintJobStates.Dispatching,
            AttemptCount = job.AttemptCount + 1,
            UpdatedAt = now
        };
    }

    public async Task CompleteDispatchAsync(
        LabelPrintJobRecord job,
        LabelDispatchOutcome outcome,
        string? errorCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var nextStatus = outcome switch
        {
            LabelDispatchOutcome.Dispatched => LabelPrintJobStates.Dispatched,
            LabelDispatchOutcome.Unknown => LabelPrintJobStates.Unknown,
            LabelDispatchOutcome.DefiniteFailure when job.AttemptCount < 3 => LabelPrintJobStates.Requested,
            _ => LabelPrintJobStates.Failed
        };
        var nextAttemptAt = nextStatus == LabelPrintJobStates.Requested ? now.AddSeconds(5) : (DateTimeOffset?)null;

        await using var connection = await dataSource.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var update = new NpgsqlCommand("""
            update labeling.print_job
            set status = @status, updated_at = @now, next_attempt_at = @next_attempt_at,
                last_error_code = @last_error_code, dispatch_lease_expires_at = null,
                dispatched_at = case when @status = 'DISPATCHED' then @now else dispatched_at end
            where id = @id and status = 'DISPATCHING'
            """, connection, transaction);
        update.Parameters.AddWithValue("status", nextStatus);
        update.Parameters.AddWithValue("now", now);
        update.Parameters.AddWithValue("next_attempt_at", (object?)nextAttemptAt ?? DBNull.Value);
        update.Parameters.AddWithValue("last_error_code", (object?)errorCode ?? DBNull.Value);
        update.Parameters.AddWithValue("id", job.Id);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("LABEL.PRINT_JOB_COMPLETION_FAILED");
        }

        var eventType = nextStatus switch
        {
            LabelPrintJobStates.Dispatched => "PRINT_DISPATCHED",
            LabelPrintJobStates.Unknown => "PRINT_DELIVERY_UNKNOWN",
            LabelPrintJobStates.Failed => "PRINT_FAILED",
            _ => "PRINT_RETRY_SCHEDULED"
        };
        await InsertEventAsync(
            connection,
            transaction,
            job.Id,
            eventType,
            job.ActorId,
            job.Reason,
            now,
            new { job.AttemptCount, errorCode },
            cancellationToken);
        await InsertAuditAndOutboxAsync(connection, transaction, job, eventType, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string> VerifyLatestAsync(
        ReceivingLabelObjectSnapshot snapshot,
        string actorId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var select = new NpgsqlCommand(JobSelectSql + "\n" + """
            where organization_group_id = @organization_group_id
              and object_type = @object_type and object_id = @object_id
              and status in ('DISPATCHED', 'UNKNOWN', 'VERIFIED')
            order by created_at desc
            limit 1
            for update
            """, connection, transaction);
        select.Parameters.AddWithValue("organization_group_id", snapshot.OrganizationGroupId);
        select.Parameters.AddWithValue("object_type", snapshot.ObjectType);
        select.Parameters.AddWithValue("object_id", Guid.Parse(snapshot.ObjectId));
        LabelPrintJobRecord? job;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            job = await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
        }

        if (job is null)
        {
            await InsertAuthorizedScanAsync(
                connection,
                transaction,
                snapshot,
                actorId,
                correlationId,
                "NOT_PRINTED",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return "NOT_PRINTED";
        }

        if (job.Status == LabelPrintJobStates.Verified)
        {
            await InsertAuthorizedScanAsync(
                connection,
                transaction,
                snapshot,
                actorId,
                correlationId,
                LabelPrintJobStates.Verified,
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return LabelPrintJobStates.Verified;
        }

        await using var update = new NpgsqlCommand("""
            update labeling.print_job
            set status = 'VERIFIED', verified_at = @now, updated_at = @now,
                correlation_id = @correlation_id
            where id = @id and status in ('DISPATCHED', 'UNKNOWN')
            """, connection, transaction);
        update.Parameters.AddWithValue("id", job.Id);
        update.Parameters.AddWithValue("now", now);
        update.Parameters.AddWithValue("correlation_id", correlationId);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("LABEL.PRINT_VERIFICATION_FAILED");
        }

        await InsertEventAsync(
            connection,
            transaction,
            job.Id,
            "PRINT_VERIFIED_BY_SCAN",
            actorId,
            null,
            now,
            new { previousStatus = job.Status },
            cancellationToken);
        await InsertAuditAndOutboxAsync(
            connection,
            transaction,
            job with { ActorId = actorId, CorrelationId = correlationId },
            "PRINT_VERIFIED_BY_SCAN",
            now,
            cancellationToken);
        await InsertAuthorizedScanAsync(
            connection,
            transaction,
            snapshot,
            actorId,
            correlationId,
            LabelPrintJobStates.Verified,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return LabelPrintJobStates.Verified;
    }

    public async Task WriteScanAttemptAsync(
        string? actorId,
        string organizationGroupId,
        string payloadHash,
        string decisionCode,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.DataSource.CreateCommand("""
            insert into labeling.scan_attempt (
              id, actor_id, organization_group_id, payload_hash, decision_code,
              correlation_id, occurred_at
            ) values (
              @id, @actor_id, @organization_group_id, @payload_hash, @decision_code,
              @correlation_id, @occurred_at
            )
            """);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("actor_id", (object?)actorId ?? DBNull.Value);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("payload_hash", payloadHash);
        command.Parameters.AddWithValue("decision_code", decisionCode);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("occurred_at", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuthorizedScanAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReceivingLabelObjectSnapshot snapshot,
        string actorId,
        string correlationId,
        string verificationStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            snapshot.ObjectType,
            snapshot.ObjectId,
            snapshot.ObjectVersion,
            snapshot.BusinessNumber,
            snapshot.State,
            verificationStatus
        }, LabelingJson.Options);
        await using var audit = new NpgsqlCommand("""
            insert into labeling.audit_pending (
              id, event_type, actor_id, organization_group_id, legal_entity_id,
              laboratory_id, customer_id, service_order_id, object_type, object_id,
              print_job_id, correlation_id, rule_version, occurred_at, payload_json
            ) values (
              @id, 'LABEL_SCAN_RESOLVED', @actor_id, @organization_group_id, @legal_entity_id,
              @laboratory_id, @customer_id, @service_order_id, @object_type, @object_id,
              null, @correlation_id, 'ATC-REC-002@2.0.0', @occurred_at, @payload_json
            )
            """, connection, transaction);
        audit.Parameters.AddWithValue("id", eventId);
        audit.Parameters.AddWithValue("actor_id", actorId);
        audit.Parameters.AddWithValue("organization_group_id", snapshot.OrganizationGroupId);
        audit.Parameters.AddWithValue("legal_entity_id", snapshot.LegalEntityId);
        audit.Parameters.AddWithValue("laboratory_id", snapshot.LaboratoryId);
        audit.Parameters.AddWithValue("customer_id", snapshot.CustomerId);
        audit.Parameters.AddWithValue("service_order_id", snapshot.ServiceOrderId);
        audit.Parameters.AddWithValue("object_type", snapshot.ObjectType);
        audit.Parameters.AddWithValue("object_id", Guid.Parse(snapshot.ObjectId));
        audit.Parameters.AddWithValue("correlation_id", correlationId);
        audit.Parameters.AddWithValue("occurred_at", now);
        audit.Parameters.Add(new NpgsqlParameter("payload_json", NpgsqlDbType.Jsonb) { Value = payload });
        await audit.ExecuteNonQueryAsync(cancellationToken);

        await using var outbox = new NpgsqlCommand("""
            insert into labeling.outbox (
              id, event_type, aggregate_type, aggregate_id, occurred_at, payload_json
            ) values (
              @id, 'LABEL_SCAN_RESOLVED', @aggregate_type, @aggregate_id, @occurred_at, @payload_json
            )
            """, connection, transaction);
        outbox.Parameters.AddWithValue("id", eventId);
        outbox.Parameters.AddWithValue("aggregate_type", snapshot.ObjectType);
        outbox.Parameters.AddWithValue("aggregate_id", Guid.Parse(snapshot.ObjectId));
        outbox.Parameters.AddWithValue("occurred_at", now);
        outbox.Parameters.Add(new NpgsqlParameter("payload_json", NpgsqlDbType.Jsonb) { Value = payload });
        await outbox.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<LabelPrintJobResult> InsertJobAsync(
        Guid printJobId,
        ReceivingLabelObjectSnapshot snapshot,
        LogicalLabelPrinter printer,
        string actorId,
        string keyHash,
        string correlationId,
        DateTimeOffset now,
        byte[] rendered,
        bool isReprint,
        string? reason,
        Guid? sourcePrintJobId,
        int successfulReprintCount,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var barcodePayload = LabelBarcodeCodec.Create(snapshot.ObjectType, Guid.Parse(snapshot.OpaqueReference));
        var templateVersion = LabelTemplateVersions.ForObjectType(snapshot.ObjectType);
        await using var insert = new NpgsqlCommand("""
            insert into labeling.print_job (
              id, organization_group_id, actor_id, legal_entity_id, laboratory_id,
              customer_id, service_order_id, object_type, object_id, object_version,
              business_number, barcode_payload, template_version, printer_id,
              printer_configuration_version, printer_host, printer_port, protocol,
              rendered_payload, copies, is_reprint, reason, source_print_job_id,
              idempotency_key_hash, status, correlation_id, created_at, updated_at
            ) values (
              @id, @organization_group_id, @actor_id, @legal_entity_id, @laboratory_id,
              @customer_id, @service_order_id, @object_type, @object_id, @object_version,
              @business_number, @barcode_payload, @template_version, @printer_id,
              @printer_configuration_version, @printer_host, @printer_port, 'TSPL2',
              @rendered_payload, 1, @is_reprint, @reason, @source_print_job_id,
              @idempotency_key_hash, 'REQUESTED', @correlation_id, @created_at, @updated_at
            )
            """, connection, transaction);
        insert.Parameters.AddWithValue("id", printJobId);
        insert.Parameters.AddWithValue("organization_group_id", snapshot.OrganizationGroupId);
        insert.Parameters.AddWithValue("actor_id", actorId);
        insert.Parameters.AddWithValue("legal_entity_id", snapshot.LegalEntityId);
        insert.Parameters.AddWithValue("laboratory_id", snapshot.LaboratoryId);
        insert.Parameters.AddWithValue("customer_id", snapshot.CustomerId);
        insert.Parameters.AddWithValue("service_order_id", snapshot.ServiceOrderId);
        insert.Parameters.AddWithValue("object_type", snapshot.ObjectType);
        insert.Parameters.AddWithValue("object_id", Guid.Parse(snapshot.ObjectId));
        insert.Parameters.AddWithValue("object_version", snapshot.ObjectVersion);
        insert.Parameters.AddWithValue("business_number", snapshot.BusinessNumber);
        insert.Parameters.AddWithValue("barcode_payload", barcodePayload);
        insert.Parameters.AddWithValue("template_version", templateVersion);
        insert.Parameters.AddWithValue("printer_id", printer.PrinterId);
        insert.Parameters.AddWithValue("printer_configuration_version", printer.ConfigurationVersion);
        insert.Parameters.AddWithValue("printer_host", printer.Host);
        insert.Parameters.AddWithValue("printer_port", printer.Port);
        insert.Parameters.AddWithValue("rendered_payload", rendered);
        insert.Parameters.AddWithValue("is_reprint", isReprint);
        insert.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        insert.Parameters.AddWithValue("source_print_job_id", (object?)sourcePrintJobId ?? DBNull.Value);
        insert.Parameters.AddWithValue("idempotency_key_hash", keyHash);
        insert.Parameters.AddWithValue("correlation_id", correlationId);
        insert.Parameters.AddWithValue("created_at", now);
        insert.Parameters.AddWithValue("updated_at", now);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        var record = new LabelPrintJobRecord(
            printJobId,
            snapshot.OrganizationGroupId,
            actorId,
            snapshot.LegalEntityId,
            snapshot.LaboratoryId,
            snapshot.CustomerId,
            snapshot.ServiceOrderId,
            snapshot.ObjectType,
            Guid.Parse(snapshot.ObjectId),
            snapshot.ObjectVersion,
            snapshot.BusinessNumber,
            barcodePayload,
            templateVersion,
            printer.PrinterId,
            printer.ConfigurationVersion,
            printer.Host,
            printer.Port,
            rendered,
            isReprint,
            reason,
            LabelPrintJobStates.Requested,
            0,
            correlationId,
            now,
            now);
        await InsertEventAsync(
            connection,
            transaction,
            printJobId,
            isReprint ? "LABEL_REPRINT_REQUESTED" : "LABEL_PRINT_REQUESTED",
            actorId,
            reason,
            now,
            new { templateVersion, printerId = printer.PrinterId, copies = 1 },
            cancellationToken);
        await InsertAuditAndOutboxAsync(
            connection,
            transaction,
            record,
            isReprint ? "LABEL_REPRINT_REQUESTED" : "LABEL_PRINT_REQUESTED",
            now,
            cancellationToken);
        return ToResult(record, successfulReprintCount);
    }

    private static async Task<int> CountSuccessfulReprintsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string organizationGroupId,
        string objectType,
        Guid objectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select count(*)
            from labeling.print_job
            where organization_group_id = @organization_group_id
              and object_type = @object_type and object_id = @object_id
              and is_reprint = true and status in ('DISPATCHED', 'VERIFIED')
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("object_type", objectType);
        command.Parameters.AddWithValue("object_id", objectId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid printJobId,
        string eventType,
        string actorId,
        string? reason,
        DateTimeOffset now,
        object details,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into labeling.print_event (
              id, print_job_id, event_type, actor_id, reason, occurred_at, details_json
            ) values (
              @id, @print_job_id, @event_type, @actor_id, @reason, @occurred_at, @details_json
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("print_job_id", printJobId);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("actor_id", actorId);
        command.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("occurred_at", now);
        command.Parameters.Add(new NpgsqlParameter("details_json", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(details, LabelingJson.Options)
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAndOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LabelPrintJobRecord job,
        string eventType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            job.ObjectType,
            objectId = job.ObjectId.ToString("N"),
            job.ObjectVersion,
            job.BusinessNumber,
            job.TemplateVersion,
            job.PrinterId,
            job.PrinterConfigurationVersion,
            job.IsReprint,
            job.Reason,
            job.AttemptCount,
            status = eventType
        }, LabelingJson.Options);
        await using var audit = new NpgsqlCommand("""
            insert into labeling.audit_pending (
              id, event_type, actor_id, organization_group_id, legal_entity_id,
              laboratory_id, customer_id, service_order_id, object_type, object_id,
              print_job_id, correlation_id, rule_version, occurred_at, payload_json
            ) values (
              @id, @event_type, @actor_id, @organization_group_id, @legal_entity_id,
              @laboratory_id, @customer_id, @service_order_id, @object_type, @object_id,
              @print_job_id, @correlation_id, 'ATC-REC-002@2.0.0', @occurred_at, @payload_json
            )
            """, connection, transaction);
        audit.Parameters.AddWithValue("id", eventId);
        audit.Parameters.AddWithValue("event_type", eventType);
        audit.Parameters.AddWithValue("actor_id", job.ActorId);
        audit.Parameters.AddWithValue("organization_group_id", job.OrganizationGroupId);
        audit.Parameters.AddWithValue("legal_entity_id", job.LegalEntityId);
        audit.Parameters.AddWithValue("laboratory_id", job.LaboratoryId);
        audit.Parameters.AddWithValue("customer_id", job.CustomerId);
        audit.Parameters.AddWithValue("service_order_id", job.ServiceOrderId);
        audit.Parameters.AddWithValue("object_type", job.ObjectType);
        audit.Parameters.AddWithValue("object_id", job.ObjectId);
        audit.Parameters.AddWithValue("print_job_id", job.Id);
        audit.Parameters.AddWithValue("correlation_id", job.CorrelationId);
        audit.Parameters.AddWithValue("occurred_at", now);
        audit.Parameters.Add(new NpgsqlParameter("payload_json", NpgsqlDbType.Jsonb) { Value = payload });
        await audit.ExecuteNonQueryAsync(cancellationToken);

        await using var outbox = new NpgsqlCommand("""
            insert into labeling.outbox (
              id, event_type, aggregate_type, aggregate_id, occurred_at, payload_json
            ) values (
              @id, @event_type, 'PrintJob', @aggregate_id, @occurred_at, @payload_json
            )
            """, connection, transaction);
        outbox.Parameters.AddWithValue("id", eventId);
        outbox.Parameters.AddWithValue("event_type", eventType);
        outbox.Parameters.AddWithValue("aggregate_id", job.Id);
        outbox.Parameters.AddWithValue("occurred_at", now);
        outbox.Parameters.Add(new NpgsqlParameter("payload_json", NpgsqlDbType.Jsonb) { Value = payload });
        await outbox.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LabelPrintJobResult ToResult(LabelPrintJobRecord record, int successfulReprintCount) => new(
        record.Id.ToString("N"),
        record.ObjectType,
        record.ObjectId.ToString("N"),
        record.BusinessNumber,
        record.TemplateVersion,
        record.PrinterId,
        record.Status,
        record.IsReprint,
        successfulReprintCount,
        record.CreatedAt,
        record.UpdatedAt);

    internal static LabelPrintJobResult ToResult(LabelPrintJobRecord record) =>
        ToResult(record, 0);

    private static LabelPrintJobRecord ReadJob(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetGuid(8),
        reader.GetInt64(9),
        reader.GetString(10),
        reader.GetString(11),
        reader.GetString(12),
        reader.GetString(13),
        reader.GetString(14),
        reader.GetString(15),
        reader.GetInt32(16),
        (byte[])reader[17],
        reader.GetBoolean(18),
        reader.IsDBNull(19) ? null : reader.GetString(19),
        reader.GetString(20),
        reader.GetInt32(21),
        reader.GetString(22),
        reader.GetFieldValue<DateTimeOffset>(23),
        reader.GetFieldValue<DateTimeOffset>(24));

    private const string JobSelectSql = """
        select id, organization_group_id, actor_id, legal_entity_id, laboratory_id,
               customer_id, service_order_id, object_type, object_id, object_version,
               business_number, barcode_payload, template_version, printer_id,
               printer_configuration_version, printer_host, printer_port, rendered_payload,
               is_reprint, reason, status, attempt_count, correlation_id, created_at, updated_at
        from labeling.print_job
        """;

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
        {
            throw new InvalidOperationException("LABEL.TRANSACTION_REQUIRED");
        }

        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}
