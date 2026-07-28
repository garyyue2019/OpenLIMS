using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Quantity;
using OpenLIMS.Contracts.Toy;
using OpenLIMS.Modules.Toy;
using Xunit;

namespace OpenLIMS.Toy.IntegrationTests;

[CollectionDefinition("toy-postgres", DisableParallelization = true)]
public sealed class ToyPostgresCollection;

[Collection("toy-postgres")]
[Trait("Profile", "toy")]
public sealed class ToyPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_toy_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Customer_claim_and_laboratory_determination_are_stored_apart()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();

        var declared = await service.RecordDeclarationAsync(
            productId, Declaration(0), "corr-declare", TestContext.Current.CancellationToken);
        var decided = await service.RecordDecisionAsync(
            productId, Decision(declared.Version, 36), "corr-decide", TestContext.Current.CancellationToken);

        // OPS-TOY-001: the claim says 36 months because the customer said so;
        // the determination says 36 months because the laboratory decided it,
        // and the two live in different tables.
        var declaration = Assert.Single(decided.Declarations);
        Assert.Equal(36, declaration.DeclaredMinimumAgeMonths);
        Assert.Equal("CUSTOMER_SUBMISSION", declaration.DeclarationSource);
        var decision = Assert.Single(decided.Decisions);
        Assert.Equal(ToyDecisionStates.Draft, decision.State);
        Assert.Equal("APPROVER-1", decision.ApprovedBy);
        Assert.Equal(2, decision.StandardRef.Version);
        Assert.Null(decided.EffectiveDecision);

        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.age_declaration"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.age_grade_decision"));
        // Product registration, declaration and determination: three facts.
        Assert.Equal(3, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from platform.outbox"));
    }

    [Fact]
    public async Task Determination_rejects_a_missing_rationale_standard_or_approver()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();
        var declared = await service.RecordDeclarationAsync(
            productId, Declaration(0), "corr-declare", TestContext.Current.CancellationToken);

        var failures = new List<string>();
        foreach (var broken in new[]
        {
            Decision(declared.Version, 36) with { Rationale = "  " },
            Decision(declared.Version, 36) with { StandardRef = new ToyVersionedReference(" ", 2) },
            Decision(declared.Version, 36) with { ApprovedBy = "" },
            Decision(declared.Version, 400)
        })
        {
            var attempt = await CaptureAsync(service.RecordDecisionAsync(
                productId, broken, "corr-broken", TestContext.Current.CancellationToken));
            failures.Add(attempt.Error!.ErrorCode);
        }

        Assert.All(failures, code => Assert.Equal(ToyErrorCodes.ValidationFailed, code));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from toy.age_grade_decision"));
        Assert.Equal(4, await CountAsync(connectionString, "select count(*) from toy.audit_attempt"));
    }

    /// <summary>
    /// AC-TOY-001, first half: the customer changes their mind, and the original
    /// determination survives it intact (OPS-TOY-002).
    /// </summary>
    [Fact]
    public async Task Re_determination_appends_a_version_and_leaves_the_first_one_intact()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();

        var declared = await service.RecordDeclarationAsync(
            productId, Declaration(0), "corr-declare", TestContext.Current.CancellationToken);
        var decided = await service.RecordDecisionAsync(
            productId, Decision(declared.Version, 36), "corr-decide", TestContext.Current.CancellationToken);
        var frozen = await service.FreezeDecisionAsync(
            productId, 1, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, decided.Version),
            "corr-freeze", TestContext.Current.CancellationToken);
        var firstEffective = frozen.EffectiveDecision;

        var reDeclared = await service.RecordDeclarationAsync(
            productId,
            Declaration(frozen.Version) with { DeclaredMinimumAgeMonths = 18, IntendedUse = "学步推车" },
            "corr-redeclare", TestContext.Current.CancellationToken);
        var reDecided = await service.RecordDecisionAsync(
            productId,
            Decision(reDeclared.Version, 18) with { Rationale = "改判为 18 个月及以上" },
            "corr-redecide", TestContext.Current.CancellationToken);
        var reFrozen = await service.FreezeDecisionAsync(
            productId, 2, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, reDecided.Version),
            "corr-refreeze", TestContext.Current.CancellationToken);

        Assert.Equal(36, firstEffective!.MinimumAgeMonths);
        Assert.Equal(ToyDecisionStates.Effective, firstEffective.State);

        var v1 = reFrozen.Decisions.Single(entry => entry.VersionNumber == 1);
        var v2 = reFrozen.Decisions.Single(entry => entry.VersionNumber == 2);
        Assert.Equal(ToyDecisionStates.Superseded, v1.State);
        Assert.Equal(ToyDecisionStates.Effective, v2.State);
        // V1 still answers with its own rationale, approver and age — nothing
        // about it was rewritten by the re-determination.
        Assert.Equal(36, v1.MinimumAgeMonths);
        Assert.Equal(firstEffective.DecisionId, v1.DecisionId);
        Assert.Equal(firstEffective.Rationale, v1.Rationale);
        Assert.Equal(18, v2.MinimumAgeMonths);
        Assert.Equal(2, reFrozen.EffectiveDecision!.VersionNumber);
        Assert.Equal(2, reFrozen.Declarations.Count);
        Assert.Single(reFrozen.Decisions, entry =>
            string.Equals(entry.State, ToyDecisionStates.Effective, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_frozen_determination_cannot_be_frozen_again_or_rewritten()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();
        var decided = await service.RecordDecisionAsync(
            productId, Decision(0, 36), "corr-decide", TestContext.Current.CancellationToken);
        var frozen = await service.FreezeDecisionAsync(
            productId, 1, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, decided.Version),
            "corr-freeze", TestContext.Current.CancellationToken);

        var again = await CaptureAsync(service.FreezeDecisionAsync(
            productId, 1, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, frozen.Version),
            "corr-freeze-again", TestContext.Current.CancellationToken));
        var missing = await CaptureAsync(service.FreezeDecisionAsync(
            productId, 9, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, frozen.Version),
            "corr-freeze-missing", TestContext.Current.CancellationToken));
        var rewrite = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update toy.age_grade_decision set minimum_age_months = 6;"));
        var erase = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from toy.age_grade_freeze;"));

        Assert.Equal(ToyErrorCodes.DecisionFrozen, again.Error!.ErrorCode);
        Assert.Equal(ToyErrorCodes.DecisionNotFound, missing.Error!.ErrorCode);
        Assert.Equal("55000", rewrite.SqlState);
        Assert.Equal("55000", erase.SqlState);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.age_grade_freeze"));
    }

    /// <summary>
    /// AC-TOY-001, second half: an abuse event exposes a part that was not
    /// reachable before, and that opens all three scopes (OPS-TOY-003).
    /// </summary>
    [Fact]
    public async Task A_newly_exposed_part_opens_mechanical_chemical_and_labeling_reassessment()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();

        var initial = await service.RecordAssessmentAsync(
            productId, Assessment(0, ToyAssessmentStages.Initial, ["shell", "wheels"]),
            "corr-initial", TestContext.Current.CancellationToken);
        var afterAbuse = await service.RecordAssessmentAsync(
            productId,
            Assessment(initial.Version, ToyAssessmentStages.AfterAbuse, ["shell", "wheels", "battery-compartment"])
                with
            { AbuseEventRef = "DROP-TEST-1" },
            "corr-abuse", TestContext.Current.CancellationToken);

        Assert.Empty(initial.Triggers);
        Assert.Equal(ToyAccessibilityStatuses.Settled, initial.AccessibilityStatus);
        Assert.Equal(3, afterAbuse.Triggers.Count);
        Assert.Equal(
            ["CHEMICAL", "LABELING", "MECHANICAL"],
            afterAbuse.Triggers.Select(trigger => trigger.Scope).Order(StringComparer.Ordinal));
        Assert.All(afterAbuse.Triggers, trigger =>
        {
            Assert.Equal(ToyTriggerStates.Pending, trigger.State);
            Assert.Equal(["battery-compartment"], trigger.NewlyExposedParts);
            Assert.Equal(2, trigger.AssessmentVersion);
        });
        Assert.Equal(ToyAccessibilityStatuses.ReassessmentPending, afterAbuse.AccessibilityStatus);
        // The initial assessment keeps its own part set; it is not back-filled.
        var recordedInitial = afterAbuse.Assessments.Single(entry => entry.VersionNumber == 1);
        Assert.Equal(["shell", "wheels"], recordedInitial.AccessibleParts);
        Assert.Null(recordedInitial.AbuseEventRef);
        Assert.Equal("DROP-TEST-1", afterAbuse.Assessments.Single(entry => entry.VersionNumber == 2).AbuseEventRef);
    }

    [Fact]
    public async Task An_assessment_that_exposes_nothing_new_raises_nothing()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();

        var initial = await service.RecordAssessmentAsync(
            productId, Assessment(0, ToyAssessmentStages.Initial, ["shell", "wheels"]),
            "corr-initial", TestContext.Current.CancellationToken);
        var same = await service.RecordAssessmentAsync(
            productId, Assessment(initial.Version, ToyAssessmentStages.AfterNormalUse, ["wheels", "shell"]),
            "corr-same", TestContext.Current.CancellationToken);
        // Losing access to a part is not a finding either.
        var fewer = await service.RecordAssessmentAsync(
            productId, Assessment(same.Version, ToyAssessmentStages.AfterNormalUse, ["shell"]),
            "corr-fewer", TestContext.Current.CancellationToken);

        Assert.Empty(fewer.Triggers);
        Assert.Equal(ToyAccessibilityStatuses.Settled, fewer.AccessibilityStatus);
        Assert.Equal(3, fewer.Assessments.Count);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from toy.reassessment_trigger"));
    }

    [Fact]
    public async Task Assessment_stage_rules_are_enforced_by_the_domain_and_the_database()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();

        var notInitialFirst = await CaptureAsync(service.RecordAssessmentAsync(
            productId, Assessment(0, ToyAssessmentStages.AfterNormalUse, ["shell"]),
            "corr-not-initial", TestContext.Current.CancellationToken));
        var initial = await service.RecordAssessmentAsync(
            productId, Assessment(0, ToyAssessmentStages.Initial, ["shell"]),
            "corr-initial", TestContext.Current.CancellationToken);
        var secondInitial = await CaptureAsync(service.RecordAssessmentAsync(
            productId, Assessment(initial.Version, ToyAssessmentStages.Initial, ["shell"]),
            "corr-second-initial", TestContext.Current.CancellationToken));
        var abuseWithoutEvent = await CaptureAsync(service.RecordAssessmentAsync(
            productId, Assessment(initial.Version, ToyAssessmentStages.AfterAbuse, ["shell"]),
            "corr-abuse-no-event", TestContext.Current.CancellationToken));
        var normalWithEvent = await CaptureAsync(service.RecordAssessmentAsync(
            productId,
            Assessment(initial.Version, ToyAssessmentStages.AfterNormalUse, ["shell"])
                with
            { AbuseEventRef = "DROP-TEST-1" },
            "corr-normal-with-event", TestContext.Current.CancellationToken));

        Assert.Equal(ToyErrorCodes.ValidationFailed, notInitialFirst.Error!.ErrorCode);
        Assert.Equal(ToyErrorCodes.ValidationFailed, secondInitial.Error!.ErrorCode);
        Assert.Equal(ToyErrorCodes.ValidationFailed, abuseWithoutEvent.Error!.ErrorCode);
        Assert.Equal(ToyErrorCodes.ValidationFailed, normalWithEvent.Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.accessibility_assessment"));

        // The same two rules are also CHECK constraints, so no future caller can
        // reach a bad row by another route.
        var directBadStage = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, """
            insert into toy.accessibility_assessment (
                assessment_id, product_id, version_number, stage, abuse_event_ref,
                assessed_by, assessed_at, event_id, correlation_id)
            select gen_random_uuid(), product_id, 2, 'AFTER_ABUSE', null,
                   'x', now(), 'evt-bad-stage', 'corr'
            from toy.product limit 1;
            """));
        Assert.Equal("23514", directBadStage.SqlState);
    }

    [Fact]
    public async Task Triggers_settle_once_and_the_status_port_pins_the_version()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var port = scope.ServiceProvider.GetRequiredService<IToyAgeGradeStatusPort>();
        var productId = NewProductId();

        var decided = await service.RecordDecisionAsync(
            productId, Decision(0, 36), "corr-decide", TestContext.Current.CancellationToken);
        var frozen = await service.FreezeDecisionAsync(
            productId, 1, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, decided.Version),
            "corr-freeze", TestContext.Current.CancellationToken);
        var initial = await service.RecordAssessmentAsync(
            productId, Assessment(frozen.Version, ToyAssessmentStages.Initial, ["shell"]),
            "corr-initial", TestContext.Current.CancellationToken);
        var current = await service.RecordAssessmentAsync(
            productId,
            Assessment(initial.Version, ToyAssessmentStages.AfterAbuse, ["shell", "spring"])
                with
            { AbuseEventRef = "TORQUE-TEST-1" },
            "corr-abuse", TestContext.Current.CancellationToken);

        var blocked = await port.EvaluateAsync(new ToyAgeGradeStatusRequest(
            "group-a", productId, current.Version, ToyContract.RuleSetVersion)
        {
            CorrelationId = "corr-status-blocked"
        }, TestContext.Current.CancellationToken);

        var firstTriggerId = current.Triggers[0].TriggerId;
        foreach (var trigger in current.Triggers)
        {
            current = await service.ResolveTriggerAsync(
                productId, trigger.TriggerId,
                ToyResolution(current.Version), "corr-resolve", TestContext.Current.CancellationToken);
        }

        var repeat = await CaptureAsync(service.ResolveTriggerAsync(
            productId, firstTriggerId, ToyResolution(current.Version),
            "corr-resolve-again", TestContext.Current.CancellationToken));
        var allowed = await port.EvaluateAsync(new ToyAgeGradeStatusRequest(
            "group-a", productId, current.Version, ToyContract.RuleSetVersion)
        {
            CorrelationId = "corr-status-allowed"
        }, TestContext.Current.CancellationToken);
        var stale = await port.EvaluateAsync(new ToyAgeGradeStatusRequest(
            "group-a", productId, current.Version - 1, ToyContract.RuleSetVersion)
        {
            CorrelationId = "corr-status-stale"
        }, TestContext.Current.CancellationToken);
        var unknownRuleSet = await port.EvaluateAsync(new ToyAgeGradeStatusRequest(
            "group-a", productId, current.Version, "TOY-AGE-GRADE@0.0.1")
        {
            CorrelationId = "corr-status-unknown"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(ToyAgeGradeDecisions.Blocked, blocked.Decision);
        Assert.Equal([ToyAgeGradeReasons.ReassessmentPending], blocked.ReasonCodes);
        Assert.Equal(ToyErrorCodes.ReassessmentNotPending, repeat.Error!.ErrorCode);
        Assert.Equal(ToyAccessibilityStatuses.Settled, current.AccessibilityStatus);
        Assert.Equal(ToyAgeGradeDecisions.Allowed, allowed.Decision);
        Assert.Equal(1, allowed.EffectiveDecisionVersion);
        Assert.Equal(36, allowed.MinimumAgeMonths);
        Assert.Equal(ToyAgeGradeDecisions.Unknown, stale.Decision);
        Assert.Equal([ToyAgeGradeReasons.VersionMismatch], stale.ReasonCodes);
        Assert.Equal(ToyAgeGradeDecisions.Unknown, unknownRuleSet.Decision);
        Assert.Equal([ToyAgeGradeReasons.RuleSetVersionUnknown], unknownRuleSet.ReasonCodes);
        // A second resolution row for the same trigger is refused by the
        // database too, not only by the derived state.
        Assert.Equal(3, await CountAsync(connectionString, "select count(*) from toy.reassessment_resolution"));
    }

    [Fact]
    public async Task Stale_expected_version_and_denied_authorization_both_fail_closed()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var productId = NewProductId();
        await using (var provider = BuildProvider(connectionString))
        {
            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
            var declared = await service.RecordDeclarationAsync(
                productId, Declaration(0), "corr-declare", TestContext.Current.CancellationToken);
            var stale = await CaptureAsync(service.RecordDecisionAsync(
                productId, Decision(declared.Version - 1, 36), "corr-stale",
                TestContext.Current.CancellationToken));
            // A later command may not re-point the product at another laboratory.
            var rescoped = await CaptureAsync(service.RecordDecisionAsync(
                productId,
                Decision(declared.Version, 36) with { ObjectScope = new ToyObjectContext("LEGAL-A", "LAB-B") },
                "corr-rescope", TestContext.Current.CancellationToken));

            Assert.Equal(ToyErrorCodes.ExpectedVersionConflict, stale.Error!.ErrorCode);
            Assert.Equal(ToyErrorCodes.ValidationFailed, rescoped.Error!.ErrorCode);
        }

        await using var denied = BuildProvider(connectionString, permit: false);
        using var deniedScope = denied.CreateScope();
        var deniedService = deniedScope.ServiceProvider.GetRequiredService<IToyProductService>();
        var refused = await CaptureAsync(deniedService.GetOverviewAsync(
            productId, "corr-denied", TestContext.Current.CancellationToken));

        Assert.Equal(ToyErrorCodes.NotAuthorized, refused.Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.age_declaration"));
        Assert.Equal(3, await CountAsync(connectionString, "select count(*) from toy.audit_attempt"));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_the_whole_command_back(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString);
            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();

            var attempt = await CaptureAsync(service.RecordDecisionAsync(
                NewProductId(), Decision(0, 36), "corr-fail", TestContext.Current.CancellationToken));

            Assert.Equal(ToyErrorCodes.PersistenceUnavailable, attempt.Error!.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from toy.age_grade_decision"));
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from toy.product"));
            // The attempt log is written on its own connection, so it survives
            // the rollback that erased the business facts.
            Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
    }

    [Fact]
    public async Task Test_unit_plan_demand_approval_and_downstream_decisions_are_reconstructable()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var planService = scope.ServiceProvider.GetRequiredService<IToyTestUnitPlanService>();
        var productId = NewProductId();
        var product = await PrepareProductAsync(productService, productId);

        var draft = await planService.CreatePlanAsync(
            productId, TestUnitPlan(product.Version, 0), "corr-plan",
            TestContext.Current.CancellationToken);
        var approved = await planService.ApproveAsync(
            productId,
            draft.PlanVersion,
            new ApproveToySampleRequirementRequest(
                draft.PlanVersion,
                ToyTestUnitPlanContract.RuleSetVersion,
                draft.InputHash,
                "components and units checked"),
            "corr-approve",
            TestContext.Current.CancellationToken);
        var allocated = await planService.RequestAllocationAsync(
            productId,
            draft.PlanVersion,
            Downstream(draft.PlanVersion),
            "corr-allocate",
            TestContext.Current.CancellationToken);
        var reconstructed = await planService.GetAsync(
            productId, draft.PlanVersion, "corr-read-plan", TestContext.Current.CancellationToken);
        var status = await scope.ServiceProvider.GetRequiredService<IToyTestUnitPlanStatusPort>()
            .EvaluateAsync(new ToyTestUnitPlanStatusRequest(
                "group-a", productId, draft.PlanVersion, ToyTestUnitPlanContract.RuleSetVersion)
            {
                CorrelationId = "corr-plan-status"
            }, TestContext.Current.CancellationToken);

        Assert.Equal(ToyTestUnitPlanStates.Draft, draft.State);
        Assert.Equal(ToySampleRequirementDecisions.PendingTechnicalApproval, draft.Requirement.Decision);
        Assert.Equal(6, draft.Requirement.Components.Count);
        Assert.Equal(ToyTestUnitPlanStates.Approved, approved.State);
        Assert.Equal(ToySampleRequirementDecisions.Approved, approved.Requirement.Decision);
        Assert.Equal("technician-a", approved.TechnicalApproval!.ApprovedBy);
        Assert.Single(allocated.DownstreamDecisions);
        Assert.Equal(2, allocated.DownstreamDecisions[0].QuantityDecisions.Count);
        Assert.Single(allocated.DownstreamDecisions[0].AllocationDecisions);
        Assert.Equal(allocated.PlanId, reconstructed.PlanId);
        Assert.Equal(allocated.InputHash, reconstructed.InputHash);
        Assert.Equal(allocated.State, reconstructed.State);
        Assert.Equal(
            allocated.Requirement.Components.Select(item => (item.ComponentId, item.Kind, item.Amount)),
            reconstructed.Requirement.Components.Select(item => (item.ComponentId, item.Kind, item.Amount)));
        Assert.Equal(
            allocated.DownstreamDecisions[0].AllocationDecisions.Select(item => item.AllocationId),
            reconstructed.DownstreamDecisions[0].AllocationDecisions.Select(item => item.AllocationId));
        Assert.Equal(ToyTestUnitPlanStatusDecisions.Allowed, status.Decision);
        Assert.Equal(["reserve-count", "reserve-mass"], status.ReservationRefs.Order(StringComparer.Ordinal));
        Assert.Equal(["allocation-1"], status.AllocationIds);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.test_unit_plan"));
        Assert.Equal(6, await CountAsync(connectionString, "select count(*) from toy.sample_demand_component"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.technical_approval"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.downstream_request"));
    }

    [Fact]
    public async Task Historical_exclusive_destructive_use_survives_plan_versions_and_changed_test_unit_ids()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var planService = scope.ServiceProvider.GetRequiredService<IToyTestUnitPlanService>();
        var productId = NewProductId();
        var product = await PrepareProductAsync(productService, productId);
        var first = await planService.CreatePlanAsync(
            productId, TestUnitPlan(product.Version, 0), "corr-plan-1",
            TestContext.Current.CancellationToken);
        var firstApproved = await planService.ApproveAsync(
            productId,
            first.PlanVersion,
            new ApproveToySampleRequirementRequest(
                first.PlanVersion, ToyTestUnitPlanContract.RuleSetVersion,
                first.InputHash, "checked"),
            "corr-approve-1",
            TestContext.Current.CancellationToken);
        await planService.RequestAllocationAsync(
            productId, firstApproved.PlanVersion, Downstream(firstApproved.PlanVersion),
            "corr-allocate-1", TestContext.Current.CancellationToken);

        var second = await planService.CreatePlanAsync(
            productId,
            TestUnitPlan(product.Version, first.PlanVersion, testUnitSuffix: "b"),
            "corr-plan-2",
            TestContext.Current.CancellationToken);
        var secondApproved = await planService.ApproveAsync(
            productId,
            second.PlanVersion,
            new ApproveToySampleRequirementRequest(
                second.PlanVersion, ToyTestUnitPlanContract.RuleSetVersion,
                second.InputHash, "checked"),
            "corr-approve-2",
            TestContext.Current.CancellationToken);
        var conflict = await CaptureAsync(planService.RequestAllocationAsync(
            productId,
            secondApproved.PlanVersion,
            Downstream(
                secondApproved.PlanVersion,
                "00000000000000000000000000000311",
                "step-drop",
                "allocation-2"),
            "corr-allocation-reuse",
            TestContext.Current.CancellationToken));

        Assert.Equal(ToyErrorCodes.DestructiveTestUnitConflict, conflict.Error!.ErrorCode);
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from toy.test_unit_plan"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.downstream_request"));
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from toy.audit_attempt where outcome = 'TOY.DESTRUCTIVE_TEST_UNIT_CONFLICT'"));
    }

    [Fact]
    public async Task Concurrent_plan_append_has_one_winner_and_one_expected_version_conflict()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        var productId = NewProductId();
        ToyProductOverview product;
        using (var setupScope = provider.CreateScope())
        {
            product = await PrepareProductAsync(
                setupScope.ServiceProvider.GetRequiredService<IToyProductService>(), productId);
        }

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<IToyTestUnitPlanService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<IToyTestUnitPlanService>();
        var calls = new[]
        {
            CaptureAsync(firstService.CreatePlanAsync(
                productId, TestUnitPlan(product.Version, 0), "corr-concurrent-a",
                TestContext.Current.CancellationToken)),
            CaptureAsync(secondService.CreatePlanAsync(
                productId, TestUnitPlan(product.Version, 0, physicalPrefix: "other"), "corr-concurrent-b",
                TestContext.Current.CancellationToken))
        };

        var results = await Task.WhenAll(calls);

        Assert.Single(results, item => item.Result is not null);
        Assert.Single(results, item => item.Error?.ErrorCode == ToyErrorCodes.ExpectedVersionConflict);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.test_unit_plan"));
    }

    [Fact]
    public async Task Approval_permission_and_blocked_downstream_fail_without_partial_facts()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var productId = NewProductId();
        ToyTestUnitPlanResult draft;
        await using (var provider = BuildProvider(connectionString, approve: false))
        {
            using var scope = provider.CreateScope();
            var product = await PrepareProductAsync(
                scope.ServiceProvider.GetRequiredService<IToyProductService>(), productId);
            var service = scope.ServiceProvider.GetRequiredService<IToyTestUnitPlanService>();
            draft = await service.CreatePlanAsync(
                productId, TestUnitPlan(product.Version, 0), "corr-plan",
                TestContext.Current.CancellationToken);
            var denied = await CaptureAsync(service.ApproveAsync(
                productId,
                draft.PlanVersion,
                new ApproveToySampleRequirementRequest(
                    draft.PlanVersion, ToyTestUnitPlanContract.RuleSetVersion,
                    draft.InputHash, "checked"),
                "corr-denied-approval",
                TestContext.Current.CancellationToken));
            Assert.Equal(ToyErrorCodes.NotAuthorized, denied.Error!.ErrorCode);
        }

        await using (var provider = BuildProvider(
                         connectionString,
                         quantityDecision: QuantityAvailabilityDecisions.Blocked))
        {
            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IToyTestUnitPlanService>();
            var approved = await service.ApproveAsync(
                productId,
                draft.PlanVersion,
                new ApproveToySampleRequirementRequest(
                    draft.PlanVersion, ToyTestUnitPlanContract.RuleSetVersion,
                    draft.InputHash, "checked"),
                "corr-approved",
                TestContext.Current.CancellationToken);
            var blocked = await CaptureAsync(service.RequestAllocationAsync(
                productId, approved.PlanVersion, Downstream(approved.PlanVersion),
                "corr-blocked-downstream", TestContext.Current.CancellationToken));
            Assert.Equal(ToyErrorCodes.DownstreamEligibilityBlocked, blocked.Error!.ErrorCode);
        }

        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.technical_approval"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from toy.downstream_request"));
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from toy.audit_attempt"));
    }

    [Fact]
    public async Task Test_unit_plan_rows_are_database_append_only_and_evidence_failure_rolls_back()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var service = scope.ServiceProvider.GetRequiredService<IToyTestUnitPlanService>();
        var productId = NewProductId();
        var product = await PrepareProductAsync(productService, productId);
        var draft = await service.CreatePlanAsync(
            productId, TestUnitPlan(product.Version, 0), "corr-plan",
            TestContext.Current.CancellationToken);

        var rewrite = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update toy.test_unit_plan set input_hash = 'rewritten';"));
        var erase = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from toy.sample_requirement;"));
        Assert.Equal("55000", rewrite.SqlState);
        Assert.Equal("55000", erase.SqlState);

        await InstallFailureTriggerAsync(connectionString, "outbox");
        try
        {
            var failed = await CaptureAsync(service.CreatePlanAsync(
                productId,
                TestUnitPlan(product.Version, draft.PlanVersion, physicalPrefix: "fresh"),
                "corr-evidence-fail",
                TestContext.Current.CancellationToken));
            Assert.Equal(ToyErrorCodes.PersistenceUnavailable, failed.Error!.ErrorCode);
            Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.test_unit_plan"));
            Assert.Equal(1, await CountAsync(connectionString,
                "select count(*) from toy.audit_attempt where outcome = 'TOY.PERSISTENCE_UNAVAILABLE'"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, "outbox");
        }
    }

    [Fact]
    public async Task Four_label_artifact_types_append_versions_verify_images_and_reject_database_mutation()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var storage = new FixedObjectStoragePort();
        await using var provider = BuildProvider(connectionString, objectStorage: storage);
        using var scope = provider.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var service = scope.ServiceProvider.GetRequiredService<IToyLabelReviewService>();
        var productId = NewProductId();
        await PrepareProductAsync(productService, productId);

        var artifacts = new List<ToyLabelArtifactResult>();
        foreach (var artifactType in ToyLabelArtifactTypes.All)
        {
            var evidence = storage.Add($"{artifactType.ToLowerInvariant()}-v1");
            artifacts.Add(await service.CreateArtifactAsync(
                productId,
                LabelArtifact(artifactType, "zh-CN", "CN", evidence),
                $"corr-{artifactType}",
                TestContext.Current.CancellationToken));
        }

        var label = artifacts.Single(item => item.ArtifactType == ToyLabelArtifactTypes.Label);
        var v2Evidence = storage.Add("label-v2");
        var versioned = await service.AppendArtifactVersionAsync(
            productId,
            label.ArtifactId,
            new AppendToyLabelArtifactVersionRequest(1, Hash("label-content-v2"), [v2Evidence]),
            "corr-label-v2",
            TestContext.Current.CancellationToken);

        Assert.Equal(4, artifacts.Count);
        Assert.Equal([1L, 2L], versioned.Versions.Select(item => item.VersionNumber));
        Assert.Equal(Hash("label-content"), versioned.Versions[0].ContentHash);
        Assert.Equal(v2Evidence.Hash, Assert.Single(versioned.Versions[1].ImageEvidenceRefs).Hash);
        Assert.Equal(5, await CountAsync(connectionString, "select count(*) from toy.label_artifact"));
        Assert.Equal(5, await CountAsync(connectionString, "select count(*) from toy.label_artifact_image"));

        var rewrite = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update toy.label_artifact set content_hash = repeat('0', 64);"));
        var erase = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from toy.label_artifact_image;"));
        Assert.Equal("55000", rewrite.SqlState);
        Assert.Equal("55000", erase.SqlState);

        var inaccessible = new ToyLabelImageEvidenceInput(
            new ToyImageObjectReference("toy-evidence", "missing.png"), Hash("missing"));
        var failed = await CaptureAsync(service.CreateArtifactAsync(
            productId,
            LabelArtifact(ToyLabelArtifactTypes.Label, "en-US", "US", inaccessible),
            "corr-missing-image",
            TestContext.Current.CancellationToken));
        Assert.Equal(ToyErrorCodes.ObjectNotAccessible, failed.Error!.ErrorCode);
        var existingButWrongHash = storage.Add("hash-mismatch") with { Hash = Hash("different-bytes") };
        var mismatch = await CaptureAsync(service.CreateArtifactAsync(
            productId,
            LabelArtifact(ToyLabelArtifactTypes.Label, "fr-FR", "FR", existingButWrongHash),
            "corr-image-hash-mismatch",
            TestContext.Current.CancellationToken));
        Assert.Equal(ToyErrorCodes.ObjectNotAccessible, mismatch.Error!.ErrorCode);
        Assert.Equal(5, await CountAsync(connectionString, "select count(*) from toy.label_artifact"));
    }

    [Fact]
    public async Task Age_change_invalidates_only_exact_chinese_scope_and_preserves_english_review()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var storage = new FixedObjectStoragePort();
        await using var provider = BuildProvider(connectionString, objectStorage: storage);
        using var scope = provider.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var service = scope.ServiceProvider.GetRequiredService<IToyLabelReviewService>();
        var impactPort = scope.ServiceProvider.GetRequiredService<IToyLabelReviewImpactPort>();
        var statusPort = scope.ServiceProvider.GetRequiredService<IToyLabelReviewStatusPort>();
        var productId = NewProductId();
        var product = await PrepareProductAsync(productService, productId);

        var chinese = await CreateApprovedReviewAsync(
            service, productId, product, "zh-CN", "CN", "AGE-CLAIM-ZH-CN", storage);
        var english = await CreateApprovedReviewAsync(
            service, productId, product, "en-US", "US", "AGE-CLAIM-EN-US", storage);
        var changed = await ChangeAgeAsync(productService, productId, product);
        var changeRef = new ToyVersionedReference(changed.EffectiveDecision!.DecisionId, 2);
        var impact = await impactPort.EvaluateAsync(new ToyLabelReviewImpactRequest(
            "group-a",
            productId,
            ToyLabelChangeTypes.AgeGradeDecision,
            changeRef,
            changed.Version,
            2,
            [new ToyVersionedReference("AGE-CLAIM-ZH-CN", 1)],
            ToyLabelReviewContract.SupportedImpactRule,
            ToyLabelReviewContract.RuleSetVersion)
        {
            CorrelationId = "corr-impact"
        }, TestContext.Current.CancellationToken);

        var chineseStatus = await statusPort.EvaluateAsync(LabelStatus(
            productId, changed.Version, 2, "CN", "zh-CN"), TestContext.Current.CancellationToken);
        var englishStatus = await statusPort.EvaluateAsync(LabelStatus(
            productId, changed.Version, 2, "US", "en-US"), TestContext.Current.CancellationToken);

        Assert.Equal(2, impact.Evaluations.Count);
        Assert.Contains(impact.Evaluations, item => item.Result == ToyLabelImpactResults.Impacted);
        Assert.Contains(impact.Evaluations, item => item.Result == ToyLabelImpactResults.NotImpacted);
        Assert.Equal(ToyLabelReviewStatusDecisions.ReReviewRequired, chineseStatus.Decision);
        Assert.Equal(chinese.ArtifactId, chineseStatus.ArtifactId);
        Assert.Equal(ToyLabelReviewStatusDecisions.Valid, englishStatus.Decision);
        Assert.Equal(english.ArtifactId, englishStatus.ArtifactId);
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from toy.label_review_impact_evaluation"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.label_review_invalidation"));
    }

    [Fact]
    public async Task Missing_impact_rule_and_scope_are_recorded_unknown_and_block_old_approval()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var storage = new FixedObjectStoragePort();
        await using var provider = BuildProvider(connectionString, objectStorage: storage);
        using var scope = provider.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var service = scope.ServiceProvider.GetRequiredService<IToyLabelReviewService>();
        var impactPort = scope.ServiceProvider.GetRequiredService<IToyLabelReviewImpactPort>();
        var statusPort = scope.ServiceProvider.GetRequiredService<IToyLabelReviewStatusPort>();
        var productId = NewProductId();
        var product = await PrepareProductAsync(productService, productId);
        await CreateApprovedReviewAsync(
            service, productId, product, "zh-CN", "CN", "AGE-CLAIM-ZH-CN", storage);
        var changed = await ChangeAgeAsync(productService, productId, product);

        var attempt = await CaptureAsync(impactPort.EvaluateAsync(
            new ToyLabelReviewImpactRequest(
                "group-a",
                productId,
                ToyLabelChangeTypes.AgeGradeDecision,
                new ToyVersionedReference(changed.EffectiveDecision!.DecisionId, 2),
                changed.Version,
                2,
                null,
                null,
                ToyLabelReviewContract.RuleSetVersion)
            {
                CorrelationId = "corr-unknown-impact"
            },
            TestContext.Current.CancellationToken).AsTask());
        var status = await statusPort.EvaluateAsync(LabelStatus(
            productId, changed.Version, 2, "CN", "zh-CN"), TestContext.Current.CancellationToken);

        Assert.Equal(ToyErrorCodes.LabelImpactUnknown, attempt.Error!.ErrorCode);
        Assert.Equal(ToyLabelReviewStatusDecisions.Unknown, status.Decision);
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from toy.label_review_impact_evaluation where result = 'UNKNOWN'"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from toy.label_review_invalidation"));
    }

    [Fact]
    public async Task Re_review_cites_the_old_review_and_change_without_rewriting_history()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var storage = new FixedObjectStoragePort();
        await using var provider = BuildProvider(connectionString, objectStorage: storage);
        using var scope = provider.CreateScope();
        var productService = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var service = scope.ServiceProvider.GetRequiredService<IToyLabelReviewService>();
        var impactPort = scope.ServiceProvider.GetRequiredService<IToyLabelReviewImpactPort>();
        var productId = NewProductId();
        var product = await PrepareProductAsync(productService, productId);
        var first = await CreateApprovedReviewAsync(
            service, productId, product, "zh-CN", "CN", "AGE-CLAIM-ZH-CN", storage);
        var changed = await ChangeAgeAsync(productService, productId, product);
        var changeRef = new ToyVersionedReference(changed.EffectiveDecision!.DecisionId, 2);
        await impactPort.EvaluateAsync(new ToyLabelReviewImpactRequest(
            "group-a", productId, ToyLabelChangeTypes.AgeGradeDecision, changeRef,
            changed.Version, 2, [new ToyVersionedReference("AGE-CLAIM-ZH-CN", 1)],
            ToyLabelReviewContract.SupportedImpactRule, ToyLabelReviewContract.RuleSetVersion),
            TestContext.Current.CancellationToken);
        var artifact = await service.AppendArtifactVersionAsync(
            productId,
            first.ArtifactId,
            new AppendToyLabelArtifactVersionRequest(1, Hash("label-content-v2"), [storage.Add("label-v2")]),
            "corr-artifact-v2",
            TestContext.Current.CancellationToken);
        var draft = await service.CreateReviewAsync(
            productId,
            artifact.ArtifactId,
            LabelReview(
                1,
                2,
                changed.Version,
                2,
                "CN",
                "zh-CN",
                "AGE-CLAIM-ZH-CN",
                ToyLabelReviewContract.SupportedImpactRule,
                1,
                new ToyLabelReviewChangeReference(ToyLabelChangeTypes.AgeGradeDecision, changeRef)),
            "corr-review-v2",
            TestContext.Current.CancellationToken);
        var approved = await service.DecideReviewAsync(
            productId,
            draft.ReviewId,
            new DecideToyLabelReviewRequest(
                2, ToyLabelReviewDecisionValues.Approved, "new artifact and age decision reviewed"),
            "corr-decision-v2",
            TestContext.Current.CancellationToken);
        var status = await service.GetStatusAsync(
            productId,
            new ToyLabelReviewStatusQuery(
                changed.Version, 2, "CN", "zh-CN", ToyLabelArtifactTypes.Label,
                ToyLabelReviewContract.RuleSetVersion),
            "corr-status-v2",
            TestContext.Current.CancellationToken);

        Assert.Equal([1L, 2L], approved.Versions.Select(item => item.ReviewVersion));
        Assert.Equal(ToyLabelReviewStates.Invalidated, approved.Versions[0].State);
        Assert.Equal(ToyLabelReviewStates.Approved, approved.Versions[1].State);
        Assert.Equal(1, approved.Versions[1].PreviousReviewVersion);
        Assert.Equal(changeRef, approved.Versions[1].TriggerChange!.ChangeRef);
        Assert.Equal(ToyLabelReviewStatusDecisions.Valid, status.Decision);
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from toy.label_artifact"));
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from toy.label_review"));
    }

    [Fact]
    public async Task Label_review_permission_concurrency_and_evidence_failure_fail_closed()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var storage = new FixedObjectStoragePort();
        var productId = NewProductId();
        ToyLabelReviewResult draft;
        await using (var provider = BuildProvider(connectionString, objectStorage: storage))
        {
            using var scope = provider.CreateScope();
            var productService = scope.ServiceProvider.GetRequiredService<IToyProductService>();
            var service = scope.ServiceProvider.GetRequiredService<IToyLabelReviewService>();
            var product = await PrepareProductAsync(productService, productId);
            var artifact = await service.CreateArtifactAsync(
                productId,
                LabelArtifact(ToyLabelArtifactTypes.Label, "zh-CN", "CN", storage.Add("label-v1")),
                "corr-artifact",
                TestContext.Current.CancellationToken);
            draft = await service.CreateReviewAsync(
                productId,
                artifact.ArtifactId,
                LabelReview(0, 1, product.Version, 1, "CN", "zh-CN", "AGE-CLAIM-ZH-CN"),
                "corr-review",
                TestContext.Current.CancellationToken);
        }

        await using (var deniedProvider = BuildProvider(
            connectionString, labelReview: false, objectStorage: storage))
        {
            using var scope = deniedProvider.CreateScope();
            var deniedService = scope.ServiceProvider.GetRequiredService<IToyLabelReviewService>();
            var denied = await CaptureAsync(deniedService.DecideReviewAsync(
                productId,
                draft.ReviewId,
                new DecideToyLabelReviewRequest(1, ToyLabelReviewDecisionValues.Approved, "checked"),
                "corr-denied",
                TestContext.Current.CancellationToken));
            Assert.Equal(ToyErrorCodes.NotAuthorized, denied.Error!.ErrorCode);
        }

        await using (var concurrentProvider = BuildProvider(connectionString, objectStorage: storage))
        {
            using var firstScope = concurrentProvider.CreateScope();
            using var secondScope = concurrentProvider.CreateScope();
            var first = firstScope.ServiceProvider.GetRequiredService<IToyLabelReviewService>();
            var second = secondScope.ServiceProvider.GetRequiredService<IToyLabelReviewService>();
            var outcomes = await Task.WhenAll(
                CaptureAsync(first.DecideReviewAsync(
                    productId, draft.ReviewId,
                    new DecideToyLabelReviewRequest(1, ToyLabelReviewDecisionValues.Approved, "approved"),
                    "corr-concurrent-a", TestContext.Current.CancellationToken)),
                CaptureAsync(second.DecideReviewAsync(
                    productId, draft.ReviewId,
                    new DecideToyLabelReviewRequest(1, ToyLabelReviewDecisionValues.Rejected, "rejected"),
                    "corr-concurrent-b", TestContext.Current.CancellationToken)));
            Assert.Single(outcomes, item => item.Result is not null);
            Assert.Single(outcomes, item => item.Error?.ErrorCode == ToyErrorCodes.ExpectedVersionConflict);
        }

        foreach (var failedWriter in new[] { "audit", "outbox" })
        {
            await InstallFailureTriggerAsync(connectionString, failedWriter);
            try
            {
                await using var failedProvider = BuildProvider(connectionString, objectStorage: storage);
                using var scope = failedProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IToyLabelReviewService>();
                var failed = await CaptureAsync(service.CreateArtifactAsync(
                    productId,
                    LabelArtifact(
                        ToyLabelArtifactTypes.Instruction,
                        failedWriter == "audit" ? "zh-CN" : "en-US",
                        failedWriter == "audit" ? "CN" : "US",
                        storage.Add($"instruction-{failedWriter}")),
                    $"corr-{failedWriter}-fail",
                    TestContext.Current.CancellationToken));
                Assert.Equal(ToyErrorCodes.PersistenceUnavailable, failed.Error!.ErrorCode);
                Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.label_artifact"));
            }
            finally
            {
                await RemoveFailureTriggerAsync(connectionString, failedWriter);
            }
        }

        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.label_review_decision"));
        Assert.True(await CountAsync(connectionString, "select count(*) from toy.audit_attempt") >= 3);
    }

    private static async Task<(object? Result, ToyDomainException? Error)> CaptureAsync<T>(Task<T> task)
    {
        try
        {
            return (await task, null);
        }
        catch (ToyDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        bool permit = true,
        string actorId = "technician-a",
        bool approve = true,
        string quantityDecision = QuantityAvailabilityDecisions.Allowed,
        string allocationDecision = AllocationStatusDecisions.Allowed,
        bool labelReview = true,
        IObjectStoragePort? objectStorage = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformDependencies(new PlatformDependencyOptions
        {
            PostgresConnectionString = connectionString,
            OidcAuthority = "https://issuer.invalid",
            OidcAudience = "openlims-api",
            ObjectStorageEndpoint = "https://storage.invalid",
            ObjectStorageBucket = "test",
            ObjectStorageAccessKey = "test",
            ObjectStorageSecretKey = "test",
            PostgresCommandTimeoutSeconds = 10,
            OidcMetadataTimeoutSeconds = 1,
            ObjectStorageProbeTimeoutSeconds = 1,
            DependencyProbeTimeoutSeconds = 2
        });
        services.AddSingleton<ICurrentOrganizationContext>(
            new DeploymentOrganizationContext(new OrganizationScope("group-a")));
        services.AddSingleton<ICurrentActorContext>(new FixedActorContext(new ActorContext(actorId, "group-a")));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        new ToyModule(connectionString).AddApiServices(services);
        services.RemoveAll<IToyAuthorizationPort>();
        services.AddSingleton<IToyAuthorizationPort>(new FixedAuthorizationPort(permit, approve, labelReview));
        services.RemoveAll<IObjectStoragePort>();
        services.AddSingleton<IObjectStoragePort>(objectStorage ?? new FixedObjectStoragePort());
        services.RemoveAll<IQuantityAvailabilityPort>();
        services.AddSingleton<IQuantityAvailabilityPort>(new FixedQuantityPort(quantityDecision));
        services.RemoveAll<IAllocationStatusPort>();
        services.AddSingleton<IAllocationStatusPort>(new FixedAllocationPort(allocationDecision));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string NewProductId() => Guid.NewGuid().ToString("N");

    private static RecordAgeDeclarationRequest Declaration(long expectedVersion) => new(
        ToyContract.RuleSetVersion, new ToyObjectContext("LEGAL-A", "LAB-A"), expectedVersion,
        36, "室内地板玩具车", "CUSTOMER_SUBMISSION");

    private static RecordAgeGradeDecisionRequest Decision(long expectedVersion, int months) => new(
        ToyContract.RuleSetVersion, new ToyObjectContext("LEGAL-A", "LAB-A"), expectedVersion,
        months, "无可分离小零件", new ToyVersionedReference("GB6675.2", 2), "APPROVER-1");

    private static RecordAccessibilityAssessmentRequest Assessment(
        long expectedVersion, string stage, IReadOnlyList<string> parts) => new(
        ToyContract.RuleSetVersion, new ToyObjectContext("LEGAL-A", "LAB-A"), expectedVersion,
        stage, null, parts);

    private static ResolveReassessmentTriggerRequest ToyResolution(long expectedVersion) => new(
        ToyContract.RuleSetVersion, expectedVersion, new ToyVersionedReference("REASSESS-1", 1));

    private static async Task<ToyProductOverview> PrepareProductAsync(
        IToyProductService service, string productId)
    {
        var decided = await service.RecordDecisionAsync(
            productId, Decision(0, 36), "corr-age", TestContext.Current.CancellationToken);
        var frozen = await service.FreezeDecisionAsync(
            productId,
            1,
            new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, decided.Version),
            "corr-age-freeze",
            TestContext.Current.CancellationToken);
        return await service.RecordAssessmentAsync(
            productId,
            Assessment(frozen.Version, ToyAssessmentStages.Initial, ["shell"]),
            "corr-accessibility",
            TestContext.Current.CancellationToken);
    }

    private static async Task<ToyProductOverview> ChangeAgeAsync(
        IToyProductService service,
        string productId,
        ToyProductOverview product)
    {
        var decided = await service.RecordDecisionAsync(
            productId,
            Decision(product.Version, 48),
            "corr-age-v2",
            TestContext.Current.CancellationToken);
        return await service.FreezeDecisionAsync(
            productId,
            2,
            new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, decided.Version),
            "corr-age-freeze-v2",
            TestContext.Current.CancellationToken);
    }

    private static CreateToyLabelArtifactRequest LabelArtifact(
        string artifactType,
        string language,
        string market,
        ToyLabelImageEvidenceInput evidence) => new(
        new ToyObjectContext("LEGAL-A", "LAB-A"),
        0,
        artifactType,
        language,
        market,
        Hash("label-content"),
        [evidence]);

    private static CreateToyLabelReviewRequest LabelReview(
        long expectedReviewVersion,
        long artifactVersion,
        long productVersion,
        long ageGradeDecisionVersion,
        string market,
        string language,
        string scope,
        ToyVersionedReference? impactRule = null,
        long? previousReviewVersion = null,
        ToyLabelReviewChangeReference? triggerChange = null) => new(
        expectedReviewVersion,
        artifactVersion,
        productVersion,
        ageGradeDecisionVersion,
        market,
        language,
        [new ToyVersionedReference(scope, 1)],
        impactRule ?? ToyLabelReviewContract.SupportedImpactRule,
        ToyLabelReviewContract.RuleSetVersion,
        previousReviewVersion,
        triggerChange);

    private static async Task<ToyLabelReviewResult> CreateApprovedReviewAsync(
        IToyLabelReviewService service,
        string productId,
        ToyProductOverview product,
        string language,
        string market,
        string reviewScope,
        FixedObjectStoragePort storage,
        ToyVersionedReference? impactRule = null)
    {
        var artifact = await service.CreateArtifactAsync(
            productId,
            LabelArtifact(ToyLabelArtifactTypes.Label, language, market, storage.Add($"{language}-{market}-v1")),
            $"corr-artifact-{language}",
            TestContext.Current.CancellationToken);
        var draft = await service.CreateReviewAsync(
            productId,
            artifact.ArtifactId,
            LabelReview(0, 1, product.Version, 1, market, language, reviewScope, impactRule),
            $"corr-review-{language}",
            TestContext.Current.CancellationToken);
        return await service.DecideReviewAsync(
            productId,
            draft.ReviewId,
            new DecideToyLabelReviewRequest(
                1, ToyLabelReviewDecisionValues.Approved, "reviewed against pinned evidence"),
            $"corr-decision-{language}",
            TestContext.Current.CancellationToken);
    }

    private static ToyLabelReviewStatusRequest LabelStatus(
        string productId,
        long productVersion,
        long ageGradeDecisionVersion,
        string market,
        string language) => new(
        "group-a",
        productId,
        productVersion,
        ageGradeDecisionVersion,
        market,
        language,
        ToyLabelArtifactTypes.Label,
        ToyLabelReviewContract.RuleSetVersion);

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static CreateToyTestUnitPlanRequest TestUnitPlan(
        long productVersion,
        long expectedPlanVersion,
        string physicalPrefix = "physical",
        string testUnitSuffix = "a")
    {
        var hazard = new ToyVersionedReference("MECHANICAL", 3);
        return new CreateToyTestUnitPlanRequest(
            ToyTestUnitPlanContract.RuleSetVersion,
            new ToyObjectContext("LEGAL-A", "LAB-A"),
            expectedPlanVersion,
            productVersion,
            1,
            1,
            "scope-matrix-1",
            5,
            [new ToyVersionedReference("scope-line-1", 2)],
            [new ToyVersionedReference("sample-rules", 4)],
            [
                new CreateToyTestUnitInput(
                    testUnitSuffix == "a"
                        ? "00000000000000000000000000000301"
                        : "00000000000000000000000000000311",
                    new ToyVersionedReference($"{physicalPrefix}-1", 7),
                    [hazard],
                    1,
                    [
                        new CreateToySequenceStepInput(
                            "step-drop", 1, new ToyVersionedReference("DROP", 2),
                            true, "DROP-CRUSH", null),
                        new CreateToySequenceStepInput(
                            "step-visual", 2, new ToyVersionedReference("VISUAL", 1),
                            false, null, new ToyVersionedReference("NONDESTRUCTIVE-SHARE", 1))
                    ]),
                new CreateToyTestUnitInput(
                    testUnitSuffix == "a"
                        ? "00000000000000000000000000000302"
                        : "00000000000000000000000000000312",
                    new ToyVersionedReference($"{physicalPrefix}-2", 4),
                    [hazard],
                    2,
                    [new CreateToySequenceStepInput(
                        "step-crush", 1, new ToyVersionedReference("CRUSH", 2),
                        true, "DROP-CRUSH", null)])
            ],
            [
                Demand("base", ToySampleDemandKinds.Base, 4m, "COUNT", "piece", "base-rule", 1),
                Demand("parallel", ToySampleDemandKinds.Parallel, 4m, "COUNT", "piece", "parallel-rule", 1),
                Demand("exclusive", ToySampleDemandKinds.ExclusiveDestructive, 2m, "COUNT", "piece", "exclusive-rule", 2),
                Demand("chemical", ToySampleDemandKinds.ChemicalMinimum, 10m, "MASS", "g", "chemical-rule", 3),
                Demand("retest", ToySampleDemandKinds.RetestReserve, 3m, "COUNT", "piece", "retest-rule", 1),
                Demand("retention", ToySampleDemandKinds.Retention, 2m, "COUNT", "piece", "retention-rule", 1)
            ]);
    }

    private static ToySampleDemandInput Demand(
        string id, string kind, decimal amount, string dimension, string unit, string rule, long version) =>
        new(
            id,
            kind,
            new ToyVersionedReference("MECHANICAL", 3),
            null,
            amount,
            dimension,
            unit,
            new ToyVersionedReference(rule, version),
            ToyApplicabilityDecisions.Allowed);

    private static RequestToyAllocationRequest Downstream(
        long expectedPlanVersion,
        string testUnitId = "00000000000000000000000000000301",
        string sequenceStepId = "step-drop",
        string allocationId = "allocation-1") => new(
        expectedPlanVersion,
        ToyTestUnitPlanContract.RuleSetVersion,
        [
            new ToyQuantityGateInput(
                "qty-count", 4, QuantityContract.RuleSetVersion, 15m, "COUNT", "piece", "reserve-count"),
            new ToyQuantityGateInput(
                "qty-mass", 2, QuantityContract.RuleSetVersion, 10m, "MASS", "g", "reserve-mass")
        ],
        [new ToyAllocationGateInput(
            allocationId, 3, AllocationContract.RuleSetVersion,
            testUnitId, sequenceStepId)]);

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for toy integration tests.");

    private static string ConnectionString() => new NpgsqlConnectionStringBuilder(AdminConnectionString())
    {
        Database = DedicatedDatabaseName
    }.ConnectionString;

    private static async Task EnsureDedicatedDatabaseAsync()
    {
        if (_databaseEnsured)
            return;

        await using var dataSource = NpgsqlDataSource.Create(AdminConnectionString());
        await using var exists = dataSource.CreateCommand("select 1 from pg_database where datname = $1");
        exists.Parameters.AddWithValue(DedicatedDatabaseName);
        if (await exists.ExecuteScalarAsync(TestContext.Current.CancellationToken) is null)
        {
            try
            {
                await using var create = dataSource.CreateCommand($"create database \"{DedicatedDatabaseName}\"");
                await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == "42P04")
            {
            }
        }

        _databaseEnsured = true;
    }

    private static async Task PrepareAsync(string connectionString)
    {
        await EnsureDedicatedDatabaseAsync();
        await PlatformMigrationRunner.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await new ToyModule(connectionString).ApplyMigrationAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              toy.audit_attempt,
              toy.allocation_decision,
              toy.quantity_decision,
              toy.downstream_request,
              toy.technical_approval,
              toy.destructive_test_unit_usage,
              toy.sample_demand_component,
              toy.sample_requirement,
              toy.test_unit_sequence_step,
              toy.test_unit_hazard_domain,
              toy.test_unit,
              toy.test_unit_plan_sample_rule,
              toy.test_unit_plan_scope_line,
              toy.test_unit_plan,
              toy.reassessment_resolution,
              toy.reassessment_trigger,
              toy.accessible_part,
              toy.accessibility_assessment,
              toy.age_grade_freeze,
              toy.age_grade_decision,
              toy.age_declaration,
              toy.product,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_toy_audit on platform.audit_intent;
                drop function if exists platform.fail_toy_audit();
                create or replace function platform.fail_toy_audit() returns trigger language plpgsql as $$
                begin
                  if new.action like '%TOY%' or new.action like '%LABEL%' then
                    raise exception 'forced toy audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_toy_audit before insert on platform.audit_intent
                for each row execute function platform.fail_toy_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_toy_outbox on platform.outbox;
                drop function if exists platform.fail_toy_outbox();
                create or replace function platform.fail_toy_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type like 'Toy%' then
                    raise exception 'forced toy outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_toy_outbox before insert on platform.outbox
                for each row execute function platform.fail_toy_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_toy_audit on platform.audit_intent;
                drop function if exists platform.fail_toy_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_toy_outbox on platform.outbox;
                drop function if exists platform.fail_toy_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FixedAuthorizationPort(bool allowed, bool approve, bool labelReview) : IToyAuthorizationPort
    {
        public ValueTask<ToyAuthorizationDecision> AuthorizeAsync(
            ToyAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            var permitted = allowed && request.Capability switch
            {
                ToyCapabilities.SampleDemandApprove => approve,
                ToyCapabilities.LabelReview => labelReview,
                _ => true
            };
            return ValueTask.FromResult(permitted ? ToyAuthorizationDecision.Permit : ToyAuthorizationDecision.Deny);
        }
    }

    private sealed class FixedObjectStoragePort : IObjectStoragePort
    {
        private readonly Dictionary<ObjectReference, byte[]> _objects = [];

        public ToyLabelImageEvidenceInput Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var reference = new ObjectReference("toy-evidence", $"{value}.png");
            _objects[reference] = bytes;
            return new ToyLabelImageEvidenceInput(
                new ToyImageObjectReference(reference.Bucket, reference.ObjectKey),
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }

        public Task PutAsync(
            ObjectReference reference,
            Stream content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(
            ObjectReference reference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_objects.TryGetValue(reference, out var bytes))
                throw new InvalidOperationException("OBJECT_NOT_FOUND");
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public Task DeleteAsync(
            ObjectReference reference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedQuantityPort(string decision) : IQuantityAvailabilityPort
    {
        public ValueTask<QuantityAvailabilityResult> EvaluateAsync(
            QuantityAvailabilityRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new QuantityAvailabilityResult(
                decision,
                decision == QuantityAvailabilityDecisions.Allowed ? [] : ["INSUFFICIENT_AVAILABLE"],
                request.QuantityAccountId,
                request.ExpectedAccountVersion,
                decision == QuantityAvailabilityDecisions.Allowed ? request.RequestedAmount + 1m : 0m,
                request.RuleSetVersion));
    }

    private sealed class FixedAllocationPort(string decision) : IAllocationStatusPort
    {
        public ValueTask<AllocationStatusResult> EvaluateAsync(
            AllocationStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AllocationStatusResult(
                decision,
                decision == AllocationStatusDecisions.Allowed ? [] : ["ALLOCATION_UNAVAILABLE"],
                request.AllocationId,
                decision == AllocationStatusDecisions.Allowed ? AllocationStates.Active : null,
                request.ExpectedSubjectAllocationVersion,
                request.RuleSetVersion));
    }

    private sealed class FixedActorContext(ActorContext actor) : ICurrentActorContext
    {
        public ActorContext? Current { get; } = actor;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
