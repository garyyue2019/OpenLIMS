using System.Text.Json;
using OpenLIMS.Contracts.Textile;
using Xunit;

namespace OpenLIMS.Textile.ContractTests;

[Trait("Profile", "textile")]
public sealed class TextilePreconditioningContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Conditions_within_explicit_tolerances_allow_reporting()
    {
        var assessment = TextilePreconditioningRules.Instance.Evaluate(
            TextileContract.RuleSetVersion,
            ConditioningRecord() with
            {
                Actual = new TextilePreconditioningConditions(20.5m, 245m, HumidityPercent: 64m)
            });

        Assert.Equal(TextilePreconditioningDecisions.WithinTolerance, assessment.Decision);
        Assert.True(assessment.ReportingAllowed);
        Assert.Empty(assessment.Deviations);
        Assert.Empty(assessment.ReasonCodes);
    }

    [Fact]
    public void Out_of_tolerance_reports_field_deviations_and_blocks_reporting()
    {
        var assessment = TextilePreconditioningRules.Instance.Evaluate(
            TextileContract.RuleSetVersion,
            ConditioningRecord() with
            {
                Actual = new TextilePreconditioningConditions(24m, 250m, HumidityPercent: 65m)
            });

        var deviation = Assert.Single(assessment.Deviations);
        Assert.Equal(TextilePreconditioningDecisions.OutOfTolerance, assessment.Decision);
        Assert.False(assessment.ReportingAllowed);
        Assert.Contains(TextilePreconditioningReasons.ConditionOutOfTolerance, assessment.ReasonCodes);
        Assert.Contains(TextilePreconditioningReasons.ApprovalRequired, assessment.ReasonCodes);
        Assert.Equal("temperatureC", deviation.Field);
        Assert.Equal(20m, deviation.PlannedValue);
        Assert.Equal(24m, deviation.ActualValue);
        Assert.Equal(4m, deviation.Deviation);
        Assert.Equal(2m, deviation.ToleranceValue);
    }

    [Fact]
    public void Approval_reference_unlocks_reporting_but_keeps_out_of_tolerance_facts()
    {
        var record = ConditioningRecord() with
        {
            Actual = new TextilePreconditioningConditions(24m, 250m, HumidityPercent: 65m),
            OutOfToleranceApproval = new TextileVersionedReference("APPROVAL-7", 1)
        };

        var assessment = TextilePreconditioningRules.Instance.Evaluate(TextileContract.RuleSetVersion, record);

        Assert.Equal(TextilePreconditioningDecisions.OutOfTolerance, assessment.Decision);
        Assert.True(assessment.ReportingAllowed);
        Assert.Single(assessment.Deviations);
        Assert.Contains(TextilePreconditioningReasons.ConditionOutOfTolerance, assessment.ReasonCodes);
        Assert.DoesNotContain(TextilePreconditioningReasons.ApprovalRequired, assessment.ReasonCodes);
    }

    [Fact]
    public void Type_specific_required_fields_fail_closed()
    {
        var conditioningWithoutHumidity = Assert.Throws<TextileContractException>(() =>
            TextilePreconditioningRules.Instance.Evaluate(
                TextileContract.RuleSetVersion,
                ConditioningRecord() with
                {
                    Planned = new TextilePreconditioningConditions(20m, 240m)
                }));
        var washingWithoutDetergent = Assert.Throws<TextileContractException>(() =>
            TextilePreconditioningRules.Instance.Evaluate(
                TextileContract.RuleSetVersion,
                WashingRecord() with
                {
                    Actual = new TextilePreconditioningConditions(40m, 45m, Program: "ISO-6330-4N", DryingMethod: "LINE-DRY")
                }));
        var unknownType = Assert.Throws<TextileContractException>(() =>
            TextilePreconditioningRules.Instance.Evaluate(
                TextileContract.RuleSetVersion,
                ConditioningRecord() with { Type = "STEAMING" }));

        Assert.Equal(TextileErrorCodes.ValidationFailed, conditioningWithoutHumidity.ErrorCode);
        Assert.Equal(TextileErrorCodes.ValidationFailed, washingWithoutDetergent.ErrorCode);
        Assert.Equal(TextilePreconditioningErrorCodes.TypeUnknown, unknownType.ErrorCode);
    }

    [Fact]
    public void Unknown_rule_set_version_is_unknown_and_blocks_reporting()
    {
        var assessment = TextilePreconditioningRules.Instance.Evaluate(
            "TEXTILE-SAMPLE-REQUIREMENT@latest",
            ConditioningRecord());

        Assert.Equal(TextilePreconditioningDecisions.Unknown, assessment.Decision);
        Assert.False(assessment.ReportingAllowed);
        Assert.Contains(TextilePreconditioningReasons.RuleSetVersionUnknown, assessment.ReasonCodes);
    }

    [Fact]
    public void Linkage_chain_round_trips_with_frozen_shape()
    {
        var record = ConditioningRecord() with
        {
            Actual = new TextilePreconditioningConditions(24m, 250m, HumidityPercent: 65m)
        };
        var assessment = TextilePreconditioningRules.Instance.Evaluate(TextileContract.RuleSetVersion, record);

        var recordJson = JsonSerializer.Serialize(record, Json);
        var assessmentJson = JsonSerializer.Serialize(assessment, Json);

        Assert.Equal(
            """{"recordId":"PRECON-0001","type":"CONDITIONING","sourceItem":{"id":"FABRIC-LOT-9","version":2},"planned":{"temperatureC":20,"durationMinutes":240,"humidityPercent":65,"program":null,"detergent":null,"dryingMethod":null},"actual":{"temperatureC":24,"durationMinutes":250,"humidityPercent":65,"program":null,"detergent":null,"dryingMethod":null},"tolerances":{"temperatureC":2,"durationMinutes":30,"humidityPercent":4},"operatorId":"operator-a","cuttingPlanId":"CUT-0001","specimenIds":["SPEC-1","SPEC-2"],"outOfToleranceApproval":null}""",
            recordJson);
        Assert.Equal(
            """{"decision":"OUT_OF_TOLERANCE","reasonCodes":["CONDITION_OUT_OF_TOLERANCE","APPROVAL_REQUIRED"],"deviations":[{"field":"temperatureC","plannedValue":20,"actualValue":24,"deviation":4,"toleranceValue":2}],"reportingAllowed":false,"ruleSetVersion":"TEXTILE-SAMPLE-REQUIREMENT@1.0.0"}""",
            assessmentJson);

        var roundTrip = JsonSerializer.Deserialize<TextilePreconditioningRecord>(recordJson, Json);
        Assert.Equal(record.RecordId, roundTrip!.RecordId);
        Assert.Equal(record.SourceItem, roundTrip.SourceItem);
        Assert.Equal(record.CuttingPlanId, roundTrip.CuttingPlanId);
        Assert.Equal(record.SpecimenIds, roundTrip.SpecimenIds);
        Assert.Equal(record.Planned, roundTrip.Planned);
        Assert.Equal(record.Actual, roundTrip.Actual);
    }

    [Fact]
    public void Washing_record_evaluates_temperature_and_duration_only()
    {
        var withinTolerance = TextilePreconditioningRules.Instance.Evaluate(
            TextileContract.RuleSetVersion,
            WashingRecord());
        var outOfTolerance = TextilePreconditioningRules.Instance.Evaluate(
            TextileContract.RuleSetVersion,
            WashingRecord() with
            {
                Actual = new TextilePreconditioningConditions(
                    40m, 90m, Program: "ISO-6330-4N", Detergent: "ECE-A", DryingMethod: "LINE-DRY")
            });

        Assert.Equal(TextilePreconditioningDecisions.WithinTolerance, withinTolerance.Decision);
        var deviation = Assert.Single(outOfTolerance.Deviations);
        Assert.Equal("durationMinutes", deviation.Field);
    }

    [Fact]
    public void Evaluation_is_deterministic_across_repeated_runs()
    {
        var record = ConditioningRecord() with
        {
            Actual = new TextilePreconditioningConditions(24m, 300m, HumidityPercent: 70m)
        };

        var first = TextilePreconditioningRules.Instance.Evaluate(TextileContract.RuleSetVersion, record);
        var second = TextilePreconditioningRules.Instance.Evaluate(TextileContract.RuleSetVersion, record);

        Assert.Equal(JsonSerializer.Serialize(first, Json), JsonSerializer.Serialize(second, Json));
    }

    private static TextilePreconditioningRecord ConditioningRecord() => new(
        "PRECON-0001",
        TextilePreconditioningTypes.Conditioning,
        new TextileVersionedReference("FABRIC-LOT-9", 2),
        new TextilePreconditioningConditions(20m, 240m, HumidityPercent: 65m),
        new TextilePreconditioningConditions(20m, 240m, HumidityPercent: 65m),
        new TextilePreconditioningTolerances(2m, 30m, HumidityPercent: 4m),
        "operator-a",
        CuttingPlanId: "CUT-0001",
        SpecimenIds: ["SPEC-1", "SPEC-2"]);

    private static TextilePreconditioningRecord WashingRecord() => new(
        "PRECON-0002",
        TextilePreconditioningTypes.Washing,
        new TextileVersionedReference("FABRIC-LOT-9", 2),
        new TextilePreconditioningConditions(40m, 45m, Program: "ISO-6330-4N", Detergent: "ECE-A", DryingMethod: "LINE-DRY"),
        new TextilePreconditioningConditions(41m, 50m, Program: "ISO-6330-4N", Detergent: "ECE-A", DryingMethod: "LINE-DRY"),
        new TextilePreconditioningTolerances(3m, 10m),
        "operator-a");
}
