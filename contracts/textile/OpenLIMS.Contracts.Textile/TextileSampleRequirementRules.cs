using System.Text.RegularExpressions;

namespace OpenLIMS.Contracts.Textile;

/// <summary>
/// Pure, deterministic reference rules for the textile future-fit contract slice.
/// No IO, no clock, no randomness — every structural violation fails closed.
/// </summary>
public sealed partial class TextileSampleRequirementRules : ITextileSampleRequirementCalculator
{
    private const decimal MaximumDimensionMm = 100_000m;
    private const int MaximumCount = 10_000;
    private static readonly Regex StableIdentifier = StableIdentifierPattern();

    public static TextileSampleRequirementRules Instance { get; } = new();

    public TextileSampleRequirementResult Calculate(TextileSampleRequirementCalculation calculation)
    {
        if (calculation is null || calculation.DemandLines is null || calculation.AvailableFabrics is null)
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
        if (!string.Equals(calculation.RuleSetVersion, TextileContract.RuleSetVersion, StringComparison.Ordinal))
        {
            return new TextileSampleRequirementResult(
                TextileCalculationDecisions.Unknown,
                [TextileCalculationReasons.RuleSetVersionUnknown],
                [],
                [],
                TextileContract.RuleSetVersion);
        }

        if (calculation.DemandLines.Count is < 1 or > MaximumCount)
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);

        foreach (var line in calculation.DemandLines)
            ValidateLine(line);
        foreach (var fabric in calculation.AvailableFabrics)
            ValidateFabric(fabric);

        var plans = BuildSpecimenPlans(calculation.DemandLines);
        var gaps = BuildGaps(plans, calculation.AvailableFabrics);

