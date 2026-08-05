using Npgsql;
using System.Text.Json;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Result;

namespace OpenLIMS.Modules.Result;

internal sealed class ResultDataSource : IAsyncDisposable
{
    public ResultDataSource(ResultPersistenceOptions options) => Value = NpgsqlDataSource.Create(options.ConnectionString);
    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed class ResultStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AcquireGroupLockAsync(Guid resultGroupId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@result_group_id, 0))", connection, transaction);
        command.Parameters.AddWithValue("result_group_id", resultGroupId.ToString("N"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ResultGroupResult> InsertGroupAsync(
        Guid resultGroupId,
        string organizationGroupId,
        CreateResultGroupRequest request,
        string batchGateDecision,
        string batchGateRuleSetVersion,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into result.result_group (
                result_group_id, organization_group_id,
                legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                batch_id, batch_version, batch_gate_decision, batch_gate_rule_set_version,
                member_id, test_item_ref, test_item_version, scope_line_id,
                rule_set_version, created_by, created_at, event_id, correlation_id
            ) values (
                @result_group_id, @organization_group_id,
                @legal_entity_id, @laboratory_id, @customer_id, @service_order_id, @product_category,
                @batch_id, @batch_version, @batch_gate_decision, @batch_gate_rule_set_version,
                @member_id, @test_item_ref, @test_item_version, @scope_line_id,
                @rule_set_version, @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("result_group_id", resultGroupId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("legal_entity_id", request.ObjectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", request.ObjectScope.LaboratoryId);
            command.Parameters.AddWithValue("customer_id", request.ObjectScope.CustomerId);
            command.Parameters.AddWithValue("service_order_id", request.ObjectScope.ServiceOrderId);
            command.Parameters.AddWithValue("product_category", request.ObjectScope.ProductCategory);
            command.Parameters.AddWithValue("batch_id", request.BatchId);
            command.Parameters.AddWithValue("batch_version", request.ExpectedBatchVersion);
            command.Parameters.AddWithValue("batch_gate_decision", batchGateDecision);
            command.Parameters.AddWithValue("batch_gate_rule_set_version", batchGateRuleSetVersion);
            command.Parameters.AddWithValue("member_id", request.MemberId);
            command.Parameters.AddWithValue("test_item_ref", request.TestItem.Id);
            command.Parameters.AddWithValue("test_item_version", request.TestItem.Version);
            command.Parameters.AddWithValue("scope_line_id", request.ScopeLineId);
            command.Parameters.AddWithValue("rule_set_version", ResultContract.RuleSetVersion);
            command.Parameters.AddWithValue("created_by", actorId);
            command.Parameters.AddWithValue("created_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "CREATE_RESULT_GROUP", resultGroupId.ToString("N"), organizationGroupId, actorId,
            null, "1", eventId, "ResultGroupCreated.v1", correlationId, now, cancellationToken);
        return new ResultGroupResult(
            resultGroupId.ToString("N"), 1, ResultContract.RuleSetVersion, request.ObjectScope,
            request.BatchId, request.ExpectedBatchVersion, batchGateDecision, batchGateRuleSetVersion,
            request.MemberId, request.TestItem, request.ScopeLineId,
            [], [], [], [], actorId, now);
    }

    public async Task<ResultGroupResult?> LoadGroupAsync(
        string organizationGroupId,
        Guid resultGroupId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        ResultObjectContext objectScope;
        string batchId, gateDecision, gateRuleSet, memberId, scopeLineId, createdBy;
        long batchVersion;
        ResultVersionedReference testItem;
        DateTimeOffset createdAt;
        await using (var command = new NpgsqlCommand("""
            select legal_entity_id, laboratory_id, customer_id, service_order_id, product_category,
                   batch_id, batch_version, batch_gate_decision, batch_gate_rule_set_version,
                   member_id, test_item_ref, test_item_version, scope_line_id, created_by, created_at
            from result.result_group
            where organization_group_id = @organization_group_id and result_group_id = @result_group_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("result_group_id", resultGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            objectScope = new ResultObjectContext(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4));
            batchId = reader.GetString(5);
            batchVersion = reader.GetInt64(6);
            gateDecision = reader.GetString(7);
            gateRuleSet = reader.GetString(8);
            memberId = reader.GetString(9);
            testItem = new ResultVersionedReference(reader.GetString(10), reader.GetInt64(11));
            scopeLineId = reader.GetString(12);
            createdBy = reader.GetString(13);
            createdAt = reader.GetFieldValue<DateTimeOffset>(14);
        }

        var observations = new List<ResultObservationResult>();
        await using (var command = new NpgsqlCommand("""
            select observation_id, group_version, kind, value, unit,
                   evidence_source, evidence_ref, evidence_version, evidence_sha256, parser_version,
                   trigger_reason, approval_ref, approval_version, recorded_by, recorded_at
            from result.result_observation where result_group_id = @id order by group_version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", resultGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                observations.Add(new ResultObservationResult(
                    reader.GetGuid(0).ToString("N"),
                    resultGroupId.ToString("N"),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    new ResultEvidence(
                        reader.GetString(5),
                        new ResultVersionedReference(reader.GetString(6), reader.GetInt64(7)),
                        reader.GetString(8),
                        reader.GetString(9)),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : new ResultVersionedReference(reader.GetString(11), reader.GetInt64(12)),
                    reader.GetString(13),
                    reader.GetFieldValue<DateTimeOffset>(14)));
            }
        }

        var derivations = new List<ResultDerivationResult>();
        await using (var command = new NpgsqlCommand("""
            select d.derivation_id, d.group_version, d.aggregation_rule_ref, d.aggregation_rule_version,
                   d.value, d.unit, d.recorded_by, d.recorded_at
            from result.result_derivation d where d.result_group_id = @id order by d.group_version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", resultGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                derivations.Add(new ResultDerivationResult(
                    reader.GetGuid(0).ToString("N"),
                    resultGroupId.ToString("N"),
                    reader.GetInt64(1),
                    new ResultVersionedReference(reader.GetString(2), reader.GetInt64(3)),
                    reader.GetString(4),
                    reader.GetString(5),
                    [],
                    reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7)));
            }
        }

        for (var index = 0; index < derivations.Count; index++)
        {
            var inputs = new List<ResultDerivationInput>();
            await using var command = new NpgsqlCommand("""
                select target_id, included, rationale from result.derivation_input
                where derivation_id = @derivation_id order by target_id
                """, connection, transaction);
            command.Parameters.AddWithValue("derivation_id", Guid.ParseExact(derivations[index].DerivationId, "N"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                inputs.Add(new ResultDerivationInput(
                    reader.GetGuid(0).ToString("N"),
                    reader.GetBoolean(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
            derivations[index] = derivations[index] with { Inputs = inputs };
        }

        var rules = new List<AdoptionRuleResult>();
        await using (var command = new NpgsqlCommand("""
            select rule_version, group_version, strategy, rule_ref, rule_ref_version, recorded_by, recorded_at
            from result.adoption_rule where result_group_id = @id order by rule_version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", resultGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rules.Add(new AdoptionRuleResult(
                    resultGroupId.ToString("N"),
                    reader.GetInt64(1),
                    reader.GetInt64(0),
                    reader.GetString(2),
                    new ResultVersionedReference(reader.GetString(3), reader.GetInt64(4)),
                    reader.GetString(5),
                    reader.GetFieldValue<DateTimeOffset>(6)));
            }
        }

        var adoptions = new List<ResultAdoptionResult>();
        await using (var command = new NpgsqlCommand("""
            select adoption_version, group_version, target_id, rule_version,
                   review_approval_ref, review_approval_version, adopted_by, adopted_at
            from result.result_adoption where result_group_id = @id order by adoption_version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", resultGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                adoptions.Add(new ResultAdoptionResult(
                    resultGroupId.ToString("N"),
                    reader.GetInt64(1),
                    reader.GetInt64(0),
                    reader.GetGuid(2).ToString("N"),
                    reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : new ResultVersionedReference(reader.GetString(4), reader.GetInt64(5)),
                    reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7)));
            }
        }

        var calculations = new List<ResultCalculationResult>();
        await using (var command = new NpgsqlCommand("""
            select calculation_id, group_version, inputs_snapshot::text, rule_snapshot::text,
                   exact_value, rounded_value, reported_value, output_unit,
                   qualification, limit_decision, executed_by, executed_at
            from result.result_calculation where result_group_id = @id order by group_version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", resultGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                calculations.Add(new ResultCalculationResult(
                    reader.GetGuid(0).ToString("N"),
                    resultGroupId.ToString("N"),
                    reader.GetInt64(1),
                    JsonSerializer.Deserialize<IReadOnlyList<ResultCalculationResolvedInput>>(reader.GetString(2), JsonOptions)
                        ?? throw new InvalidOperationException("RES.CALCULATION_INPUTS_MISSING"),
                    JsonSerializer.Deserialize<ResultCalculationRule>(reader.GetString(3), JsonOptions)
                        ?? throw new InvalidOperationException("RES.CALCULATION_RULE_MISSING"),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetFieldValue<DateTimeOffset>(11)));
            }
        }

        var assessments = new List<ResultAccreditationAssessmentResult>();
        await using (var command = new NpgsqlCommand("""
            select assessment_id, group_version, stage, target_id,
                   accreditation_ref, accreditation_version, method_ref, method_version,
                   site_id, product_or_matrix, parameter_id, range_unit, range_lower, range_upper,
                   valid_from, valid_to, authorized_actor_ids::text, decision, reason_codes::text,
                   assessed_by, assessed_at
            from result.accreditation_assessment where result_group_id = @id order by group_version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", resultGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                assessments.Add(new ResultAccreditationAssessmentResult(
                    reader.GetGuid(0).ToString("N"),
                    resultGroupId.ToString("N"),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3).ToString("N"),
                    new ResultVersionedReference(reader.GetString(4), reader.GetInt64(5)),
                    new ResultVersionedReference(reader.GetString(6), reader.GetInt64(7)),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetDecimal(12),
                    reader.GetDecimal(13),
                    reader.GetFieldValue<DateOnly>(14),
                    reader.GetFieldValue<DateOnly>(15),
                    JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(16), JsonOptions)
                        ?? throw new InvalidOperationException("RES.ACCREDITATION_ACTORS_MISSING"),
                    reader.GetString(17),
                    JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(18), JsonOptions)
                        ?? throw new InvalidOperationException("RES.ACCREDITATION_REASONS_MISSING"),
                    reader.GetString(19),
                    reader.GetFieldValue<DateTimeOffset>(20)));
            }
        }

        var version = Math.Max(1, new[]
        {
            observations.Count > 0 ? observations.Max(o => o.GroupVersion) : 1,
            derivations.Count > 0 ? derivations.Max(d => d.GroupVersion) : 1,
            rules.Count > 0 ? rules.Max(r => r.GroupVersion) : 1,
            adoptions.Count > 0 ? adoptions.Max(a => a.GroupVersion) : 1,
            calculations.Count > 0 ? calculations.Max(c => c.GroupVersion) : 1,
            assessments.Count > 0 ? assessments.Max(a => a.GroupVersion) : 1
        }.Max());
        return new ResultGroupResult(
            resultGroupId.ToString("N"), version, ResultContract.RuleSetVersion, objectScope,
            batchId, batchVersion, gateDecision, gateRuleSet, memberId, testItem, scopeLineId,
            observations, derivations, rules, adoptions, createdBy, createdAt)
        {
            Calculations = calculations,
            AccreditationAssessments = assessments
        };
    }

    public async Task<ResultObservationResult> InsertObservationAsync(
        Guid resultGroupId,
        long groupVersion,
        string organizationGroupId,
        AddResultObservationRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var observationId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into result.result_observation (
                observation_id, result_group_id, group_version, kind, value, unit,
                evidence_source, evidence_ref, evidence_version, evidence_sha256, parser_version,
                trigger_reason, approval_ref, approval_version, recorded_by, recorded_at, event_id, correlation_id
            ) values (
                @observation_id, @result_group_id, @group_version, @kind, @value, @unit,
                @evidence_source, @evidence_ref, @evidence_version, @evidence_sha256, @parser_version,
                @trigger_reason, @approval_ref, @approval_version, @recorded_by, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("observation_id", observationId);
            command.Parameters.AddWithValue("result_group_id", resultGroupId);
            command.Parameters.AddWithValue("group_version", groupVersion);
            command.Parameters.AddWithValue("kind", request.Kind);
            command.Parameters.AddWithValue("value", request.Value);
            command.Parameters.AddWithValue("unit", request.Unit);
            command.Parameters.AddWithValue("evidence_source", request.Evidence.SourceSystem);
            command.Parameters.AddWithValue("evidence_ref", request.Evidence.ExternalRef.Id);
            command.Parameters.AddWithValue("evidence_version", request.Evidence.ExternalRef.Version);
            command.Parameters.AddWithValue("evidence_sha256", request.Evidence.Sha256);
            command.Parameters.AddWithValue("parser_version", request.Evidence.ParserVersion);
            command.Parameters.AddWithValue("trigger_reason", (object?)request.TriggerReason ?? DBNull.Value);
            command.Parameters.AddWithValue("approval_ref", (object?)request.ApprovalRef?.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("approval_version", (object?)request.ApprovalRef?.Version ?? DBNull.Value);
            command.Parameters.AddWithValue("recorded_by", actorId);
            command.Parameters.AddWithValue("recorded_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "ADD_RESULT_OBSERVATION", resultGroupId.ToString("N"), organizationGroupId, actorId,
            (groupVersion - 1).ToString(), groupVersion.ToString(),
            eventId, "ResultObservationRecorded.v1", correlationId, now, cancellationToken);
        return new ResultObservationResult(
            observationId.ToString("N"), resultGroupId.ToString("N"), groupVersion,
            request.Kind, request.Value, request.Unit, request.Evidence,
            request.TriggerReason, request.ApprovalRef, actorId, now);
    }

    public async Task<ResultDerivationResult> InsertDerivationAsync(
        Guid resultGroupId,
        long groupVersion,
        string organizationGroupId,
        AddResultDerivationRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var derivationId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into result.result_derivation (
                derivation_id, result_group_id, group_version, aggregation_rule_ref, aggregation_rule_version,
                value, unit, recorded_by, recorded_at, event_id, correlation_id
            ) values (
                @derivation_id, @result_group_id, @group_version, @aggregation_rule_ref, @aggregation_rule_version,
                @value, @unit, @recorded_by, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("derivation_id", derivationId);
            command.Parameters.AddWithValue("result_group_id", resultGroupId);
            command.Parameters.AddWithValue("group_version", groupVersion);
            command.Parameters.AddWithValue("aggregation_rule_ref", request.AggregationRule.Id);
            command.Parameters.AddWithValue("aggregation_rule_version", request.AggregationRule.Version);
            command.Parameters.AddWithValue("value", request.Value);
            command.Parameters.AddWithValue("unit", request.Unit);
            command.Parameters.AddWithValue("recorded_by", actorId);
            command.Parameters.AddWithValue("recorded_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var input in request.Inputs)
        {
            await using var command = new NpgsqlCommand("""
                insert into result.derivation_input (derivation_id, target_id, included, rationale)
                values (@derivation_id, @target_id, @included, @rationale)
                """, connection, transaction);
            command.Parameters.AddWithValue("derivation_id", derivationId);
            command.Parameters.AddWithValue("target_id", Guid.ParseExact(input.TargetId, "N"));
            command.Parameters.AddWithValue("included", input.Included);
            command.Parameters.AddWithValue("rationale", (object?)input.Rationale ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "ADD_RESULT_DERIVATION", resultGroupId.ToString("N"), organizationGroupId, actorId,
            (groupVersion - 1).ToString(), groupVersion.ToString(),
            eventId, "ResultDerivationRecorded.v1", correlationId, now, cancellationToken);
        return new ResultDerivationResult(
            derivationId.ToString("N"), resultGroupId.ToString("N"), groupVersion,
            request.AggregationRule, request.Value, request.Unit, request.Inputs, actorId, now);
    }

    public async Task<ResultCalculationResult> InsertCalculationAsync(
        Guid resultGroupId,
        long groupVersion,
        string organizationGroupId,
        ResultCalculationExecution execution,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var calculationId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into result.result_calculation (
                calculation_id, result_group_id, group_version, inputs_snapshot, rule_snapshot,
                exact_value, rounded_value, reported_value, output_unit,
                qualification, limit_decision, executed_by, executed_at, event_id, correlation_id
            ) values (
                @calculation_id, @result_group_id, @group_version,
                cast(@inputs_snapshot as jsonb), cast(@rule_snapshot as jsonb),
                @exact_value, @rounded_value, @reported_value, @output_unit,
                @qualification, @limit_decision, @executed_by, @executed_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("calculation_id", calculationId);
            command.Parameters.AddWithValue("result_group_id", resultGroupId);
            command.Parameters.AddWithValue("group_version", groupVersion);
            command.Parameters.AddWithValue("inputs_snapshot", JsonSerializer.Serialize(execution.Inputs, JsonOptions));
            command.Parameters.AddWithValue("rule_snapshot", JsonSerializer.Serialize(execution.Rule, JsonOptions));
            command.Parameters.AddWithValue("exact_value", execution.ExactValue);
            command.Parameters.AddWithValue("rounded_value", execution.RoundedValue);
            command.Parameters.AddWithValue("reported_value", execution.ReportedValue);
            command.Parameters.AddWithValue("output_unit", execution.Rule.OutputUnit);
            command.Parameters.AddWithValue("qualification", execution.Qualification);
            command.Parameters.AddWithValue("limit_decision", execution.LimitDecision);
            command.Parameters.AddWithValue("executed_by", actorId);
            command.Parameters.AddWithValue("executed_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "EXECUTE_RESULT_CALCULATION", resultGroupId.ToString("N"), organizationGroupId, actorId,
            (groupVersion - 1).ToString(), groupVersion.ToString(),
            eventId, "ResultCalculationExecuted.v1", correlationId, now, cancellationToken);
        return new ResultCalculationResult(
            calculationId.ToString("N"),
            resultGroupId.ToString("N"),
            groupVersion,
            execution.Inputs,
            execution.Rule,
            execution.ExactValue,
            execution.RoundedValue,
            execution.ReportedValue,
            execution.Rule.OutputUnit,
            execution.Qualification,
            execution.LimitDecision,
            actorId,
            now);
    }

    public async Task<AdoptionRuleResult> InsertAdoptionRuleAsync(
        Guid resultGroupId,
        long groupVersion,
        long ruleVersion,
        string organizationGroupId,
        RecordAdoptionRuleRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into result.adoption_rule (
                result_group_id, rule_version, group_version, strategy, rule_ref, rule_ref_version,
                recorded_by, recorded_at, event_id, correlation_id
            ) values (
                @result_group_id, @rule_version, @group_version, @strategy, @rule_ref, @rule_ref_version,
                @recorded_by, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("result_group_id", resultGroupId);
            command.Parameters.AddWithValue("rule_version", ruleVersion);
            command.Parameters.AddWithValue("group_version", groupVersion);
            command.Parameters.AddWithValue("strategy", request.Strategy);
            command.Parameters.AddWithValue("rule_ref", request.RuleRef.Id);
            command.Parameters.AddWithValue("rule_ref_version", request.RuleRef.Version);
            command.Parameters.AddWithValue("recorded_by", actorId);
            command.Parameters.AddWithValue("recorded_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RECORD_ADOPTION_RULE", resultGroupId.ToString("N"), organizationGroupId, actorId,
            (groupVersion - 1).ToString(), groupVersion.ToString(),
            eventId, "ResultAdoptionRuleRecorded.v1", correlationId, now, cancellationToken);
        return new AdoptionRuleResult(
            resultGroupId.ToString("N"), groupVersion, ruleVersion,
            request.Strategy, request.RuleRef, actorId, now);
    }

    public async Task<ResultAdoptionResult> InsertAdoptionAsync(
        Guid resultGroupId,
        long groupVersion,
        long adoptionVersion,
        long ruleVersion,
        string organizationGroupId,
        AdoptResultRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into result.result_adoption (
                result_group_id, adoption_version, group_version, target_id, rule_version,
                review_approval_ref, review_approval_version, adopted_by, adopted_at, event_id, correlation_id
            ) values (
                @result_group_id, @adoption_version, @group_version, @target_id, @rule_version,
                @review_approval_ref, @review_approval_version, @adopted_by, @adopted_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("result_group_id", resultGroupId);
            command.Parameters.AddWithValue("adoption_version", adoptionVersion);
            command.Parameters.AddWithValue("group_version", groupVersion);
            command.Parameters.AddWithValue("target_id", Guid.ParseExact(request.TargetId, "N"));
            command.Parameters.AddWithValue("rule_version", ruleVersion);
            command.Parameters.AddWithValue("review_approval_ref", (object?)request.ReviewApprovalRef?.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("review_approval_version", (object?)request.ReviewApprovalRef?.Version ?? DBNull.Value);
            command.Parameters.AddWithValue("adopted_by", actorId);
            command.Parameters.AddWithValue("adopted_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "ADOPT_RESULT", resultGroupId.ToString("N"), organizationGroupId, actorId,
            (groupVersion - 1).ToString(), groupVersion.ToString(),
            eventId, "ResultAdopted.v1", correlationId, now, cancellationToken);
        return new ResultAdoptionResult(
            resultGroupId.ToString("N"), groupVersion, adoptionVersion,
            request.TargetId, ruleVersion, request.ReviewApprovalRef, actorId, now);
    }

    public async Task<ResultAccreditationAssessmentResult> InsertAccreditationAssessmentAsync(
        Guid resultGroupId,
        long groupVersion,
        string organizationGroupId,
        ResultAccreditationEvaluation evaluation,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var request = evaluation.Request;
        var assessmentId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into result.accreditation_assessment (
                assessment_id, result_group_id, group_version, stage, target_id,
                accreditation_ref, accreditation_version, method_ref, method_version,
                site_id, product_or_matrix, parameter_id, range_unit, range_lower, range_upper,
                valid_from, valid_to, authorized_actor_ids, decision, reason_codes,
                assessed_by, assessed_at, event_id, correlation_id
            ) values (
                @assessment_id, @result_group_id, @group_version, @stage, @target_id,
                @accreditation_ref, @accreditation_version, @method_ref, @method_version,
                @site_id, @product_or_matrix, @parameter_id, @range_unit, @range_lower, @range_upper,
                @valid_from, @valid_to, cast(@authorized_actor_ids as jsonb), @decision, cast(@reason_codes as jsonb),
                @assessed_by, @assessed_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("assessment_id", assessmentId);
            command.Parameters.AddWithValue("result_group_id", resultGroupId);
            command.Parameters.AddWithValue("group_version", groupVersion);
            command.Parameters.AddWithValue("stage", request.Stage);
            command.Parameters.AddWithValue(
                "target_id",
                request.TargetId is null ? DBNull.Value : Guid.ParseExact(request.TargetId, "N"));
            command.Parameters.AddWithValue("accreditation_ref", request.Accreditation.Id);
            command.Parameters.AddWithValue("accreditation_version", request.Accreditation.Version);
            command.Parameters.AddWithValue("method_ref", request.Method.Id);
            command.Parameters.AddWithValue("method_version", request.Method.Version);
            command.Parameters.AddWithValue("site_id", request.SiteId);
            command.Parameters.AddWithValue("product_or_matrix", request.ProductOrMatrix);
            command.Parameters.AddWithValue("parameter_id", request.Parameter);
            command.Parameters.AddWithValue("range_unit", request.RangeUnit);
            command.Parameters.AddWithValue("range_lower", request.RangeLower);
            command.Parameters.AddWithValue("range_upper", request.RangeUpper);
            command.Parameters.AddWithValue("valid_from", request.ValidFrom);
            command.Parameters.AddWithValue("valid_to", request.ValidTo);
            command.Parameters.AddWithValue("authorized_actor_ids", JsonSerializer.Serialize(request.AuthorizedActorIds, JsonOptions));
            command.Parameters.AddWithValue("decision", evaluation.Decision);
            command.Parameters.AddWithValue("reason_codes", JsonSerializer.Serialize(evaluation.ReasonCodes, JsonOptions));
            command.Parameters.AddWithValue("assessed_by", actorId);
            command.Parameters.AddWithValue("assessed_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RECORD_RESULT_ACCREDITATION", resultGroupId.ToString("N"), organizationGroupId, actorId,
            (groupVersion - 1).ToString(), groupVersion.ToString(),
            eventId, "ResultAccreditationAssessed.v1", correlationId, now, cancellationToken);
        return new ResultAccreditationAssessmentResult(
            assessmentId.ToString("N"),
            resultGroupId.ToString("N"),
            groupVersion,
            request.Stage,
            request.TargetId,
            request.Accreditation,
            request.Method,
            request.SiteId,
            request.ProductOrMatrix,
            request.Parameter,
            request.RangeUnit,
            request.RangeLower,
            request.RangeUpper,
            request.ValidFrom,
            request.ValidTo,
            request.AuthorizedActorIds,
            evaluation.Decision,
            evaluation.ReasonCodes,
            actorId,
            now);
    }

    public Task WriteReadAuditAsync(
        string resultGroupId,
        long version,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, resultGroupId, action, ResultContract.RuleSetVersion,
            version.ToString(), version.ToString(), correlationId, now), cancellationToken);

    private async Task WritePlatformEvidenceAsync(
        string action,
        string groupKey,
        string organizationGroupId,
        string actorId,
        string? beforeVersion,
        string afterVersion,
        string eventId,
        string messageType,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, groupKey, action, ResultContract.RuleSetVersion,
            beforeVersion, afterVersion, correlationId, now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("RES.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class ResultAttemptAuditWriter(ResultDataSource dataSource)
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
            insert into result.audit_attempt (
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
