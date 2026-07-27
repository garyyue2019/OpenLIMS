using OpenLIMS.Contracts.Toy;
using OpenLIMS.Modules.Toy;
using Xunit;

namespace OpenLIMS.Toy.UnitTests;

[Trait("Profile", "toy")]
public sealed class ToyDomainTests
{
    [Fact]
    public void Declaration_requires_scope_use_source_and_a_plausible_age()
    {
        ToyDomain.ValidateDeclaration(Declaration());

        Assert.Throws<ToyDomainException>(() => ToyDomain.ValidateDeclaration(null));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateDeclaration(Declaration() with { RuleSetVersion = "TOY-AGE-GRADE@9.9.9" }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateDeclaration(Declaration() with { IntendedUse = " " }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateDeclaration(Declaration() with { DeclarationSource = "" }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateDeclaration(Declaration() with { DeclaredMinimumAgeMonths = -1 }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateDeclaration(Declaration() with { DeclaredMinimumAgeMonths = 217 }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateDeclaration(Declaration() with { ObjectScope = new ToyObjectContext("LE-1", " ") }));
    }

    [Fact]
    public void Decision_needs_rationale_standard_and_approver()
    {
        // OPS-TOY-001: these four are what make it a determination rather than
        // an opinion, so each one missing is its own rejection.
        ToyDomain.ValidateDecision(Decision());

        Assert.Equal(ToyErrorCodes.ValidationFailed,
            Assert.Throws<ToyDomainException>(() =>
                ToyDomain.ValidateDecision(Decision() with { Rationale = "  " })).ErrorCode);
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateDecision(Decision() with { StandardRef = null! }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateDecision(Decision() with { StandardRef = new ToyVersionedReference("GB6675.2", 0) }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateDecision(Decision() with { ApprovedBy = "" }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateDecision(Decision() with { MinimumAgeMonths = 400 }));
    }

    [Fact]
    public void Abuse_stage_and_abuse_event_must_agree()
    {
        ToyDomain.ValidateAssessment(Assessment(ToyAssessmentStages.Initial));
        ToyDomain.ValidateAssessment(Assessment(ToyAssessmentStages.AfterNormalUse));
        ToyDomain.ValidateAssessment(
            Assessment(ToyAssessmentStages.AfterAbuse) with { AbuseEventRef = "DROP-TEST-1" });

        // Naming no event makes an abuse finding untraceable; naming one on a
        // stage that ran no abuse describes something that did not happen.
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateAssessment(Assessment(ToyAssessmentStages.AfterAbuse)));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateAssessment(
                Assessment(ToyAssessmentStages.AfterAbuse) with { AbuseEventRef = " " }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateAssessment(
                Assessment(ToyAssessmentStages.Initial) with { AbuseEventRef = "DROP-TEST-1" }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateAssessment(Assessment("AFTER_LUNCH")));
    }

    [Fact]
    public void Assessment_parts_must_be_a_non_empty_distinct_set()
    {
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateAssessment(Assessment(ToyAssessmentStages.Initial) with { AccessibleParts = [] }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateAssessment(
                Assessment(ToyAssessmentStages.Initial) with { AccessibleParts = ["shell", "shell"] }));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.ValidateAssessment(
                Assessment(ToyAssessmentStages.Initial) with { AccessibleParts = ["shell", " "] }));
    }

    [Fact]
    public void Version_one_is_the_initial_stage_and_nothing_else_is()
    {
        ToyDomain.RequireInitialFirst(ToyAssessmentStages.Initial, 1);
        ToyDomain.RequireInitialFirst(ToyAssessmentStages.AfterAbuse, 2);

        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.RequireInitialFirst(ToyAssessmentStages.AfterNormalUse, 1));
        Assert.Throws<ToyDomainException>(() =>
            ToyDomain.RequireInitialFirst(ToyAssessmentStages.Initial, 2));
    }

    [Fact]
    public void Only_a_draft_determination_can_be_frozen()
    {
        ToyDomain.RequireFreezable(DecisionEntry(1, ToyDecisionStates.Draft));

        Assert.Equal(ToyErrorCodes.DecisionNotFound,
            Assert.Throws<ToyDomainException>(() => ToyDomain.RequireFreezable(null)).ErrorCode);
        Assert.Equal(ToyErrorCodes.DecisionFrozen,
            Assert.Throws<ToyDomainException>(() =>
                ToyDomain.RequireFreezable(DecisionEntry(1, ToyDecisionStates.Effective))).ErrorCode);
        Assert.Equal(ToyErrorCodes.DecisionFrozen,
            Assert.Throws<ToyDomainException>(() =>
                ToyDomain.RequireFreezable(DecisionEntry(1, ToyDecisionStates.Superseded))).ErrorCode);
    }

    [Fact]
    public void Newly_exposed_parts_are_additions_only()
    {
        var initial = AssessmentEntry(1, ToyAssessmentStages.Initial, ["shell", "wheels"]);

        // The very first assessment has nothing to be new against.
        Assert.Empty(ToyDomain.NewlyExposedParts(["shell"], null));
        Assert.Empty(ToyDomain.NewlyExposedParts(["shell", "wheels"], initial));
        // Losing access to a part cannot bring new requirements with it.
        Assert.Empty(ToyDomain.NewlyExposedParts(["shell"], initial));
        Assert.Equal(
            ["battery-compartment", "spring"],
            ToyDomain.NewlyExposedParts(["spring", "shell", "battery-compartment"], initial));
    }

    [Fact]
    public void Accessibility_is_pending_while_any_trigger_is_open()
    {
        Assert.Equal(ToyAccessibilityStatuses.Settled, ToyDomain.ResolveAccessibilityStatus([]));
        Assert.Equal(
            ToyAccessibilityStatuses.Settled,
            ToyDomain.ResolveAccessibilityStatus([Trigger(ToyTriggerStates.Resolved)]));
        Assert.Equal(
            ToyAccessibilityStatuses.ReassessmentPending,
            ToyDomain.ResolveAccessibilityStatus(
                [Trigger(ToyTriggerStates.Resolved), Trigger(ToyTriggerStates.Pending)]));
    }

    [Fact]
    public void Only_a_pending_trigger_can_be_resolved()
    {
        ToyDomain.RequirePending(Trigger(ToyTriggerStates.Pending));

        Assert.Equal(ToyErrorCodes.ReassessmentNotPending,
            Assert.Throws<ToyDomainException>(() => ToyDomain.RequirePending(null)).ErrorCode);
        Assert.Equal(ToyErrorCodes.ReassessmentNotPending,
            Assert.Throws<ToyDomainException>(() =>
                ToyDomain.RequirePending(Trigger(ToyTriggerStates.Resolved))).ErrorCode);
    }

    [Fact]
    public void Resolution_requires_an_approved_conclusion_reference()
    {
        ToyDomain.ValidateResolution(new ResolveReassessmentTriggerRequest(
            ToyContract.RuleSetVersion, 4, new ToyVersionedReference("REASSESS-1", 1)));

        Assert.Throws<ToyDomainException>(() => ToyDomain.ValidateResolution(null));
        Assert.Throws<ToyDomainException>(() => ToyDomain.ValidateResolution(
            new ResolveReassessmentTriggerRequest(ToyContract.RuleSetVersion, 4, null!)));
        Assert.Throws<ToyDomainException>(() => ToyDomain.ValidateResolution(
            new ResolveReassessmentTriggerRequest(
                ToyContract.RuleSetVersion, 4, new ToyVersionedReference("REASSESS-1", 0))));
    }

    [Fact]
    public void Effective_determination_is_the_highest_frozen_one()
    {
        Assert.Null(ToyDomain.ResolveEffectiveDecision([DecisionEntry(1, ToyDecisionStates.Draft)]));
        var decisions = new[]
        {
            DecisionEntry(1, ToyDecisionStates.Superseded),
            DecisionEntry(2, ToyDecisionStates.Effective),
            DecisionEntry(3, ToyDecisionStates.Draft)
        };
        Assert.Equal(2, ToyDomain.ResolveEffectiveDecision(decisions)!.VersionNumber);
    }

    [Fact]
    public void Status_port_blocks_without_a_determination_or_with_an_open_reassessment()
    {
        var settled = Overview(
            [DecisionEntry(1, ToyDecisionStates.Effective)], [], ToyAccessibilityStatuses.Settled);
        var allowed = ToyDomain.EvaluateStatus(StatusRequest(settled.Version), settled);
        Assert.Equal(ToyAgeGradeDecisions.Allowed, allowed.Decision);
        Assert.Equal(1, allowed.EffectiveDecisionVersion);
        Assert.Equal(36, allowed.MinimumAgeMonths);

        var undecided = Overview([DecisionEntry(1, ToyDecisionStates.Draft)], [], ToyAccessibilityStatuses.Settled);
        var blocked = ToyDomain.EvaluateStatus(StatusRequest(undecided.Version), undecided);
        Assert.Equal(ToyAgeGradeDecisions.Blocked, blocked.Decision);
        Assert.Equal([ToyAgeGradeReasons.NoEffectiveDecision], blocked.ReasonCodes);

        var pending = Overview(
            [DecisionEntry(1, ToyDecisionStates.Effective)],
            [Trigger(ToyTriggerStates.Pending)],
            ToyAccessibilityStatuses.ReassessmentPending);
        var pendingResult = ToyDomain.EvaluateStatus(StatusRequest(pending.Version), pending);
        Assert.Equal(ToyAgeGradeDecisions.Blocked, pendingResult.Decision);
        Assert.Equal([ToyAgeGradeReasons.ReassessmentPending], pendingResult.ReasonCodes);
    }

    [Fact]
    public void Status_port_answers_unknown_rather_than_guessing()
    {
        var overview = Overview(
            [DecisionEntry(1, ToyDecisionStates.Effective)], [], ToyAccessibilityStatuses.Settled);

        var staleVersion = ToyDomain.EvaluateStatus(StatusRequest(overview.Version + 1), overview);
        Assert.Equal(ToyAgeGradeDecisions.Unknown, staleVersion.Decision);
        Assert.Equal([ToyAgeGradeReasons.VersionMismatch], staleVersion.ReasonCodes);

        var unknownRuleSet = ToyDomain.EvaluateStatus(
            StatusRequest(overview.Version) with { RuleSetVersion = "TOY-AGE-GRADE@0.0.1" }, overview);
        Assert.Equal(ToyAgeGradeDecisions.Unknown, unknownRuleSet.Decision);
        Assert.Equal([ToyAgeGradeReasons.RuleSetVersionUnknown], unknownRuleSet.ReasonCodes);

        var missing = ToyDomain.EvaluateStatus(StatusRequest(1), null);
        Assert.Equal(ToyAgeGradeDecisions.Unknown, missing.Decision);
        Assert.Equal([ToyAgeGradeReasons.ToyUnavailable], missing.ReasonCodes);
        // UNKNOWN must never read as "accessibility is fine".
        Assert.Equal(ToyAccessibilityStatuses.ReassessmentPending, missing.AccessibilityStatus);
    }

    [Fact]
    public void Contract_pins_the_stages_and_scopes()
    {
        Assert.Equal(["INITIAL", "AFTER_NORMAL_USE", "AFTER_ABUSE"], ToyAssessmentStages.All);
        Assert.Equal(["MECHANICAL", "CHEMICAL", "LABELING"], ToyReassessmentScopes.All);
        Assert.Equal("TOY-AGE-GRADE@1.0.0", ToyContract.RuleSetVersion);
    }

    private static RecordAgeDeclarationRequest Declaration() => new(
        ToyContract.RuleSetVersion, Scope(), 1, 36, "室内地板玩具车", "CUSTOMER_SUBMISSION");

    private static RecordAgeGradeDecisionRequest Decision() => new(
        ToyContract.RuleSetVersion, Scope(), 1, 36, "无可分离小零件，符合 3 岁及以上",
        new ToyVersionedReference("GB6675.2", 2), "APPROVER-1");

    private static RecordAccessibilityAssessmentRequest Assessment(string stage) => new(
        ToyContract.RuleSetVersion, Scope(), 1, stage, null, ["shell", "wheels"]);

    private static ToyObjectContext Scope() => new("LE-1", "LAB-1");

    private static ToyAgeGradeDecisionEntry DecisionEntry(int versionNumber, string state) => new(
        Guid.NewGuid().ToString("N"), "PROD-1", versionNumber, 36, "依据",
        new ToyVersionedReference("GB6675.2", 2), "APPROVER-1", state,
        DateTimeOffset.UnixEpoch, state == ToyDecisionStates.Draft ? null : DateTimeOffset.UnixEpoch);

    private static ToyAccessibilityAssessmentEntry AssessmentEntry(
        int versionNumber, string stage, IReadOnlyList<string> parts) => new(
        Guid.NewGuid().ToString("N"), "PROD-1", versionNumber, stage,
        stage == ToyAssessmentStages.AfterAbuse ? "DROP-TEST-1" : null,
        parts, "TECH-1", DateTimeOffset.UnixEpoch);

    private static ToyReassessmentTriggerEntry Trigger(string state) => new(
        Guid.NewGuid().ToString("N"), "PROD-1", 2, ToyReassessmentScopes.Mechanical,
        ["battery-compartment"], state, null, null, null);

    private static ToyProductOverview Overview(
        IReadOnlyList<ToyAgeGradeDecisionEntry> decisions,
        IReadOnlyList<ToyReassessmentTriggerEntry> triggers,
        string accessibilityStatus) => new(
        "PROD-1", 7, ToyContract.RuleSetVersion, Scope(),
        ToyDomain.ResolveEffectiveDecision(decisions), [], decisions, [], triggers, accessibilityStatus);

    private static ToyAgeGradeStatusRequest StatusRequest(long expectedVersion) => new(
        "ORG-1", "PROD-1", expectedVersion, ToyContract.RuleSetVersion);
}
