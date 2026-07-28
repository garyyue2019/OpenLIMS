using OpenLIMS.Contracts.Textile;
using OpenLIMS.Modules.Textile;
using Xunit;

namespace OpenLIMS.Textile.UnitTests;

[Trait("Profile", "textile")]
public sealed class TextileRuntimeDomainTests
{
    [Fact]
    public void CalculateRequirement_IsDeterministicAndPinsInputHash()
    {
        var request = RequirementRequest();

        var first = TextileRuntimeDomain.CalculateRequirement(request);
        var second = TextileRuntimeDomain.CalculateRequirement(request);

        Assert.Equal(TextileCalculationDecisions.Sufficient, first.Result.Decision);
        Assert.Equal(first.InputHash, second.InputHash);
        Assert.Equal(64, first.InputHash.Length);
        Assert.Equal(TextileContract.RuleSetVersion, first.Result.RuleSetVersion);
    }

    [Fact]
    public void CalculateRequirement_PreservesInsufficientGapAndUnknownDecision()
    {
        var insufficient = TextileRuntimeDomain.CalculateRequirement(
            RequirementRequest(availableArea: 10m));
        var unknown = TextileRuntimeDomain.CalculateRequirement(
            RequirementRequest(ruleSetVersion: "TEXTILE-SAMPLE-REQUIREMENT@99.0.0"));

        Assert.Equal(TextileCalculationDecisions.Insufficient, insufficient.Result.Decision);
        Assert.Single(insufficient.Result.Gaps);
        Assert.Equal(590m, insufficient.Result.Gaps[0].GapAreaSquareMm);
        Assert.Equal(TextileCalculationDecisions.Unknown, unknown.Result.Decision);
        Assert.Contains(TextileCalculationReasons.RuleSetVersionUnknown, unknown.Result.ReasonCodes);
    }

    [Fact]
    public void ValidatePlan_RequiresPinnedRequirementAndValidFrozenContract()
    {
        var requirement = RequirementRecord(TextileCalculationDecisions.Sufficient);
        var valid = PlanRequest(requirement.InputHash);

        TextileRuntimeDomain.ValidatePlan(valid, requirement);

        var mismatchedHash = valid with { SampleRequirementInputHash = "different" };
        var invalidPlan = valid with
        {
            Plan = valid.Plan with { GeneratedSpecimenIds = ["SPEC-1"] }
        };
        Assert.Equal(
            TextileErrorCodes.ValidationFailed,
            Assert.Throws<TextileOperationException>(() =>
                TextileRuntimeDomain.ValidatePlan(mismatchedHash, requirement)).ErrorCode);
        Assert.Equal(
            TextileErrorCodes.ValidationFailed,
            Assert.Throws<TextileContractException>(() =>
                TextileRuntimeDomain.ValidatePlan(invalidPlan, requirement)).ErrorCode);
    }

    [Theory]
    [InlineData(TextileCalculationDecisions.Insufficient)]
    [InlineData(TextileCalculationDecisions.Unknown)]
    public void RequireApprovable_FailsClosedWhenRequirementIsNotSufficient(string decision)
    {
        var requirement = RequirementRecord(decision);
        var plan = PlanResult(requirement);

        var error = Assert.Throws<TextileOperationException>(() =>
            TextileRuntimeDomain.RequireApprovable(
                plan,
                new ApproveTextileCuttingPlanRequest(
                    plan.Version,
                    requirement.InputHash,
                    TextileContract.RuleSetVersion,
                    "reviewed")));

        Assert.Equal(TextileErrorCodes.SampleRequirementNotApprovable, error.ErrorCode);
    }

    [Fact]
    public void EvaluateStatus_ReturnsAllowedOnlyForApprovedExactRuleSet()
    {
        var requirement = RequirementRecord(TextileCalculationDecisions.Sufficient);
        var approved = PlanResult(requirement) with
        {
            State = TextileCuttingPlanStates.Approved,
            Approval = new TextileCuttingPlanApproval(
                "PLAN-1", 1, "REQ-1", 1, requirement.InputHash,
                TextileContract.RuleSetVersion, "approver", DateTimeOffset.UnixEpoch, "reviewed")
        };

        Assert.Equal(
            TextileStatusDecisions.Allowed,
            TextileRuntimeDomain.EvaluateStatus(approved, TextileContract.RuleSetVersion).Decision);
        Assert.Equal(
            TextileStatusDecisions.Unknown,
            TextileRuntimeDomain.EvaluateStatus(approved, "TEXTILE-SAMPLE-REQUIREMENT@99.0.0").Decision);
        Assert.Equal(
            TextileStatusDecisions.Blocked,
            TextileRuntimeDomain.EvaluateStatus(approved with
            {
                State = TextileCuttingPlanStates.Draft,
                Approval = null
            }, TextileContract.RuleSetVersion).Decision);
    }

    private static CreateTextileSampleRequirementRequest RequirementRequest(
        decimal availableArea = 1_000m,
        string ruleSetVersion = TextileContract.RuleSetVersion) => new(
            "REQ-1",
            0,
            Scope(),
            new TextileSampleRequirementCalculation(
                ruleSetVersion,
                [DemandLine()],
                [new TextileAvailableFabric(Ref("STYLE"), Ref("RED"), Ref("FRONT"), "BODY", availableArea)]));

    private static TextileDemandLine DemandLine() => new(
        Ref("STYLE"), Ref("RED"), Ref("FRONT"), Ref("COTTON"), "BODY",
        TextileDirections.Warp, Ref("TENSILE"), 3, 1, 1, true, 10m, 12m,
        ExclusiveDestructiveGroupId: "GROUP-A");

    private static TextileSampleRequirementRecord RequirementRecord(string decision)
    {
        var draft = TextileRuntimeDomain.CalculateRequirement(RequirementRequest());
        return new TextileSampleRequirementRecord(
            "REQ-1", 1, Scope(), draft.Calculation,
            draft.Result with { Decision = decision }, draft.InputHash,
            "creator", DateTimeOffset.UnixEpoch);
    }

    private static CreateTextileCuttingPlanRequest PlanRequest(string requirementHash) => new(
        "PLAN-1", 0, "REQ-1", 1, requirementHash, TextileContract.RuleSetVersion,
        new TextileCuttingPlan(
            "PLAN-1", Ref("FABRIC-LOT"), "BODY", TextileDirections.Warp,
            10m, 12m, 5, 20m, "TPL-1", "operator", ["SPEC-1", "SPEC-2", "SPEC-3", "SPEC-4", "SPEC-5"]));

    private static TextileCuttingPlanResult PlanResult(TextileSampleRequirementRecord requirement) => new(
        "PLAN-1", 1, Scope(), requirement, PlanRequest(requirement.InputHash).Plan,
        TextileCuttingPlanStates.Draft, "plan-hash", TextileContract.RuleSetVersion,
        "creator", DateTimeOffset.UnixEpoch, null);

    private static TextileObjectScope Scope() => new("legal-a", "lab-a");

    private static TextileVersionedReference Ref(string id) => new(id, 1);
}
