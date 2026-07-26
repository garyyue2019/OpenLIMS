using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Scope;

namespace OpenLIMS.Modules.Scope;

internal sealed class ScopeDataSource : IAsyncDisposable
{
    public ScopeDataSource(ScopePersistenceOptions options) => Value = NpgsqlDataSource.Create(options.ConnectionString);
    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed record ScopeMatrixHeader(
    Guid ScopeMatrixId,
    long Version,
    string OrganizationGroupId,
    ScopeObjectContext ObjectScope,
    string State,
    string RuleSetVersion,
    string ApprovedBy,
    DateTimeOffset ApprovedAt);

internal sealed class ScopeStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireMatrixLockAsync(Guid scopeMatrixId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@scope_matrix_id, 0))",
            connection,
            transaction);
        command.Parameters.AddWithValue("scope_matrix_id", scopeMatrixId.ToString("N"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ScopeMatrixHeader?> LoadCurrentHeaderAsync(
        string organizationGroupId,
        Guid scopeMatrixId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var suffix = forUpdate ? " for update" : string.Empty;
        await using var command = new NpgsqlCommand($"""
            select scope_matrix_id, version, organization_group_id,
                   legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                   state, rule_set_version, approved_by, approved_at
            from scope.scope_matrix_version
            where organization_group_id = @organization_group_id
              and scope_matrix_id = @scope_matrix_id
            order by version desc
            limit 1{suffix}
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("scope_matrix_id", scopeMatrixId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHeader(reader) : null;
    }

    public async Task<ScopeMatrixVersionResult?> LoadVersionAsync(
        string organizationGroupId,
        Guid scopeMatrixId,
        long version,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        ScopeMatrixHeader? header;
        await using (var command = new NpgsqlCommand("""
            select scope_matrix_id, version, organization_group_id,
                   legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                   state, rule_set_version, approved_by, approved_at
            from scope.scope_matrix_version
            where organization_group_id = @organization_group_id
              and scope_matrix_id = @scope_matrix_id
              and version = @version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("scope_matrix_id", scopeMatrixId);
            command.Parameters.AddWithValue("version", version);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            header = await reader.ReadAsync(cancellationToken) ? ReadHeader(reader) : null;
        }

        return header is null ? null : await LoadResultAsync(header, cancellationToken);
    }

    public async Task<ScopeMatrixVersionResult?> LoadCurrentAsync(
        string organizationGroupId,
        Guid scopeMatrixId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var header = await LoadCurrentHeaderAsync(
            organizationGroupId, scopeMatrixId, forUpdate, cancellationToken);
        return header is null ? null : await LoadResultAsync(header, cancellationToken);
    }

    public async Task<ScopeMatrixVersionResult> InsertAsync(
        Guid scopeMatrixId,
        long version,
        string organizationGroupId,
        ScopeObjectContext objectScope,
        IReadOnlyList<ScopeLineResult> lines,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into scope.scope_matrix_version (
                scope_matrix_id, version, organization_group_id,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                state, rule_set_version, approved_by, approved_at, event_id, correlation_id
            ) values (
                @scope_matrix_id, @version, @organization_group_id,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                'APPROVED', @rule_set_version, @approved_by, @approved_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("scope_matrix_id", scopeMatrixId);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("legal_entity_id", objectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", objectScope.LaboratoryId);
            command.Parameters.AddWithValue("customer_id", objectScope.CustomerId);
            command.Parameters.AddWithValue("service_order_id", objectScope.ServiceOrderId);
            command.Parameters.AddWithValue("product_category", objectScope.ProductCategory);
            command.Parameters.AddWithValue("rule_set_version", ScopeContract.RuleSetVersion);
            command.Parameters.AddWithValue("approved_by", actorId);
            command.Parameters.AddWithValue("approved_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var line in lines)
            await InsertLineAsync(connection, transaction, scopeMatrixId, version, line, cancellationToken);

        var matrixId = scopeMatrixId.ToString("N");
        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            matrixId,
            version == 1 ? "APPROVE_SCOPE_MATRIX" : "APPROVE_SCOPE_MATRIX_REVISION",
            ScopeContract.RuleSetVersion,
            version == 1 ? null : (version - 1).ToString(),
            version.ToString(),
            correlationId,
            now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(
            eventId,
            "ScopeMatrixApproved.v1",
            now), cancellationToken);

        return new ScopeMatrixVersionResult(
            matrixId,
            version,
            ScopeMatrixStates.Approved,
            ScopeContract.RuleSetVersion,
            objectScope,
            lines,
            actorId,
            now);
    }

    public Task WriteReadAuditAsync(
        ScopeMatrixVersionResult result,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            result.ScopeMatrixId,
            action,
            ScopeContract.RuleSetVersion,
            result.Version.ToString(),
            result.Version.ToString(),
            correlationId,
            now), cancellationToken);

    private async Task<ScopeMatrixVersionResult> LoadResultAsync(
        ScopeMatrixHeader header,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var lines = new List<ScopeLineResult>();
        await using var command = new NpgsqlCommand("""
            select scope_line_id, subject_type, subject_ref, subject_version,
                   target_market_ref, target_market_version,
                   requirement_clause_ref, requirement_clause_version,
                   test_item_ref, test_item_version, method_ref, method_version,
                   method_option, sample_requirement_ref, sample_requirement_version,
                   evaluation_mode, work_center_ref, work_center_version, report_position,
                   limit_rule_ref, limit_rule_version, decision_rule_ref, decision_rule_version,
                   non_evaluation_reason, waiver_approval_ref, waiver_approval_version
            from scope.scope_line_version
            where scope_matrix_id = @scope_matrix_id and matrix_version = @matrix_version
            order by scope_line_id
            """, connection, transaction);
        command.Parameters.AddWithValue("scope_matrix_id", header.ScopeMatrixId);
        command.Parameters.AddWithValue("matrix_version", header.Version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new ScopeLineResult(
                reader.GetString(0),
                reader.GetString(1),
                RequiredReference(reader, 2, 3),
                RequiredReference(reader, 4, 5),
                RequiredReference(reader, 6, 7),
                RequiredReference(reader, 8, 9),
                RequiredReference(reader, 10, 11),
                reader.GetString(12),
                RequiredReference(reader, 13, 14),
                reader.GetString(15),
                RequiredReference(reader, 16, 17),
                reader.GetString(18),
                OptionalReference(reader, 19, 20),
                OptionalReference(reader, 21, 22),
                reader.IsDBNull(23) ? null : reader.GetString(23),
                OptionalReference(reader, 24, 25)));
        }

        return new ScopeMatrixVersionResult(
            header.ScopeMatrixId.ToString("N"),
            header.Version,
            header.State,
            header.RuleSetVersion,
            header.ObjectScope,
            lines,
            header.ApprovedBy,
            header.ApprovedAt);
    }

    private static async Task InsertLineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid scopeMatrixId,
        long version,
        ScopeLineResult line,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            insert into scope.scope_line_version (
                scope_matrix_id, matrix_version, scope_line_id,
                subject_type, subject_ref, subject_version,
                target_market_ref, target_market_version,
                requirement_clause_ref, requirement_clause_version,
                test_item_ref, test_item_version, method_ref, method_version, method_option,
                sample_requirement_ref, sample_requirement_version, evaluation_mode,
                work_center_ref, work_center_version, report_position,
                limit_rule_ref, limit_rule_version, decision_rule_ref, decision_rule_version,
                non_evaluation_reason, waiver_approval_ref, waiver_approval_version
            ) values (
                @scope_matrix_id, @matrix_version, @scope_line_id,
                @subject_type, @subject_ref, @subject_version,
                @target_market_ref, @target_market_version,
                @requirement_clause_ref, @requirement_clause_version,
                @test_item_ref, @test_item_version, @method_ref, @method_version, @method_option,
                @sample_requirement_ref, @sample_requirement_version, @evaluation_mode,
                @work_center_ref, @work_center_version, @report_position,
                @limit_rule_ref, @limit_rule_version, @decision_rule_ref, @decision_rule_version,
                @non_evaluation_reason, @waiver_approval_ref, @waiver_approval_version
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("scope_matrix_id", scopeMatrixId);
        command.Parameters.AddWithValue("matrix_version", version);
        command.Parameters.AddWithValue("scope_line_id", line.ScopeLineId);
        command.Parameters.AddWithValue("subject_type", line.SubjectType);
        AddReference(command, "subject", line.Subject);
        AddReference(command, "target_market", line.TargetMarket);
        AddReference(command, "requirement_clause", line.RequirementClause);
        AddReference(command, "test_item", line.TestItem);
        AddReference(command, "method", line.Method);
        command.Parameters.AddWithValue("method_option", line.MethodOption);
        AddReference(command, "sample_requirement", line.SampleRequirement);
        command.Parameters.AddWithValue("evaluation_mode", line.EvaluationMode);
        AddReference(command, "work_center", line.WorkCenter);
        command.Parameters.AddWithValue("report_position", line.ReportPosition);
        AddOptionalReference(command, "limit_rule", line.LimitRule);
        AddOptionalReference(command, "decision_rule", line.DecisionRule);
        command.Parameters.AddWithValue("non_evaluation_reason", (object?)line.NonEvaluationReason ?? DBNull.Value);
        AddOptionalReference(command, "waiver_approval", line.WaiverApproval);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddReference(NpgsqlCommand command, string name, ScopeVersionedReference value)
    {
        command.Parameters.AddWithValue($"{name}_ref", value.Id);
        command.Parameters.AddWithValue($"{name}_version", value.Version);
    }

    private static void AddOptionalReference(NpgsqlCommand command, string name, ScopeVersionedReference? value)
    {
        command.Parameters.AddWithValue($"{name}_ref", (object?)value?.Id ?? DBNull.Value);
        command.Parameters.AddWithValue($"{name}_version", (object?)value?.Version ?? DBNull.Value);
    }

    private static ScopeMatrixHeader ReadHeader(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetInt64(1),
        reader.GetString(2),
        new ScopeObjectContext(
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7)),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetString(10),
        reader.GetFieldValue<DateTimeOffset>(11));

    private static ScopeVersionedReference RequiredReference(NpgsqlDataReader reader, int id, int version) =>
        new(reader.GetString(id), reader.GetInt64(version));

    private static ScopeVersionedReference? OptionalReference(NpgsqlDataReader reader, int id, int version) =>
        reader.IsDBNull(id) || reader.IsDBNull(version)
            ? null
            : new ScopeVersionedReference(reader.GetString(id), reader.GetInt64(version));

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("SCP.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class ScopeAttemptAuditWriter(ScopeDataSource dataSource)
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
            insert into scope.audit_attempt (
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
