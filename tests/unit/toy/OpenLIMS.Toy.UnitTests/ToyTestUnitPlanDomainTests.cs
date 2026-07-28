using OpenLIMS.Contracts.Toy;
using OpenLIMS.Modules.Toy;
using Xunit;

namespace OpenLIMS.Toy.UnitTests;

[Trait("Profile", "toy")]
public sealed class ToyTestUnitPlanDomainTests
{
    [Fact]
    public void Complete_versioned_inputs_produce_explainable_deterministic_demand()
    {
        var first = ToyTestUnitPlanDomain.CalculateDraft(Request());
        var second = ToyTestUnitPlanDomain.CalculateDraft(Request());

        Assert.Equal(ToySampleRequirementDecisions.PendingTechnicalApproval, first.Decision);
        Assert.Equal(first.InputHash, second.InputHash);
        Assert.Equal(
            [
                ToySampleDemandKinds.Base,
                ToySampleDemandKinds.ChemicalMinimum,
                ToySampleDemandKinds.ExclusiveDestructive,
                ToySampleDemandKinds.Parallel,
                ToySampleDemandKinds.Retention,
                ToySampleDemandKinds.RetestReserve
            ],
            first.Components.Select(component => component.Kind).Order(StringComparer.Ordinal));
        Assert.All(first.Components, component => Assert.True(component.SourceRuleRef.Version > 0));
        Assert.Equal(15m, first.Totals.Single(total => total.Dimension == "COUNT").Amount);
        Assert.Equal(10m, first.Totals.Single(total => total.Dimension == "MASS").Amount);
    }

    [Theory]
    [InlineData(0, 1, 2, ToyErrorCodes.TestUnitPlanInvalid)]
    [InlineData(1, 1, 3, ToyErrorCodes.TestUnitPlanInvalid)]
    [InlineData(1, 1, 1, ToyErrorCodes.TestUnitPlanInvalid)]
    public void Parallel_number_and_sequence_must_be_positive_contiguous_and_unique(
        int parallelNumber, int firstOrder, int secondOrder, string expectedCode)
    {
        var request = Request();
        var unit = request.TestUnits[0] with
        {
            ParallelNumber = parallelNumber,
            SequenceSteps =
            [
                request.TestUnits[0].SequenceSteps[0] with { SequenceOrder = firstOrder },
                request.TestUnits[0].SequenceSteps[1] with { SequenceOrder = secondOrder }
            ]
        };

        var exception = Assert.Throws<ToyDomainException>(() =>
            ToyTestUnitPlanDomain.CalculateDraft(request with { TestUnits = [unit, request.TestUnits[1]] }));

        Assert.Equal(expectedCode, exception.ErrorCode);
    }

    [Fact]
    public void One_test_unit_cannot_host_two_tasks_from_the_same_exclusive_destructive_group()
    {
        var request = Request();
        var unit = request.TestUnits[0] with
        {
            SequenceSteps =
            [
                request.TestUnits[0].SequenceSteps[0],
                request.TestUnits[0].SequenceSteps[1] with
                {
                    Destructive = true,
                    ExclusiveDestructiveGroupId = "DROP-CRUSH",
                    ShareRuleRef = null
                }
            ]
        };

        var exception = Assert.Throws<ToyDomainException>(() =>
            ToyTestUnitPlanDomain.CalculateDraft(request with { TestUnits = [unit, request.TestUnits[1]] }));

        Assert.Equal(ToyErrorCodes.DestructiveTestUnitConflict, exception.ErrorCode);
    }

    [Fact]
    public void Unknown_applicability_or_missing_chemical_minimum_fails_closed()
    {
        var request = Request();
        var unknown = request.DemandInputs
            .Select(input => input.Kind == ToySampleDemandKinds.Base
                ? input with { Applicability = ToyApplicabilityDecisions.Unknown }
                : input)
            .ToArray();
        var missingChemical = request.DemandInputs
            .Where(input => input.Kind != ToySampleDemandKinds.ChemicalMinimum)
            .ToArray();

        Assert.Equal(ToyErrorCodes.SampleRequirementUnknown,
            Assert.Throws<ToyDomainException>(() =>
                ToyTestUnitPlanDomain.CalculateDraft(request with { DemandInputs = unknown })).ErrorCode);
        Assert.Equal(ToyErrorCodes.SampleRequirementUnknown,
            Assert.Throws<ToyDomainException>(() =>
                ToyTestUnitPlanDomain.CalculateDraft(request with { DemandInputs = missingChemical })).ErrorCode);
    }

    [Fact]
    public void One_rule_reference_cannot_silently_change_dimension_or_unit()
    {
        var request = Request();
        var conflicting = request.DemandInputs
            .Append(request.DemandInputs[0] with
            {
                ComponentId = "base-conflict",
                Amount = 1m,
                Dimension = "MASS",
                Unit = "g"
            })
            .ToArray();

        var exception = Assert.Throws<ToyDomainException>(() =>
            ToyTestUnitPlanDomain.CalculateDraft(request with { DemandInputs = conflicting }));

        Assert.Equal(ToyErrorCodes.SampleRequirementUnknown, exception.ErrorCode);
    }

