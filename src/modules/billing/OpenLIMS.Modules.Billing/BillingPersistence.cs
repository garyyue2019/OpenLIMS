using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Billing;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Billing;

internal sealed class BillingDataSource : IAsyncDisposable
{
    public BillingDataSource(BillingPersistenceOptions options) => Value = NpgsqlDataSource.Create(options.ConnectionString);
    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed class BillingStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task<bool> DuplicateExistsAsync(
        string organizationGroupId,
        CreateBillingEvidenceRequest request,
        string adoptionTargetId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select exists (
                select 1 from billing.billing_evidence
                where organization_group_id = @organization_group_id
                  and result_group_id = @result_group_id
                  and group_version = @group_version
                  and adoption_target_id = @adoption_target_id
                  and contract_baseline_ref = @contract_baseline_ref
                  and contract_baseline_version = @contract_baseline_version
                  and charge_dimension = @charge_dimension
                  and billing_rule_version = @billing_rule_version
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("result_group_id", request.ResultGroupId);
        command.Parameters.AddWithValue("group_version", request.ExpectedGroupVersion);
        command.Parameters.AddWithValue("adoption_target_id", adoptionTargetId);
        command.Parameters.AddWithValue("contract_baseline_ref", request.ContractBaseline.Id);
        command.Parameters.AddWithValue("contract_baseline_version", request.ContractBaseline.Version);
        command.Parameters.AddWithValue("charge_dimension", request.ChargeDimension);
        command.Parameters.AddWithValue("billing_rule_version", request.BillingRuleVersion);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<BillingEvidenceResult> InsertEvidenceAsync(
        Guid evidenceId,
        string organizationGroupId,
        CreateBillingEvidenceRequest request,
        string adoptionTargetId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into billing.billing_evidence (
                billing_evidence_id, organization_group_id,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                result_group_id, group_version, adoption_target_id,
                contract_baseline_ref, contract_baseline_version, charge_dimension, billing_rule_version,
                amount, currency_ref, currency_version, zero_amount_reason, stage,
                rule_set_version, recorded_by, recorded_at, event_id, correlation_id
            ) values (
                @billing_evidence_id, @organization_group_id,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @result_group_id, @group_version, @adoption_target_id,
                @contract_baseline_ref, @contract_baseline_version, @charge_dimension, @billing_rule_version,
                @amount, @currency_ref, @currency_version, @zero_amount_reason, @stage,
                @rule_set_version, @recorded_by, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("billing_evidence_id", evidenceId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("legal_entity_id", request.ObjectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", request.ObjectScope.LaboratoryId);
            command.Parameters.AddWithValue("customer_id", request.ObjectScope.CustomerId);
            command.Parameters.AddWithValue("service_order_id", request.ObjectScope.ServiceOrderId);
            command.Parameters.AddWithValue("product_category", request.ObjectScope.ProductCategory);
            command.Parameters.AddWithValue("result_group_id", request.ResultGroupId);
            command.Parameters.AddWithValue("group_version", request.ExpectedGroupVersion);
            command.Parameters.AddWithValue("adoption_target_id", adoptionTargetId);
            command.Parameters.AddWithValue("contract_baseline_ref", request.ContractBaseline.Id);
            command.Parameters.AddWithValue("contract_baseline_version", request.ContractBaseline.Version);
            command.Parameters.AddWithValue("charge_dimension", request.ChargeDimension);
            command.Parameters.AddWithValue("billing_rule_version", request.BillingRuleVersion);
            command.Parameters.AddWithValue("amount", request.Amount);
            command.Parameters.AddWithValue("currency_ref", request.Currency.Id);
            command.Parameters.AddWithValue("currency_version", request.Currency.Version);
            command.Parameters.AddWithValue("zero_amount_reason", (object?)request.ZeroAmountReason ?? DBNull.Value);
            command.Parameters.AddWithValue("stage", BillingStages.BillableCandidate);
            command.Parameters.AddWithValue("rule_set_version", BillingContract.RuleSetVersion);
            command.Parameters.AddWithValue("recorded_by", actorId);
            command.Parameters.AddWithValue("recorded_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "CREATE_BILLING_EVIDENCE", evidenceId.ToString("N"), organizationGroupId, actorId,
            eventId, "BillingEvidenceCreated.v1", correlationId, now, cancellationToken);
        return new BillingEvidenceResult(
            evidenceId.ToString("N"), BillingStages.BillableCandidate, BillingContract.RuleSetVersion,
            request.ObjectScope, request.ResultGroupId, request.ExpectedGroupVersion, adoptionTargetId,
            request.ContractBaseline, request.ChargeDimension, request.BillingRuleVersion,
            request.Amount, request.Currency, request.ZeroAmountReason, [], actorId, now);
    }

    public async Task<BillingEvidenceResult?> LoadEvidenceAsync(
        string organizationGroupId,
        Guid evidenceId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        BillingEvidenceResult? evidence = null;
        await using (var command = new NpgsqlCommand("""
            select legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                   result_group_id, group_version, adoption_target_id,
                   contract_baseline_ref, contract_baseline_version, charge_dimension, billing_rule_version,
                   amount, currency_ref, currency_version, zero_amount_reason, stage, recorded_by, recorded_at
            from billing.billing_evidence
            where organization_group_id = @organization_group_id and billing_evidence_id = @billing_evidence_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("billing_evidence_id", evidenceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            evidence = new BillingEvidenceResult(
                evidenceId.ToString("N"),
                reader.GetString(16),
                BillingContract.RuleSetVersion,
                new BillingObjectContext(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4)),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetString(7),
                new BillingVersionedReference(reader.GetString(8), reader.GetInt64(9)),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetDecimal(12),
                new BillingVersionedReference(reader.GetString(13), reader.GetInt64(14)),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                [],
                reader.GetString(17),
                reader.GetFieldValue<DateTimeOffset>(18));
        }

        var adjustments = new List<BillingAdjustmentResult>();
        await using (var command = new NpgsqlCommand("""
            select adjustment_id, amount, reason, recorded_by, recorded_at
            from billing.billing_adjustment where billing_evidence_id = @id order by recorded_at, adjustment_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", evidenceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                adjustments.Add(new BillingAdjustmentResult(
                    reader.GetGuid(0).ToString("N"),
                    evidenceId.ToString("N"),
                    reader.GetDecimal(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }

        return evidence with { Adjustments = adjustments };
    }

    public async Task<BillingAdjustmentResult> InsertAdjustmentAsync(
        Guid evidenceId,
        string organizationGroupId,
        AddBillingAdjustmentRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var adjustmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into billing.billing_adjustment (
                adjustment_id, billing_evidence_id, amount, reason,
                recorded_by, recorded_at, event_id, correlation_id
            ) values (
                @adjustment_id, @billing_evidence_id, @amount, @reason,
                @recorded_by, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("adjustment_id", adjustmentId);
            command.Parameters.AddWithValue("billing_evidence_id", evidenceId);
            command.Parameters.AddWithValue("amount", request.Amount);
            command.Parameters.AddWithValue("reason", request.Reason);
            command.Parameters.AddWithValue("recorded_by", actorId);
            command.Parameters.AddWithValue("recorded_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "ADD_BILLING_ADJUSTMENT", evidenceId.ToString("N"), organizationGroupId, actorId,
            eventId, "BillingAdjustmentRecorded.v1", correlationId, now, cancellationToken);
        return new BillingAdjustmentResult(
            adjustmentId.ToString("N"), evidenceId.ToString("N"),
            request.Amount, request.Reason, actorId, now);
    }

    public Task WriteReadAuditAsync(
        string evidenceId,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, evidenceId, action, BillingContract.RuleSetVersion,
            "1", "1", correlationId, now), cancellationToken);

    private async Task WritePlatformEvidenceAsync(
        string action,
        string evidenceKey,
        string organizationGroupId,
        string actorId,
        string eventId,
        string messageType,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, evidenceKey, action, BillingContract.RuleSetVersion,
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

internal sealed class BillingAttemptAuditWriter(BillingDataSource dataSource)
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
            insert into billing.audit_attempt (
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
