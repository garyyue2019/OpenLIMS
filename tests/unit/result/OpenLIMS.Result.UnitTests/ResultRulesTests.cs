using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Result;
using OpenLIMS.Modules.Result;
using Xunit;

namespace OpenLIMS.Result.UnitTests;

[Trait("Profile", "result")]
public sealed class ResultRulesTests
{
    private static readonly string ObservationA = Guid.Parse("00000000-0000-0000-0000-000000000061").ToString("N");
    private static readonly string ObservationRetest = Guid.Parse("00000000-0000-0000-0000-000000000062").ToString("N");
    private static readonly string DerivationA = Guid.Parse("00000000-0000-0000-0000-000000000063").ToString("N");
    private static readonly string CalculationA = Guid.Parse("00000000-0000-0000-0000-000000000064").ToString("N");
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Initial_observation_forbids_trigger_and_approval_while_others_require_them()
    {
        var initial = ResultRules.ValidateObservation(Observation(ResultObservationKinds.Initial), false);
        var missingTrigger = Assert.Throws<ResultDomainException>(() =>
            ResultRules.ValidateObservation(Observation(ResultObservationKinds.Duplicate), false));
        var duplicate = ResultRules.ValidateObservation(
            Observation(ResultObservationKinds.Duplicate) with
            {
                TriggerReason = "duplicate check",
                ApprovalRef = new ResultVersionedReference("APPROVAL-1", 1)
            }, false);

        Assert.Equal(ResultObservationKinds.Initial, initial.Kind);
        Assert.Equal(ResultErrorCodes.ValidationFailed, missingTrigger.ErrorCode);
        Assert.Equal(ResultObservationKinds.Duplicate, duplicate.Kind);
    }

    [Fact]
    public void Retest_observation_requires_pre_recorded_adoption_rule()
    {
        var retest = Observation(ResultObservationKinds.Retest) with
        {
            TriggerReason = "qc deviation",
            ApprovalRef = new ResultVersionedReference("APPROVAL-1", 1)
        };
        var withoutRule = Assert.Throws<ResultDomainException>(() =>
            ResultRules.ValidateObservation(retest, adoptionRuleExists: false));
        var withRule = ResultRules.ValidateObservation(retest, adoptionRuleExists: true);

        Assert.Equal(ResultErrorCodes.AdoptionRuleRequired, withoutRule.ErrorCode);
        Assert.Equal(ResultObservationKinds.Retest, withRule.Kind);
    }

    [Fact]
    public void Evidence_requires_known_source_sha256_and_parser_version()
    {
        var badSha = Assert.Throws<ResultDomainException>(() =>
            ResultRules.ValidateObservation(Observation(ResultObservationKinds.Initial) with
            {
                Evidence = Evidence() with { Sha256 = "xyz" }
            }, false));
        var badSource = Assert.Throws<ResultDomainException>(() =>
            ResultRules.ValidateObservation(Observation(ResultObservationKinds.Initial) with
            {
                Evidence = Evidence() with { SourceSystem = "USB" }
            }, false));

        Assert.Equal(ResultErrorCodes.ValidationFailed, badSha.ErrorCode);
        Assert.Equal(ResultErrorCodes.ValidationFailed, badSource.ErrorCode);
    }

    [Fact]
    public void Derivation_rejects_dangling_duplicate_and_rationale_free_excluded_inputs()
    {
        var existing = new HashSet<string>(StringComparer.Ordinal) { ObservationA };
        var valid = ResultRules.ValidateDerivation(Derivation(
            new ResultDerivationInput(ObservationA, true)), existing);
        var dangling = Assert.Throws<ResultDomainException>(() =>
            ResultRules.ValidateDerivation(Derivation(
                new ResultDerivationInput(ObservationRetest, true)), existing));
        var duplicated = Assert.Throws<ResultDomainException>(() =>
            ResultRules.ValidateDerivation(Derivation(
                new ResultDerivationInput(ObservationA, true),
                new ResultDerivationInput(ObservationA, false, "dup")), existing));
        var noRationale = Assert.Throws<ResultDomainException>(() =>
            ResultRules.ValidateDerivation(Derivation(
                new ResultDerivationInput(ObservationA, false)), existing));

        Assert.Single(valid.Inputs);
        Assert.Equal(ResultErrorCodes.ValidationFailed, dangling.ErrorCode);
        Assert.Equal(ResultErrorCodes.ValidationFailed, duplicated.ErrorCode);
        Assert.Equal(ResultErrorCodes.ValidationFailed, noRationale.ErrorCode);
    }

