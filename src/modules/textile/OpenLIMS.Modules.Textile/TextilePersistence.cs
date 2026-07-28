using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Textile;

namespace OpenLIMS.Modules.Textile;

internal sealed class TextileDataSource : IAsyncDisposable
{
    public TextileDataSource(TextilePersistenceOptions options) =>
        Value = NpgsqlDataSource.Create(options.ConnectionString);

    public NpgsqlDataSource Value { get; }

    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed class TextileStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public Task AcquireRequirementLockAsync(
        string organizationGroupId,
        string requirementId,
        CancellationToken cancellationToken) =>
        AcquireLockAsync(
            $"textile.requirement:{organizationGroupId}:{requirementId}",
            cancellationToken);

    public Task AcquirePlanLockAsync(
        string organizationGroupId,
        string cuttingPlanId,
        CancellationToken cancellationToken) =>
        AcquireLockAsync(
            $"textile.plan:{organizationGroupId}:{cuttingPlanId}",
            cancellationToken);

    public Task<long> CurrentRequirementVersionAsync(
        string organizationGroupId,
        string requirementId,
        CancellationToken cancellationToken) =>
        CurrentVersionAsync(
            "textile.sample_requirement",
            "requirement_id",
            organizationGroupId,
            requirementId,
            cancellationToken);

    public Task<long> CurrentPlanVersionAsync(
        string organizationGroupId,
        string cuttingPlanId,
        CancellationToken cancellationToken) =>
        CurrentVersionAsync(
            "textile.cutting_plan",
            "cutting_plan_id",
            organizationGroupId,
            cuttingPlanId,
            cancellationToken);

