namespace OpenLIMS.Contracts.Textile;

public static class TextileContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "TEXTILE-SAMPLE-REQUIREMENT@1.0.0";
}

public static class TextileDirections
{
    public const string Warp = "WARP";
    public const string Weft = "WEFT";
    public const string Lengthwise = "LENGTHWISE";
    public const string Crosswise = "CROSSWISE";
}

public static class TextileCalculationDecisions
{
    public const string Sufficient = "SUFFICIENT";
    public const string Insufficient = "INSUFFICIENT";
    public const string Unknown = "UNKNOWN";
}

public static class TextileCalculationReasons
{
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string SampleInsufficient = "SAMPLE_INSUFFICIENT";
}

public static class TextileErrorCodes
{
    public const string ValidationFailed = "TEX.VALIDATION_FAILED";
    public const string DirectionUnknown = "TEX.DIRECTION_UNKNOWN";
    public const string ExclusiveShareRejected = "TEX.EXCLUSIVE_SHARE_REJECTED";
    public const string ApplicabilityUnknown = "TEX.APPLICABILITY_UNKNOWN";
}

public sealed class TextileContractException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed record TextileVersionedReference(string Id, long Version);

public sealed record TextileDemandLine(
    TextileVersionedReference Style,
    TextileVersionedReference Colorway,
    TextileVersionedReference Component,
    TextileVersionedReference Material,
    string Position,
    string Direction,
    TextileVersionedReference TestItem,
    int ParallelCount,
    int RetestReserveCount,
    int RetentionReserveCount,
    bool Destructive,
    decimal SpecimenLengthMm,
    decimal SpecimenWidthMm,
    TextileVersionedReference? Preconditioning = null,
    string? ExclusiveDestructiveGroupId = null,
    string? ShareGroupId = null);

public sealed record TextileAvailableFabric(
    TextileVersionedReference Style,
    TextileVersionedReference Colorway,
    TextileVersionedReference Component,
    string Position,
    decimal AvailableAreaSquareMm);

public sealed record TextileSampleRequirementCalculation(
    string RuleSetVersion,
    IReadOnlyList<TextileDemandLine> DemandLines,
    IReadOnlyList<TextileAvailableFabric> AvailableFabrics);

public sealed record TextileSpecimenPlan(
    TextileVersionedReference Style,
    TextileVersionedReference Colorway,
    TextileVersionedReference Component,
    string Position,
    string Direction,
    TextileVersionedReference TestItem,
    int RequiredSpecimenCount,
    decimal AreaSquareMm,
    string? ShareGroupId);

public sealed record TextileGapContributor(string Direction, TextileVersionedReference TestItem);

public sealed record TextileSufficiencyGap(
    TextileVersionedReference Style,
    TextileVersionedReference Colorway,
    TextileVersionedReference Component,
    string Position,
    decimal RequiredAreaSquareMm,
    decimal AvailableAreaSquareMm,
    decimal GapAreaSquareMm,
    IReadOnlyList<TextileGapContributor> ContributingItems);

public sealed record TextileSampleRequirementResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<TextileSpecimenPlan> SpecimenPlans,
    IReadOnlyList<TextileSufficiencyGap> Gaps,
    string RuleSetVersion);

public sealed record TextileCuttingPlan(
    string CuttingPlanId,
    TextileVersionedReference SourceItem,
    string SamplingPosition,
    string Direction,
    decimal LengthMm,
    decimal WidthMm,
    int PlannedCount,
    decimal MinDistanceFromSelvedgeMm,
    string TemplateVersion,
    string OperatorId,
    IReadOnlyList<string> GeneratedSpecimenIds);

public interface ITextileSampleRequirementCalculator
{
    TextileSampleRequirementResult Calculate(TextileSampleRequirementCalculation calculation);
}
