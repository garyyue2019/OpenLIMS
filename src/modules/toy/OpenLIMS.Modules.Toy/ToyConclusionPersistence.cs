using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class ToyConclusionStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireCorrelationLockAsync(
        string organizationGroupId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@key, 0))",
            connection,
            transaction);
        command.Parameters.AddWithValue("key", $"openlims.toy.conclusion.{organizationGroupId}.{correlationId}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertItemConformityConclusionAsync(
        Guid conclusionId,
        ItemConformityConclusionDraft draft,
        ToyResolvedResultEvidence evidence,
        string organizationGroupId,
        string actorId,
        DateTimeOffset timestamp,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand(
            """
            insert into toy.conclusion (
                conclusion_id, organization_group_id, conclusion_level,
                legal_entity_id, laboratory_id,
                adopted_result_ref, adopted_result_version,
                resolved_target_ref, resolved_target_kind,
                result_recorded_by, result_group_version,
                requirement_ref, requirement_version,
                rule_set_version, statement, content_hash,
                approved_by, approved_at, version,
                event_id, correlation_id, created_by
            ) values (
                @conclusion_id, @organization_group_id, @conclusion_level,
                @legal_entity_id, @laboratory_id,
                @adopted_result_ref, @adopted_result_version,
                @resolved_target_ref, @resolved_target_kind,
                @result_recorded_by, @result_group_version,
                @requirement_ref, @requirement_version,
                @rule_set_version, @statement, @content_hash,
                @approved_by, @approved_at, 1,
                @event_id, @correlation_id, @created_by
            )
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("conclusion_id", conclusionId);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("conclusion_level", ToyConclusionLevels.ItemConformity);
        command.Parameters.AddWithValue("legal_entity_id", evidence.ObjectScope.LegalEntityId);
        command.Parameters.AddWithValue("laboratory_id", evidence.ObjectScope.LaboratoryId);
        command.Parameters.AddWithValue("adopted_result_ref", draft.AdoptedResultRef);
        command.Parameters.AddWithValue("adopted_result_version", draft.AdoptedResultVersion);
        command.Parameters.AddWithValue("resolved_target_ref", evidence.TargetId);
        command.Parameters.AddWithValue("resolved_target_kind", evidence.TargetKind);
        command.Parameters.AddWithValue("result_recorded_by", evidence.RecordedBy);
        command.Parameters.AddWithValue("result_group_version", evidence.CurrentGroupVersion);
        command.Parameters.AddWithValue("requirement_ref", draft.RequirementRef);
        command.Parameters.AddWithValue("requirement_version", draft.RequirementVersion);
        command.Parameters.AddWithValue("rule_set_version", draft.RuleSetVersion);
        command.Parameters.AddWithValue("statement", draft.Statement);
        command.Parameters.AddWithValue("content_hash", draft.ContentHash);
        command.Parameters.AddWithValue("approved_by", actorId);
        command.Parameters.AddWithValue("approved_at", timestamp);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("created_by", actorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteCreateEvidenceAsync(
            conclusionId,
            organizationGroupId,
            actorId,
            "CREATE_TOY_ITEM_CONCLUSION",
            eventId,
            correlationId,
            timestamp,
            cancellationToken);
    }

    public async Task InsertTestedScopeConformityConclusionAsync(
        Guid conclusionId,
        TestedScopeConformityConclusionDraft draft,
        IReadOnlyDictionary<string, ToyResolvedResultEvidence> evidenceByTestUnit,
        ToyObjectContext objectScope,
        string organizationGroupId,
        string actorId,
        DateTimeOffset timestamp,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        var signatureRef = $"{draft.ReauthenticationRef.Id}@{draft.ReauthenticationRef.Version}";
        await using (var command = new NpgsqlCommand(
            """
            insert into toy.conclusion (
                conclusion_id, organization_group_id, conclusion_level,
                legal_entity_id, laboratory_id,
                product_ref, product_version,
                test_unit_plan_ref, test_unit_plan_version,
                rule_set_version, statement, content_hash,
                approved_by, approved_at, version,
                signature_ref, reauthentication_ref, reauthentication_version, signing_intent,
                event_id, correlation_id, created_by
            ) values (
                @conclusion_id, @organization_group_id, @conclusion_level,
                @legal_entity_id, @laboratory_id,
                @product_ref, @product_version,
                @test_unit_plan_ref, @test_unit_plan_version,
                @rule_set_version, @statement, @content_hash,
                @approved_by, @approved_at, 1,
                @signature_ref, @reauthentication_ref, @reauthentication_version, @signing_intent,
                @event_id, @correlation_id, @created_by
            )
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("conclusion_id", conclusionId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("conclusion_level", ToyConclusionLevels.TestedScopeConformity);
            command.Parameters.AddWithValue("legal_entity_id", objectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", objectScope.LaboratoryId);
            command.Parameters.AddWithValue("product_ref", draft.ProductRef);
            command.Parameters.AddWithValue("product_version", draft.ProductVersion);
            command.Parameters.AddWithValue("test_unit_plan_ref", draft.TestUnitPlanRef);
            command.Parameters.AddWithValue("test_unit_plan_version", draft.TestUnitPlanVersion);
            command.Parameters.AddWithValue("rule_set_version", draft.RuleSetVersion);
            command.Parameters.AddWithValue("statement", draft.Statement);
            command.Parameters.AddWithValue("content_hash", draft.ContentHash);
            command.Parameters.AddWithValue("approved_by", actorId);
            command.Parameters.AddWithValue("approved_at", timestamp);
            command.Parameters.AddWithValue("signature_ref", signatureRef);
            command.Parameters.AddWithValue("reauthentication_ref", draft.ReauthenticationRef.Id);
            command.Parameters.AddWithValue("reauthentication_version", draft.ReauthenticationRef.Version);
            command.Parameters.AddWithValue("signing_intent", draft.SigningIntent);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            command.Parameters.AddWithValue("created_by", actorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var testUnit in draft.TestUnits)
        {
            var evidence = evidenceByTestUnit[testUnit.TestUnitId];
            await using var command = new NpgsqlCommand(
                """
                insert into toy.conclusion_test_unit (
                    conclusion_id, test_unit_id,
                    physical_object_ref, physical_object_version,
                    hazard_domain_ref, hazard_domain_version,
                    adopted_result_ref, adopted_result_version,
                    result_provenance_graph_ref, result_provenance_graph_version,
                    coverage_decision_ref, coverage_decision_version,
                    requirement_refs,
                    resolved_target_ref, resolved_target_kind,
                    result_recorded_by, result_group_version
                ) values (
                    @conclusion_id, @test_unit_id,
                    @physical_object_ref, @physical_object_version,
                    @hazard_domain_ref, @hazard_domain_version,
                    @adopted_result_ref, @adopted_result_version,
                    @result_provenance_graph_ref, @result_provenance_graph_version,
                    @coverage_decision_ref, @coverage_decision_version,
                    @requirement_refs,
                    @resolved_target_ref, @resolved_target_kind,
                    @result_recorded_by, @result_group_version
                )
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("conclusion_id", conclusionId);
            command.Parameters.AddWithValue("test_unit_id", testUnit.TestUnitId);
            command.Parameters.AddWithValue("physical_object_ref", testUnit.PhysicalObjectRef);
            command.Parameters.AddWithValue("physical_object_version", testUnit.PhysicalObjectVersion);
            command.Parameters.AddWithValue("hazard_domain_ref", testUnit.HazardDomainRef);
            command.Parameters.AddWithValue("hazard_domain_version", testUnit.HazardDomainVersion);
            command.Parameters.AddWithValue("adopted_result_ref", testUnit.AdoptedResultRef);
            command.Parameters.AddWithValue("adopted_result_version", testUnit.AdoptedResultVersion);
            command.Parameters.AddWithValue("result_provenance_graph_ref", testUnit.ResultProvenanceGraphRef);
            command.Parameters.AddWithValue("result_provenance_graph_version", testUnit.ResultProvenanceGraphVersion);
            command.Parameters.AddWithValue("coverage_decision_ref", testUnit.CoverageDecisionRef!);
            command.Parameters.AddWithValue("coverage_decision_version", testUnit.CoverageDecisionVersion);
            command.Parameters.AddWithValue("requirement_refs", testUnit.RequirementRefs?.ToArray() ?? []);
            command.Parameters.AddWithValue("resolved_target_ref", evidence.TargetId);
            command.Parameters.AddWithValue("resolved_target_kind", evidence.TargetKind);
            command.Parameters.AddWithValue("result_recorded_by", evidence.RecordedBy);
            command.Parameters.AddWithValue("result_group_version", evidence.CurrentGroupVersion);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var hazardDomain in draft.CoveredHazardDomains)
        {
            await using var command = new NpgsqlCommand(
                """
                insert into toy.conclusion_hazard_domain (conclusion_id, hazard_domain_ref)
                values (@conclusion_id, @hazard_domain_ref)
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("conclusion_id", conclusionId);
            command.Parameters.AddWithValue("hazard_domain_ref", hazardDomain);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var uncovered in draft.UncoveredScopes)
        {
            await using var command = new NpgsqlCommand(
                """
                insert into toy.conclusion_uncovered_scope (conclusion_id, scope, reason, detail)
                values (@conclusion_id, @scope, @reason, @detail)
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("conclusion_id", conclusionId);
            command.Parameters.AddWithValue("scope", uncovered.Scope);
            command.Parameters.AddWithValue("reason", uncovered.Reason);
            command.Parameters.AddWithValue("detail", uncovered.Detail);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var reference in draft.ExternalReferences ?? [])
        {
            await using var command = new NpgsqlCommand(
                """
                insert into toy.conclusion_external_reference (
                    conclusion_id, issuer, reference, stated_scope, not_part_of_this_conclusion)
                values (@conclusion_id, @issuer, @reference, @stated_scope, @not_part_of_this_conclusion)
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("conclusion_id", conclusionId);
            command.Parameters.AddWithValue("issuer", reference.Issuer);
            command.Parameters.AddWithValue("reference", reference.Reference);
            command.Parameters.AddWithValue("stated_scope", reference.StatedScope);
            command.Parameters.AddWithValue("not_part_of_this_conclusion", reference.NotPartOfThisConclusion);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteCreateEvidenceAsync(
            conclusionId,
            organizationGroupId,
            actorId,
            "CREATE_TOY_TESTED_SCOPE_CONCLUSION",
            eventId,
            correlationId,
            timestamp,
            cancellationToken);
    }

    public async Task<StoredToyConclusion?> LoadByCorrelationAsync(
        string organizationGroupId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            """
            select conclusion_id
            from toy.conclusion
            where organization_group_id = @organization_group_id
              and correlation_id = @correlation_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid conclusionId
            ? await LoadConclusionAsync(organizationGroupId, conclusionId, cancellationToken)
            : null;
    }

    public async Task<StoredToyConclusion?> LoadConclusionAsync(
        string organizationGroupId,
        Guid conclusionId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        BaseConclusionRow row;
        await using (var command = new NpgsqlCommand(
            """
            select conclusion_id, conclusion_level, statement,
                   approved_by, approved_at, version, signature_ref, content_hash,
                   legal_entity_id, laboratory_id
            from toy.conclusion
            where conclusion_id = @conclusion_id
              and organization_group_id = @organization_group_id
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("conclusion_id", conclusionId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            row = ReadBaseRow(reader);
        }

        string[]? coveredHazardDomains = null;
        UncoveredScopeInput[]? uncoveredScopes = null;
        ExternalReferenceInput[]? externalReferences = null;
        if (string.Equals(row.Level, ToyConclusionLevels.TestedScopeConformity, StringComparison.Ordinal))
        {
            coveredHazardDomains = await LoadCoveredHazardDomainsAsync(conclusionId, cancellationToken);
            uncoveredScopes = await LoadUncoveredScopesAsync(conclusionId, cancellationToken);
            externalReferences = await LoadExternalReferencesAsync(conclusionId, cancellationToken);
        }

        return ToStored(row, coveredHazardDomains, uncoveredScopes, externalReferences);
    }

    public async Task<IReadOnlyList<StoredToyConclusion>> LoadConclusionsByProductAsync(
        string productRef,
        long productVersion,
        string organizationGroupId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var results = new List<StoredToyConclusion>();
        await using var command = new NpgsqlCommand(
            """
            select conclusion_id, conclusion_level, statement,
                   approved_by, approved_at, version, signature_ref, content_hash,
                   legal_entity_id, laboratory_id
            from toy.conclusion
            where product_ref = @product_ref
              and product_version = @product_version
              and organization_group_id = @organization_group_id
            order by approved_at desc, conclusion_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("product_ref", productRef);
        command.Parameters.AddWithValue("product_version", productVersion);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ToStored(ReadBaseRow(reader), null, null, null));
        return results;
    }

    public Task WriteReadAuditAsync(
        StoredToyConclusion conclusion,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            conclusion.Result.ConclusionId,
            action,
            ToyConclusionContract.RuleSetVersion,
            conclusion.Result.Version.ToString(),
            conclusion.Result.Version.ToString(),
            correlationId,
            timestamp), cancellationToken);

    private async Task<string[]> LoadCoveredHazardDomainsAsync(
        Guid conclusionId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var values = new List<string>();
        await using var command = new NpgsqlCommand(
            """
            select hazard_domain_ref
            from toy.conclusion_hazard_domain
            where conclusion_id = @conclusion_id
            order by hazard_domain_ref
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("conclusion_id", conclusionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(reader.GetString(0));
        return values.ToArray();
    }

    private async Task<UncoveredScopeInput[]> LoadUncoveredScopesAsync(
        Guid conclusionId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var values = new List<UncoveredScopeInput>();
        await using var command = new NpgsqlCommand(
            """
            select scope, reason, detail
            from toy.conclusion_uncovered_scope
            where conclusion_id = @conclusion_id
            order by scope
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("conclusion_id", conclusionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new UncoveredScopeInput(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return values.ToArray();
    }

    private async Task<ExternalReferenceInput[]?> LoadExternalReferencesAsync(
        Guid conclusionId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var values = new List<ExternalReferenceInput>();
        await using var command = new NpgsqlCommand(
            """
            select issuer, reference, stated_scope, not_part_of_this_conclusion
            from toy.conclusion_external_reference
            where conclusion_id = @conclusion_id
            order by issuer, reference
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("conclusion_id", conclusionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new ExternalReferenceInput(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }
        return values.Count == 0 ? null : values.ToArray();
    }

    private async Task WriteCreateEvidenceAsync(
        Guid conclusionId,
        string organizationGroupId,
        string actorId,
        string action,
        string eventId,
        string correlationId,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            conclusionId.ToString("N"),
            action,
            ToyConclusionContract.RuleSetVersion,
            null,
            "1",
            correlationId,
            timestamp), cancellationToken);
        await outboxWriter.WriteAsync(
            new OutboxEnvelope(eventId, "ToyConclusionCreated.v1", timestamp),
            cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("TOY.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }

    private static BaseConclusionRow ReadBaseRow(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetFieldValue<DateTimeOffset>(4),
        reader.GetInt64(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9));

    private static StoredToyConclusion ToStored(
        BaseConclusionRow row,
        IReadOnlyList<string>? coveredHazardDomains,
        IReadOnlyList<UncoveredScopeInput>? uncoveredScopes,
        IReadOnlyList<ExternalReferenceInput>? externalReferences) => new(
        new ToyConclusionResult(
            row.Id.ToString("N"),
            row.Level,
            row.Statement,
            row.ApprovedBy,
            row.ApprovedAt,
            row.Version,
            row.SignatureRef,
            coveredHazardDomains,
            uncoveredScopes,
            externalReferences,
            row.ContentHash),
        row.LegalEntityId is null || row.LaboratoryId is null
            ? null
            : new ToyObjectContext(row.LegalEntityId, row.LaboratoryId));

    private sealed record BaseConclusionRow(
        Guid Id,
        string Level,
        string Statement,
        string ApprovedBy,
        DateTimeOffset ApprovedAt,
        long Version,
        string? SignatureRef,
        string? ContentHash,
        string? LegalEntityId,
        string? LaboratoryId);
}

internal sealed record ToyResolvedResultEvidence(
    string ResultGroupId,
    long AdoptionVersion,
    long CurrentGroupVersion,
    string TargetId,
    string TargetKind,
    string RecordedBy,
    ToyObjectContext ObjectScope);

internal sealed record StoredToyConclusion(
    ToyConclusionResult Result,
    ToyObjectContext? ObjectScope);
