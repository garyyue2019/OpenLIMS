namespace OpenLIMS.Contracts.Textile;

public static class TextilePreconditioningTypes
{
    public const string Conditioning = "CONDITIONING";
    public const string Washing = "WASHING";
}

public static class TextilePreconditioningDecisions
{
    public const string WithinTolerance = "WITHIN_TOLERANCE";
    public const string OutOfTolerance = "OUT_OF_TOLERANCE";
    public const string Unknown = "UNKNOWN";
}

public static class TextilePreconditioningReasons
{
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string ConditionOutOfTolerance = "CONDITION_OUT_OF_TOLERANCE";
    public const string ApprovalRequired = "APPROVAL_REQUIRED";
}

public static class TextilePreconditioningErrorCodes
{
    public const string TypeUnknown = "TEX.PRECONDITIONING_TYPE_UNKNOWN";
}

public sealed record TextilePreconditioningConditions(
    decimal TemperatureC,
    decimal DurationMinutes,
    decimal? HumidityPercent = null,
    string? Program = null,
    string? Detergent = null,
    string? DryingMethod = null);

public sealed record TextilePreconditioningTolerances(
    decimal TemperatureC,
    decimal DurationMinutes,
    decimal? HumidityPercent = null);

public sealed record TextilePreconditioningRecord(
    string RecordId,
    string Type,
    TextileVersionedReference SourceItem,
    TextilePreconditioningConditions Planned,
    TextilePreconditioningConditions Actual,
    TextilePreconditioningTolerances Tolerances,
    string OperatorId,
    string? CuttingPlanId = null,
    IReadOnlyList<string>? SpecimenIds = null,
    TextileVersionedReference? OutOfToleranceApproval = null);

public sealed record TextilePreconditioningDeviation(
    string Field,
    decimal PlannedValue,
    decimal ActualValue,
    decimal Deviation,
    decimal ToleranceValue);

public sealed record TextilePreconditioningAssessment(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<TextilePreconditioningDeviation> Deviations,
    bool ReportingAllowed,
    string RuleSetVersion);

public interface ITextilePreconditioningEvaluator
{
    TextilePreconditioningAssessment Evaluate(string ruleSetVersion, TextilePreconditioningRecord record);
}