    [Fact]
    public void Calculation_is_deterministic_and_preserves_rule_inputs_rounding_and_limit_decision()
    {
        var execution = ResultRules.ExecuteCalculation(
            CalculationRequest(ObservationA, CalculationRule() with
            {
                DilutionFactor = 2m,
                DecimalPlaces = 1,
                RoundingMode = ResultRoundingModes.AwayFromZero,
                Lod = 1m,
                Loq = 2m,
                LimitOperator = ResultLimitOperators.LessThanOrEqual,
                UpperLimit = 25m
            }),
            Group());

        Assert.Equal(25m, execution.ExactValue);
        Assert.Equal(25m, execution.RoundedValue);
        Assert.Equal("25", execution.ReportedValue);
        Assert.Equal(ResultDetectionQualifications.Quantified, execution.Qualification);
        Assert.Equal(ResultLimitDecisions.Pass, execution.LimitDecision);
        Assert.Equal(ObservationA, Assert.Single(execution.Inputs).TargetId);
        Assert.Equal("CALC-1", execution.Rule.CalculationRule.Id);
    }

    [Fact]
    public void Calculation_fails_closed_for_unit_mismatch_invalid_detection_or_unquantified_limit()
    {
        var belowLoq = ResultRules.ExecuteCalculation(
            CalculationRequest(ObservationA, CalculationRule() with
            {
                UnitMultiplier = 0.1m,
                Lod = 1m,
                Loq = 2m,
                LimitOperator = ResultLimitOperators.LessThanOrEqual,
                UpperLimit = 3m
            }),
            Group());
        var unitMismatch = Assert.Throws<ResultDomainException>(() =>
            ResultRules.ExecuteCalculation(
                CalculationRequest(ObservationA, CalculationRule() with { InputUnit = "UG-KG" }),
                Group()));
        var invalidDetection = Assert.Throws<ResultDomainException>(() =>
            ResultRules.ExecuteCalculation(
                CalculationRequest(ObservationA, CalculationRule() with { Lod = 5m, Loq = 4m }),
                Group()));

        Assert.Equal(ResultDetectionQualifications.BelowLoq, belowLoq.Qualification);
        Assert.Equal("<LOQ", belowLoq.ReportedValue);
        Assert.Equal(ResultLimitDecisions.Unknown, belowLoq.LimitDecision);
        Assert.Equal(ResultErrorCodes.CalculationFailed, unitMismatch.ErrorCode);
        Assert.Equal(ResultErrorCodes.ValidationFailed, invalidDetection.ErrorCode);
    }

    [Fact]
    public void Retest_replaces_original_strategy_blocks_adopting_favorable_initial()
    {
        var group = Group();
        var rule = Rule(ResultAdoptionStrategies.RetestReplacesOriginal);
        var adoptInitial = Adoption(ObservationA);
        var adoptRetest = Adoption(ObservationRetest);
        var adoptDerivationWithRetest = Adoption(DerivationA);

        var violation = Assert.Throws<ResultDomainException>(() =>
            ResultRules.RequireStrategyCompliance(adoptInitial, rule, group));
        ResultRules.RequireStrategyCompliance(adoptRetest, rule, group);
        ResultRules.RequireStrategyCompliance(adoptDerivationWithRetest, rule, group);

        Assert.Equal(ResultErrorCodes.AdoptionStrategyViolation, violation.ErrorCode);
    }

