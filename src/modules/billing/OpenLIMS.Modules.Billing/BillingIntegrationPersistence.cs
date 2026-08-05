using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Billing;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Billing;

internal sealed record StoredBillingExportBatch(
    BillingExportBatchResult Batch,
    string OrganizationGroupId,
    string RequestHash);

internal sealed record StoredBillingHandoff(
    BillingHandoffResult Handoff,
    BillingObjectContext ObjectScope,
    string OrganizationGroupId);

internal sealed class BillingIntegrationStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireKeyLockAsync(string category, string key, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtext(@lock_key))", connection, transaction);
        command.Parameters.AddWithValue("lock_key", $"billing.{category}:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredBillingExportBatch?> LoadBatchByIdempotencyAsync(
        string organizationGroupId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select batch_id from billing.export_batch
            where organization_group_id = @organization_group_id and idempotency_key = @idempotency_key
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? await LoadBatchAsync(organizationGroupId, id, cancellationToken) : null;
    }

    public async Task<StoredBillingExportBatch?> LoadBatchAsync(
        string organizationGroupId,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        BillingObjectContext? scope = null;
        string? schemaVersion = null, currencyRef = null, contentHash = null;
        string? canonical = null, createdBy = null, requestHash = null;
        long currencyVersion = 0;
        decimal total = 0;
        DateTimeOffset createdAt = default;
        await using (var command = new NpgsqlCommand("""
            select legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                   export_schema_version, currency_ref, currency_version, total_amount,
                   content_hash, canonical_content, created_by, created_at, request_hash
            from billing.export_batch
            where organization_group_id = @organization_group_id and batch_id = @batch_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("batch_id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            scope = new BillingObjectContext(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4));
            schemaVersion = reader.GetString(5);
            currencyRef = reader.GetString(6);
            currencyVersion = reader.GetInt64(7);
            total = reader.GetDecimal(8);
            contentHash = reader.GetString(9);
            canonical = reader.GetString(10);
            createdBy = reader.GetString(11);
            createdAt = reader.GetFieldValue<DateTimeOffset>(12);
            requestHash = reader.GetString(13);
        }

        var items = new List<BillingExportItemResult>();
        await using (var command = new NpgsqlCommand("""
            select billing_evidence_id, result_group_id, group_version,
                   base_amount, adjustment_amount, net_amount, currency_ref, currency_version
            from billing.export_item where batch_id = @batch_id order by billing_evidence_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("batch_id", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new BillingExportItemResult(
                    reader.GetGuid(0).ToString("N"), reader.GetString(1), reader.GetInt64(2),
                    reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
                    new BillingVersionedReference(reader.GetString(6), reader.GetInt64(7))));
            }
        }

        return new StoredBillingExportBatch(
            new BillingExportBatchResult(
                batchId.ToString("N"), scope!, schemaVersion!, items, total,
                new BillingVersionedReference(currencyRef!, currencyVersion), contentHash!, canonical!,
                createdBy!, createdAt),
            organizationGroupId,
            requestHash!);
    }

    public async Task<BillingExportBatchResult> InsertBatchAsync(
        Guid batchId,
        string organizationGroupId,
        CreateBillingExportBatchRequest request,
        BillingObjectContext objectScope,
        IReadOnlyList<BillingExportItemResult> items,
        string canonicalContent,
        string contentHash,
        string requestHash,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var currency = items[0].Currency;
        var total = items.Sum(entry => entry.NetAmount);
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into billing.export_batch (
                batch_id, organization_group_id,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                export_schema_version, currency_ref, currency_version, total_amount,
                content_hash, canonical_content, request_hash, idempotency_key,
                created_by, created_at, event_id, correlation_id
            ) values (
                @batch_id, @organization_group_id,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @export_schema_version, @currency_ref, @currency_version, @total_amount,
                @content_hash, @canonical_content, @request_hash, @idempotency_key,
                @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("batch_id", batchId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("legal_entity_id", objectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", objectScope.LaboratoryId);
            command.Parameters.AddWithValue("customer_id", objectScope.CustomerId);
            command.Parameters.AddWithValue("service_order_id", objectScope.ServiceOrderId);
            command.Parameters.AddWithValue("product_category", objectScope.ProductCategory);
            command.Parameters.AddWithValue("export_schema_version", request.ExportSchemaVersion);
            command.Parameters.AddWithValue("currency_ref", currency.Id);
            command.Parameters.AddWithValue("currency_version", currency.Version);
            command.Parameters.AddWithValue("total_amount", total);
            command.Parameters.AddWithValue("content_hash", contentHash);
            command.Parameters.AddWithValue("canonical_content", canonicalContent);
            command.Parameters.AddWithValue("request_hash", requestHash);
            command.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);
            command.Parameters.AddWithValue("created_by", actorId);
            command.Parameters.AddWithValue("created_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in items)
        {
            await using var command = new NpgsqlCommand("""
                insert into billing.export_item (
                    batch_id, billing_evidence_id, result_group_id, group_version,
                    base_amount, adjustment_amount, net_amount, currency_ref, currency_version
                ) values (
                    @batch_id, @billing_evidence_id, @result_group_id, @group_version,
                    @base_amount, @adjustment_amount, @net_amount, @currency_ref, @currency_version
                )
                """, connection, transaction);
            command.Parameters.AddWithValue("batch_id", batchId);
            command.Parameters.AddWithValue("billing_evidence_id", Guid.Parse(item.BillingEvidenceId));
            command.Parameters.AddWithValue("result_group_id", item.ResultGroupId);
            command.Parameters.AddWithValue("group_version", item.GroupVersion);
            command.Parameters.AddWithValue("base_amount", item.BaseAmount);
            command.Parameters.AddWithValue("adjustment_amount", item.AdjustmentAmount);
            command.Parameters.AddWithValue("net_amount", item.NetAmount);
            command.Parameters.AddWithValue("currency_ref", item.Currency.Id);
            command.Parameters.AddWithValue("currency_version", item.Currency.Version);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "CREATE_BILLING_EXPORT_BATCH", batchId.ToString("N"), organizationGroupId, actorId,
            eventId, "Billing.ExportBatchCreated.v1", correlationId, now, cancellationToken,
            BillingContract.ExportRuleSetVersion);
        return new BillingExportBatchResult(
            batchId.ToString("N"), objectScope, request.ExportSchemaVersion, items, total,
            currency, contentHash, canonicalContent, actorId, now);
    }

    public async Task<StoredBillingHandoff?> LoadHandoffByIdempotencyAsync(
        string organizationGroupId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select handoff_id from billing.handoff
            where organization_group_id = @organization_group_id and idempotency_key = @idempotency_key
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? await LoadHandoffAsync(organizationGroupId, id, cancellationToken) : null;
    }

    public async Task<StoredBillingHandoff?> LoadHandoffAsync(
        string organizationGroupId,
        Guid handoffId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        Guid batchId;
        string? externalSystem = null, mode = null, endpointRef = null, createdBy = null;
        long endpointVersion = 0;
        DateTimeOffset createdAt = default;
        BillingObjectContext? scope = null;
        await using (var command = new NpgsqlCommand("""
            select h.batch_id, h.external_system, h.mode, h.endpoint_ref, h.endpoint_version,
                   h.created_by, h.created_at,
                   b.legal_entity_id, b.laboratory_id, b.customer_id, b.service_order_id, b.product_category
            from billing.handoff h
            join billing.export_batch b on b.batch_id = h.batch_id
            where h.organization_group_id = @organization_group_id
              and h.handoff_id = @handoff_id
              and b.organization_group_id = h.organization_group_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("handoff_id", handoffId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            batchId = reader.GetGuid(0);
            externalSystem = reader.GetString(1);
            mode = reader.GetString(2);
            endpointRef = reader.GetString(3);
            endpointVersion = reader.GetInt64(4);
            createdBy = reader.GetString(5);
            createdAt = reader.GetFieldValue<DateTimeOffset>(6);
            scope = new BillingObjectContext(
                reader.GetString(7), reader.GetString(8), reader.GetString(9),
                reader.GetString(10), reader.GetString(11));
        }

        var attempts = await LoadHandoffAttemptsAsync(handoffId, cancellationToken);
        return new StoredBillingHandoff(
            new BillingHandoffResult(
                handoffId.ToString("N"), batchId.ToString("N"), externalSystem!, mode!,
                new BillingVersionedReference(endpointRef!, endpointVersion),
                BillingIntegrationRules.ResolveHandoffStatus(attempts), attempts, createdBy!, createdAt),
            scope!, organizationGroupId);
    }

    public async Task<IReadOnlyList<StoredBillingHandoff>> LoadDifferenceCandidatesAsync(
        string organizationGroupId,
        string? externalSystem,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var ids = new List<Guid>();
        await using (var command = new NpgsqlCommand("""
            select handoff_id from billing.handoff
            where organization_group_id = @organization_group_id
              and (@external_system is null or external_system = @external_system)
            order by created_at, handoff_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("external_system", (object?)externalSystem ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                ids.Add(reader.GetGuid(0));
        }

        var results = new List<StoredBillingHandoff>();
        foreach (var id in ids)
        {
            var handoff = await LoadHandoffAsync(organizationGroupId, id, cancellationToken);
            if (handoff is not null &&
                !string.Equals(handoff.Handoff.Status, BillingHandoffOutcomes.Succeeded, StringComparison.Ordinal))
            {
                results.Add(handoff);
            }
        }
        return results;
    }

    public async Task<BillingHandoffResult> InsertHandoffAsync(
        Guid handoffId,
        StoredBillingExportBatch batch,
        CreateBillingHandoffRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into billing.handoff (
                handoff_id, organization_group_id, batch_id, external_system, mode,
                endpoint_ref, endpoint_version, idempotency_key,
                created_by, created_at, event_id, correlation_id
            ) values (
                @handoff_id, @organization_group_id, @batch_id, @external_system, @mode,
                @endpoint_ref, @endpoint_version, @idempotency_key,
                @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("handoff_id", handoffId);
        command.Parameters.AddWithValue("organization_group_id", batch.OrganizationGroupId);
        command.Parameters.AddWithValue("batch_id", Guid.Parse(batch.Batch.BatchId));
        command.Parameters.AddWithValue("external_system", request.ExternalSystem);
        command.Parameters.AddWithValue("mode", request.Mode);
        command.Parameters.AddWithValue("endpoint_ref", request.Endpoint.Id);
        command.Parameters.AddWithValue("endpoint_version", request.Endpoint.Version);
        command.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);
        command.Parameters.AddWithValue("created_by", actorId);
        command.Parameters.AddWithValue("created_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            "CREATE_BILLING_HANDOFF", handoffId.ToString("N"), batch.OrganizationGroupId, actorId,
            eventId, "Billing.HandoffCreated.v1", correlationId, now, cancellationToken,
            BillingContract.HandoffRuleSetVersion);
        return new BillingHandoffResult(
            handoffId.ToString("N"), batch.Batch.BatchId, request.ExternalSystem, request.Mode,
            request.Endpoint, BillingHandoffOutcomes.Pending, [], actorId, now);
    }

    public async Task<BillingHandoffAttemptResult?> LoadHandoffAttemptByIdempotencyAsync(
        Guid handoffId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select attempt_id, attempt_number, outcome, external_reference, detail_code,
                   voucher_number, company_code, fiscal_year, fiscal_period, posting_date,
                   attempted_by, attempted_at
            from billing.handoff_attempt
            where handoff_id = @handoff_id and idempotency_key = @idempotency_key
            """, connection, transaction);
        command.Parameters.AddWithValue("handoff_id", handoffId);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapHandoffAttempt(reader, handoffId) : null;
    }

    public async Task<BillingHandoffAttemptResult> InsertHandoffAttemptAsync(
        Guid attemptId,
        StoredBillingHandoff handoff,
        RecordBillingHandoffAttemptRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var handoffId = Guid.Parse(handoff.Handoff.HandoffId);
        var attemptNumber = handoff.Handoff.Attempts.Count + 1;
        var posting = request.ErpPosting;
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into billing.handoff_attempt (
                attempt_id, handoff_id, attempt_number, idempotency_key,
                outcome, external_reference, detail_code,
                voucher_number, company_code, fiscal_year, fiscal_period, posting_date,
                attempted_by, attempted_at, event_id, correlation_id
            ) values (
                @attempt_id, @handoff_id, @attempt_number, @idempotency_key,
                @outcome, @external_reference, @detail_code,
                @voucher_number, @company_code, @fiscal_year, @fiscal_period, @posting_date,
                @attempted_by, @attempted_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("attempt_id", attemptId);
        command.Parameters.AddWithValue("handoff_id", handoffId);
        command.Parameters.AddWithValue("attempt_number", attemptNumber);
        command.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);
        command.Parameters.AddWithValue("outcome", request.Outcome);
        command.Parameters.AddWithValue("external_reference", (object?)request.ExternalReference ?? DBNull.Value);
        command.Parameters.AddWithValue("detail_code", (object?)request.DetailCode ?? DBNull.Value);
        command.Parameters.AddWithValue("voucher_number", (object?)posting?.VoucherNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("company_code", (object?)posting?.CompanyCode ?? DBNull.Value);
        command.Parameters.AddWithValue("fiscal_year", (object?)posting?.FiscalYear ?? DBNull.Value);
        command.Parameters.AddWithValue("fiscal_period", (object?)posting?.Period ?? DBNull.Value);
        command.Parameters.AddWithValue("posting_date", (object?)posting?.PostingDate ?? DBNull.Value);
        command.Parameters.AddWithValue("attempted_by", actorId);
        command.Parameters.AddWithValue("attempted_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            "RECORD_BILLING_HANDOFF_ATTEMPT", attemptId.ToString("N"), handoff.OrganizationGroupId, actorId,
            eventId, "Billing.HandoffAttempted.v1", correlationId, now, cancellationToken,
            BillingContract.HandoffRuleSetVersion);
        return new BillingHandoffAttemptResult(
            attemptId.ToString("N"), handoffId.ToString("N"), attemptNumber,
            request.Outcome, request.ExternalReference, request.DetailCode,
            posting, actorId, now);
    }

    public Task WriteReadAuditAsync(
        string objectId,
        string organizationGroupId,
        string actorId,
        string action,
        string ruleSetVersion,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, objectId, action, ruleSetVersion,
            "1", "1", correlationId, now), cancellationToken);

    private async Task<IReadOnlyList<BillingHandoffAttemptResult>> LoadHandoffAttemptsAsync(
        Guid handoffId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var attempts = new List<BillingHandoffAttemptResult>();
        await using var command = new NpgsqlCommand("""
            select attempt_id, attempt_number, outcome, external_reference, detail_code,
                   voucher_number, company_code, fiscal_year, fiscal_period, posting_date,
                   attempted_by, attempted_at
            from billing.handoff_attempt where handoff_id = @handoff_id order by attempt_number
            """, connection, transaction);
        command.Parameters.AddWithValue("handoff_id", handoffId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            attempts.Add(MapHandoffAttempt(reader, handoffId));
        return attempts;
    }

    private static BillingHandoffAttemptResult MapHandoffAttempt(NpgsqlDataReader reader, Guid handoffId)
    {
        ErpPostingConfirmation? posting = reader.IsDBNull(5)
            ? null
            : new ErpPostingConfirmation(
                reader.GetString(5), reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8),
                reader.GetFieldValue<DateOnly>(9));
        return new BillingHandoffAttemptResult(
            reader.GetGuid(0).ToString("N"), handoffId.ToString("N"), reader.GetInt32(1),
            reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), posting,
            reader.GetString(10), reader.GetFieldValue<DateTimeOffset>(11));
    }

    private async Task WritePlatformEvidenceAsync(
        string action,
        string objectId,
        string organizationGroupId,
        string actorId,
        string eventId,
        string messageType,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string ruleSetVersion)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, objectId, action, ruleSetVersion,
            null, "1", correlationId, now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("BIL.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}
