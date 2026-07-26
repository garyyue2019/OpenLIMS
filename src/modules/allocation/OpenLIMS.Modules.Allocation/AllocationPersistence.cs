using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Allocation;

internal sealed class AllocationDataSource : IAsyncDisposable
{
    public AllocationDataSource(AllocationPersistenceOptions options) => Value = NpgsqlDataSource.Create(options.ConnectionString);
    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed class AllocationStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireSubjectLockAsync(
        string organizationGroupId,
        string subjectType,
        string subjectRef,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@subject_key, 0))",
            connection,
            transaction);
        command.Parameters.AddWithValue("subject_key", $"{organizationGroupId}|{subjectType}|{subjectRef}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AllocationSubjectState> LoadSubjectStateAsync(
        string organizationGroupId,
        string subjectType,
        string subjectRef,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select
                coalesce((
                    select max(v) from (
                        select max(subject_allocation_version) as v
                        from allocation.test_object_allocation
                        where organization_group_id = @organization_group_id
                          and subject_type = @subject_type
                          and subject_ref = @subject_ref
                        union all
                        select max(subject_allocation_version)
                        from allocation.allocation_release
                        where organization_group_id = @organization_group_id
                          and subject_type = @subject_type
                          and subject_ref = @subject_ref
                    ) versions
                ), 0) as current_version,
                exists (
                    select 1
                    from allocation.test_object_allocation a
                    where a.organization_group_id = @organization_group_id
                      and a.subject_type = @subject_type
                      and a.subject_ref = @subject_ref
                      and a.destructive
                      and not exists (
                          select 1 from allocation.allocation_release r
                          where r.allocation_id = a.allocation_id
                      )
                ) as has_active_destructive
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("subject_type", subjectType);
        command.Parameters.AddWithValue("subject_ref", subjectRef);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new AllocationSubjectState(reader.GetInt64(0), reader.GetBoolean(1));
    }

    public async Task<TestObjectAllocationResult> InsertAllocationAsync(
        Guid allocationId,
        long subjectAllocationVersion,
        string organizationGroupId,
        CreateTestObjectAllocationRequest request,
        AllocationGateResult receivingGate,
        AllocationGateResult scopeGate,
        AllocationGateResult quantityGate,
        decimal quantityAvailableAmount,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into allocation.test_object_allocation (
                allocation_id, organization_group_id,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                subject_type, subject_ref, subject_version, subject_allocation_version,
                identity_assignment_ref, identity_assignment_version, received_item_id,
                scope_matrix_id, scope_line_id, plan_step_ref, plan_step_version,
                purpose, sequence_order, destructive,
                quantity_account_id, requested_amount, dimension, unit,
                storage_condition_ref, storage_condition_version, valid_until, reservation_entry_id,
                receiving_decision, receiving_item_version, receiving_rule_set_version,
                scope_decision, scope_matrix_version, scope_rule_set_version,
                quantity_decision, quantity_account_version, quantity_available_amount, quantity_rule_set_version,
                rule_set_version, assigned_by, assigned_at, event_id, correlation_id
            ) values (
                @allocation_id, @organization_group_id,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @subject_type, @subject_ref, @subject_version, @subject_allocation_version,
                @identity_assignment_ref, @identity_assignment_version, @received_item_id,
                @scope_matrix_id, @scope_line_id, @plan_step_ref, @plan_step_version,
                @purpose, @sequence_order, @destructive,
                @quantity_account_id, @requested_amount, @dimension, @unit,
                @storage_condition_ref, @storage_condition_version, @valid_until, @reservation_entry_id,
                @receiving_decision, @receiving_item_version, @receiving_rule_set_version,
                @scope_decision, @scope_matrix_version, @scope_rule_set_version,
                @quantity_decision, @quantity_account_version, @quantity_available_amount, @quantity_rule_set_version,
                @rule_set_version, @assigned_by, @assigned_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("allocation_id", allocationId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("legal_entity_id", request.ObjectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", request.ObjectScope.LaboratoryId);
            command.Parameters.AddWithValue("customer_id", request.ObjectScope.CustomerId);
            command.Parameters.AddWithValue("service_order_id", request.ObjectScope.ServiceOrderId);
            command.Parameters.AddWithValue("product_category", request.ObjectScope.ProductCategory);
            command.Parameters.AddWithValue("subject_type", request.Subject.SubjectType);
            command.Parameters.AddWithValue("subject_ref", request.Subject.Id);
            command.Parameters.AddWithValue("subject_version", request.Subject.Version);
            command.Parameters.AddWithValue("subject_allocation_version", subjectAllocationVersion);
            command.Parameters.AddWithValue("identity_assignment_ref", request.IdentityAssignment.Id);
            command.Parameters.AddWithValue("identity_assignment_version", request.IdentityAssignment.Version);
            command.Parameters.AddWithValue("received_item_id", request.ReceivedItemId);
            command.Parameters.AddWithValue("scope_matrix_id", request.ScopeMatrixId);
            command.Parameters.AddWithValue("scope_line_id", request.ScopeLineId);
            command.Parameters.AddWithValue("plan_step_ref", request.PlanStep.Id);
            command.Parameters.AddWithValue("plan_step_version", request.PlanStep.Version);
            command.Parameters.AddWithValue("purpose", request.Purpose);
            command.Parameters.AddWithValue("sequence_order", request.SequenceOrder);
            command.Parameters.AddWithValue("destructive", request.Destructive);
            command.Parameters.AddWithValue("quantity_account_id", request.QuantityAccountId);
            command.Parameters.AddWithValue("requested_amount", request.RequestedAmount);
            command.Parameters.AddWithValue("dimension", request.Dimension);
            command.Parameters.AddWithValue("unit", request.Unit);
            command.Parameters.AddWithValue("storage_condition_ref", request.StorageCondition.Id);
            command.Parameters.AddWithValue("storage_condition_version", request.StorageCondition.Version);
            command.Parameters.AddWithValue("valid_until", request.ValidUntil);
            command.Parameters.AddWithValue("reservation_entry_id", (object?)request.ReservationEntryId ?? DBNull.Value);
            command.Parameters.AddWithValue("receiving_decision", receivingGate.Decision);
            command.Parameters.AddWithValue("receiving_item_version", receivingGate.PinnedVersion ?? 0L);
            command.Parameters.AddWithValue("receiving_rule_set_version", receivingGate.RuleSetVersion);
            command.Parameters.AddWithValue("scope_decision", scopeGate.Decision);
            command.Parameters.AddWithValue("scope_matrix_version", scopeGate.PinnedVersion ?? 0L);
            command.Parameters.AddWithValue("scope_rule_set_version", scopeGate.RuleSetVersion);
            command.Parameters.AddWithValue("quantity_decision", quantityGate.Decision);
            command.Parameters.AddWithValue("quantity_account_version", quantityGate.PinnedVersion ?? 0L);
            command.Parameters.AddWithValue("quantity_available_amount", quantityAvailableAmount);
            command.Parameters.AddWithValue("quantity_rule_set_version", quantityGate.RuleSetVersion);
            command.Parameters.AddWithValue("rule_set_version", AllocationContract.RuleSetVersion);
            command.Parameters.AddWithValue("assigned_by", actorId);
            command.Parameters.AddWithValue("assigned_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var allocationKey = allocationId.ToString("N");
        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            allocationKey,
            "ASSIGN_TEST_OBJECT_ALLOCATION",
            AllocationContract.RuleSetVersion,
            (subjectAllocationVersion - 1).ToString(),
            subjectAllocationVersion.ToString(),
            correlationId,
            now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(
            eventId,
            "TestObjectAllocationAssigned.v1",
            now), cancellationToken);

        return new TestObjectAllocationResult(
            allocationKey,
            AllocationStates.Active,
            subjectAllocationVersion,
            AllocationContract.RuleSetVersion,
            request.ObjectScope,
            request.Subject,
            request.IdentityAssignment,
            request.ScopeMatrixId,
            request.ScopeLineId,
            request.PlanStep,
            request.Purpose,
            request.SequenceOrder,
            request.Destructive,
            request.QuantityAccountId,
            request.RequestedAmount,
            request.Dimension,
            request.Unit,
            request.StorageCondition,
            request.ValidUntil,
            request.ReservationEntryId,
            receivingGate,
            scopeGate,
            quantityGate,
            actorId,
            now,
            null,
            null,
            null);
    }

    public async Task<TestObjectAllocationResult?> LoadAllocationAsync(
        string organizationGroupId,
        Guid allocationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select a.allocation_id,
                   a.legal_entity_id, a.laboratory_id, a.customer_id, a.service_order_id, a.product_category,
                   a.subject_type, a.subject_ref, a.subject_version, a.subject_allocation_version,
                   a.identity_assignment_ref, a.identity_assignment_version,
                   a.scope_matrix_id, a.scope_line_id, a.plan_step_ref, a.plan_step_version,
                   a.purpose, a.sequence_order, a.destructive,
                   a.quantity_account_id, a.requested_amount, a.dimension, a.unit,
                   a.storage_condition_ref, a.storage_condition_version, a.valid_until, a.reservation_entry_id,
                   a.receiving_decision, a.receiving_item_version, a.receiving_rule_set_version,
                   a.scope_decision, a.scope_matrix_version, a.scope_rule_set_version,
                   a.quantity_decision, a.quantity_account_version, a.quantity_available_amount, a.quantity_rule_set_version,
                   a.assigned_by, a.assigned_at,
                   r.reason, r.released_by, r.released_at
            from allocation.test_object_allocation a
            left join allocation.allocation_release r on r.allocation_id = a.allocation_id
            where a.organization_group_id = @organization_group_id
              and a.allocation_id = @allocation_id
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("allocation_id", allocationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var released = !reader.IsDBNull(39);
        return new TestObjectAllocationResult(
            reader.GetGuid(0).ToString("N"),
            released ? AllocationStates.Released : AllocationStates.Active,
            reader.GetInt64(9),
            AllocationContract.RuleSetVersion,
            new AllocationObjectContext(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)),
            new AllocationSubjectReference(reader.GetString(6), reader.GetString(7), reader.GetInt64(8)),
            new AllocationVersionedReference(reader.GetString(10), reader.GetInt64(11)),
            reader.GetString(12),
            reader.GetString(13),
            new AllocationVersionedReference(reader.GetString(14), reader.GetInt64(15)),
            reader.GetString(16),
            reader.GetInt32(17),
            reader.GetBoolean(18),
            reader.GetString(19),
            reader.GetDecimal(20),
            reader.GetString(21),
            reader.GetString(22),
            new AllocationVersionedReference(reader.GetString(23), reader.GetInt64(24)),
            reader.GetFieldValue<DateTimeOffset>(25),
            reader.IsDBNull(26) ? null : reader.GetString(26),
            new AllocationGateResult(
                AllocationGateSources.Receiving,
                reader.GetString(27),
                reader.GetInt64(28),
                reader.GetString(29),
                []),
            new AllocationGateResult(
                AllocationGateSources.Scope,
                reader.GetString(30),
                reader.GetInt64(31),
                reader.GetString(32),
                []),
            new AllocationGateResult(
                AllocationGateSources.Quantity,
                reader.GetString(33),
                reader.GetInt64(34),
                reader.GetString(36),
                []),
            reader.GetString(37),
            reader.GetFieldValue<DateTimeOffset>(38),
            reader.IsDBNull(39) ? null : reader.GetString(39),
            reader.IsDBNull(40) ? null : reader.GetString(40),
            reader.IsDBNull(41) ? null : reader.GetFieldValue<DateTimeOffset>(41));
    }

    public async Task<AllocationReleaseResult> InsertReleaseAsync(
        TestObjectAllocationResult allocation,
        long subjectAllocationVersion,
        string organizationGroupId,
        string reason,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into allocation.allocation_release (
                allocation_id, organization_group_id, subject_type, subject_ref,
                subject_allocation_version, reason, released_by, released_at, event_id, correlation_id
            ) values (
                @allocation_id, @organization_group_id, @subject_type, @subject_ref,
                @subject_allocation_version, @reason, @released_by, @released_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("allocation_id", Guid.ParseExact(allocation.AllocationId, "N"));
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("subject_type", allocation.Subject.SubjectType);
            command.Parameters.AddWithValue("subject_ref", allocation.Subject.Id);
            command.Parameters.AddWithValue("subject_allocation_version", subjectAllocationVersion);
            command.Parameters.AddWithValue("reason", reason);
            command.Parameters.AddWithValue("released_by", actorId);
            command.Parameters.AddWithValue("released_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            allocation.AllocationId,
            "RELEASE_TEST_OBJECT_ALLOCATION",
            AllocationContract.RuleSetVersion,
            (subjectAllocationVersion - 1).ToString(),
            subjectAllocationVersion.ToString(),
            correlationId,
            now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(
            eventId,
            "TestObjectAllocationReleased.v1",
            now), cancellationToken);

        return new AllocationReleaseResult(
            allocation.AllocationId,
            AllocationStates.Released,
            reason,
            actorId,
            now);
    }

    public Task WriteReadAuditAsync(
        string allocationId,
        long version,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            allocationId,
            action,
            AllocationContract.RuleSetVersion,
            version.ToString(),
            version.ToString(),
            correlationId,
            now), cancellationToken);

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("ALC.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class AllocationAttemptAuditWriter(AllocationDataSource dataSource)
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
            insert into allocation.audit_attempt (
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
