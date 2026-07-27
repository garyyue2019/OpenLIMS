using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class ToyDataSource : IAsyncDisposable
{
    public ToyDataSource(ToyPersistenceOptions options) => Value = NpgsqlDataSource.Create(options.ConnectionString);
    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed class ToyStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireProductLockAsync(Guid productId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
        command.Parameters.AddWithValue("key", $"openlims.toy.product.{productId:N}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The product record is created by whichever command first names it. It
    /// carries the object scope every later command is authorized against, so
    /// a mismatched scope is rejected rather than quietly re-bound.
    /// </summary>
    public async Task EnsureProductAsync(
        Guid productId,
        string organizationGroupId,
        ToyObjectContext objectScope,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into toy.product (
                product_id, organization_group_id, legal_entity_id, laboratory_id,
                rule_set_version, registered_by, registered_at, event_id, correlation_id
            ) values (
                @product_id, @organization_group_id, @legal_entity_id, @laboratory_id,
                @rule_set_version, @registered_by, @registered_at, @event_id, @correlation_id
            )
            on conflict (product_id) do nothing
            """, connection, transaction);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("legal_entity_id", objectScope.LegalEntityId);
        command.Parameters.AddWithValue("laboratory_id", objectScope.LaboratoryId);
        command.Parameters.AddWithValue("rule_set_version", ToyContract.RuleSetVersion);
        command.Parameters.AddWithValue("registered_by", actorId);
        command.Parameters.AddWithValue("registered_at", now);
        command.Parameters.AddWithValue("event_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, productId.ToString("N"), "REGISTER_TOY_PRODUCT",
            ToyContract.RuleSetVersion, null, "1", correlationId, now), cancellationToken);
    }

    public async Task InsertDeclarationAsync(
        Guid productId,
        long productVersion,
        RecordAgeDeclarationRequest request,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into toy.age_declaration (
                declaration_id, product_id, product_version, declared_minimum_age_months,
                intended_use, declaration_source, declared_by, declared_at, event_id, correlation_id
            ) values (
                @declaration_id, @product_id, @product_version, @declared_minimum_age_months,
                @intended_use, @declaration_source, @declared_by, @declared_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("declaration_id", Guid.NewGuid());
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("product_version", productVersion);
            command.Parameters.AddWithValue("declared_minimum_age_months", request.DeclaredMinimumAgeMonths);
            command.Parameters.AddWithValue("intended_use", request.IntendedUse);
            command.Parameters.AddWithValue("declaration_source", request.DeclarationSource);
            command.Parameters.AddWithValue("declared_by", actorId);
            command.Parameters.AddWithValue("declared_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RECORD_TOY_AGE_DECLARATION", productId.ToString("N"), organizationGroupId, actorId,
            eventId, "Toy.AgeDeclared.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertDecisionAsync(
        Guid productId,
        int versionNumber,
        RecordAgeGradeDecisionRequest request,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into toy.age_grade_decision (
                decision_id, product_id, version_number, minimum_age_months, rationale,
                standard_ref, standard_version, approved_by, decided_at, event_id, correlation_id
            ) values (
                @decision_id, @product_id, @version_number, @minimum_age_months, @rationale,
                @standard_ref, @standard_version, @approved_by, @decided_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("decision_id", Guid.NewGuid());
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("version_number", versionNumber);
            command.Parameters.AddWithValue("minimum_age_months", request.MinimumAgeMonths);
            command.Parameters.AddWithValue("rationale", request.Rationale);
            command.Parameters.AddWithValue("standard_ref", request.StandardRef.Id);
            command.Parameters.AddWithValue("standard_version", request.StandardRef.Version);
            command.Parameters.AddWithValue("approved_by", request.ApprovedBy);
            command.Parameters.AddWithValue("decided_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RECORD_TOY_AGE_GRADE_DECISION", productId.ToString("N"), organizationGroupId, actorId,
            eventId, "Toy.AgeGradeDecided.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertFreezeAsync(
        Guid productId,
        int versionNumber,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into toy.age_grade_freeze (
                freeze_id, product_id, version_number, frozen_by, frozen_at, event_id, correlation_id
            ) values (
                @freeze_id, @product_id, @version_number, @frozen_by, @frozen_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("freeze_id", Guid.NewGuid());
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("version_number", versionNumber);
            command.Parameters.AddWithValue("frozen_by", actorId);
            command.Parameters.AddWithValue("frozen_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "FREEZE_TOY_AGE_GRADE_DECISION", productId.ToString("N"), organizationGroupId, actorId,
            eventId, "Toy.AgeGradeFrozen.v1", correlationId, now, cancellationToken);
    }

    public async Task<Guid> InsertAssessmentAsync(
        Guid productId,
        int versionNumber,
        RecordAccessibilityAssessmentRequest request,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var assessmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into toy.accessibility_assessment (
                assessment_id, product_id, version_number, stage, abuse_event_ref,
                assessed_by, assessed_at, event_id, correlation_id
            ) values (
                @assessment_id, @product_id, @version_number, @stage, @abuse_event_ref,
                @assessed_by, @assessed_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("assessment_id", assessmentId);
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("version_number", versionNumber);
            command.Parameters.AddWithValue("stage", request.Stage);
            command.Parameters.AddWithValue("abuse_event_ref", (object?)request.AbuseEventRef ?? DBNull.Value);
            command.Parameters.AddWithValue("assessed_by", actorId);
            command.Parameters.AddWithValue("assessed_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var part in request.AccessibleParts)
        {
            await using var command = new NpgsqlCommand("""
                insert into toy.accessible_part (part_row_id, assessment_id, part_name)
                values (@part_row_id, @assessment_id, @part_name)
                """, connection, transaction);
            command.Parameters.AddWithValue("part_row_id", Guid.NewGuid());
            command.Parameters.AddWithValue("assessment_id", assessmentId);
            command.Parameters.AddWithValue("part_name", part);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RECORD_TOY_ACCESSIBILITY_ASSESSMENT", productId.ToString("N"), organizationGroupId, actorId,
            eventId, "Toy.AccessibilityAssessed.v1", correlationId, now, cancellationToken);
        return assessmentId;
    }

    public async Task InsertTriggersAsync(
        Guid productId,
        Guid assessmentId,
        int assessmentVersion,
        IReadOnlyList<string> newlyExposedParts,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        foreach (var scope in ToyReassessmentScopes.All)
        {
            var eventId = Guid.NewGuid().ToString("N");
            await using (var command = new NpgsqlCommand("""
                insert into toy.reassessment_trigger (
                    trigger_id, product_id, assessment_id, assessment_version, scope,
                    newly_exposed_parts, raised_at, event_id, correlation_id
                ) values (
                    @trigger_id, @product_id, @assessment_id, @assessment_version, @scope,
                    @newly_exposed_parts, @raised_at, @event_id, @correlation_id
                )
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("trigger_id", Guid.NewGuid());
                command.Parameters.AddWithValue("product_id", productId);
                command.Parameters.AddWithValue("assessment_id", assessmentId);
                command.Parameters.AddWithValue("assessment_version", assessmentVersion);
                command.Parameters.AddWithValue("scope", scope);
                command.Parameters.AddWithValue("newly_exposed_parts", newlyExposedParts.ToArray());
                command.Parameters.AddWithValue("raised_at", now);
                command.Parameters.AddWithValue("event_id", eventId);
                command.Parameters.AddWithValue("correlation_id", correlationId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await WritePlatformEvidenceAsync(
                $"RAISE_TOY_REASSESSMENT_{scope}", productId.ToString("N"), organizationGroupId, actorId,
                eventId, "Toy.ReassessmentRaised.v1", correlationId, now, cancellationToken);
        }
    }

    public async Task InsertResolutionAsync(
        Guid productId,
        Guid triggerId,
        ResolveReassessmentTriggerRequest request,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into toy.reassessment_resolution (
                resolution_id, trigger_id, product_id, resolution_ref, resolution_version,
                resolved_by, resolved_at, event_id, correlation_id
            ) values (
                @resolution_id, @trigger_id, @product_id, @resolution_ref, @resolution_version,
                @resolved_by, @resolved_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("resolution_id", Guid.NewGuid());
            command.Parameters.AddWithValue("trigger_id", triggerId);
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("resolution_ref", request.ResolutionRef.Id);
            command.Parameters.AddWithValue("resolution_version", request.ResolutionRef.Version);
            command.Parameters.AddWithValue("resolved_by", actorId);
            command.Parameters.AddWithValue("resolved_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RESOLVE_TOY_REASSESSMENT", productId.ToString("N"), organizationGroupId, actorId,
            eventId, "Toy.ReassessmentResolved.v1", correlationId, now, cancellationToken);
    }

    public async Task<ToyProductOverview?> LoadProductAsync(
        string organizationGroupId, Guid productId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        ToyObjectContext? objectScope = null;
        await using (var command = new NpgsqlCommand("""
            select legal_entity_id, laboratory_id from toy.product
            where organization_group_id = @organization_group_id and product_id = @product_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("product_id", productId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            objectScope = new ToyObjectContext(reader.GetString(0), reader.GetString(1));
        }

        var declarations = await LoadDeclarationsAsync(productId, cancellationToken);
        var frozen = await LoadFreezesAsync(productId, cancellationToken);
        var decisions = await LoadDecisionsAsync(productId, frozen, cancellationToken);
        var assessments = await LoadAssessmentsAsync(productId, cancellationToken);
        var triggers = await LoadTriggersAsync(productId, cancellationToken);

        var resolvedCount = triggers.Count(trigger =>
            string.Equals(trigger.State, ToyTriggerStates.Resolved, StringComparison.Ordinal));
        var version = 1L + declarations.Count + decisions.Count + frozen.Count
                      + assessments.Count + triggers.Count + resolvedCount;
        return new ToyProductOverview(
            productId.ToString("N"), version, ToyContract.RuleSetVersion, objectScope,
            ToyDomain.ResolveEffectiveDecision(decisions),
            declarations, decisions, assessments, triggers,
            ToyDomain.ResolveAccessibilityStatus(triggers));
    }

    private async Task<IReadOnlyList<ToyAgeDeclarationEntry>> LoadDeclarationsAsync(
        Guid productId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var declarations = new List<ToyAgeDeclarationEntry>();
        await using var command = new NpgsqlCommand("""
            select declaration_id, declared_minimum_age_months, intended_use,
                   declaration_source, declared_by, declared_at
            from toy.age_declaration where product_id = @id
            -- product_version rises with every appended fact, so it orders
            -- declarations deterministically even under a fixed clock.
            order by product_version, declaration_id
            """, connection, transaction);
        command.Parameters.AddWithValue("id", productId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            declarations.Add(new ToyAgeDeclarationEntry(
                reader.GetGuid(0).ToString("N"), productId.ToString("N"), reader.GetInt32(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return declarations;
    }

    private async Task<IReadOnlyDictionary<int, DateTimeOffset>> LoadFreezesAsync(
        Guid productId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var frozen = new Dictionary<int, DateTimeOffset>();
        await using var command = new NpgsqlCommand("""
            select version_number, frozen_at from toy.age_grade_freeze
            where product_id = @id order by version_number
            """, connection, transaction);
        command.Parameters.AddWithValue("id", productId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            frozen[reader.GetInt32(0)] = reader.GetFieldValue<DateTimeOffset>(1);
        return frozen;
    }

    private async Task<IReadOnlyList<ToyAgeGradeDecisionEntry>> LoadDecisionsAsync(
        Guid productId,
        IReadOnlyDictionary<int, DateTimeOffset> frozen,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var effectiveVersion = frozen.Count == 0 ? (int?)null : frozen.Keys.Max();
        var decisions = new List<ToyAgeGradeDecisionEntry>();
        await using var command = new NpgsqlCommand("""
            select decision_id, version_number, minimum_age_months, rationale,
                   standard_ref, standard_version, approved_by, decided_at
            from toy.age_grade_decision where product_id = @id order by version_number
            """, connection, transaction);
        command.Parameters.AddWithValue("id", productId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var versionNumber = reader.GetInt32(1);
            var frozenAt = frozen.TryGetValue(versionNumber, out var at) ? at : (DateTimeOffset?)null;
            var state = frozenAt is null
                ? ToyDecisionStates.Draft
                : versionNumber == effectiveVersion
                    ? ToyDecisionStates.Effective
                    : ToyDecisionStates.Superseded;
            decisions.Add(new ToyAgeGradeDecisionEntry(
                reader.GetGuid(0).ToString("N"), productId.ToString("N"), versionNumber,
                reader.GetInt32(2), reader.GetString(3),
                new ToyVersionedReference(reader.GetString(4), reader.GetInt64(5)),
                reader.GetString(6), state, reader.GetFieldValue<DateTimeOffset>(7), frozenAt));
        }

        return decisions;
    }

    private async Task<IReadOnlyList<ToyAccessibilityAssessmentEntry>> LoadAssessmentsAsync(
        Guid productId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var assessments = new List<(Guid Id, ToyAccessibilityAssessmentEntry Entry)>();
        await using (var command = new NpgsqlCommand("""
            select assessment_id, version_number, stage, abuse_event_ref, assessed_by, assessed_at
            from toy.accessibility_assessment where product_id = @id order by version_number
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", productId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var assessmentId = reader.GetGuid(0);
                assessments.Add((assessmentId, new ToyAccessibilityAssessmentEntry(
                    assessmentId.ToString("N"), productId.ToString("N"), reader.GetInt32(1),
                    reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                    [], reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5))));
            }
        }

        var result = new List<ToyAccessibilityAssessmentEntry>(assessments.Count);
        foreach (var (assessmentId, entry) in assessments)
        {
            var parts = new List<string>();
            await using var command = new NpgsqlCommand("""
                select part_name from toy.accessible_part
                where assessment_id = @id order by part_name
                """, connection, transaction);
            command.Parameters.AddWithValue("id", assessmentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                parts.Add(reader.GetString(0));
            result.Add(entry with { AccessibleParts = parts });
        }

        return result;
    }

    private async Task<IReadOnlyList<ToyReassessmentTriggerEntry>> LoadTriggersAsync(
        Guid productId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var triggers = new List<ToyReassessmentTriggerEntry>();
        await using var command = new NpgsqlCommand("""
            select t.trigger_id, t.assessment_version, t.scope, t.newly_exposed_parts,
                   r.resolution_ref, r.resolution_version, r.resolved_by, r.resolved_at
            from toy.reassessment_trigger t
            left join toy.reassessment_resolution r on r.trigger_id = t.trigger_id
            where t.product_id = @id
            order by t.assessment_version, t.scope
            """, connection, transaction);
        command.Parameters.AddWithValue("id", productId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var resolved = !reader.IsDBNull(4);
            triggers.Add(new ToyReassessmentTriggerEntry(
                reader.GetGuid(0).ToString("N"), productId.ToString("N"), reader.GetInt32(1),
                reader.GetString(2), reader.GetFieldValue<string[]>(3),
                resolved ? ToyTriggerStates.Resolved : ToyTriggerStates.Pending,
                resolved ? new ToyVersionedReference(reader.GetString(4), reader.GetInt64(5)) : null,
                resolved ? reader.GetString(6) : null,
                resolved ? reader.GetFieldValue<DateTimeOffset>(7) : null));
        }

        return triggers;
    }

    public Task WriteReadAuditAsync(
        string productId, long productVersion, string organizationGroupId, string actorId,
        string action, string correlationId, DateTimeOffset now, CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, productId, action, ToyContract.RuleSetVersion,
            productVersion.ToString(), productVersion.ToString(), correlationId, now), cancellationToken);

    private async Task WritePlatformEvidenceAsync(
        string action, string objectId, string organizationGroupId, string actorId,
        string eventId, string messageType, string correlationId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, objectId, action, ToyContract.RuleSetVersion,
            null, "1", correlationId, now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("TOY.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class ToyAttemptAuditWriter(ToyDataSource dataSource)
{
    public async Task WriteAsync(
        string commandType, string? actorId, string organizationGroupId, string targetHash,
        string correlationId, string outcome, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.Value.CreateCommand("""
            insert into toy.audit_attempt (
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
