using OpenLIMS.Contracts.Report;
using OpenLIMS.Modules.Report;
using Xunit;

namespace OpenLIMS.Report.UnitTests;

[Trait("Profile", "report")]
public sealed class ReportRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
    private const string ReportId = "00000000000000000000000000000110";
    private static readonly IReadOnlySet<int> NoLines = new HashSet<int>();
    private static readonly IReadOnlySet<string> NoAttributions = new HashSet<string>();

    [Fact]
    public void Report_requires_the_pinned_rule_set_and_stable_identifiers()
    {
        var normalized = ReportRules.ValidateReport(Report());

        Assert.Equal("RPT-2026-0001", normalized.ReportNumber);
        Assert.Throws<ReportDomainException>(() => ReportRules.ValidateReport(Report() with { RuleSetVersion = "RPT-ISSUANCE@latest" }));
        Assert.Throws<ReportDomainException>(() => ReportRules.ValidateReport(Report() with { ReportNumber = "bad number" }));
        Assert.Throws<ReportDomainException>(() => ReportRules.ValidateReport(null));
    }

    [Fact]
    public void Line_accepts_only_the_five_scope_partitions()
    {
        foreach (var partition in ReportScopePartitions.All)
        {
            var validated = ReportRules.ValidateLine(
                Line() with { ScopePartition = partition }, NoLines, NoAttributions);
            Assert.Equal(partition, validated.ScopePartition);
        }

        Assert.Equal(
            ["ACTUAL_TESTED", "APPROVED_COVERAGE", "NOT_EVALUATED", "CUSTOMER_DECLARED", "LABORATORY_CONCLUSION"],
            ReportScopePartitions.All);
        Assert.Throws<ReportDomainException>(() =>
            ReportRules.ValidateLine(Line() with { ScopePartition = "PROBABLY_FINE" }, NoLines, NoAttributions));
    }

    [Fact]
    public void Line_rejects_reused_line_numbers_and_duplicate_attribution()
    {
        var reusedNumber = Assert.Throws<ReportDomainException>(() =>
            ReportRules.ValidateLine(Line(), new HashSet<int> { 1 }, NoAttributions));
        var duplicate = Assert.Throws<ReportDomainException>(() =>
            ReportRules.ValidateLine(
                Line(), NoLines,
                new HashSet<string> { ReportRules.AttributionKey("SCOPE-LINE-1", "GROUP-1") }));

        Assert.Equal(ReportErrorCodes.ValidationFailed, reusedNumber.ErrorCode);
        Assert.Equal(ReportErrorCodes.DuplicateAttribution, duplicate.ErrorCode);
    }

    [Fact]
    public void Line_rejects_malformed_hashes_and_non_positive_pinned_versions()
    {
        Assert.Throws<ReportDomainException>(() => ReportRules.ValidateLine(
            Line() with { AccreditationRef = new AccreditationScopeReference("ACC-1", 1, "nope") }, NoLines, NoAttributions));
        Assert.Throws<ReportDomainException>(() => ReportRules.ValidateLine(
            Line() with { AccreditationRef = new AccreditationScopeReference("ACC-1", 0, new string('a', 64)) }, NoLines, NoAttributions));
        Assert.Throws<ReportDomainException>(() => ReportRules.ValidateLine(
            Line() with { ExpectedBatchVersion = 0 }, NoLines, NoAttributions));
        Assert.Throws<ReportDomainException>(() => ReportRules.ValidateLine(
            Line() with { QcRuns = [new ReportVersionedReference("QC-RUN-1", 0)] }, NoLines, NoAttributions));
        Assert.Throws<ReportDomainException>(() => ReportRules.ValidateLine(
            Line() with { LineNumber = 0 }, NoLines, NoAttributions));
    }

    [Fact]
    public void A_line_must_cite_at_least_one_qc_run_and_never_the_same_run_twice()
    {
        // BUS-RPT-002: the gate asks every run naming the target, so an
        // uncited run would be an unasked question rather than a pass.
        var none = Assert.Throws<ReportDomainException>(() =>
            ReportRules.ValidateLine(Line() with { QcRuns = [] }, NoLines, NoAttributions));
        var duplicated = Assert.Throws<ReportDomainException>(() =>
            ReportRules.ValidateLine(
                Line() with
                {
                    QcRuns =
                    [
                        new ReportVersionedReference("QC-RUN-1", 6),
                        new ReportVersionedReference("QC-RUN-1", 7)
                    ]
                },
                NoLines, NoAttributions));
        var several = ReportRules.ValidateLine(
            Line() with
            {
                QcRuns =
                [
                    new ReportVersionedReference("QC-RUN-1", 6),
                    new ReportVersionedReference("QC-RUN-2", 2)
                ]
            },
            NoLines, NoAttributions);

        Assert.Equal(ReportErrorCodes.ValidationFailed, none.ErrorCode);
        Assert.Equal(ReportErrorCodes.ValidationFailed, duplicated.ErrorCode);
        Assert.Equal(2, several.QcRuns.Count);
    }

    [Fact]
    public void Issuance_gate_refuses_to_replay_a_decision_taken_before_a_line_was_appended()
    {
        // The evaluation carries one accreditation verdict per line it saw, so
        // a shorter verdict list means a later line was never put to any port.
        var evaluation = new ReportGateEvaluationResult(
            Guid.NewGuid().ToString("N"), ReportId, 3, ReportGateDecisions.Allowed, [],
            [new ReportLineAccreditationVerdict(1, ReportAccreditationStatuses.Accredited, [])],
            "signatory-a", "operator-a", Now);
        var covered = Report(ReportStates.Draft, [LineResult()], [evaluation]);
        var appended = Report(
            ReportStates.Draft,
            [LineResult(), LineResult() with { LineNumber = 2, ScopeLineId = "SCOPE-LINE-2" }],
            [evaluation]);

        var allowed = ReportRules.EvaluateIssuanceGate(GateRequest(covered.Version), covered);
        var stale = ReportRules.EvaluateIssuanceGate(GateRequest(appended.Version), appended);

        Assert.Equal(ReportGateDecisions.Allowed, allowed.Decision);
        Assert.Equal(ReportGateDecisions.Blocked, stale.Decision);
        Assert.Contains(ReportBlockerReasons.GateEvaluationRequired, stale.ReasonCodes);
    }

    [Fact]
    public void Accreditation_passes_only_when_all_six_dimensions_agree()
    {
        var verdict = ReportRules.EvaluateAccreditation(
            LineResult(), Scope(), new SignatoryAuthorityOutcome(true, []), Now);

        Assert.Equal(ReportAccreditationStatuses.Accredited, verdict.Status);
        Assert.Empty(verdict.FailedDimensions);
        Assert.Equal(6, ReportAccreditationDimensions.All.Count);
    }

    [Theory]
    [InlineData(ReportAccreditationDimensions.Site)]
    [InlineData(ReportAccreditationDimensions.MethodVersion)]
    [InlineData(ReportAccreditationDimensions.ProductMatrix)]
    [InlineData(ReportAccreditationDimensions.ParameterRange)]
    [InlineData(ReportAccreditationDimensions.Validity)]
    [InlineData(ReportAccreditationDimensions.Signatory)]
    public void Each_accreditation_dimension_independently_fails_the_line(string dimension)
    {
        var scope = dimension switch
        {
            ReportAccreditationDimensions.Site => Scope() with { SiteId = "LAB-OTHER" },
            ReportAccreditationDimensions.MethodVersion => Scope() with
            {
                Method = new ReportVersionedReference("METHOD-TENSILE", 9)
            },
            ReportAccreditationDimensions.ProductMatrix => Scope() with { ProductMatrix = "METAL" },
            ReportAccreditationDimensions.ParameterRange => Scope() with { ParameterRange = "0-10N" },
            ReportAccreditationDimensions.Validity => Scope() with { ValidUntil = Now.AddDays(-1) },
            _ => Scope() with { AuthorizedSignatories = ["someone-else"] }
        };
        var signatory = dimension == ReportAccreditationDimensions.Signatory
            ? new SignatoryAuthorityOutcome(false, [ReportBlockerReasons.SignatoryNotAuthorized])
            : new SignatoryAuthorityOutcome(true, []);

        var verdict = ReportRules.EvaluateAccreditation(LineResult(), scope, signatory, Now);

        Assert.Equal(ReportAccreditationStatuses.NotAccredited, verdict.Status);
        Assert.Contains(dimension, verdict.FailedDimensions);
    }

    [Fact]
    public void An_unresolvable_accreditation_reference_fails_every_dimension()
    {
        var verdict = ReportRules.EvaluateAccreditation(
            LineResult(), null, new SignatoryAuthorityOutcome(true, []), Now);
        var blocker = ReportRules.AccreditationBlocker(LineResult(), verdict);

        Assert.Equal(ReportAccreditationStatuses.NotAccredited, verdict.Status);
        Assert.Equal(ReportAccreditationDimensions.All, verdict.FailedDimensions);
        Assert.Equal(ReportBlockerReasons.AccreditationReferenceMissing, blocker.ReasonCode);
        Assert.Equal(1, blocker.LineNumber);
    }

    [Fact]
    public void A_line_that_claims_nothing_is_simply_not_accredited_and_never_blocks()
    {
        var verdict = ReportRules.EvaluateAccreditation(
            LineResult() with { ClaimsAccreditation = false }, null, new SignatoryAuthorityOutcome(false, []), Now);

        Assert.Equal(ReportAccreditationStatuses.NotAccredited, verdict.Status);
        Assert.Empty(verdict.FailedDimensions);
    }

    [Fact]
    public void Expired_scope_maps_to_an_expiry_blocker_and_signatory_failure_to_a_signatory_step()
    {
        var expired = ReportRules.EvaluateAccreditation(
            LineResult(), Scope() with { ValidUntil = Now.AddDays(-1) }, new SignatoryAuthorityOutcome(true, []), Now);
        var unauthorized = ReportRules.EvaluateAccreditation(
            LineResult(), Scope(), new SignatoryAuthorityOutcome(false, []), Now);

        Assert.Equal(ReportBlockerReasons.AccreditationExpired,
            ReportRules.AccreditationBlocker(LineResult(), expired).ReasonCode);
        Assert.Equal(ReportNextSteps.AssignAuthorizedSignatory,
            Assert.Single(ReportRules.AccreditationBlocker(LineResult(), unauthorized).AllowedNextSteps));
    }

    [Fact]
    public void A_source_verdict_becomes_a_blocker_unless_it_allowed()
    {
        var allowed = ReportRules.SourceBlocker(
            ReportGateSources.BatchStatus, "BATCH-1", "Batch", ReportGateDecisions.Allowed,
            "BATCH@1", ReportNextSteps.UnfreezeOrReplaceBatch, 1);
        var blocked = ReportRules.SourceBlocker(
            ReportGateSources.BatchStatus, "BATCH-1", "Batch", ReportGateDecisions.Blocked,
            "BATCH@1", ReportNextSteps.UnfreezeOrReplaceBatch, 1);
        var unknown = ReportRules.SourceBlocker(
            ReportGateSources.BatchStatus, "BATCH-1", "Batch", ReportGateDecisions.Unknown,
            "BATCH@1", ReportNextSteps.UnfreezeOrReplaceBatch, 1);

        Assert.Null(allowed);
        Assert.Equal(ReportBlockerReasons.SourceBlocked, blocked!.ReasonCode);
        Assert.Equal(ReportNextSteps.UnfreezeOrReplaceBatch, Assert.Single(blocked.AllowedNextSteps));
        Assert.Equal(ReportBlockerReasons.SourceUnknown, unknown!.ReasonCode);
        Assert.Contains(ReportNextSteps.RetryWhenSourceAvailable, unknown.AllowedNextSteps);
        Assert.Equal("BATCH@1", blocked.RuleSetVersion);
    }

    [Fact]
    public void Trace_blocker_names_every_missing_link()
    {
        Assert.Null(ReportRules.TraceBlocker(LineResult()));

        var missingBatch = ReportRules.TraceBlocker(LineResult() with
        {
            TraceRefs = Trace() with { BatchId = "" }
        });
        var missingSeveral = ReportRules.TraceBlocker(LineResult() with
        {
            AdoptionTargetId = "",
            TraceRefs = Trace() with { AllocationId = "", ReceivedItemId = "" }
        });

        Assert.Equal(ReportBlockerReasons.TraceIncomplete, missingBatch!.ReasonCode);
        Assert.Contains("batchId", missingBatch.ObjectRef, StringComparison.Ordinal);
        Assert.Contains("adoptionTargetId", missingSeveral!.ObjectRef, StringComparison.Ordinal);
        Assert.Contains("allocationId", missingSeveral.ObjectRef, StringComparison.Ordinal);
        Assert.Contains("receivedItemId", missingSeveral.ObjectRef, StringComparison.Ordinal);
    }

    [Fact]
    public void A_laboratory_conclusion_line_blocks_because_conformity_decisions_await_od_034()
    {
        var conclusion = ReportRules.ConformityBlocker(
            LineResult() with { ScopePartition = ReportScopePartitions.LaboratoryConclusion });

        Assert.Equal(ReportBlockerReasons.ConformityDecisionUnavailable, conclusion!.ReasonCode);
        Assert.Equal(ReportNextSteps.AwaitConformityDecisionCapability, Assert.Single(conclusion.AllowedNextSteps));
        foreach (var partition in ReportScopePartitions.All.Where(p => p != ReportScopePartitions.LaboratoryConclusion))
        {
            Assert.Null(ReportRules.ConformityBlocker(LineResult() with { ScopePartition = partition }));
        }
    }

    [Fact]
    public void An_unknown_source_makes_the_whole_decision_unknown()
    {
        var blocked = ReportRules.SourceBlocker(
            ReportGateSources.BatchStatus, "B", "Batch", ReportGateDecisions.Blocked, "v", "step", 1)!;
        var unknown = ReportRules.SourceBlocker(
            ReportGateSources.QcReportability, "Q", "QcRun", ReportGateDecisions.Unknown, "v", "step", 1)!;

        Assert.Equal(ReportGateDecisions.Allowed, ReportRules.ResolveDecision([]));
        Assert.Equal(ReportGateDecisions.Blocked, ReportRules.ResolveDecision([blocked]));
        Assert.Equal(ReportGateDecisions.Unknown, ReportRules.ResolveDecision([blocked, unknown]));
    }

    [Fact]
    public void Issuance_gate_replays_the_latest_evaluation_and_fails_closed_on_every_unknown()
    {
        var report = ReportWithEvaluation(ReportGateDecisions.Allowed);

        var allowed = ReportRules.EvaluateIssuanceGate(GateRequest(report.Version), report);
        var stale = ReportRules.EvaluateIssuanceGate(GateRequest(report.Version + 3), report);
        var unknownRule = ReportRules.EvaluateIssuanceGate(
            GateRequest(report.Version) with { RuleSetVersion = "RPT-ISSUANCE@latest" }, report);
        var missing = ReportRules.EvaluateIssuanceGate(GateRequest(1), null);

        Assert.Equal(ReportGateDecisions.Allowed, allowed.Decision);
        Assert.Equal(ReportGateDecisions.Unknown, stale.Decision);
        Assert.Contains(ReportBlockerReasons.VersionMismatch, stale.ReasonCodes);
        Assert.Equal(ReportGateDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(ReportBlockerReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
        Assert.Equal(ReportGateDecisions.Blocked, missing.Decision);
        Assert.Contains(ReportBlockerReasons.ReportRequired, missing.ReasonCodes);
    }

    [Fact]
    public void Issuance_gate_blocks_a_report_with_no_lines_or_no_evaluation()
    {
        var noLines = Report(ReportStates.Draft, lines: [], evaluations: []);
        var noEvaluation = Report(ReportStates.Draft, lines: [LineResult()], evaluations: []);

        var linesResult = ReportRules.EvaluateIssuanceGate(GateRequest(noLines.Version), noLines);
        var evaluationResult = ReportRules.EvaluateIssuanceGate(GateRequest(noEvaluation.Version), noEvaluation);

        Assert.Equal(ReportGateDecisions.Blocked, linesResult.Decision);
        Assert.Contains(ReportBlockerReasons.LinesRequired, linesResult.ReasonCodes);
        Assert.Equal(ReportGateDecisions.Blocked, evaluationResult.Decision);
        Assert.Contains(ReportBlockerReasons.GateEvaluationRequired, evaluationResult.ReasonCodes);
    }

    private static CreateReportRequest Report() => new(
        ReportContract.RuleSetVersion,
        new ReportObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        "RPT-2026-0001");

    private static AddReportLineRequest Line() => new(
        1, ReportContract.RuleSetVersion, 1, "GROUP-1", 4, "SCOPE-LINE-1",
        ReportScopePartitions.ActualTested, Trace(),
        new AccreditationScopeReference("ACC-CNAS-1", 2, new string('a', 64)),
        Claim(), [new ReportVersionedReference("QC-RUN-1", 6)], "INST-FILE-1", 3, 2, "SCOPE-MATRIX-1", 2, 3, 4);

    private static ReportTraceReferences Trace() => new(
        "BATCH-1", "ALLOC-1", "ITEM-1", new ReportVersionedReference("REQ-SNAPSHOT-1", 1));

    private static AccreditationClaim Claim() => new(
        "LAB-A", new ReportVersionedReference("METHOD-TENSILE", 3), "RIGID-PLASTIC", "0-500N",
        Now.AddYears(1), "signatory-a");

    private static AccreditationScopeSnapshot Scope() => new(
        "LAB-A", new ReportVersionedReference("METHOD-TENSILE", 3), "RIGID-PLASTIC", "0-500N",
        Now.AddYears(1), ["signatory-a"]);

    private static ReportLineResult LineResult() => new(
        "line-1", ReportId, 1, "GROUP-1", 4, "TARGET-1", "RESULT@1.0.0", "SCOPE-LINE-1",
        ReportScopePartitions.ActualTested, Trace(),
        new ReportLineGateReferences([new ReportVersionedReference("QC-RUN-1", 6)], "INST-FILE-1", 3, "SCOPE-MATRIX-1", 2, 2, 3, 4),
        new AccreditationScopeReference("ACC-CNAS-1", 2, new string('a', 64)),
        Claim(), true, null, "operator-a", Now);

    private static ReportIssuanceGateRequest GateRequest(long expectedVersion) => new(
        "group-a", ReportId, expectedVersion, ReportContract.RuleSetVersion);

    private static ReportResult ReportWithEvaluation(string decision) => Report(
        ReportStates.Draft,
        [LineResult()],
        [new ReportGateEvaluationResult(
            Guid.NewGuid().ToString("N"), ReportId, 3, decision, [],
            [new ReportLineAccreditationVerdict(1, ReportAccreditationStatuses.Accredited, [])],
            "signatory-a", "operator-a", Now)]);

    private static ReportResult Report(
        string state,
        IReadOnlyList<ReportLineResult> lines,
        IReadOnlyList<ReportGateEvaluationResult> evaluations) => new(
        ReportId, 1 + lines.Count + evaluations.Count, state, ReportContract.RuleSetVersion,
        new ReportObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        "RPT-2026-0001", lines, evaluations, "operator-a", Now);
}
