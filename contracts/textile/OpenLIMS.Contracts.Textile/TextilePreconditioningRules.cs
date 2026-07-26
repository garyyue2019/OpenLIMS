using System.Text.RegularExpressions;

namespace OpenLIMS.Contracts.Textile;

/// <summary>
/// Pure, deterministic preconditioning evaluation for the textile future-fit
/// contract slice. Plan/actual stay separate; approval only unlocks reporting
/// and never rewrites the out-of-tolerance facts.
/// </summary>
public sealed partial class TextilePreconditioningRules : ITextilePreconditioningEvaluator
{
    private const decimal MaximumValue = 100_000m;
    private static readonly Regex StableIdentifier = StableIdentifierPattern();

    public static TextilePreconditioningRules Instance { get; } = new();

    public TextilePreconditioningAssessment Evaluate(string ruleSetVersion, TextilePreconditioningRecord record)
    {
        ValidateRecord(record);
        if (!string.Equals(ruleSetVersion, TextileContract.RuleSetVersion, StringComparison.Ordinal))
        {
            return new TextilePreconditioningAssessment(
                TextilePreconditioningDecisions.Unknown,
                [TextilePreconditioningReasons.RuleSetVersionUnknown],
                [],
                false,
                TextileContract.RuleSetVersion);
        }

        var deviations = new List<TextilePreconditioningDeviation>();
        AddDeviation(deviations, "temperatureC",
            record.Planned.TemperatureC, record.Actual.TemperatureC, record.Tolerances.TemperatureC);
        AddDeviation(deviations, "durationMinutes",
            record.Planned.DurationMinutes, record.Actual.DurationMinutes, record.Tolerances.DurationMinutes);
        if (string.Equals(record.Type, TextilePreconditioningTypes.Conditioning, StringComparison.Ordinal))
        {
            AddDeviation(deviations, "humidityPercent",
                record.Planned.HumidityPercent!.Value,
                record.Actual.HumidityPercent!.Value,
                record.Tolerances.HumidityPercent!.Value);
        }

        if (deviations.Count == 0)
        {
            return new TextilePreconditioningAssessment(
                TextilePreconditioningDecisions.WithinTolerance,
                [],
                [],
                true,
                TextileContract.RuleSetVersion);
        }

        var approved = record.OutOfToleranceApproval is not null;
        var reasons = approved
            ? new[] { TextilePreconditioningReasons.ConditionOutOfTolerance }
            : [TextilePreconditioningReasons.ConditionOutOfTolerance, TextilePreconditioningReasons.ApprovalRequired];
        return new TextilePreconditioningAssessment(
            TextilePreconditioningDecisions.OutOfTolerance,
            reasons,
            deviations,
            approved,
            TextileContract.RuleSetVersion);
    }

    private static void AddDeviation(
        List<TextilePreconditioningDeviation> deviations,
        string field,
        decimal planned,
        decimal actual,
        decimal tolerance)
    {
        var deviation = Math.Abs(actual - planned);
        if (deviation > tolerance)
            deviations.Add(new TextilePreconditioningDeviation(field, planned, actual, deviation, tolerance));
    }

    private static void ValidateRecord(TextilePreconditioningRecord? record)
    {
        if (record is null ||
            !IsIdentifier(record.RecordId) ||
            !IsReference(record.SourceItem) ||
            !IsIdentifier(record.OperatorId) ||
            record.Planned is null ||
            record.Actual is null ||
            record.Tolerances is null ||
            (record.CuttingPlanId is not null && !IsIdentifier(record.CuttingPlanId)) ||
            (record.OutOfToleranceApproval is not null && !IsReference(record.OutOfToleranceApproval)) ||
            (record.SpecimenIds is not null &&
             (record.SpecimenIds.Any(id => !IsIdentifier(id)) ||
              record.SpecimenIds.Distinct(StringComparer.Ordinal).Count() != record.SpecimenIds.Count)))
        {
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
        }

        var isConditioning = string.Equals(record.Type, TextilePreconditioningTypes.Conditioning, StringComparison.Ordinal);
        var isWashing = string.Equals(record.Type, TextilePreconditioningTypes.Washing, StringComparison.Ordinal);
        if (!isConditioning && !isWashing)
            throw new TextileContractException(TextilePreconditioningErrorCodes.TypeUnknown);

        ValidateConditions(record.Planned, isConditioning, isWashing);
        ValidateConditions(record.Actual, isConditioning, isWashing);
        if (!IsToleranceValue(record.Tolerances.TemperatureC) ||
            !IsToleranceValue(record.Tolerances.DurationMinutes) ||
            (isConditioning && (record.Tolerances.HumidityPercent is null ||
                                !IsToleranceValue(record.Tolerances.HumidityPercent.Value))) ||
            (isWashing && record.Tolerances.HumidityPercent is not null))
        {
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
        }
    }

    private static void ValidateConditions(TextilePreconditioningConditions conditions, bool isConditioning, bool isWashing)
    {
        if (!IsMeasuredValue(conditions.TemperatureC) ||
            conditions.DurationMinutes <= 0 || conditions.DurationMinutes >= MaximumValue)
        {
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
        }

        if (isConditioning &&
            (conditions.HumidityPercent is null ||
             conditions.HumidityPercent is < 0 or > 100 ||
             conditions.Program is not null ||
             conditions.Detergent is not null ||
             conditions.DryingMethod is not null))
        {
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
        }

        if (isWashing &&
            (conditions.HumidityPercent is not null ||
             !IsIdentifier(conditions.Program) ||
             !IsIdentifier(conditions.Detergent) ||
             !IsIdentifier(conditions.DryingMethod)))
        {
            throw new TextileContractException(TextileErrorCodes.ValidationFailed);
        }
    }

    private static bool IsMeasuredValue(decimal value) => value > -273 && value < MaximumValue;

    private static bool IsToleranceValue(decimal value) => value >= 0 && value < MaximumValue;

    private static bool IsReference(TextileVersionedReference? value) =>
        value is not null && value.Version >= 1 && IsIdentifier(value.Id);

    private static bool IsIdentifier(string? value) =>
        value is not null && StableIdentifier.IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierPattern();
}
