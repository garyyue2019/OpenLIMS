using OpenLIMS.Contracts.Report;
using OpenLIMS.Modules.Report;
using Xunit;

namespace OpenLIMS.Report.UnitTests;

[Trait("Profile", "report")]
public sealed class ReportVersionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
    private const string ReportId = "00000000000000000000000000000110";

    [Fact]
    public void The_same_report_always_canonicalises_and_hashes_identically()
    {
        var first = ReportVersionRules.Canonicalize(Report(), 1);
        var second = ReportVersionRules.Canonicalize(Report(), 1);

        Assert.Equal(first, second);
        Assert.Equal(ReportVersionRules.ComputeHash(first), ReportVersionRules.ComputeHash(second));
        Assert.Matches("^[a-f0-9]{64}$", ReportVersionRules.ComputeHash(first));
    }

    [Fact]
    public void Line_order_does_not_change_the_hash_but_line_content_does()
    {
        var ordered = Report(lines: [Line(1), Line(2)]);
        var shuffled = Report(lines: [Line(2), Line(1)]);
        var edited = Report(lines: [Line(1) with { AdoptionTargetId = "adopted-target-9" }, Line(2)]);

        Assert.Equal(
            ReportVersionRules.ComputeHash(ReportVersionRules.Canonicalize(ordered, 1)),
            ReportVersionRules.ComputeHash(ReportVersionRules.Canonicalize(shuffled, 1)));
        Assert.NotEqual(
            ReportVersionRules.ComputeHash(ReportVersionRules.Canonicalize(ordered, 1)),
            ReportVersionRules.ComputeHash(ReportVersionRules.Canonicalize(edited, 1)));
    }

    [Theory]
    [InlineData("scopePartition")]
    [InlineData("groupVersion")]
    [InlineData("scopeLine")]
    [InlineData("batch")]
    [InlineData("accreditationRef")]
    [InlineData("accreditationVerdict")]
    [InlineData("versionNumber")]
    public void Every_signable_field_moves_the_hash(string mutated)
    {
        var baseline = ReportVersionRules.ComputeHash(ReportVersionRules.Canonicalize(Report(), 1));
        var (report, version) = mutated switch
        {
            "scopePartition" => (Report(lines: [Line(1) with { ScopePartition = ReportScopePartitions.NotEvaluated }]), 1),
            "groupVersion" => (Report(lines: [Line(1) with { GroupVersion = 99 }]), 1),
            "scopeLine" => (Report(lines: [Line(1) with { ScopeLineId = "SCOPE-LINE-9" }]), 1),
            "batch" => (Report(lines: [Line(1) with { TraceRefs = Trace() with { BatchId = "BATCH-9" } }]), 1),
            "accreditationRef" => (Report(lines: [Line(1) with
            {
                AccreditationRef = new AccreditationScopeReference("ACC-OTHER", 2, new string('a', 64))
            }]), 1),
            "accreditationVerdict" => (Report(verdictStatus: ReportAccreditationStatuses.NotAccredited), 1),
            _ => (Report(), 2)
        };

        Assert.NotEqual(baseline, ReportVersionRules.ComputeHash(
            ReportVersionRules.Canonicalize(report, version)));
    }

    [Fact]
    public void Issuance_demands_all_three_signing_requirements()
    {
        ReportVersionRules.ValidateIssuance(Issue());

        // SEC-SIGN-001 lists three; missing any one of them is the same refusal.
        Assert.Equal(ReportErrorCodes.SignatureRequirementsUnmet,
            Assert.Throws<ReportDomainException>(() => ReportVersionRules.ValidateIssuance(
                Issue() with { ReauthenticationRef = null! })).ErrorCode);
        Assert.Equal(ReportErrorCodes.SignatureRequirementsUnmet,
            Assert.Throws<ReportDomainException>(() => ReportVersionRules.ValidateIssuance(
                Issue() with { SigningIntent = "   " })).ErrorCode);
        Assert.Equal(ReportErrorCodes.SignatureRequirementsUnmet,
            Assert.Throws<ReportDomainException>(() => ReportVersionRules.ValidateIssuance(
                Issue() with { ExpectedContentHash = "" })).ErrorCode);
        Assert.Equal(ReportErrorCodes.SignatureRequirementsUnmet,
            Assert.Throws<ReportDomainException>(() => ReportVersionRules.ValidateIssuance(
                Issue() with { ReauthenticationRef = new ReportVersionedReference("REAUTH-1", 0) })).ErrorCode);
        Assert.Equal(ReportErrorCodes.ValidationFailed,
            Assert.Throws<ReportDomainException>(() => ReportVersionRules.ValidateIssuance(
                Issue() with { RuleSetVersion = "RPT-ISSUANCE@latest" })).ErrorCode);
    }

    [Fact]
    public void A_changed_content_hash_is_what_makes_an_old_signature_invalid()
    {
        var current = ReportVersionRules.ComputeHash(ReportVersionRules.Canonicalize(Report(), 1));
        var stale = ReportVersionRules.ComputeHash(
            ReportVersionRules.Canonicalize(Report(lines: [Line(1) with { GroupVersion = 5 }]), 1));

        ReportVersionRules.RequireMatchingHash(current, current);
        ReportVersionRules.RequireMatchingHash(current.ToUpperInvariant(), current);
        Assert.Equal(ReportErrorCodes.ContentHashMismatch,
            Assert.Throws<ReportDomainException>(() =>
                ReportVersionRules.RequireMatchingHash(stale, current)).ErrorCode);
    }

    [Fact]
    public void Issuance_requires_an_allowed_gate_that_covered_every_line()
    {
        ReportVersionRules.RequireSatisfiedGate(Report());

        var blockedGate = Report(gateDecision: ReportGateDecisions.Blocked);
        var noGate = Report(withGate: false);
        var staleGate = Report(lines: [Line(1), Line(2)]);

        foreach (var report in new[] { blockedGate, noGate, staleGate })
        {
            Assert.Equal(ReportErrorCodes.IssuanceGateNotSatisfied,
                Assert.Throws<ReportDomainException>(() =>
                    ReportVersionRules.RequireSatisfiedGate(report)).ErrorCode);
        }
    }

    [Fact]
    public void The_five_controlled_actions_are_exactly_the_ones_od_022_names()
    {
        Assert.Equal(
            ["CORRECTION", "SUPPLEMENT", "WITHDRAWAL", "VOID", "SUPERSESSION"],
            ReportControlledActionKinds.All);
        Assert.Equal(["CORRECTION", "SUPPLEMENT"], ReportControlledActionKinds.ProduceNewVersion);
    }

    [Fact]
    public void Correction_and_supplement_require_an_impact_assessment()
    {
        foreach (var kind in ReportControlledActionKinds.ProduceNewVersion)
        {
            ReportVersionRules.ValidateControlledAction(Action(kind) with
            {
                ImpactAssessmentRef = new ReportVersionedReference("IMPACT-1", 1)
            });
            Assert.Equal(ReportErrorCodes.ImpactAssessmentRequired,
                Assert.Throws<ReportDomainException>(() =>
                    ReportVersionRules.ValidateControlledAction(Action(kind))).ErrorCode);
        }
    }

    [Fact]
    public void Supersession_needs_a_new_report_number_and_the_others_must_not_carry_one()
    {
        ReportVersionRules.ValidateControlledAction(
            Action(ReportControlledActionKinds.Supersession) with { SupersedingReportNumber = "RPT-2026-0002" });
        ReportVersionRules.ValidateControlledAction(Action(ReportControlledActionKinds.Withdrawal));
        ReportVersionRules.ValidateControlledAction(Action(ReportControlledActionKinds.Void));

        Assert.Throws<ReportDomainException>(() =>
            ReportVersionRules.ValidateControlledAction(Action(ReportControlledActionKinds.Supersession)));
        Assert.Throws<ReportDomainException>(() =>
            ReportVersionRules.ValidateControlledAction(
                Action(ReportControlledActionKinds.Withdrawal) with { SupersedingReportNumber = "RPT-2026-0002" }));
        Assert.Throws<ReportDomainException>(() =>
            ReportVersionRules.ValidateControlledAction(
                Action(ReportControlledActionKinds.Withdrawal) with
                {
                    ImpactAssessmentRef = new ReportVersionedReference("IMPACT-1", 1)
                }));
        Assert.Throws<ReportDomainException>(() =>
            ReportVersionRules.ValidateControlledAction(Action("SHRED")));
        Assert.Throws<ReportDomainException>(() =>
            ReportVersionRules.ValidateControlledAction(Action(ReportControlledActionKinds.Void) with { Reason = " " }));
    }

    [Fact]
    public void A_blank_superseding_number_is_rejected_by_the_rule_not_by_the_database()
    {
        // The CHECK constraint reads "absent" as NULL, so a whitespace-only
        // value on a withdrawal or void is a validation failure — it must not
        // slip past the rule and come back as a persistence outage.
        foreach (var blank in new[] { "", " " })
        {
            foreach (var kind in new[] { ReportControlledActionKinds.Withdrawal, ReportControlledActionKinds.Void })
            {
                Assert.Equal(ReportErrorCodes.ValidationFailed,
                    Assert.Throws<ReportDomainException>(() =>
                        ReportVersionRules.ValidateControlledAction(
                            Action(kind) with { SupersedingReportNumber = blank })).ErrorCode);
            }

            Assert.Equal(ReportErrorCodes.ValidationFailed,
                Assert.Throws<ReportDomainException>(() =>
                    ReportVersionRules.ValidateControlledAction(
                        Action(ReportControlledActionKinds.Supersession) with
                        {
                            SupersedingReportNumber = blank
                        })).ErrorCode);
        }
    }

    [Fact]
    public void Actions_only_apply_to_issued_versions_on_a_live_chain()
    {
        var live = Chain(issued: [1]);
        var voided = Chain(issued: [1], chainState: ReportChainStates.Voided);
        var withdrawn = Chain(issued: [1], withdrawn: [1]);
        var superseded = Chain(issued: [1, 2], superseded: [1]);

        ReportVersionRules.RequireActionable(live, Action(ReportControlledActionKinds.Withdrawal));

        Assert.Equal(ReportErrorCodes.VersionChainClosed,
            Assert.Throws<ReportDomainException>(() => ReportVersionRules.RequireActionable(
                voided, Action(ReportControlledActionKinds.Withdrawal))).ErrorCode);
        Assert.Equal(ReportErrorCodes.VersionNotIssued,
            Assert.Throws<ReportDomainException>(() => ReportVersionRules.RequireActionable(
                live, Action(ReportControlledActionKinds.Withdrawal) with { VersionNumber = 7 })).ErrorCode);
        Assert.Equal(ReportErrorCodes.ValidationFailed,
            Assert.Throws<ReportDomainException>(() => ReportVersionRules.RequireActionable(
                withdrawn, Action(ReportControlledActionKinds.Withdrawal))).ErrorCode);
        Assert.Equal(ReportErrorCodes.ValidationFailed,
            Assert.Throws<ReportDomainException>(() => ReportVersionRules.RequireActionable(
                superseded, Action(ReportControlledActionKinds.Correction) with
                {
                    ImpactAssessmentRef = new ReportVersionedReference("IMPACT-1", 1)
                })).ErrorCode);
    }

    [Fact]
    public void A_chain_is_superseded_at_most_once()
    {
        // BUS-RPT-005 forbids repeating a controlled action, and the
        // verification page carries a single superseding report number. A
        // duplicate would be a permanent row in an append-only evidence log.
        var supersession = Action(ReportControlledActionKinds.Supersession) with
        {
            SupersedingReportNumber = "RPT-2026-0009"
        };

        ReportVersionRules.RequireActionable(Chain(issued: [1]), supersession);

        var alreadySuperseded = Chain(issued: [1, 2], supersedingReportNumber: "RPT-2026-0003");
        Assert.Equal(ReportErrorCodes.ValidationFailed,
            Assert.Throws<ReportDomainException>(() =>
                ReportVersionRules.RequireActionable(alreadySuperseded, supersession)).ErrorCode);
        Assert.Equal(ReportErrorCodes.ValidationFailed,
            Assert.Throws<ReportDomainException>(() =>
                ReportVersionRules.RequireActionable(
                    alreadySuperseded, supersession with { VersionNumber = 2 })).ErrorCode);
    }

    [Fact]
    public void Version_state_and_current_version_follow_the_action_log()
    {
        var fresh = Chain(issued: [1]);
        var corrected = Chain(issued: [1, 2], superseded: [1]);
        var withdrawnLatest = Chain(issued: [1, 2], superseded: [1], withdrawn: [2]);
        var voided = Chain(issued: [1, 2], superseded: [1], chainState: ReportChainStates.Voided);

        Assert.Equal(ReportVersionStates.Issued, ReportVersionRules.ResolveVersionState(1, fresh));
        Assert.Equal(1, ReportVersionRules.ResolveCurrentVersion(fresh));

        Assert.Equal(ReportVersionStates.Superseded, ReportVersionRules.ResolveVersionState(1, corrected));
        Assert.Equal(ReportVersionStates.Issued, ReportVersionRules.ResolveVersionState(2, corrected));
        Assert.Equal(2, ReportVersionRules.ResolveCurrentVersion(corrected));

        Assert.Equal(ReportVersionStates.Withdrawn, ReportVersionRules.ResolveVersionState(2, withdrawnLatest));
        Assert.Null(ReportVersionRules.ResolveCurrentVersion(withdrawnLatest));

        Assert.Equal(ReportVersionStates.Voided, ReportVersionRules.ResolveVersionState(1, voided));
        Assert.Null(ReportVersionRules.ResolveCurrentVersion(voided));
    }

    [Fact]
    public void Version_chain_port_pins_the_current_version_and_fails_closed()
    {
        var chain = Chain(issued: [1, 2], superseded: [1]);
        const string hash = "abc";

        var allowed = ReportVersionRules.EvaluateChain(Request(2), chain, hash);
        var stale = ReportVersionRules.EvaluateChain(Request(1), chain, hash);
        var unknownRule = ReportVersionRules.EvaluateChain(
            Request(2) with { RuleSetVersion = "RPT-ISSUANCE@latest" }, chain, hash);
        var never = ReportVersionRules.EvaluateChain(Request(1), Chain(issued: []), null);
        var voided = ReportVersionRules.EvaluateChain(
            Request(1), Chain(issued: [1], chainState: ReportChainStates.Voided), hash);

        Assert.Equal(ReportVersionChainDecisions.Allowed, allowed.Decision);
        Assert.Equal(2, allowed.CurrentVersionNumber);
        Assert.Equal(hash, allowed.ContentHash);
        Assert.Equal(ReportVersionChainDecisions.Unknown, stale.Decision);
        Assert.Contains(ReportVersionChainReasons.VersionMismatch, stale.ReasonCodes);
        Assert.Equal(ReportVersionChainDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(ReportVersionChainReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
        Assert.Equal(ReportVersionChainDecisions.Blocked, never.Decision);
        Assert.Contains(ReportVersionChainReasons.NoIssuedVersion, never.ReasonCodes);
        Assert.Equal(ReportVersionChainDecisions.Blocked, voided.Decision);
        Assert.Contains(ReportVersionChainReasons.ChainVoided, voided.ReasonCodes);
    }

    private static IssueReportRequest Issue() => new(
        3, ReportContract.RuleSetVersion, new ReportVersionedReference("REAUTH-1", 1),
        "I approve and sign this report", new string('a', 64), "signatory-a");

    private static PerformControlledActionRequest Action(string kind) => new(
        4, ReportContract.RuleSetVersion, 1, kind, "per SOP");

    private static ReportVersionChainRequest Request(int expected) => new(
        "group-a", ReportId, expected, ReportContract.RuleSetVersion);

    private static ReportVersionChainState Chain(
        IReadOnlyList<int> issued,
        IReadOnlyList<int>? withdrawn = null,
        IReadOnlyList<int>? superseded = null,
        string chainState = ReportChainStates.Active,
        string? supersedingReportNumber = null) => new(
        chainState,
        new HashSet<int>(issued),
        new HashSet<int>(withdrawn ?? []),
        new HashSet<int>(superseded ?? []),
        supersedingReportNumber);

    private static ReportTraceReferences Trace() => new(
        "BATCH-1", "ALLOC-1", "ITEM-1", new ReportVersionedReference("REQ-SNAPSHOT-1", 1));

    private static ReportLineResult Line(int number) => new(
        $"line-{number}", ReportId, number, $"GROUP-{number}", 4, $"adopted-target-{number}", "RESULT@1.0.0",
        $"SCOPE-LINE-{number}", ReportScopePartitions.ActualTested, Trace(),
        new ReportLineGateReferences(
            [new ReportVersionedReference("QC-RUN-1", 6)], "INST-FILE-1", 3, "SCOPE-MATRIX-1", 2, 2, 3, 4),
        new AccreditationScopeReference("ACC-CNAS-1", 2, new string('a', 64)),
        new AccreditationClaim(
            "CNAS-L1234-S1", new ReportVersionedReference("METHOD-TENSILE", 3), "RIGID-PLASTIC", "0-500N",
            Now.AddYears(1), "signatory-a"),
        true, null, "operator-a", Now);

    private static ReportResult Report(
        IReadOnlyList<ReportLineResult>? lines = null,
        bool withGate = true,
        string gateDecision = ReportGateDecisions.Allowed,
        string verdictStatus = ReportAccreditationStatuses.Accredited)
    {
        lines ??= [Line(1)];
        // The gate carries one verdict per line it saw, which is what makes a
        // stale evaluation detectable.
        var evaluations = withGate
            ? new List<ReportGateEvaluationResult>
            {
                new(Guid.NewGuid().ToString("N"), ReportId, 2, gateDecision, [],
                    [new ReportLineAccreditationVerdict(1, verdictStatus, [])],
                    "signatory-a", "operator-a", Now)
            }
            : [];
        return new ReportResult(
            ReportId, 1 + lines.Count + evaluations.Count, ReportStates.PendingApproval,
            ReportContract.RuleSetVersion,
            new ReportObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
            "RPT-2026-0001", lines, evaluations, "operator-a", Now);
    }
}
