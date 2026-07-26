using OpenLIMS.Contracts.Qc;
using OpenLIMS.Modules.Qc;
using Xunit;

namespace OpenLIMS.Qc.UnitTests;

[Trait("Profile", "qc")]
public sealed class QcRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
    private const string RunId = "00000000000000000000000000000100";
    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>();

    [Fact]
    public void Run_pins_method_and_rule_set_versions_and_rejects_unknown_rule_sets()
    {
        var normalized = QcRules.ValidateRun(Run());

        Assert.Equal("METHOD-TENSILE", normalized.Method.Id);
        Assert.Equal(3, normalized.Method.Version);
        Assert.Equal(2, normalized.QcRuleSet.Version);
        Assert.Throws<QcDomainException>(() => QcRules.ValidateRun(Run() with { RuleSetVersion = "QC-IMPACT@latest" }));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateRun(Run() with { ExpectedBatchVersion = 0 }));
        Assert.Throws<QcDomainException>(() =>
            QcRules.ValidateRun(Run() with { Method = new QcVersionedReference("METHOD-TENSILE", 0) }));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateRun(Run() with { BatchId = "bad batch" }));
    }

    [Fact]
    public void Result_requires_known_control_type_verdict_and_basis()
    {
        QcRules.ValidateResult(Result(QcVerdicts.Pass));

        Assert.Throws<QcDomainException>(() => QcRules.ValidateResult(Result(QcVerdicts.Pass) with { ControlType = "VIBES" }));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateResult(Result("MAYBE")));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateResult(Result(QcVerdicts.Fail) with { VerdictBasis = "  " }));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateResult(Result(QcVerdicts.Fail) with { ObservedValue = "" }));
        Assert.Throws<QcDomainException>(() =>
            QcRules.ValidateResult(Result(QcVerdicts.Pass) with { Rule = new QcVersionedReference("RULE-1", 0) }));
    }

    [Fact]
    public void Any_failing_rule_fails_the_whole_run()
    {
        Assert.Equal(QcRunStates.Passed, QcRules.ResolveVerdict([Entry(QcVerdicts.Pass), Entry(QcVerdicts.Pass)]));
        Assert.Equal(QcRunStates.Failed, QcRules.ResolveVerdict([Entry(QcVerdicts.Pass), Entry(QcVerdicts.Fail)]));
        Assert.Throws<QcDomainException>(() => QcRules.ResolveVerdict([]));
    }

    [Fact]
    public void Impact_scope_must_name_targets_and_rejects_the_empty_and_duplicate_cases()
    {
        var targets = QcRules.ValidateImpact(Impact(Target("GROUP-1"), Target("GROUP-2")), Nothing);

        Assert.Equal(2, targets.Count);
        // RULE-022: an empty impact set is exactly the "only fix the result that
        // tripped" shortcut the rule forbids.
        Assert.Throws<QcDomainException>(() => QcRules.ValidateImpact(Impact(), Nothing));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateImpact(Impact(Target("GROUP-1"), Target("GROUP-1")), Nothing));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateImpact(
            Impact(Target("GROUP-1")),
            new HashSet<string> { QcRules.ImpactKey(QcImpactTargetTypes.ResultGroup, "GROUP-1") }));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateImpact(
            Impact(Target("GROUP-1") with { TargetType = "GUESS" }), Nothing));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateImpact(
            Impact(Target("GROUP-1") with { TargetVersion = 0 }), Nothing));
    }

    [Fact]
    public void Gates_are_exactly_the_five_kinds_and_cannot_be_satisfied_twice()
    {
        Assert.Equal(
            [
                QcReleaseGateKinds.Investigation, QcReleaseGateKinds.ImpactScope,
                QcReleaseGateKinds.ValidityDecision, QcReleaseGateKinds.AdoptionRule,
                QcReleaseGateKinds.TechnicalReview
            ],
            QcReleaseGateKinds.Required);
        QcRules.ValidateGate(Gate(QcReleaseGateKinds.Investigation), Nothing);

        Assert.Throws<QcDomainException>(() => QcRules.ValidateGate(Gate("DEVIATION_APPROVAL"), Nothing));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateGate(
            Gate(QcReleaseGateKinds.Investigation),
            new HashSet<string> { QcReleaseGateKinds.Investigation }));
        Assert.Throws<QcDomainException>(() => QcRules.ValidateGate(
            Gate(QcReleaseGateKinds.Investigation) with { EvidenceRef = new QcVersionedReference("INV-1", 0) }, Nothing));
    }

    [Fact]
    public void Deviation_approval_is_not_a_release_gate()
    {
        var run = FailedRun(gateKinds: QcReleaseGateKinds.Required, deviationApprovals: 1);
        var withOnlyDeviation = FailedRun(gateKinds: [], deviationApprovals: 3);

        // RULE-010: approvals accumulate freely and still leave every gate open.
        Assert.Empty(QcRules.OutstandingGates(run));
        Assert.Equal(QcReleaseGateKinds.Required, QcRules.OutstandingGates(withOnlyDeviation));
        Assert.Equal(QcErrorCodes.ReleaseGateIncomplete,
            Assert.Throws<QcDomainException>(() => QcRules.RequireReleasable(withOnlyDeviation)).ErrorCode);
        QcRules.RequireReleasable(run);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Release_is_refused_while_any_single_gate_is_missing(int missingIndex)
    {
        var kinds = QcReleaseGateKinds.Required.Where((_, index) => index != missingIndex).ToList();
        var run = FailedRun(gateKinds: kinds, deviationApprovals: 1);

        var outstanding = QcRules.OutstandingGates(run);
        var exception = Assert.Throws<QcDomainException>(() => QcRules.RequireReleasable(run));

        Assert.Equal(QcReleaseGateKinds.Required[missingIndex], Assert.Single(outstanding));
        Assert.Equal(QcErrorCodes.ReleaseGateIncomplete, exception.ErrorCode);
    }

    [Fact]
    public void Release_is_refused_when_the_impact_scope_was_never_recorded()
    {
        var run = FailedRun(gateKinds: QcReleaseGateKinds.Required, deviationApprovals: 0, impactTargets: []);

        Assert.Equal(QcErrorCodes.ReleaseGateIncomplete,
            Assert.Throws<QcDomainException>(() => QcRules.RequireReleasable(run)).ErrorCode);
    }

    [Fact]
    public void Reportability_blocks_affected_targets_until_release()
    {
        var failed = FailedRun(gateKinds: [QcReleaseGateKinds.Investigation], deviationApprovals: 1);
        var released = failed with { State = QcRunStates.Released };

        var blocked = QcRules.EvaluateReportability(Reportability(failed.Version, "GROUP-1"), failed);
        var allowed = QcRules.EvaluateReportability(Reportability(released.Version, "GROUP-1"), released);
        var outsideScope = QcRules.EvaluateReportability(Reportability(failed.Version, "GROUP-9"), failed);

        Assert.Equal(QcReportabilityDecisions.Blocked, blocked.Decision);
        Assert.Contains(QcReportabilityReasons.QcFailureUnreleased, blocked.ReasonCodes);
        Assert.Equal(4, blocked.OutstandingGates.Count);
        Assert.Equal(QcReportabilityDecisions.Allowed, allowed.Decision);
        Assert.Equal(QcReportabilityDecisions.Blocked, outsideScope.Decision);
        Assert.Contains(QcReportabilityReasons.TargetNotInImpactScope, outsideScope.ReasonCodes);
    }

    [Fact]
    public void Reportability_fails_closed_on_unknown_rule_sets_versions_and_pending_verdicts()
    {
        var failed = FailedRun(gateKinds: [], deviationApprovals: 0);
        var open = failed with { State = QcRunStates.Open };
        var passed = failed with { State = QcRunStates.Passed };

        var unknownRule = QcRules.EvaluateReportability(
            Reportability(failed.Version, "GROUP-1") with { RuleSetVersion = "QC-IMPACT@latest" }, failed);
        var stale = QcRules.EvaluateReportability(Reportability(failed.Version + 7, "GROUP-1"), failed);
        var missing = QcRules.EvaluateReportability(Reportability(1, "GROUP-1"), null);
        var pending = QcRules.EvaluateReportability(Reportability(open.Version, "GROUP-1"), open);
        var passedResult = QcRules.EvaluateReportability(Reportability(passed.Version, "GROUP-1"), passed);

        Assert.Equal(QcReportabilityDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(QcReportabilityReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
        Assert.Equal(QcReportabilityDecisions.Unknown, stale.Decision);
        Assert.Contains(QcReportabilityReasons.VersionMismatch, stale.ReasonCodes);
        Assert.Equal(QcReportabilityDecisions.Blocked, missing.Decision);
        Assert.Contains(QcReportabilityReasons.QcRunRequired, missing.ReasonCodes);
        Assert.Equal(QcReportabilityDecisions.Blocked, pending.Decision);
        Assert.Contains(QcReportabilityReasons.VerdictPending, pending.ReasonCodes);
        Assert.Equal(QcReportabilityDecisions.Allowed, passedResult.Decision);
    }

    private static CreateQcRunRequest Run() => new(
        QcContract.RuleSetVersion,
        new QcObjectContext("LEGAL-A", "LAB-A"),
        "00000000000000000000000000000040",
        2,
        new QcVersionedReference("METHOD-TENSILE", 3),
        new QcVersionedReference("QC-RULESET-TOY", 2));

    private static AddQcResultRequest Result(string verdict) => new(
        1, QcContract.RuleSetVersion, new QcVersionedReference("RULE-BLANK", 1),
        QcControlTypes.Blank, "0.02", verdict, "within blank tolerance");

    private static QcResultEntry Entry(string verdict) => new(
        Guid.NewGuid().ToString("N"), RunId, new QcVersionedReference("RULE-BLANK", 1),
        QcControlTypes.Blank, "0.02", verdict, "basis", "operator-a", Now);

    private static RecordQcImpactRequest Impact(params QcImpactTarget[] targets) =>
        new(2, QcContract.RuleSetVersion, targets);

    private static QcImpactTarget Target(string id) => new(QcImpactTargetTypes.ResultGroup, id, 3);

    private static SatisfyQcReleaseGateRequest Gate(string kind) => new(
        3, QcContract.RuleSetVersion, kind, new QcVersionedReference("EVIDENCE-1", 1));

    private static QcReportabilityRequest Reportability(long expectedVersion, string targetId) => new(
        "group-a", RunId, expectedVersion, QcContract.RuleSetVersion, targetId);

    private static QcRunResult FailedRun(
        IReadOnlyList<string> gateKinds,
        int deviationApprovals,
        IReadOnlyList<string>? impactTargets = null)
    {
        impactTargets ??= ["GROUP-1", "GROUP-2"];
        return new QcRunResult(
            RunId,
            1 + 1 + 1 + impactTargets.Count + gateKinds.Count + deviationApprovals,
            QcRunStates.Failed,
            QcContract.RuleSetVersion,
            new QcObjectContext("LEGAL-A", "LAB-A"),
            "00000000000000000000000000000040", 2,
            "ALLOWED", "BATCH-MANAGEMENT@1.0.0",
            new QcVersionedReference("METHOD-TENSILE", 3),
            new QcVersionedReference("QC-RULESET-TOY", 2),
            [Entry(QcVerdicts.Fail)],
            [.. impactTargets.Select(id => new QcImpactEntry(
                Guid.NewGuid().ToString("N"), RunId, QcImpactTargetTypes.ResultGroup, id, 3, "operator-a", Now))],
            [.. gateKinds.Select(kind => new QcReleaseGateEntry(
                Guid.NewGuid().ToString("N"), RunId, kind, new QcVersionedReference("EVIDENCE-1", 1), "reviewer-a", Now))],
            [.. Enumerable.Range(0, deviationApprovals).Select(index => new QcDeviationApprovalEntry(
                Guid.NewGuid().ToString("N"), RunId, new QcVersionedReference($"DEV-{index}", 1),
                "approved deviation", "quality-lead", Now))],
            null, null, "operator-a", Now);
    }
}
