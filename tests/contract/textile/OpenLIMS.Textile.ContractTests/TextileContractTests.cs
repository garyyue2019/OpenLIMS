using System.Text.Json;
using OpenLIMS.Contracts.Textile;
using Xunit;

namespace OpenLIMS.Textile.ContractTests;

[Trait("Profile", "textile")]
public sealed class TextileContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Required_specimen_count_is_parallel_plus_retest_plus_retention()
    {
        var result = TextileSampleRequirementRules.Instance.Calculate(Calculation(
            [Line("ITEM-TENSILE", destructive: false, parallel: 3, retest: 1, retention: 1)],
            [Fabric(1_000_000m)]));

        var plan = Assert.Single(result.SpecimenPlans);
        Assert.Equal(TextileCalculationDecisions.Sufficient, result.Decision);
        Assert.Equal(5, plan.RequiredSpecimenCount);
        Assert.Equal(5 * 250m * 50m, plan.AreaSquareMm);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void Insufficient_area_reports_gap_by_style_colorway_component_position_with_items()
    {
        var result = TextileSampleRequirementRules.Instance.Calculate(Calculation(
            [
                Line("ITEM-TEAR", destructive: true, parallel: 3, retest: 1, retention: 0,
                    exclusiveGroup: "EXCL-A", direction: TextileDirections.Warp),
                Line("ITEM-BURST", destructive: true, parallel: 3, retest: 1, retention: 0,
                    exclusiveGroup: "EXCL-A", direction: TextileDirections.Weft)
            ],
            [Fabric(60_000m)]));

        var gap = Assert.Single(result.Gaps);
        Assert.Equal(TextileCalculationDecisions.Insufficient, result.Decision);
        Assert.Contains(TextileCalculationReasons.SampleInsufficient, result.ReasonCodes);
        Assert.Equal("STYLE-1", gap.Style.Id);
        Assert.Equal("COLOR-NAVY", gap.Colorway.Id);
        Assert.Equal("COMP-BODY", gap.Component.Id);
        Assert.Equal("FRONT-PANEL", gap.Position);
        Assert.Equal(100_000m, gap.RequiredAreaSquareMm);
        Assert.Equal(60_000m, gap.AvailableAreaSquareMm);
        Assert.Equal(40_000m, gap.GapAreaSquareMm);
        Assert.Equal(2, gap.ContributingItems.Count);
        Assert.Contains(gap.ContributingItems, item =>
            item.Direction == TextileDirections.Warp && item.TestItem.Id == "ITEM-TEAR");
        Assert.Contains(gap.ContributingItems, item =>
            item.Direction == TextileDirections.Weft && item.TestItem.Id == "ITEM-BURST");
    }

    [Fact]
    public void Destructive_lines_never_share_cut_pieces()
    {
        var exception = Assert.Throws<TextileContractException>(() =>
            TextileSampleRequirementRules.Instance.Calculate(Calculation(
                [
                    Line("ITEM-TEAR", destructive: true, parallel: 3, retest: 0, retention: 0,
                        exclusiveGroup: "EXCL-A", shareGroup: "SHARE-1"),
                    Line("ITEM-BURST", destructive: true, parallel: 3, retest: 0, retention: 0,
                        exclusiveGroup: "EXCL-B", shareGroup: "SHARE-1")
                ],
                [Fabric(1_000_000m)])));

        Assert.Equal(TextileErrorCodes.ExclusiveShareRejected, exception.ErrorCode);
    }

    [Fact]
    public void Non_destructive_identical_lines_share_specimens_by_maximum_demand()
    {
        var result = TextileSampleRequirementRules.Instance.Calculate(Calculation(
            [
                Line("ITEM-COLOR", destructive: false, parallel: 2, retest: 1, retention: 0, shareGroup: "SHARE-ND"),
                Line("ITEM-PH", destructive: false, parallel: 3, retest: 0, retention: 1, shareGroup: "SHARE-ND")
            ],
            [Fabric(1_000_000m)]));

        var plan = Assert.Single(result.SpecimenPlans);
        Assert.Equal(TextileCalculationDecisions.Sufficient, result.Decision);
        Assert.Equal("SHARE-ND", plan.ShareGroupId);
        Assert.Equal(4, plan.RequiredSpecimenCount);
        Assert.Equal(4 * 250m * 50m, plan.AreaSquareMm);
    }

    [Fact]
    public void Unknown_rule_set_version_is_unknown_and_unknown_direction_fails_closed()
    {
        var unknownRule = TextileSampleRequirementRules.Instance.Calculate(Calculation(
            [Line("ITEM-TENSILE", destructive: false, parallel: 3, retest: 0, retention: 0)],
            [Fabric(1_000_000m)]) with
        {
            RuleSetVersion = "TEXTILE-SAMPLE-REQUIREMENT@latest"
        });
        var badDirection = Assert.Throws<TextileContractException>(() =>
            TextileSampleRequirementRules.Instance.Calculate(Calculation(
                [Line("ITEM-TENSILE", destructive: false, parallel: 3, retest: 0, retention: 0,
                    direction: "BIAS")],
                [Fabric(1_000_000m)])));

        Assert.Equal(TextileCalculationDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(TextileCalculationReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
        Assert.Empty(unknownRule.SpecimenPlans);
        Assert.Equal(TextileErrorCodes.DirectionUnknown, badDirection.ErrorCode);
    }

    [Fact]
    public void Exclusive_group_on_non_destructive_line_is_rejected()
    {
        var exception = Assert.Throws<TextileContractException>(() =>
            TextileSampleRequirementRules.Instance.Calculate(Calculation(
                [Line("ITEM-TEAR", destructive: false, parallel: 3, retest: 0, retention: 0,
                    exclusiveGroup: "EXCL-A")],
                [Fabric(1_000_000m)])));

        Assert.Equal(TextileErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Cutting_plan_validation_accepts_complete_plan_and_rejects_inconsistent_plans()
    {
        TextileSampleRequirementRules.ValidateCuttingPlan(CuttingPlan());

        var badDirection = Assert.Throws<TextileContractException>(() =>
            TextileSampleRequirementRules.ValidateCuttingPlan(CuttingPlan() with { Direction = "DIAGONAL" }));
        var badCount = Assert.Throws<TextileContractException>(() =>
            TextileSampleRequirementRules.ValidateCuttingPlan(CuttingPlan() with { PlannedCount = 3 }));
        var badDimension = Assert.Throws<TextileContractException>(() =>
            TextileSampleRequirementRules.ValidateCuttingPlan(CuttingPlan() with { LengthMm = 0m }));

        Assert.Equal(TextileErrorCodes.DirectionUnknown, badDirection.ErrorCode);
        Assert.Equal(TextileErrorCodes.ValidationFailed, badCount.ErrorCode);
        Assert.Equal(TextileErrorCodes.ValidationFailed, badDimension.ErrorCode);
    }

    [Fact]
    public void Serialization_shape_is_frozen_for_calculation_result_and_cutting_plan()
    {
        var calculation = Calculation(
            [Line("ITEM-TENSILE", destructive: false, parallel: 2, retest: 1, retention: 1)],
            [Fabric(30_000m)]);
        var result = TextileSampleRequirementRules.Instance.Calculate(calculation);

        var calculationJson = JsonSerializer.Serialize(calculation, Json);
        var resultJson = JsonSerializer.Serialize(result, Json);
        var cuttingPlanJson = JsonSerializer.Serialize(CuttingPlan(), Json);

        Assert.Equal(
            """{"ruleSetVersion":"TEXTILE-SAMPLE-REQUIREMENT@1.0.0","demandLines":[{"style":{"id":"STYLE-1","version":1},"colorway":{"id":"COLOR-NAVY","version":1},"component":{"id":"COMP-BODY","version":1},"material":{"id":"MAT-COTTON","version":1},"position":"FRONT-PANEL","direction":"WARP","testItem":{"id":"ITEM-TENSILE","version":1},"parallelCount":2,"retestReserveCount":1,"retentionReserveCount":1,"destructive":false,"specimenLengthMm":250,"specimenWidthMm":50,"preconditioning":{"id":"PRECON-STD","version":1},"exclusiveDestructiveGroupId":null,"shareGroupId":null}],"availableFabrics":[{"style":{"id":"STYLE-1","version":1},"colorway":{"id":"COLOR-NAVY","version":1},"component":{"id":"COMP-BODY","version":1},"position":"FRONT-PANEL","availableAreaSquareMm":30000}]}""",
            calculationJson);
        Assert.Equal(
            """{"decision":"INSUFFICIENT","reasonCodes":["SAMPLE_INSUFFICIENT"],"specimenPlans":[{"style":{"id":"STYLE-1","version":1},"colorway":{"id":"COLOR-NAVY","version":1},"component":{"id":"COMP-BODY","version":1},"position":"FRONT-PANEL","direction":"WARP","testItem":{"id":"ITEM-TENSILE","version":1},"requiredSpecimenCount":4,"areaSquareMm":50000,"shareGroupId":null}],"gaps":[{"style":{"id":"STYLE-1","version":1},"colorway":{"id":"COLOR-NAVY","version":1},"component":{"id":"COMP-BODY","version":1},"position":"FRONT-PANEL","requiredAreaSquareMm":50000,"availableAreaSquareMm":30000,"gapAreaSquareMm":20000,"contributingItems":[{"direction":"WARP","testItem":{"id":"ITEM-TENSILE","version":1}}]}],"ruleSetVersion":"TEXTILE-SAMPLE-REQUIREMENT@1.0.0"}""",
            resultJson);
        Assert.Equal(
            """{"cuttingPlanId":"CUT-0001","sourceItem":{"id":"FABRIC-LOT-9","version":2},"samplingPosition":"FRONT-PANEL","direction":"WARP","lengthMm":250,"widthMm":50,"plannedCount":2,"minDistanceFromSelvedgeMm":150,"templateVersion":"TPL-3","operatorId":"operator-a","generatedSpecimenIds":["SPEC-1","SPEC-2"]}""",
            cuttingPlanJson);

        var calculationRoundTrip = JsonSerializer.Deserialize<TextileSampleRequirementCalculation>(calculationJson, Json);
        var cuttingPlanRoundTrip = JsonSerializer.Deserialize<TextileCuttingPlan>(cuttingPlanJson, Json);
        Assert.Equal(calculation.RuleSetVersion, calculationRoundTrip!.RuleSetVersion);
        Assert.Equal(calculation.DemandLines.Single(), calculationRoundTrip.DemandLines.Single());
        Assert.Equal(CuttingPlan() with { GeneratedSpecimenIds = cuttingPlanRoundTrip!.GeneratedSpecimenIds },
            cuttingPlanRoundTrip);
    }

    [Fact]
    public void Calculation_is_deterministic_across_repeated_runs()
    {
        var calculation = Calculation(
            [
                Line("ITEM-TEAR", destructive: true, parallel: 3, retest: 1, retention: 0,
                    exclusiveGroup: "EXCL-A", direction: TextileDirections.Warp),
                Line("ITEM-COLOR", destructive: false, parallel: 2, retest: 0, retention: 1,
                    direction: TextileDirections.Crosswise)
            ],
            [Fabric(70_000m)]);

        var first = TextileSampleRequirementRules.Instance.Calculate(calculation);
        var second = TextileSampleRequirementRules.Instance.Calculate(calculation);

        Assert.Equal(JsonSerializer.Serialize(first, Json), JsonSerializer.Serialize(second, Json));
    }

    private static TextileSampleRequirementCalculation Calculation(
        IReadOnlyList<TextileDemandLine> lines,
        IReadOnlyList<TextileAvailableFabric> fabrics) => new(
        TextileContract.RuleSetVersion,
        lines,
        fabrics);

    private static TextileDemandLine Line(
        string testItemId,
        bool destructive,
        int parallel,
        int retest,
        int retention,
        string? exclusiveGroup = null,
        string? shareGroup = null,
        string direction = TextileDirections.Warp) => new(
        new TextileVersionedReference("STYLE-1", 1),
        new TextileVersionedReference("COLOR-NAVY", 1),
        new TextileVersionedReference("COMP-BODY", 1),
        new TextileVersionedReference("MAT-COTTON", 1),
        "FRONT-PANEL",
        direction,
        new TextileVersionedReference(testItemId, 1),
        parallel,
        retest,
        retention,
        destructive,
        250m,
        50m,
        new TextileVersionedReference("PRECON-STD", 1),
        exclusiveGroup,
        shareGroup);

    private static TextileAvailableFabric Fabric(decimal availableAreaSquareMm) => new(
        new TextileVersionedReference("STYLE-1", 1),
        new TextileVersionedReference("COLOR-NAVY", 1),
        new TextileVersionedReference("COMP-BODY", 1),
        "FRONT-PANEL",
        availableAreaSquareMm);

    private static TextileCuttingPlan CuttingPlan() => new(
        "CUT-0001",
        new TextileVersionedReference("FABRIC-LOT-9", 2),
        "FRONT-PANEL",
        TextileDirections.Warp,
        250m,
        50m,
        2,
        150m,
        "TPL-3",
        "operator-a",
        ["SPEC-1", "SPEC-2"]);
}
