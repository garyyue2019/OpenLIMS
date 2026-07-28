using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class ToyTestUnitPlanStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquirePlanLockAsync(Guid productId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
        command.Parameters.AddWithValue("key", $"openlims.toy.test-unit-plan.{productId:N}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> CurrentPlanVersionAsync(
        string organizationGroupId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select coalesce(max(p.plan_version), 0)
            from toy.test_unit_plan p
            join toy.product pr on pr.product_id = p.product_id
            where p.product_id = @product_id
              and pr.organization_group_id = @organization_group_id
            """, connection, transaction);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task InsertPlanAsync(
        Guid productId,
        long planVersion,
        CreateToyTestUnitPlanRequest request,
        ToySampleDemandCalculation calculation,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var planRowId = Guid.NewGuid();
        var planId = await ExistingPlanIdAsync(productId, cancellationToken) ?? Guid.NewGuid();
        var requirementId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");

        await using (var command = new NpgsqlCommand("""
            insert into toy.test_unit_plan (
                plan_row_id, plan_id, product_id, product_version, plan_version,
                age_grade_decision_version, accessibility_assessment_version,
                scope_matrix_id, scope_matrix_version, rule_set_version, input_hash,
                created_by, created_at, event_id, correlation_id
            ) values (
                @plan_row_id, @plan_id, @product_id, @product_version, @plan_version,
                @age_grade_decision_version, @accessibility_assessment_version,
                @scope_matrix_id, @scope_matrix_version, @rule_set_version, @input_hash,
                @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("plan_row_id", planRowId);
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("product_version", request.ProductVersion);
            command.Parameters.AddWithValue("plan_version", planVersion);
            command.Parameters.AddWithValue("age_grade_decision_version", request.AgeGradeDecisionVersion);
            command.Parameters.AddWithValue("accessibility_assessment_version", request.AccessibilityAssessmentVersion);
            command.Parameters.AddWithValue("scope_matrix_id", request.ScopeMatrixId);
            command.Parameters.AddWithValue("scope_matrix_version", request.ScopeMatrixVersion);
            command.Parameters.AddWithValue("rule_set_version", request.RuleSetVersion);
            command.Parameters.AddWithValue("input_hash", calculation.InputHash);
            command.Parameters.AddWithValue("created_by", actorId);
            command.Parameters.AddWithValue("created_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertReferencesAsync(
            "toy.test_unit_plan_scope_line", planRowId, request.ScopeLineRefs, cancellationToken);
        await InsertReferencesAsync(
            "toy.test_unit_plan_sample_rule", planRowId, request.SampleRuleRefs, cancellationToken);

        foreach (var unit in request.TestUnits)
        {
            var testUnitRowId = Guid.NewGuid();
            var testUnitId = Guid.Parse(unit.TestUnitId);
            await using (var command = new NpgsqlCommand("""
                insert into toy.test_unit (
                    test_unit_row_id, plan_row_id, test_unit_id,
                    physical_object_ref, physical_object_version, parallel_number
                ) values (
                    @test_unit_row_id, @plan_row_id, @test_unit_id,
                    @physical_object_ref, @physical_object_version, @parallel_number
                )
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("test_unit_row_id", testUnitRowId);
                command.Parameters.AddWithValue("plan_row_id", planRowId);
                command.Parameters.AddWithValue("test_unit_id", testUnitId);
                command.Parameters.AddWithValue("physical_object_ref", unit.PhysicalObjectRef.Id);
                command.Parameters.AddWithValue("physical_object_version", unit.PhysicalObjectRef.Version);
                command.Parameters.AddWithValue("parallel_number", unit.ParallelNumber);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var hazard in unit.HazardDomainRefs)
            {
                await using var command = new NpgsqlCommand("""
                    insert into toy.test_unit_hazard_domain (
                        row_id, test_unit_row_id, reference_id, reference_version
                    ) values (@row_id, @test_unit_row_id, @reference_id, @reference_version)
                    """, connection, transaction);
                command.Parameters.AddWithValue("row_id", Guid.NewGuid());
                command.Parameters.AddWithValue("test_unit_row_id", testUnitRowId);
                command.Parameters.AddWithValue("reference_id", hazard.Id);
                command.Parameters.AddWithValue("reference_version", hazard.Version);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var step in unit.SequenceSteps)
            {
                await using (var command = new NpgsqlCommand("""
                    insert into toy.test_unit_sequence_step (
                        step_row_id, test_unit_row_id, step_id, sequence_order,
                        task_ref, task_version, destructive, exclusive_destructive_group_id,
                        share_rule_ref, share_rule_version
                    ) values (
                        @step_row_id, @test_unit_row_id, @step_id, @sequence_order,
                        @task_ref, @task_version, @destructive, @exclusive_destructive_group_id,
                        @share_rule_ref, @share_rule_version
                    )
                    """, connection, transaction))
                {
                    command.Parameters.AddWithValue("step_row_id", Guid.NewGuid());
                    command.Parameters.AddWithValue("test_unit_row_id", testUnitRowId);
                    command.Parameters.AddWithValue("step_id", step.StepId);
                    command.Parameters.AddWithValue("sequence_order", step.SequenceOrder);
                    command.Parameters.AddWithValue("task_ref", step.TaskRef.Id);
                    command.Parameters.AddWithValue("task_version", step.TaskRef.Version);
                    command.Parameters.AddWithValue("destructive", step.Destructive);
                    command.Parameters.AddWithValue(
                        "exclusive_destructive_group_id",
                        (object?)step.ExclusiveDestructiveGroupId ?? DBNull.Value);
                    command.Parameters.AddWithValue(
                        "share_rule_ref",
                        (object?)step.ShareRuleRef?.Id ?? DBNull.Value);
                    command.Parameters.AddWithValue(
                        "share_rule_version",
                        (object?)step.ShareRuleRef?.Version ?? DBNull.Value);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

            }
        }

        await using (var requirement = new NpgsqlCommand("""
            insert into toy.sample_requirement (
                requirement_id, plan_row_id, requirement_version,
                input_hash, rule_set_version, created_at
            ) values (
                @requirement_id, @plan_row_id, @requirement_version,
                @input_hash, @rule_set_version, @created_at
            )
            """, connection, transaction))
        {
            requirement.Parameters.AddWithValue("requirement_id", requirementId);
            requirement.Parameters.AddWithValue("plan_row_id", planRowId);
            requirement.Parameters.AddWithValue("requirement_version", planVersion);
            requirement.Parameters.AddWithValue("input_hash", calculation.InputHash);
            requirement.Parameters.AddWithValue("rule_set_version", calculation.RuleSetVersion);
            requirement.Parameters.AddWithValue("created_at", now);
            await requirement.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var component in calculation.Components)
        {
            await using var command = new NpgsqlCommand("""
                insert into toy.sample_demand_component (
                    component_row_id, requirement_id, component_id, kind,
                    hazard_domain_ref, hazard_domain_version, test_unit_id,
                    amount, dimension, unit, source_rule_ref, source_rule_version
                ) values (
                    @component_row_id, @requirement_id, @component_id, @kind,
                    @hazard_domain_ref, @hazard_domain_version, @test_unit_id,
                    @amount, @dimension, @unit, @source_rule_ref, @source_rule_version
                )
                """, connection, transaction);
            command.Parameters.AddWithValue("component_row_id", Guid.NewGuid());
            command.Parameters.AddWithValue("requirement_id", requirementId);
            command.Parameters.AddWithValue("component_id", component.ComponentId);
            command.Parameters.AddWithValue("kind", component.Kind);
            command.Parameters.AddWithValue("hazard_domain_ref", (object?)component.HazardDomainRef?.Id ?? DBNull.Value);
            command.Parameters.AddWithValue("hazard_domain_version", (object?)component.HazardDomainRef?.Version ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "test_unit_id",
                component.TestUnitId is null ? DBNull.Value : Guid.Parse(component.TestUnitId));
            command.Parameters.AddWithValue("amount", component.Amount);
            command.Parameters.AddWithValue("dimension", component.Dimension);
            command.Parameters.AddWithValue("unit", component.Unit);
            command.Parameters.AddWithValue("source_rule_ref", component.SourceRuleRef.Id);
            command.Parameters.AddWithValue("source_rule_version", component.SourceRuleRef.Version);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEvidenceAsync(
            productId.ToString("N"), organizationGroupId, actorId,
            "CREATE_TEST_UNIT_PLAN", eventId, "Toy.TestUnitPlanCreated.v1",
            (planVersion - 1).ToString(), planVersion.ToString(),
            correlationId, now, cancellationToken);
    }

    public async Task InsertApprovalAsync(
        ToyTestUnitPlanResult plan,
        ApproveToySampleRequirementRequest request,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into toy.technical_approval (
                approval_id, requirement_id, approved_by, approved_at,
                approval_comment, input_hash, rule_set_version, event_id, correlation_id
            ) values (
                @approval_id, @requirement_id, @approved_by, @approved_at,
                @approval_comment, @input_hash, @rule_set_version, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("approval_id", Guid.NewGuid());
        command.Parameters.AddWithValue("requirement_id", Guid.Parse(plan.Requirement.RequirementId));
        command.Parameters.AddWithValue("approved_by", actorId);
        command.Parameters.AddWithValue("approved_at", now);
        command.Parameters.AddWithValue("approval_comment", request.ApprovalComment);
        command.Parameters.AddWithValue("input_hash", request.InputHash);
        command.Parameters.AddWithValue("rule_set_version", request.RuleSetVersion);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WriteEvidenceAsync(
            plan.ProductId, organizationGroupId, actorId,
            "APPROVE_SAMPLE_REQUIREMENT", eventId, "Toy.SampleRequirementApproved.v1",
            plan.PlanVersion.ToString(), plan.PlanVersion.ToString(),
            correlationId, now, cancellationToken);
    }

    public async Task InsertDownstreamDecisionAsync(
        ToyTestUnitPlanResult plan,
        IReadOnlyList<ToyQuantityDecisionEntry> quantityDecisions,
        IReadOnlyList<ToyAllocationDecisionEntry> allocationDecisions,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var requestId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into toy.downstream_request (
                request_id, requirement_id, requested_by, requested_at, event_id, correlation_id
            ) values (
                @request_id, @requirement_id, @requested_by, @requested_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("request_id", requestId);
            command.Parameters.AddWithValue("requirement_id", Guid.Parse(plan.Requirement.RequirementId));
            command.Parameters.AddWithValue("requested_by", actorId);
            command.Parameters.AddWithValue("requested_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var decision in quantityDecisions)
        {
            await using var command = new NpgsqlCommand("""
                insert into toy.quantity_decision (
                    decision_id, request_id, quantity_account_id,
                    expected_account_version, current_account_version,
                    requested_amount, available_amount, dimension, unit, reservation_ref,
                    decision, reason_codes, rule_set_version
                ) values (
                    @decision_id, @request_id, @quantity_account_id,
                    @expected_account_version, @current_account_version,
                    @requested_amount, @available_amount, @dimension, @unit, @reservation_ref,
                    @decision, @reason_codes, @rule_set_version
                )
                """, connection, transaction);
            command.Parameters.AddWithValue("decision_id", Guid.NewGuid());
            command.Parameters.AddWithValue("request_id", requestId);
            command.Parameters.AddWithValue("quantity_account_id", decision.QuantityAccountId);
            command.Parameters.AddWithValue("expected_account_version", decision.ExpectedAccountVersion);
            command.Parameters.AddWithValue("current_account_version", decision.CurrentAccountVersion);
            command.Parameters.AddWithValue("requested_amount", decision.RequestedAmount);
            command.Parameters.AddWithValue("available_amount", decision.AvailableAmount);
            command.Parameters.AddWithValue("dimension", decision.Dimension);
            command.Parameters.AddWithValue("unit", decision.Unit);
            command.Parameters.AddWithValue("reservation_ref", decision.ReservationRef);
            command.Parameters.AddWithValue("decision", decision.Decision);
            command.Parameters.AddWithValue("reason_codes", decision.ReasonCodes.ToArray());
            command.Parameters.AddWithValue("rule_set_version", decision.RuleSetVersion);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var decision in allocationDecisions)
        {
            // Usage becomes permanent when an approved downstream allocation
            // is actually bound, not merely when a draft plan mentions the
            // TestUnit. The row is never removed on a general allocation
            // release, and its unique key rejects reuse in every later plan.
            await using (var usage = new NpgsqlCommand("""
                insert into toy.destructive_test_unit_usage (
                    usage_id, product_id, plan_row_id, test_unit_id,
                    physical_object_ref, physical_object_version,
                    exclusive_destructive_group_id, recorded_at
                )
                select @usage_id, p.product_id, p.plan_row_id, u.test_unit_id,
                       u.physical_object_ref, u.physical_object_version,
                       s.exclusive_destructive_group_id, @recorded_at
                from toy.test_unit_plan p
                join toy.test_unit u on u.plan_row_id = p.plan_row_id
                join toy.test_unit_sequence_step s on s.test_unit_row_id = u.test_unit_row_id
                where p.product_id = @product_id
                  and p.plan_version = @plan_version
                  and u.test_unit_id = @test_unit_id
                  and s.step_id = @sequence_step_id
                  and s.exclusive_destructive_group_id is not null
                """, connection, transaction))
            {
                usage.Parameters.AddWithValue("usage_id", Guid.NewGuid());
                usage.Parameters.AddWithValue("product_id", Guid.Parse(plan.ProductId));
                usage.Parameters.AddWithValue("plan_version", plan.PlanVersion);
                usage.Parameters.AddWithValue("test_unit_id", Guid.Parse(decision.TestUnitId));
                usage.Parameters.AddWithValue("sequence_step_id", decision.SequenceStepId);
                usage.Parameters.AddWithValue("recorded_at", now);
                await usage.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new NpgsqlCommand("""
                insert into toy.allocation_decision (
                    decision_id, request_id, allocation_id,
                    expected_subject_allocation_version, current_subject_allocation_version,
                    allocation_state, test_unit_id, sequence_step_id,
                    decision, reason_codes, rule_set_version
                ) values (
                    @decision_id, @request_id, @allocation_id,
                    @expected_subject_allocation_version, @current_subject_allocation_version,
                    @allocation_state, @test_unit_id, @sequence_step_id,
                    @decision, @reason_codes, @rule_set_version
                )
                """, connection, transaction);
            command.Parameters.AddWithValue("decision_id", Guid.NewGuid());
            command.Parameters.AddWithValue("request_id", requestId);
            command.Parameters.AddWithValue("allocation_id", decision.AllocationId);
            command.Parameters.AddWithValue(
                "expected_subject_allocation_version", decision.ExpectedSubjectAllocationVersion);
            command.Parameters.AddWithValue(
                "current_subject_allocation_version", decision.CurrentSubjectAllocationVersion);
            command.Parameters.AddWithValue("allocation_state", decision.State);
            command.Parameters.AddWithValue("test_unit_id", Guid.Parse(decision.TestUnitId));
            command.Parameters.AddWithValue("sequence_step_id", decision.SequenceStepId);
            command.Parameters.AddWithValue("decision", decision.Decision);
            command.Parameters.AddWithValue("reason_codes", decision.ReasonCodes.ToArray());
            command.Parameters.AddWithValue("rule_set_version", decision.RuleSetVersion);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEvidenceAsync(
            plan.ProductId, organizationGroupId, actorId,
            "REQUEST_TOY_ALLOCATION", eventId, "Toy.AllocationRequested.v1",
            plan.PlanVersion.ToString(), plan.PlanVersion.ToString(),
            correlationId, now, cancellationToken);
    }

    public async Task<ToyTestUnitPlanResult?> LoadAsync(
        string organizationGroupId,
        Guid productId,
        long planVersion,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        PlanRow? row = null;
        await using (var command = new NpgsqlCommand("""
            select p.plan_row_id, p.plan_id, p.product_version, p.plan_version,
                   p.age_grade_decision_version, p.accessibility_assessment_version,
                   p.scope_matrix_id, p.scope_matrix_version, p.rule_set_version,
                   p.input_hash, p.created_by, p.created_at,
                   pr.legal_entity_id, pr.laboratory_id
            from toy.test_unit_plan p
            join toy.product pr on pr.product_id = p.product_id
            where p.product_id = @product_id
              and p.plan_version = @plan_version
              and pr.organization_group_id = @organization_group_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("plan_version", planVersion);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            row = new PlanRow(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetInt64(2), reader.GetInt64(3),
                reader.GetInt64(4), reader.GetInt64(5), reader.GetString(6), reader.GetInt64(7),
                reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                new ToyObjectContext(reader.GetString(12), reader.GetString(13)));
        }

        var scopeLines = await LoadReferencesAsync("toy.test_unit_plan_scope_line", row.PlanRowId, cancellationToken);
        var sampleRules = await LoadReferencesAsync("toy.test_unit_plan_sample_rule", row.PlanRowId, cancellationToken);
        var testUnits = await LoadTestUnitsAsync(row.PlanRowId, cancellationToken);
        var requirement = await LoadRequirementAsync(row.PlanRowId, cancellationToken);
        var approval = await LoadApprovalAsync(requirement.RequirementId, requirement.RequirementVersion, cancellationToken);
        var downstream = await LoadDownstreamAsync(requirement.RequirementId, cancellationToken);
        var superseded = approval is not null && await HasLaterApprovalAsync(productId, row.PlanVersion, cancellationToken);
        var planState = approval is null
            ? ToyTestUnitPlanStates.Draft
            : superseded
                ? ToyTestUnitPlanStates.Superseded
                : ToyTestUnitPlanStates.Approved;
        var requirementDecision = approval is null
            ? ToySampleRequirementDecisions.PendingTechnicalApproval
            : superseded
                ? ToySampleRequirementDecisions.Superseded
                : ToySampleRequirementDecisions.Approved;

        return new ToyTestUnitPlanResult(
            row.PlanId.ToString("N"),
            productId.ToString("N"),
            row.ProductVersion,
            row.PlanVersion,
            row.AgeGradeDecisionVersion,
            row.AccessibilityAssessmentVersion,
            row.ScopeMatrixId,
            row.ScopeMatrixVersion,
            scopeLines,
            sampleRules,
            row.RuleSetVersion,
            planState,
            row.InputHash,
            row.ObjectScope,
            testUnits,
            requirement with { Decision = requirementDecision },
            approval,
            downstream,
            row.CreatedBy,
            row.CreatedAt);
    }

    public Task WriteReadAuditAsync(
        ToyTestUnitPlanResult plan,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            plan.ProductId,
            action,
            ToyTestUnitPlanContract.RuleSetVersion,
            plan.PlanVersion.ToString(),
            plan.PlanVersion.ToString(),
            correlationId,
            now), cancellationToken);

    private async Task<Guid?> ExistingPlanIdAsync(Guid productId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select plan_id from toy.test_unit_plan
            where product_id = @product_id order by plan_version limit 1
            """, connection, transaction);
        command.Parameters.AddWithValue("product_id", productId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    private async Task InsertReferencesAsync(
        string table,
        Guid planRowId,
        IReadOnlyList<ToyVersionedReference> references,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        foreach (var reference in references)
        {
            await using var command = new NpgsqlCommand($"""
                insert into {table} (row_id, plan_row_id, reference_id, reference_version)
                values (@row_id, @plan_row_id, @reference_id, @reference_version)
                """, connection, transaction);
            command.Parameters.AddWithValue("row_id", Guid.NewGuid());
            command.Parameters.AddWithValue("plan_row_id", planRowId);
            command.Parameters.AddWithValue("reference_id", reference.Id);
            command.Parameters.AddWithValue("reference_version", reference.Version);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ToyVersionedReference>> LoadReferencesAsync(
        string table,
        Guid planRowId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var result = new List<ToyVersionedReference>();
        await using var command = new NpgsqlCommand($"""
            select reference_id, reference_version from {table}
            where plan_row_id = @plan_row_id order by reference_id, reference_version
            """, connection, transaction);
        command.Parameters.AddWithValue("plan_row_id", planRowId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ToyVersionedReference(reader.GetString(0), reader.GetInt64(1)));
        return result;
    }

    private async Task<IReadOnlyList<ToyTestUnitEntry>> LoadTestUnitsAsync(
        Guid planRowId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var rows = new List<(Guid RowId, Guid TestUnitId, ToyVersionedReference Physical, int Parallel)>();
        await using (var command = new NpgsqlCommand("""
            select test_unit_row_id, test_unit_id, physical_object_ref,
                   physical_object_version, parallel_number
            from toy.test_unit where plan_row_id = @plan_row_id
            order by parallel_number, test_unit_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("plan_row_id", planRowId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    new ToyVersionedReference(reader.GetString(2), reader.GetInt64(3)),
                    reader.GetInt32(4)));
            }
        }

        var result = new List<ToyTestUnitEntry>(rows.Count);
        foreach (var row in rows)
        {
            var hazards = new List<ToyVersionedReference>();
            await using (var command = new NpgsqlCommand("""
                select reference_id, reference_version
                from toy.test_unit_hazard_domain where test_unit_row_id = @row_id
                order by reference_id, reference_version
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("row_id", row.RowId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    hazards.Add(new ToyVersionedReference(reader.GetString(0), reader.GetInt64(1)));
            }

            var steps = new List<ToySequenceStepEntry>();
            await using (var command = new NpgsqlCommand("""
                select step_id, sequence_order, task_ref, task_version, destructive,
                       exclusive_destructive_group_id, share_rule_ref, share_rule_version
                from toy.test_unit_sequence_step where test_unit_row_id = @row_id
                order by sequence_order
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("row_id", row.RowId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    steps.Add(new ToySequenceStepEntry(
                        reader.GetString(0),
                        reader.GetInt32(1),
                        new ToyVersionedReference(reader.GetString(2), reader.GetInt64(3)),
                        reader.GetBoolean(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6)
                            ? null
                            : new ToyVersionedReference(reader.GetString(6), reader.GetInt64(7))));
                }
            }

            result.Add(new ToyTestUnitEntry(
                row.TestUnitId.ToString("N"), row.Physical, hazards, row.Parallel, steps));
        }

        return result;
    }

    private async Task<ToySampleRequirementEntry> LoadRequirementAsync(
        Guid planRowId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        Guid requirementId;
        long version;
        string inputHash;
        string ruleSetVersion;
        await using (var command = new NpgsqlCommand("""
            select requirement_id, requirement_version, input_hash, rule_set_version
            from toy.sample_requirement where plan_row_id = @plan_row_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("plan_row_id", planRowId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("TOY.SAMPLE_REQUIREMENT_MISSING");
            requirementId = reader.GetGuid(0);
            version = reader.GetInt64(1);
            inputHash = reader.GetString(2);
            ruleSetVersion = reader.GetString(3);
        }

        var components = new List<ToySampleDemandComponent>();
        await using (var command = new NpgsqlCommand("""
            select component_id, kind, hazard_domain_ref, hazard_domain_version,
                   test_unit_id, amount, dimension, unit, source_rule_ref, source_rule_version
            from toy.sample_demand_component where requirement_id = @requirement_id
            order by kind, component_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("requirement_id", requirementId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                components.Add(new ToySampleDemandComponent(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2)
                        ? null
                        : new ToyVersionedReference(reader.GetString(2), reader.GetInt64(3)),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4).ToString("N"),
                    reader.GetDecimal(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    new ToyVersionedReference(reader.GetString(8), reader.GetInt64(9))));
            }
        }

        var totals = components
            .GroupBy(component => (component.Dimension, component.Unit))
            .Select(group => new ToySampleDemandTotal(
                group.Key.Dimension, group.Key.Unit, group.Sum(item => item.Amount)))
            .OrderBy(total => total.Dimension, StringComparer.Ordinal)
            .ThenBy(total => total.Unit, StringComparer.Ordinal)
            .ToArray();
        return new ToySampleRequirementEntry(
            requirementId.ToString("N"),
            version,
            components,
            totals,
            ToySampleRequirementDecisions.PendingTechnicalApproval,
            [],
            inputHash,
            ruleSetVersion);
    }

    private async Task<ToyTechnicalApprovalEntry?> LoadApprovalAsync(
        string requirementId,
        long requirementVersion,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select approved_by, approved_at, approval_comment, input_hash, rule_set_version
            from toy.technical_approval where requirement_id = @requirement_id
            """, connection, transaction);
        command.Parameters.AddWithValue("requirement_id", Guid.Parse(requirementId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ToyTechnicalApprovalEntry(
                requirementId,
                requirementVersion,
                reader.GetString(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4))
            : null;
    }

    private async Task<IReadOnlyList<ToyDownstreamDecisionEntry>> LoadDownstreamAsync(
        string requirementId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var rows = new List<(Guid RequestId, string Actor, DateTimeOffset At)>();
        await using (var command = new NpgsqlCommand("""
            select request_id, requested_by, requested_at
            from toy.downstream_request where requirement_id = @requirement_id
            order by requested_at, request_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("requirement_id", Guid.Parse(requirementId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));
        }

        var result = new List<ToyDownstreamDecisionEntry>(rows.Count);
        foreach (var row in rows)
        {
            var quantity = new List<ToyQuantityDecisionEntry>();
            await using (var command = new NpgsqlCommand("""
                select quantity_account_id, expected_account_version, current_account_version,
                       requested_amount, available_amount, dimension, unit, reservation_ref,
                       decision, reason_codes, rule_set_version
                from toy.quantity_decision where request_id = @request_id
                order by quantity_account_id
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("request_id", row.RequestId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    quantity.Add(new ToyQuantityDecisionEntry(
                        reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                        reader.GetDecimal(3), reader.GetDecimal(4), reader.GetString(5),
                        reader.GetString(6), reader.GetString(7), reader.GetString(8),
                        reader.GetFieldValue<string[]>(9), reader.GetString(10)));
                }
            }

            var allocation = new List<ToyAllocationDecisionEntry>();
            await using (var command = new NpgsqlCommand("""
                select allocation_id, expected_subject_allocation_version,
                       current_subject_allocation_version, allocation_state,
                       test_unit_id, sequence_step_id, decision, reason_codes, rule_set_version
                from toy.allocation_decision where request_id = @request_id
                order by allocation_id
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("request_id", row.RequestId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    allocation.Add(new ToyAllocationDecisionEntry(
                        reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                        reader.GetString(3), reader.GetGuid(4).ToString("N"), reader.GetString(5),
                        reader.GetString(6), reader.GetFieldValue<string[]>(7), reader.GetString(8)));
                }
            }

            result.Add(new ToyDownstreamDecisionEntry(
                row.RequestId.ToString("N"), quantity, allocation, row.Actor, row.At));
        }

        return result;
    }

    private async Task<bool> HasLaterApprovalAsync(
        Guid productId,
        long planVersion,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select exists (
                select 1 from toy.test_unit_plan later
                join toy.sample_requirement sr on sr.plan_row_id = later.plan_row_id
                join toy.technical_approval ta on ta.requirement_id = sr.requirement_id
                where later.product_id = @product_id and later.plan_version > @plan_version
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("plan_version", planVersion);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task WriteEvidenceAsync(
        string productId,
        string organizationGroupId,
        string actorId,
        string action,
        string eventId,
        string messageType,
        string? beforeVersion,
        string afterVersion,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            productId,
            action,
            ToyTestUnitPlanContract.RuleSetVersion,
            beforeVersion,
            afterVersion,
            correlationId,
            now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("TOY.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }

    private sealed record PlanRow(
        Guid PlanRowId,
        Guid PlanId,
        long ProductVersion,
        long PlanVersion,
        long AgeGradeDecisionVersion,
        long AccessibilityAssessmentVersion,
        string ScopeMatrixId,
        long ScopeMatrixVersion,
        string RuleSetVersion,
        string InputHash,
        string CreatedBy,
        DateTimeOffset CreatedAt,
        ToyObjectContext ObjectScope);
}