    [Fact]
    public void Technical_review_strategy_requires_review_approval_reference()
    {
        var group = Group();
        var rule = Rule(ResultAdoptionStrategies.TechnicalReviewSelects);

        var violation = Assert.Throws<ResultDomainException>(() =>
            ResultRules.RequireStrategyCompliance(Adoption(ObservationA), rule, group));
        ResultRules.RequireStrategyCompliance(
            Adoption(ObservationA) with { ReviewApprovalRef = new ResultVersionedReference("REVIEW-1", 1) },
            rule, group);

        Assert.Equal(ResultErrorCodes.AdoptionStrategyViolation, violation.ErrorCode);
    }

    [Fact]
    public void Retest_strategy_allows_a_calculation_that_includes_the_latest_retest()
    {
        var group = Group() with
        {
            Calculations =
            [
                new ResultCalculationResult(
                    CalculationA, Group().ResultGroupId, 5,
                    [new ResultCalculationResolvedInput(ObservationRetest, 11.9m, "MG-KG", 1m)],
                    CalculationRule(), 11.9m, 11.9m, "11.9", "MG-KG",
                    ResultDetectionQualifications.Quantified, ResultLimitDecisions.NotEvaluated,
                    "a", Now)
            ]
        };

        ResultRules.RequireStrategyCompliance(
            Adoption(CalculationA), Rule(ResultAdoptionStrategies.RetestReplacesOriginal), group);
    }

    [Fact]
    public void Accreditation_requires_compatible_execution_and_result_evidence()
    {
        var adopted = Group() with
        {
            Version = 6,
            Adoptions =
            [
                new ResultAdoptionResult(
                    Group().ResultGroupId, 6, 1, ObservationRetest, 1, null, "a", Now)
            ]
        };
        var execution = ResultRules.EvaluateAccreditationAssessment(
            AccreditationRequest(ResultAccreditationStages.Execution, null, 6), adopted, "a", Now);
        var result = ResultRules.EvaluateAccreditationAssessment(
            AccreditationRequest(ResultAccreditationStages.Result, ObservationRetest, 7), adopted, "a", Now);
        var withEvidence = adopted with
        {
            Version = 8,
            AccreditationAssessments =
            [
                Assessment("00000000000000000000000000000081", 7, execution),
                Assessment("00000000000000000000000000000082", 8, result)
            ]
        };

        var eligibility = ResultRules.EvaluateAccreditationEligibility(
            new ResultAccreditationEligibilityRequest(
                "group-a", withEvidence.ResultGroupId, 8, ResultContract.AccreditationRuleSetVersion),
            withEvidence,
            "a",
            Now);

        Assert.Equal(ResultAccreditationDecisions.Eligible, execution.Decision);
        Assert.Equal(ResultAccreditationDecisions.Eligible, result.Decision);
        Assert.Equal(ResultAccreditationDecisions.Eligible, eligibility.Decision);
        Assert.Empty(eligibility.ReasonCodes);
    }

    [Fact]
    public void Expired_or_mismatched_accreditation_blocks_eligibility()
    {
        var adopted = Group() with
        {
            Version = 6,
            Adoptions =
            [
                new ResultAdoptionResult(
                    Group().ResultGroupId, 6, 1, ObservationRetest, 1, null, "a", Now)
            ]
        };
        var blockedExecution = ResultRules.EvaluateAccreditationAssessment(
            AccreditationRequest(ResultAccreditationStages.Execution, null, 6) with
            {
                SiteId = "LAB-B",
                ValidTo = new DateOnly(2026, 8, 4)
            }, adopted, "a", Now);
        var result = ResultRules.EvaluateAccreditationAssessment(
            AccreditationRequest(ResultAccreditationStages.Result, ObservationRetest, 7), adopted, "a", Now);
        var withEvidence = adopted with
        {
            Version = 8,
            AccreditationAssessments =
            [
                Assessment("00000000000000000000000000000083", 7, blockedExecution),
                Assessment("00000000000000000000000000000084", 8, result)
            ]
        };

        var eligibility = ResultRules.EvaluateAccreditationEligibility(
            new ResultAccreditationEligibilityRequest(
                "group-a", withEvidence.ResultGroupId, 8, ResultContract.AccreditationRuleSetVersion),
            withEvidence,
            "a",
            Now);

        Assert.Equal(ResultAccreditationDecisions.Blocked, blockedExecution.Decision);
        Assert.Contains(ResultAccreditationReasons.SiteMismatch, blockedExecution.ReasonCodes);
        Assert.Contains(ResultAccreditationReasons.Expired, blockedExecution.ReasonCodes);
        Assert.Equal(ResultAccreditationDecisions.Blocked, eligibility.Decision);
        Assert.Contains(ResultAccreditationEligibilityReasons.AssessmentBlocked, eligibility.ReasonCodes);
        Assert.Contains(ResultAccreditationEligibilityReasons.AssessmentExpired, eligibility.ReasonCodes);
    }

