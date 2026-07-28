using System.Data;
using Npgsql;
using NpgsqlTypes;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class ToyConclusionStore(NpgsqlDataSource dataSource)
{
    public async Task InsertItemConformityConclusionAsync(
        string conclusionId,
        ItemConformityConclusionDraft draft,
        string organizationGroupId,
        string actorId,
        DateTimeOffset timestamp,
        string correlationId,
        ITransactionToken transactionToken)
    {
        var connection = ((NpgsqlTransactionToken)transactionToken).Connection;
        var transaction = ((NpgsqlTransactionToken)transactionToken).Transaction;

        await using var command = new NpgsqlCommand(
            """
            insert into toy.conclusion (
                conclusion_id, organization_group_id, conclusion_level,
                adopted_result_ref, adopted_result_version,
                requirement_ref, requirement_version,
                rule_set_version, statement,
                approved_by, approved_at, version,
                event_id, correlation_id
            ) values (
                @conclusion_id, @organization_group_id, @conclusion_level,
                @adopted_result_ref, @adopted_result_version,
                @requirement_ref, @requirement_version,
                @rule_set_version, @statement,
                @approved_by, @approved_at, @version,
                @event_id, @correlation_id
            )
            """,
            connection,
            transaction);

        command.Parameters.Add(new NpgsqlParameter("conclusion_id", NpgsqlDbType.Uuid) { Value = Guid.Parse(conclusionId) });
        command.Parameters.Add(new NpgsqlParameter("organization_group_id", NpgsqlDbType.Text) { Value = organizationGroupId });
        command.Parameters.Add(new NpgsqlParameter("conclusion_level", NpgsqlDbType.Text) { Value = ToyConclusionLevels.ItemConformity });
        command.Parameters.Add(new NpgsqlParameter("adopted_result_ref", NpgsqlDbType.Text) { Value = draft.AdoptedResultRef });
        command.Parameters.Add(new NpgsqlParameter("adopted_result_version", NpgsqlDbType.Bigint) { Value = draft.AdoptedResultVersion });
        command.Parameters.Add(new NpgsqlParameter("requirement_ref", NpgsqlDbType.Text) { Value = draft.RequirementRef });
        command.Parameters.Add(new NpgsqlParameter("requirement_version", NpgsqlDbType.Bigint) { Value = draft.RequirementVersion });
        command.Parameters.Add(new NpgsqlParameter("rule_set_version", NpgsqlDbType.Text) { Value = draft.RuleSetVersion });
        command.Parameters.Add(new NpgsqlParameter("statement", NpgsqlDbType.Text) { Value = draft.Statement });
        command.Parameters.Add(new NpgsqlParameter("approved_by", NpgsqlDbType.Text) { Value = actorId });
        command.Parameters.Add(new NpgsqlParameter("approved_at", NpgsqlDbType.TimestampTz) { Value = timestamp });
        command.Parameters.Add(new NpgsqlParameter("version", NpgsqlDbType.Bigint) { Value = 1L });
        command.Parameters.Add(new NpgsqlParameter("event_id", NpgsqlDbType.Text) { Value = Guid.NewGuid().ToString("N") });
        command.Parameters.Add(new NpgsqlParameter("correlation_id", NpgsqlDbType.Text) { Value = correlationId });

        await command.ExecuteNonQueryAsync();
    }

    public async Task InsertTestedScopeConformityConclusionAsync(
        string conclusionId,
        TestedScopeConformityConclusionDraft draft,
        string organizationGroupId,
        string actorId,
        DateTimeOffset timestamp,
        string correlationId,
        ITransactionToken transactionToken)
    {
        var connection = ((NpgsqlTransactionToken)transactionToken).Connection;
        var transaction = ((NpgsqlTransactionToken)transactionToken).Transaction;

        // Insert main conclusion record
        await using var mainCommand = new NpgsqlCommand(
            """
            insert into toy.conclusion (
                conclusion_id, organization_group_id, conclusion_level,
                product_ref, product_version,
                test_unit_plan_ref, test_unit_plan_version,
                rule_set_version, statement,
                approved_by, approved_at, version,
                signature_ref,
                event_id, correlation_id
            ) values (
                @conclusion_id, @organization_group_id, @conclusion_level,
                @product_ref, @product_version,
                @test_unit_plan_ref, @test_unit_plan_version,
                @rule_set_version, @statement,
                @approved_by, @approved_at, @version,
                @signature_ref,
                @event_id, @correlation_id
            )
            """,
            connection,
            transaction);

        mainCommand.Parameters.Add(new NpgsqlParameter("conclusion_id", NpgsqlDbType.Uuid) { Value = Guid.Parse(conclusionId) });
        mainCommand.Parameters.Add(new NpgsqlParameter("organization_group_id", NpgsqlDbType.Text) { Value = organizationGroupId });
        mainCommand.Parameters.Add(new NpgsqlParameter("conclusion_level", NpgsqlDbType.Text) { Value = ToyConclusionLevels.TestedScopeConformity });
        mainCommand.Parameters.Add(new NpgsqlParameter("product_ref", NpgsqlDbType.Text) { Value = draft.ProductRef });
        mainCommand.Parameters.Add(new NpgsqlParameter("product_version", NpgsqlDbType.Bigint) { Value = draft.ProductVersion });
        mainCommand.Parameters.Add(new NpgsqlParameter("test_unit_plan_ref", NpgsqlDbType.Text) { Value = draft.TestUnitPlanRef });
        mainCommand.Parameters.Add(new NpgsqlParameter("test_unit_plan_version", NpgsqlDbType.Bigint) { Value = draft.TestUnitPlanVersion });
        mainCommand.Parameters.Add(new NpgsqlParameter("rule_set_version", NpgsqlDbType.Text) { Value = draft.RuleSetVersion });
        mainCommand.Parameters.Add(new NpgsqlParameter("statement", NpgsqlDbType.Text) { Value = draft.Statement });
        mainCommand.Parameters.Add(new NpgsqlParameter("approved_by", NpgsqlDbType.Text) { Value = actorId });
        mainCommand.Parameters.Add(new NpgsqlParameter("approved_at", NpgsqlDbType.TimestampTz) { Value = timestamp });
        mainCommand.Parameters.Add(new NpgsqlParameter("version", NpgsqlDbType.Bigint) { Value = 1L });
        mainCommand.Parameters.Add(new NpgsqlParameter("signature_ref", NpgsqlDbType.Text) { Value = (object?)null ?? DBNull.Value }); // TODO: SEC-SIGN-001
        mainCommand.Parameters.Add(new NpgsqlParameter("event_id", NpgsqlDbType.Text) { Value = Guid.NewGuid().ToString("N") });
        mainCommand.Parameters.Add(new NpgsqlParameter("correlation_id", NpgsqlDbType.Text) { Value = correlationId });

        await mainCommand.ExecuteNonQueryAsync();

        // Insert test unit evidence
        foreach (var testUnit in draft.TestUnits)
        {
            await using var tuCommand = new NpgsqlCommand(
                """
                insert into toy.conclusion_test_unit (
                    conclusion_id, test_unit_id,
                    physical_object_ref, physical_object_version,
                    hazard_domain_ref, hazard_domain_version,
                    adopted_result_ref, adopted_result_version,
                    result_provenance_graph_ref, result_provenance_graph_version,
                    coverage_decision_ref, coverage_decision_version
                ) values (
                    @conclusion_id, @test_unit_id,
                    @physical_object_ref, @physical_object_version,
                    @hazard_domain_ref, @hazard_domain_version,
                    @adopted_result_ref, @adopted_result_version,
                    @result_provenance_graph_ref, @result_provenance_graph_version,
                    @coverage_decision_ref, @coverage_decision_version
                )
                """,
                connection,
                transaction);

            tuCommand.Parameters.Add(new NpgsqlParameter("conclusion_id", NpgsqlDbType.Uuid) { Value = Guid.Parse(conclusionId) });
            tuCommand.Parameters.Add(new NpgsqlParameter("test_unit_id", NpgsqlDbType.Text) { Value = testUnit.TestUnitId });
            tuCommand.Parameters.Add(new NpgsqlParameter("physical_object_ref", NpgsqlDbType.Text) { Value = testUnit.PhysicalObjectRef });
            tuCommand.Parameters.Add(new NpgsqlParameter("physical_object_version", NpgsqlDbType.Bigint) { Value = testUnit.PhysicalObjectVersion });
            tuCommand.Parameters.Add(new NpgsqlParameter("hazard_domain_ref", NpgsqlDbType.Text) { Value = testUnit.HazardDomainRef });
            tuCommand.Parameters.Add(new NpgsqlParameter("hazard_domain_version", NpgsqlDbType.Bigint) { Value = testUnit.HazardDomainVersion });
            tuCommand.Parameters.Add(new NpgsqlParameter("adopted_result_ref", NpgsqlDbType.Text) { Value = testUnit.AdoptedResultRef });
            tuCommand.Parameters.Add(new NpgsqlParameter("adopted_result_version", NpgsqlDbType.Bigint) { Value = testUnit.AdoptedResultVersion });
            tuCommand.Parameters.Add(new NpgsqlParameter("result_provenance_graph_ref", NpgsqlDbType.Text) { Value = testUnit.ResultProvenanceGraphRef });
            tuCommand.Parameters.Add(new NpgsqlParameter("result_provenance_graph_version", NpgsqlDbType.Bigint) { Value = testUnit.ResultProvenanceGraphVersion });
            tuCommand.Parameters.Add(new NpgsqlParameter("coverage_decision_ref", NpgsqlDbType.Text) { Value = (object?)testUnit.CoverageDecisionRef ?? DBNull.Value });
            tuCommand.Parameters.Add(new NpgsqlParameter("coverage_decision_version", NpgsqlDbType.Bigint) { Value = testUnit.CoverageDecisionVersion });

            await tuCommand.ExecuteNonQueryAsync();
        }

        // Insert covered hazard domains
        foreach (var hazardDomain in draft.CoveredHazardDomains)
        {
            await using var hdCommand = new NpgsqlCommand(
                """
                insert into toy.conclusion_hazard_domain (
                    conclusion_id, hazard_domain_ref
                ) values (
                    @conclusion_id, @hazard_domain_ref
                )
                """,
                connection,
                transaction);

            hdCommand.Parameters.Add(new NpgsqlParameter("conclusion_id", NpgsqlDbType.Uuid) { Value = Guid.Parse(conclusionId) });
            hdCommand.Parameters.Add(new NpgsqlParameter("hazard_domain_ref", NpgsqlDbType.Text) { Value = hazardDomain });

            await hdCommand.ExecuteNonQueryAsync();
        }

        // Insert uncovered scopes (mandatory disclosure)
        foreach (var uncovered in draft.UncoveredScopes)
        {
            await using var ucCommand = new NpgsqlCommand(
                """
                insert into toy.conclusion_uncovered_scope (
                    conclusion_id, scope, reason, detail
                ) values (
                    @conclusion_id, @scope, @reason, @detail
                )
                """,
                connection,
                transaction);

            ucCommand.Parameters.Add(new NpgsqlParameter("conclusion_id", NpgsqlDbType.Uuid) { Value = Guid.Parse(conclusionId) });
            ucCommand.Parameters.Add(new NpgsqlParameter("scope", NpgsqlDbType.Text) { Value = uncovered.Scope });
            ucCommand.Parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Text) { Value = uncovered.Reason });
            ucCommand.Parameters.Add(new NpgsqlParameter("detail", NpgsqlDbType.Text) { Value = uncovered.Detail });

            await ucCommand.ExecuteNonQueryAsync();
        }

        // Insert external references (optional, informational only)
        if (draft.ExternalReferences is not null)
        {
            foreach (var extRef in draft.ExternalReferences)
            {
                await using var erCommand = new NpgsqlCommand(
                    """
                    insert into toy.conclusion_external_reference (
                        conclusion_id, issuer, reference, stated_scope, not_part_of_this_conclusion
                    ) values (
                        @conclusion_id, @issuer, @reference, @stated_scope, @not_part_of_this_conclusion
                    )
                    """,
                    connection,
                    transaction);

                erCommand.Parameters.Add(new NpgsqlParameter("conclusion_id", NpgsqlDbType.Uuid) { Value = Guid.Parse(conclusionId) });
                erCommand.Parameters.Add(new NpgsqlParameter("issuer", NpgsqlDbType.Text) { Value = extRef.Issuer });
                erCommand.Parameters.Add(new NpgsqlParameter("reference", NpgsqlDbType.Text) { Value = extRef.Reference });
                erCommand.Parameters.Add(new NpgsqlParameter("stated_scope", NpgsqlDbType.Text) { Value = extRef.StatedScope });
                erCommand.Parameters.Add(new NpgsqlParameter("not_part_of_this_conclusion", NpgsqlDbType.Boolean) { Value = extRef.NotPartOfThisConclusion });

                await erCommand.ExecuteNonQueryAsync();
            }
        }
    }

    public async Task<IReadOnlyList<string>> GetResultRecordersAsync(
        IReadOnlyList<string> adoptedResultRefs,
        CancellationToken cancellationToken)
    {
        // TODO: This should query the Result module to get recorder IDs
        // For now, return empty list (SoD check will pass)
        // In real implementation, this would be a cross-module query
        await Task.CompletedTask;
        return Array.Empty<string>();
    }

    public async Task<ToyConclusionResult?> GetConclusionAsync(
        string conclusionId,
        string organizationGroupId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select
                conclusion_id, conclusion_level, statement,
                approved_by, approved_at, version, signature_ref
            from toy.conclusion
            where conclusion_id = @conclusion_id
              and organization_group_id = @organization_group_id
            """,
            connection);

        command.Parameters.Add(new NpgsqlParameter("conclusion_id", NpgsqlDbType.Uuid) { Value = Guid.Parse(conclusionId) });
        command.Parameters.Add(new NpgsqlParameter("organization_group_id", NpgsqlDbType.Text) { Value = organizationGroupId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var level = reader.GetString(1);
        var statement = reader.GetString(2);
        var approvedBy = reader.GetString(3);
        var approvedAt = reader.GetFieldValue<DateTimeOffset>(4);
        var version = reader.GetInt64(5);
        var signatureRef = reader.IsDBNull(6) ? null : reader.GetString(6);

        // Load covered hazard domains and uncovered scopes for TESTED_SCOPE_CONFORMITY
        string[]? coveredHazardDomains = null;
        UncoveredScopeInput[]? uncoveredScopes = null;
        ExternalReferenceInput[]? externalReferences = null;

        if (level == ToyConclusionLevels.TestedScopeConformity)
        {
            await reader.CloseAsync();

            // Load covered hazard domains
            await using var hdCommand = new NpgsqlCommand(
                """
                select hazard_domain_ref
                from toy.conclusion_hazard_domain
                where conclusion_id = @conclusion_id
                order by hazard_domain_ref
                """,
                connection);
            hdCommand.Parameters.Add(new NpgsqlParameter("conclusion_id", NpgsqlDbType.Uuid) { Value = Guid.Parse(conclusionId) });

            var hdList = new List<string>();
            await using var hdReader = await hdCommand.ExecuteReaderAsync(cancellationToken);
            while (await hdReader.ReadAsync(cancellationToken))
            {
                hdList.Add(hdReader.GetString(0));
            }
            coveredHazardDomains = hdList.ToArray();

            await hdReader.CloseAsync();

            // Load uncovered scopes
            await using var ucCommand = new NpgsqlCommand(
                """
                select scope, reason, detail
                from toy.conclusion_uncovered_scope
                where conclusion_id = @conclusion_id
                order by scope
                """,
                connection);
            ucCommand.Parameters.Add(new NpgsqlParameter("conclusion_id", NpgsqlDbType.Uuid) { Value = Guid.Parse(conclusionId) });

            var ucList = new List<UncoveredScopeInput>();
            await using var ucReader = await ucCommand.ExecuteReaderAsync(cancellationToken);
            while (await ucReader.ReadAsync(cancellationToken))
            {
                ucList.Add(new UncoveredScopeInput(
                    ucReader.GetString(0),
                    ucReader.GetString(1),
                    ucReader.GetString(2)));
            }
            uncoveredScopes = ucList.ToArray();

            await ucReader.CloseAsync();

            // Load external references
            await using var erCommand = new NpgsqlCommand(
                """
                select issuer, reference, stated_scope, not_part_of_this_conclusion
                from toy.conclusion_external_reference
                where conclusion_id = @conclusion_id
                order by issuer, reference
                """,
                connection);
            erCommand.Parameters.Add(new NpgsqlParameter("conclusion_id", NpgsqlDbType.Uuid) { Value = Guid.Parse(conclusionId) });

            var erList = new List<ExternalReferenceInput>();
            await using var erReader = await erCommand.ExecuteReaderAsync(cancellationToken);
            while (await erReader.ReadAsync(cancellationToken))
            {
                erList.Add(new ExternalReferenceInput(
                    erReader.GetString(0),
                    erReader.GetString(1),
                    erReader.GetString(2),
                    erReader.GetBoolean(3)));
            }
            externalReferences = erList.Count > 0 ? erList.ToArray() : null;
        }

        return new ToyConclusionResult(
            conclusionId,
            level,
            statement,
            approvedBy,
            approvedAt,
            version,
            signatureRef,
            coveredHazardDomains,
            uncoveredScopes,
            externalReferences);
    }

    public async Task<IReadOnlyList<ToyConclusionResult>> GetConclusionsByProductAsync(
        string productRef,
        long productVersion,
        string organizationGroupId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select
                conclusion_id, conclusion_level, statement,
                approved_by, approved_at, version, signature_ref
            from toy.conclusion
            where product_ref = @product_ref
              and product_version = @product_version
              and organization_group_id = @organization_group_id
            order by approved_at desc
            """,
            connection);

        command.Parameters.Add(new NpgsqlParameter("product_ref", NpgsqlDbType.Text) { Value = productRef });
        command.Parameters.Add(new NpgsqlParameter("product_version", NpgsqlDbType.Bigint) { Value = productVersion });
        command.Parameters.Add(new NpgsqlParameter("organization_group_id", NpgsqlDbType.Text) { Value = organizationGroupId });

        var results = new List<ToyConclusionResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var conclusionId = reader.GetGuid(0).ToString("N");
            var level = reader.GetString(1);
            var statement = reader.GetString(2);
            var approvedBy = reader.GetString(3);
            var approvedAt = reader.GetFieldValue<DateTimeOffset>(4);
            var version = reader.GetInt64(5);
            var signatureRef = reader.IsDBNull(6) ? null : reader.GetString(6);

            results.Add(new ToyConclusionResult(
                conclusionId,
                level,
                statement,
                approvedBy,
                approvedAt,
                version,
                signatureRef,
                null, // For list view, don't load details
                null,
                null));
        }

        return results;
    }
}