    public async Task<TextileSampleRequirementRecord> InsertRequirementAsync(
        string organizationGroupId,
        CreateTextileSampleRequirementRequest request,
        long version,
        TextileRequirementDraft draft,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into textile.sample_requirement (
                requirement_id, version, organization_group_id,
                legal_entity_id, laboratory_id, rule_set_version, input_hash,
                calculation, result, decision,
                created_by, created_at, event_id, correlation_id
            ) values (
                @requirement_id, @version, @organization_group_id,
                @legal_entity_id, @laboratory_id, @rule_set_version, @input_hash,
                @calculation, @result, @decision,
                @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("requirement_id", request.RequirementId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("legal_entity_id", request.ObjectScope.LegalEntityId);
        command.Parameters.AddWithValue("laboratory_id", request.ObjectScope.LaboratoryId);
        command.Parameters.AddWithValue("rule_set_version", draft.Result.RuleSetVersion);
        command.Parameters.AddWithValue("input_hash", draft.InputHash);
        command.Parameters.Add(new NpgsqlParameter("calculation", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(draft.Calculation, TextileJson.Options)
        });
        command.Parameters.Add(new NpgsqlParameter("result", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(draft.Result, TextileJson.Options)
        });
        command.Parameters.AddWithValue("decision", draft.Result.Decision);
        command.Parameters.AddWithValue("created_by", actorId);
        command.Parameters.AddWithValue("created_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            actorId,
            organizationGroupId,
            request.RequirementId,
            "CALCULATE_TEXTILE_SAMPLE_REQUIREMENT",
            request.ExpectedCurrentVersion.ToString(),
            version.ToString(),
            eventId,
            string.Equals(
                draft.Result.Decision,
                TextileCalculationDecisions.Insufficient,
                StringComparison.Ordinal)
                ? "TextileSampleShortageDetected.v1"
                : "TextileSampleRequirementCalculated.v1",
            correlationId,
            now,
            cancellationToken);

        return new TextileSampleRequirementRecord(
            request.RequirementId,
            version,
            request.ObjectScope,
            draft.Calculation,
            draft.Result,
            draft.InputHash,
            actorId,
            now);
    }

    public async Task<TextileSampleRequirementRecord?> LoadRequirementAsync(
        string organizationGroupId,
        string requirementId,
        long version,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select legal_entity_id, laboratory_id, calculation::text, result::text,
                   input_hash, created_by, created_at
            from textile.sample_requirement
            where organization_group_id = @organization_group_id
              and requirement_id = @requirement_id
              and version = @version
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("requirement_id", requirementId);
        command.Parameters.AddWithValue("version", version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var calculation = JsonSerializer.Deserialize<TextileSampleRequirementCalculation>(
            reader.GetString(2), TextileJson.Options)
            ?? throw new InvalidOperationException("TEX.REQUIREMENT_CALCULATION_MISSING");
        var result = JsonSerializer.Deserialize<TextileSampleRequirementResult>(
            reader.GetString(3), TextileJson.Options)
            ?? throw new InvalidOperationException("TEX.REQUIREMENT_RESULT_MISSING");
        return new TextileSampleRequirementRecord(
            requirementId,
            version,
            new TextileObjectScope(reader.GetString(0), reader.GetString(1)),
            calculation,
            result,
            reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6));
    }

    public async Task<TextileCuttingPlanResult> InsertPlanAsync(
        string organizationGroupId,
        CreateTextileCuttingPlanRequest request,
        long version,
        TextileSampleRequirementRecord requirement,
        string inputHash,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into textile.cutting_plan (
                cutting_plan_id, version, organization_group_id,
                legal_entity_id, laboratory_id,
                sample_requirement_id, sample_requirement_version,
                sample_requirement_input_hash, rule_set_version, input_hash, plan,
                created_by, created_at, event_id, correlation_id
            ) values (
                @cutting_plan_id, @version, @organization_group_id,
                @legal_entity_id, @laboratory_id,
                @sample_requirement_id, @sample_requirement_version,
                @sample_requirement_input_hash, @rule_set_version, @input_hash, @plan,
                @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("cutting_plan_id", request.CuttingPlanId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("legal_entity_id", requirement.ObjectScope.LegalEntityId);
        command.Parameters.AddWithValue("laboratory_id", requirement.ObjectScope.LaboratoryId);
        command.Parameters.AddWithValue("sample_requirement_id", requirement.RequirementId);
        command.Parameters.AddWithValue("sample_requirement_version", requirement.Version);
        command.Parameters.AddWithValue("sample_requirement_input_hash", requirement.InputHash);
        command.Parameters.AddWithValue("rule_set_version", request.RuleSetVersion);
        command.Parameters.AddWithValue("input_hash", inputHash);
        command.Parameters.Add(new NpgsqlParameter("plan", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(request.Plan, TextileJson.Options)
        });
        command.Parameters.AddWithValue("created_by", actorId);
        command.Parameters.AddWithValue("created_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            actorId,
            organizationGroupId,
            request.CuttingPlanId,
            "CREATE_TEXTILE_CUTTING_PLAN",
            request.ExpectedCurrentVersion.ToString(),
            version.ToString(),
            eventId,
            "TextileCuttingPlanCreated.v1",
            correlationId,
            now,
            cancellationToken);

        return new TextileCuttingPlanResult(
            request.CuttingPlanId,
            version,
            requirement.ObjectScope,
            requirement,
            request.Plan,
            TextileCuttingPlanStates.Draft,
            inputHash,
            request.RuleSetVersion,
            actorId,
            now,
            null);
    }

    public async Task<TextileCuttingPlanResult?> LoadPlanAsync(
        string organizationGroupId,
        string cuttingPlanId,
        long version,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        string legalEntityId;
        string laboratoryId;
        string requirementId;
        long requirementVersion;
        string ruleSetVersion;
        string inputHash;
        TextileCuttingPlan plan;
        string createdBy;
        DateTimeOffset createdAt;

        await using (var command = new NpgsqlCommand("""
            select legal_entity_id, laboratory_id,
                   sample_requirement_id, sample_requirement_version,
                   rule_set_version, input_hash, plan::text, created_by, created_at
            from textile.cutting_plan
            where organization_group_id = @organization_group_id
              and cutting_plan_id = @cutting_plan_id
              and version = @version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("cutting_plan_id", cuttingPlanId);
            command.Parameters.AddWithValue("version", version);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            legalEntityId = reader.GetString(0);
            laboratoryId = reader.GetString(1);
            requirementId = reader.GetString(2);
            requirementVersion = reader.GetInt64(3);
            ruleSetVersion = reader.GetString(4);
            inputHash = reader.GetString(5);
            plan = JsonSerializer.Deserialize<TextileCuttingPlan>(
                reader.GetString(6), TextileJson.Options)
                ?? throw new InvalidOperationException("TEX.CUTTING_PLAN_PAYLOAD_MISSING");
            createdBy = reader.GetString(7);
            createdAt = reader.GetFieldValue<DateTimeOffset>(8);
        }

        var requirement = await LoadRequirementAsync(
            organizationGroupId,
            requirementId,
            requirementVersion,
            cancellationToken)
            ?? throw new InvalidOperationException("TEX.CUTTING_PLAN_REQUIREMENT_MISSING");
        TextileCuttingPlanApproval? approval = null;
        await using (var command = new NpgsqlCommand("""
            select sample_requirement_id, sample_requirement_version,
                   sample_requirement_input_hash, rule_set_version,
                   approved_by, approved_at, approval_comment
            from textile.cutting_plan_approval
            where organization_group_id = @organization_group_id
              and cutting_plan_id = @cutting_plan_id
              and cutting_plan_version = @version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("cutting_plan_id", cuttingPlanId);
            command.Parameters.AddWithValue("version", version);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                approval = new TextileCuttingPlanApproval(
                    cuttingPlanId,
                    version,
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6));
            }
        }

        var currentVersion = await CurrentPlanVersionAsync(
            organizationGroupId,
            cuttingPlanId,
            cancellationToken);
        var state = approval is null
            ? TextileCuttingPlanStates.Draft
            : version < currentVersion
                ? TextileCuttingPlanStates.Superseded
                : TextileCuttingPlanStates.Approved;
        return new TextileCuttingPlanResult(
            cuttingPlanId,
            version,
            new TextileObjectScope(legalEntityId, laboratoryId),
            requirement,
            plan,
            state,
            inputHash,
            ruleSetVersion,
            createdBy,
            createdAt,
            approval);
    }

    public async Task InsertApprovalAsync(
        string organizationGroupId,
        TextileCuttingPlanResult plan,
        ApproveTextileCuttingPlanRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into textile.cutting_plan_approval (
                organization_group_id, cutting_plan_id, cutting_plan_version,
                sample_requirement_id, sample_requirement_version,
                sample_requirement_input_hash, rule_set_version,
                approved_by, approved_at, approval_comment, event_id, correlation_id
            ) values (
                @organization_group_id, @cutting_plan_id, @cutting_plan_version,
                @sample_requirement_id, @sample_requirement_version,
                @sample_requirement_input_hash, @rule_set_version,
                @approved_by, @approved_at, @approval_comment, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("cutting_plan_id", plan.CuttingPlanId);
        command.Parameters.AddWithValue("cutting_plan_version", plan.Version);
        command.Parameters.AddWithValue("sample_requirement_id", plan.SampleRequirement.RequirementId);
        command.Parameters.AddWithValue("sample_requirement_version", plan.SampleRequirement.Version);
        command.Parameters.AddWithValue("sample_requirement_input_hash", request.SampleRequirementInputHash);
        command.Parameters.AddWithValue("rule_set_version", request.RuleSetVersion);
        command.Parameters.AddWithValue("approved_by", actorId);
        command.Parameters.AddWithValue("approved_at", now);
        command.Parameters.AddWithValue("approval_comment", (object?)request.ApprovalComment ?? DBNull.Value);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            actorId,
            organizationGroupId,
            plan.CuttingPlanId,
            "APPROVE_TEXTILE_CUTTING_PLAN",
            plan.Version.ToString(),
            plan.Version.ToString(),
            eventId,
            "TextileCuttingPlanApproved.v1",
            correlationId,
            now,
            cancellationToken);
    }

    public async Task WriteReadAuditAsync(
        TextileCuttingPlanResult plan,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            plan.CuttingPlanId,
            action,
            plan.RuleSetVersion,
            plan.Version.ToString(),
            plan.Version.ToString(),
            correlationId,
            now), cancellationToken);
    }

    private async Task AcquireLockAsync(string key, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtext(@key))",
            connection,
            transaction);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<long> CurrentVersionAsync(
        string table,
        string idColumn,
        string organizationGroupId,
        string id,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            $"select coalesce(max(version), 0) from {table} where organization_group_id = @organization_group_id and {idColumn} = @id",
            connection,
            transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task WritePlatformEvidenceAsync(
        string actorId,
        string organizationGroupId,
        string objectId,
        string action,
        string? beforeVersion,
        string? afterVersion,
        string eventId,
        string messageType,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            objectId,
            action,
            TextileContract.RuleSetVersion,
            beforeVersion,
            afterVersion,
            correlationId,
            now), cancellationToken);
        await outboxWriter.WriteAsync(
            new OutboxEnvelope(eventId, messageType, now),
            cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("TEX.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class TextileAttemptAuditWriter(TextileDataSource dataSource)
{
    public async Task WriteAsync(
        string commandType,
        string? actorId,
        string organizationGroupId,
        string target,
        string correlationId,
        string outcome,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.Value.CreateCommand("""
            insert into textile.audit_attempt (
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
        command.Parameters.AddWithValue("target_hash", HashTarget(target));
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string HashTarget(string target) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(target)));
}