    [Fact]
    public void Accreditation_eligibility_blocks_a_current_actor_outside_the_result_authorization_list()
    {
        var adopted = Group() with
        {
            Version = 6,
            Adoptions =
            [
                new ResultAdoptionResult(
                    Group().ResultGroupId, 6, 1, ObservationRetest, 1, null, "a", Now)
            ]
        };
        var execution = ResultRules.EvaluateAccreditationAssessment(
            AccreditationRequest(ResultAccreditationStages.Execution, null, 6), adopted, "a", Now);
        var result = ResultRules.EvaluateAccreditationAssessment(
            AccreditationRequest(ResultAccreditationStages.Result, ObservationRetest, 7), adopted, "a", Now);
        var withEvidence = adopted with
        {
            Version = 8,
            AccreditationAssessments =
            [
                Assessment("00000000000000000000000000000085", 7, execution),
                Assessment("00000000000000000000000000000086", 8, result)
            ]
        };

        var eligibility = ResultRules.EvaluateAccreditationEligibility(
            new ResultAccreditationEligibilityRequest(
                "group-a", withEvidence.ResultGroupId, 8, ResultContract.AccreditationRuleSetVersion),
            withEvidence,
            "signer-b",
            Now);

        Assert.Equal(ResultAccreditationDecisions.Blocked, eligibility.Decision);
        Assert.Contains(ResultAccreditationEligibilityReasons.CurrentActorUnauthorized, eligibility.ReasonCodes);
    }

    [Fact]
    public void Status_requires_adoption_and_pins_group_version_and_rule_set()
    {
        var group = Group();
        var withAdoption = group with
        {
            Adoptions = [new ResultAdoptionResult(group.ResultGroupId, 5, 1, ObservationRetest, 1, null, "a", DateTimeOffset.MinValue)],
            Version = 5
        };

        var noAdoption = ResultRules.EvaluateStatus(Status(group.Version), group);
        var allowed = ResultRules.EvaluateStatus(Status(5), withAdoption);
        var stale = ResultRules.EvaluateStatus(Status(4), withAdoption);
        var unknownRule = ResultRules.EvaluateStatus(
            Status(5) with { RuleSetVersion = "RESULT-ADOPTION@latest" }, withAdoption);
        var missing = ResultRules.EvaluateStatus(Status(1), null);

        Assert.Equal(ResultAdoptionDecisions.Blocked, noAdoption.Decision);
        Assert.Contains(ResultAdoptionReasons.AdoptionRequired, noAdoption.ReasonCodes);
        Assert.Equal(ResultAdoptionDecisions.Allowed, allowed.Decision);
        Assert.Equal(ObservationRetest, allowed.EffectiveTargetId);
        Assert.Equal(ResultAdoptionDecisions.Unknown, stale.Decision);
        Assert.Equal(ResultAdoptionDecisions.Unknown, unknownRule.Decision);
        Assert.Equal(ResultAdoptionDecisions.Blocked, missing.Decision);
    }