    [Fact]
    public void Approval_requires_pending_known_demand_and_matching_frozen_hash()
    {
        var draft = ToyTestUnitPlanDomain.CalculateDraft(Request());

        ToyTestUnitPlanDomain.RequireApprovable(draft.Decision, draft.InputHash, draft.InputHash);
        Assert.Equal(ToyErrorCodes.SampleRequirementUnknown,
            Assert.Throws<ToyDomainException>(() =>
                ToyTestUnitPlanDomain.RequireApprovable(
                    ToySampleRequirementDecisions.Unknown, draft.InputHash, draft.InputHash)).ErrorCode);
        Assert.Equal(ToyErrorCodes.ValidationFailed,
            Assert.Throws<ToyDomainException>(() =>
                ToyTestUnitPlanDomain.RequireApprovable(draft.Decision, draft.InputHash, "stale-hash")).ErrorCode);
    }

    [Fact]
    public void Downstream_use_requires_an_approved_requirement_and_exact_totals()
    {
        var draft = ToyTestUnitPlanDomain.CalculateDraft(Request());
        var quantity = new[]
        {
            new ToyQuantityGateInput("qty-count", 4, "SAMPLE-QUANTITY@1.0.0", 15m, "COUNT", "piece", "res-count"),
            new ToyQuantityGateInput("qty-mass", 2, "SAMPLE-QUANTITY@1.0.0", 10m, "MASS", "g", "res-mass")
        };

        ToyTestUnitPlanDomain.ValidateDownstreamRequest(
            ToySampleRequirementDecisions.Approved, draft.Totals, quantity);

        Assert.Equal(ToyErrorCodes.SampleRequirementNotApproved,
            Assert.Throws<ToyDomainException>(() => ToyTestUnitPlanDomain.ValidateDownstreamRequest(
                draft.Decision, draft.Totals, quantity)).ErrorCode);
        Assert.Equal(ToyErrorCodes.SampleRequirementUnknown,
            Assert.Throws<ToyDomainException>(() => ToyTestUnitPlanDomain.ValidateDownstreamRequest(
                ToySampleRequirementDecisions.Approved,
                draft.Totals,
                quantity.Select(item => item.Dimension == "MASS" ? item with { Amount = 9m } : item).ToArray())).ErrorCode);
    }

    private static CreateToyTestUnitPlanRequest Request()
    {
        var hazard = new ToyVersionedReference("MECHANICAL", 3);
        var unit1 = new CreateToyTestUnitInput(
            "00000000000000000000000000000301",
            new ToyVersionedReference("physical-1", 7),
            [hazard],
            1,
            [
                new CreateToySequenceStepInput(
                    "step-drop", 1, new ToyVersionedReference("DROP", 2), true, "DROP-CRUSH", null),
                new CreateToySequenceStepInput(
                    "step-visual", 2, new ToyVersionedReference("VISUAL", 1), false, null,
                    new ToyVersionedReference("NONDESTRUCTIVE-SHARE", 1))
            ]);
        var unit2 = new CreateToyTestUnitInput(
            "00000000000000000000000000000302",
            new ToyVersionedReference("physical-2", 4),
            [hazard],
            2,
            [new CreateToySequenceStepInput(
                "step-crush", 1, new ToyVersionedReference("CRUSH", 2), true, "DROP-CRUSH", null)]);

        return new CreateToyTestUnitPlanRequest(
            ToyTestUnitPlanContract.RuleSetVersion,
            new ToyObjectContext("LEGAL-A", "LAB-A"),
            0,
            9,
            2,
            3,
            "scope-matrix-1",
            5,
            [new ToyVersionedReference("scope-line-1", 2)],
            [new ToyVersionedReference("sample-rules", 4)],
            [unit1, unit2],
            [
                Demand("base", ToySampleDemandKinds.Base, 4m, "COUNT", "piece", "base-rule", 1),
                Demand("parallel", ToySampleDemandKinds.Parallel, 4m, "COUNT", "piece", "parallel-rule", 1),
                Demand("exclusive", ToySampleDemandKinds.ExclusiveDestructive, 2m, "COUNT", "piece", "exclusive-rule", 2),
                Demand("chemical", ToySampleDemandKinds.ChemicalMinimum, 10m, "MASS", "g", "chemical-rule", 3),
                Demand("retest", ToySampleDemandKinds.RetestReserve, 3m, "COUNT", "piece", "retest-rule", 1),
                Demand("retention", ToySampleDemandKinds.Retention, 2m, "COUNT", "piece", "retention-rule", 1)
            ]);
    }

    private static ToySampleDemandInput Demand(
        string id, string kind, decimal amount, string dimension, string unit, string rule, long version) =>
        new(
            id,
            kind,
            new ToyVersionedReference("MECHANICAL", 3),
            null,
            amount,
            dimension,
            unit,
            new ToyVersionedReference(rule, version),
            ToyApplicabilityDecisions.Allowed);
}