        return gaps.Count > 0
            ? new TextileSampleRequirementResult(
                TextileCalculationDecisions.Insufficient,
                [TextileCalculationReasons.SampleInsufficient],
                plans,
                gaps,
                TextileContract.RuleSetVersion)
            : new TextileSampleRequirementResult(
                TextileCalculationDecisions.Sufficient,
                [],
                plans,
                [],
                TextileContract.RuleSetVersion);
    }

    public static void ValidateCuttingPlan(TextileCuttingPlan? plan)
    {
        if (plan is null ||
            !IsIdentifier(plan.CuttingPlanId) ||
            !IsReference(plan.SourceItem) ||
            !IsIdentifier(plan.SamplingPosition) ||
            !IsPositiveDimension(plan.LengthMm) ||
            !IsPositiveDimension(plan.WidthMm) ||
            plan.PlannedCount is < 1 or > MaximumCount ||
            plan.MinDistanceFromSelvedgeMm < 0 ||
            plan.MinDistanceFromSelvedgeMm >= MaximumDimensionMm ||
            !IsIdentifier(plan.TemplateVersion) ||
            !IsIdentifier(plan.OperatorId) ||
            plan.GeneratedSpecimenIds is null)
        {
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
        }

        if (!IsKnownDirection(plan.Direction))
            throw new TextileContractException(TextileErrorCodes.DirectionUnknown);

        if (plan.GeneratedSpecimenIds.Count != plan.PlannedCount ||
            plan.GeneratedSpecimenIds.Any(id => !IsIdentifier(id)) ||
            plan.GeneratedSpecimenIds.Distinct(StringComparer.Ordinal).Count() != plan.GeneratedSpecimenIds.Count)
        {
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
        }
    }

    private static void ValidateLine(TextileDemandLine? line)
    {
        if (line is null ||
            !IsReference(line.Style) ||
            !IsReference(line.Colorway) ||
            !IsReference(line.Component) ||
            !IsReference(line.Material) ||
            !IsIdentifier(line.Position) ||
            !IsReference(line.TestItem) ||
            line.ParallelCount is < 1 or > MaximumCount ||
            line.RetestReserveCount is < 0 or > MaximumCount ||
            line.RetentionReserveCount is < 0 or > MaximumCount ||
            !IsPositiveDimension(line.SpecimenLengthMm) ||
            !IsPositiveDimension(line.SpecimenWidthMm) ||
            (line.Preconditioning is not null && !IsReference(line.Preconditioning)) ||
            (line.ExclusiveDestructiveGroupId is not null && !IsIdentifier(line.ExclusiveDestructiveGroupId)) ||
            (line.ShareGroupId is not null && !IsIdentifier(line.ShareGroupId)))
        {
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
        }

        if (!IsKnownDirection(line.Direction))
            throw new TextileContractException(TextileErrorCodes.DirectionUnknown);

        if (line.ExclusiveDestructiveGroupId is not null && !line.Destructive)
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
    }

    private static void ValidateFabric(TextileAvailableFabric? fabric)
    {
        if (fabric is null ||
            !IsReference(fabric.Style) ||
            !IsReference(fabric.Colorway) ||
            !IsReference(fabric.Component) ||
            !IsIdentifier(fabric.Position) ||
            fabric.AvailableAreaSquareMm < 0 ||
            fabric.AvailableAreaSquareMm >= MaximumDimensionMm * MaximumDimensionMm)
        {
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
        }
    }

    private static List<TextileSpecimenPlan> BuildSpecimenPlans(IReadOnlyList<TextileDemandLine> lines)
    {
        var plans = new List<TextileSpecimenPlan>();
        var shareGroups = new Dictionary<string, List<TextileDemandLine>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.ShareGroupId is null)
            {
                plans.Add(Plan(line, RequiredCount(line), null));
                continue;
            }

            if (!shareGroups.TryGetValue(line.ShareGroupId, out var group))
            {
                group = [];
                shareGroups.Add(line.ShareGroupId, group);
            }

            group.Add(line);
        }

        foreach (var (shareGroupId, group) in shareGroups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (group.Any(line => line.Destructive))
                throw new TextileContractException(TextileErrorCodes.ExclusiveShareRejected);

            var first = group[0];
            if (group.Any(line => !SameSharedSpecification(line, first)))
                throw new TextileContractException(TextileErrorCodes.ValidationFailed);

            plans.Add(Plan(first, group.Max(RequiredCount), shareGroupId));
        }

        return plans;
    }

    private static List<TextileSufficiencyGap> BuildGaps(
        IReadOnlyList<TextileSpecimenPlan> plans,
        IReadOnlyList<TextileAvailableFabric> fabrics)
    {
        var gaps = new List<TextileSufficiencyGap>();
        var groups = plans
            .GroupBy(plan => (plan.Style, plan.Colorway, plan.Component, plan.Position))
            .OrderBy(group => group.Key.Style.Id, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Colorway.Id, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Component.Id, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Position, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var requiredArea = group.Sum(plan => plan.AreaSquareMm);
            var availableArea = fabrics
                .Where(fabric =>
                    fabric.Style == group.Key.Style &&
                    fabric.Colorway == group.Key.Colorway &&
                    fabric.Component == group.Key.Component &&
                    string.Equals(fabric.Position, group.Key.Position, StringComparison.Ordinal))
                .Sum(fabric => fabric.AvailableAreaSquareMm);
            if (requiredArea <= availableArea)
                continue;

            gaps.Add(new TextileSufficiencyGap(
                group.Key.Style,
                group.Key.Colorway,
                group.Key.Component,
                group.Key.Position,
                requiredArea,
                availableArea,
                requiredArea - availableArea,
                [.. group.Select(plan => new TextileGapContributor(plan.Direction, plan.TestItem))]));
        }

        return gaps;
    }

    private static TextileSpecimenPlan Plan(TextileDemandLine line, int requiredCount, string? shareGroupId) => new(
        line.Style,
        line.Colorway,
        line.Component,
        line.Position,
        line.Direction,
        line.TestItem,
        requiredCount,
        requiredCount * line.SpecimenLengthMm * line.SpecimenWidthMm,
        shareGroupId);

    private static int RequiredCount(TextileDemandLine line) =>
        line.ParallelCount + line.RetestReserveCount + line.RetentionReserveCount;

    private static bool SameSharedSpecification(TextileDemandLine line, TextileDemandLine reference) =>
        line.Style == reference.Style &&
        line.Colorway == reference.Colorway &&
        line.Component == reference.Component &&
        line.Material == reference.Material &&
        string.Equals(line.Position, reference.Position, StringComparison.Ordinal) &&
        string.Equals(line.Direction, reference.Direction, StringComparison.Ordinal) &&
        line.SpecimenLengthMm == reference.SpecimenLengthMm &&
        line.SpecimenWidthMm == reference.SpecimenWidthMm &&
        Equals(line.Preconditioning, reference.Preconditioning);

    private static bool IsKnownDirection(string? value) => value is
        TextileDirections.Warp or TextileDirections.Weft or
        TextileDirections.Lengthwise or TextileDirections.Crosswise;

    private static bool IsPositiveDimension(decimal value) => value > 0 && value < MaximumDimensionMm;

    private static bool IsReference(TextileVersionedReference? value) =>
        value is not null && value.Version >= 1 && IsIdentifier(value.Id);

    private static bool IsIdentifier(string? value) =>
        value is not null && StableIdentifier.IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierPattern();
}