    [Fact]
    public void Conclusion_evidence_resolves_the_adopted_target_recorder_and_scope()
    {
        var group = Group() with
        {
            Version = 5,
            Adoptions =
            [
                new ResultAdoptionResult(
                    Group().ResultGroupId,
                    5,
                    1,
                    ObservationRetest,
                    1,
                    null,
                    "adopter",
                    DateTimeOffset.MinValue)
            ]
        };
        var request = new ResultConclusionEvidenceRequest(
            "group-a",
            group.ResultGroupId,
            1,
            ResultContract.RuleSetVersion);

        var allowed = ResultRules.EvaluateConclusionEvidence(request, group);
        var missingAdoption = ResultRules.EvaluateConclusionEvidence(
            request with { AdoptionVersion = 9 }, group);
        var unknownRule = ResultRules.EvaluateConclusionEvidence(
            request with { RuleSetVersion = "RESULT-ADOPTION@latest" }, group);
        var missingGroup = ResultRules.EvaluateConclusionEvidence(request, null);

        Assert.Equal(ResultConclusionEvidenceDecisions.Allowed, allowed.Decision);
        Assert.Equal(ObservationRetest, allowed.TargetId);
        Assert.Equal(ResultObservationKinds.Retest, allowed.TargetKind);
        Assert.Equal("a", allowed.RecordedBy);
        Assert.Equal(group.ObjectScope, allowed.ObjectScope);
        Assert.Equal(ResultConclusionEvidenceDecisions.Blocked, missingAdoption.Decision);
        Assert.Contains(ResultConclusionEvidenceReasons.AdoptionVersionMissing, missingAdoption.ReasonCodes);
        Assert.Equal(ResultConclusionEvidenceDecisions.Unknown, unknownRule.Decision);
        Assert.Equal(ResultConclusionEvidenceDecisions.Unknown, missingGroup.Decision);
    }

    [Fact]
    public void Conclusion_evidence_is_unknown_when_the_adopted_target_cannot_be_rebuilt()
    {
        var group = Group() with
        {
            Version = 5,
            Adoptions =
            [
                new ResultAdoptionResult(
                    Group().ResultGroupId,
                    5,
                    1,
                    Guid.Parse("00000000-0000-0000-0000-000000000099").ToString("N"),
                    1,
                    null,
                    "adopter",
                    DateTimeOffset.MinValue)
            ]
        };

        var result = ResultRules.EvaluateConclusionEvidence(
            new ResultConclusionEvidenceRequest(
                "group-a",
                group.ResultGroupId,
                1,
                ResultContract.RuleSetVersion),
            group);

        Assert.Equal(ResultConclusionEvidenceDecisions.Unknown, result.Decision);
        Assert.Contains(ResultConclusionEvidenceReasons.TargetUnavailable, result.ReasonCodes);
        Assert.Null(result.RecordedBy);
    }

    [Fact]
    public async Task Authorization_requires_all_exact_scope_claims()
    {
        var context = new DefaultHttpContext { User = Principal(includeProductCategory: true) };
        var port = new HttpClaimsResultAuthorizationPort(new HttpContextAccessor { HttpContext = context });

        var allowed = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);
        context.User = Principal(includeProductCategory: false);
        var denied = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    private static AddResultObservationRequest Observation(string kind) => new(
        2, ResultContract.RuleSetVersion, kind, "12.5", "MG-KG", Evidence());

    private static ResultEvidence Evidence() => new(
        ResultEvidenceSources.Cds,
        new ResultVersionedReference("CDS-SEQ-1", 1),
        new string('a', 64),
        "PARSER-2.1");

    private static AddResultDerivationRequest Derivation(params ResultDerivationInput[] inputs) => new(
        3, ResultContract.RuleSetVersion,
        new ResultVersionedReference("AGG-MEAN", 1), "12.7", "MG-KG", inputs);

    private static ExecuteResultCalculationRequest CalculationRequest(
        string targetId,
        ResultCalculationRule rule) => new(
        4,
        ResultContract.CalculationRuleSetVersion,
        [new ResultCalculationInput(targetId, 1m)],
        rule);

    private static ResultCalculationRule CalculationRule() => new(
        new ResultVersionedReference("CALC-1", 1),
        new ResultVersionedReference("UNIT-1", 1),
        "MG-KG",
        "MG-KG",
        1m,
        0m,
        1m,
        1m,
        2,
        ResultRoundingModes.ToEven,
        null,
        null,
        ResultLimitOperators.None,
        ResultLimitEvaluationBases.Exact,
        null,
        null);

    private static RecordResultAccreditationAssessmentRequest AccreditationRequest(
        string stage,
        string? targetId,
        long expectedVersion) => new(
        expectedVersion,
        ResultContract.AccreditationRuleSetVersion,
        stage,
        targetId,
        new ResultVersionedReference("ACC-1", 2),
        new ResultVersionedReference("METHOD-1", 3),
        "LAB-A",
        "TOYS",
        "ITEM-PB",
        "MG-KG",
        0m,
        20m,
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 12, 31),
        ["a"]);

    private static ResultAccreditationAssessmentResult Assessment(
        string id,
        long groupVersion,
        ResultAccreditationEvaluation evaluation)
    {
        var request = evaluation.Request;
        return new ResultAccreditationAssessmentResult(
            id,
            Group().ResultGroupId,
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
            "a",
            Now);
    }

    private static AdoptResultRequest Adoption(string targetId) => new(
        4, ResultContract.RuleSetVersion, targetId);

    private static AdoptionRuleResult Rule(string strategy) => new(
        "g", 3, 1, strategy, new ResultVersionedReference("RULE-1", 1), "a", DateTimeOffset.MinValue);

    private static ResultGroupResult Group() => new(
        Guid.Parse("00000000-0000-0000-0000-000000000060").ToString("N"), 4,
        ResultContract.RuleSetVersion,
        new ResultObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        "BATCH-1", 2, "ALLOWED", "BATCH-EXECUTION@1.0.0", "MEMBER-1",
        new ResultVersionedReference("ITEM-PB", 1), new string('c', 64),
        [
            new ResultObservationResult(ObservationA, "g", 2, ResultObservationKinds.Initial,
                "12.5", "MG-KG", EvidenceResult(), null, null, "a", DateTimeOffset.MinValue),
            new ResultObservationResult(ObservationRetest, "g", 4, ResultObservationKinds.Retest,
                "11.9", "MG-KG", EvidenceResult(), "qc", new ResultVersionedReference("AP-1", 1), "a", DateTimeOffset.MinValue)
        ],
        [
            new ResultDerivationResult(DerivationA, "g", 5,
                new ResultVersionedReference("AGG-MEAN", 1), "12.2", "MG-KG",
                [new ResultDerivationInput(ObservationRetest, true)], "a", DateTimeOffset.MinValue)
        ],
        [Rule(ResultAdoptionStrategies.RetestReplacesOriginal)],
        [],
        "a", DateTimeOffset.MinValue);

    private static ResultEvidence EvidenceResult() => new(
        ResultEvidenceSources.Cds, new ResultVersionedReference("CDS-SEQ-1", 1), new string('a', 64), "PARSER-2.1");

    private static ResultAdoptionStatusRequest Status(long expectedVersion) => new(
        "group-a", Guid.Parse("00000000-0000-0000-0000-000000000060").ToString("N"),
        expectedVersion, ResultContract.RuleSetVersion);

    private static ResultAuthorizationRequest AuthRequest() => new(
        "group-a", "actor-a",
        new ResultObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        ResultCapabilities.Record);

    private static ClaimsPrincipal Principal(bool includeProductCategory)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "actor-a"),
            new("organization_group", "group-a"),
            new(ResultClaimTypes.Capability, ResultCapabilities.Record),
            new(ResultClaimTypes.LegalEntity, "LEGAL-A"),
            new(ResultClaimTypes.Laboratory, "LAB-A"),
            new(ResultClaimTypes.Customer, "CUSTOMER-A"),
            new(ResultClaimTypes.ServiceOrder, "ORDER-A")
        };
        if (includeProductCategory) claims.Add(new Claim(ResultClaimTypes.ProductCategory, "TOYS"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
