using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

public sealed class ToyDomainException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

internal static class ToyTestUnitPlanDomain
{
    public static ToySampleDemandCalculation CalculateDraft(CreateToyTestUnitPlanRequest? request)
    {
        ValidateRequest(request);
        var input = request!;
        ValidateTestUnits(input.TestUnits);
        ValidateDemand(input);

        var components = input.DemandInputs
            .Select(item => new ToySampleDemandComponent(
                item.ComponentId,
                item.Kind,
                item.HazardDomainRef,
                item.TestUnitId,
                item.Amount,
                item.Dimension,
                item.Unit,
                item.SourceRuleRef))
            .OrderBy(component => component.Kind, StringComparer.Ordinal)
            .ThenBy(component => component.ComponentId, StringComparer.Ordinal)
            .ToArray();
        var totals = components
            .GroupBy(component => (component.Dimension, component.Unit))
            .Select(group => new ToySampleDemandTotal(group.Key.Dimension, group.Key.Unit, group.Sum(item => item.Amount)))
            .OrderBy(total => total.Dimension, StringComparer.Ordinal)
            .ThenBy(total => total.Unit, StringComparer.Ordinal)
            .ToArray();

        return new ToySampleDemandCalculation(
            components,
            totals,
            ToySampleRequirementDecisions.PendingTechnicalApproval,
            [],
            HashCanonical(input),
            input.RuleSetVersion);
    }

    public static void RequireApprovable(string decision, string frozenInputHash, string suppliedInputHash)
    {
        if (!string.Equals(decision, ToySampleRequirementDecisions.PendingTechnicalApproval, StringComparison.Ordinal))
            throw new ToyDomainException(ToyErrorCodes.SampleRequirementUnknown);
        if (string.IsNullOrWhiteSpace(suppliedInputHash) ||
            !string.Equals(frozenInputHash, suppliedInputHash, StringComparison.Ordinal))
        {
            throw new ToyDomainException(ToyErrorCodes.ValidationFailed);
        }
    }

    public static void ValidateDownstreamRequest(
        string requirementDecision,
        IReadOnlyList<ToySampleDemandTotal> totals,
        IReadOnlyList<ToyQuantityGateInput>? quantityChecks)
    {
        if (!string.Equals(requirementDecision, ToySampleRequirementDecisions.Approved, StringComparison.Ordinal))
            throw new ToyDomainException(ToyErrorCodes.SampleRequirementNotApproved);
        if (quantityChecks is null || quantityChecks.Count == 0 ||
            quantityChecks.Any(item =>
                string.IsNullOrWhiteSpace(item.QuantityAccountId) ||
                item.ExpectedAccountVersion < 1 ||
                string.IsNullOrWhiteSpace(item.RuleSetVersion) ||
                item.Amount < 0m ||
                string.IsNullOrWhiteSpace(item.Dimension) ||
                string.IsNullOrWhiteSpace(item.Unit) ||
                string.IsNullOrWhiteSpace(item.ReservationRef)) ||
            quantityChecks.Select(item => item.ReservationRef).Distinct(StringComparer.Ordinal).Count() != quantityChecks.Count)
        {
            throw new ToyDomainException(ToyErrorCodes.SampleRequirementUnknown);
        }

        var requested = quantityChecks
            .GroupBy(item => (item.Dimension, item.Unit))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        var required = totals.ToDictionary(item => (item.Dimension, item.Unit), item => item.Amount);
        if (requested.Count != required.Count ||
            required.Any(item => !requested.TryGetValue(item.Key, out var amount) || amount != item.Value))
        {
            throw new ToyDomainException(ToyErrorCodes.SampleRequirementUnknown);
        }
    }

    public static void ValidateAllocationChecks(
        IReadOnlyList<ToyTestUnitEntry> testUnits,
        IReadOnlyList<ToyAllocationGateInput>? allocationChecks)
    {
        if (allocationChecks is null || allocationChecks.Count == 0 ||
            allocationChecks.Any(item =>
                string.IsNullOrWhiteSpace(item.AllocationId) ||
                item.ExpectedSubjectAllocationVersion < 1 ||
                string.IsNullOrWhiteSpace(item.RuleSetVersion) ||
                string.IsNullOrWhiteSpace(item.TestUnitId) ||
                string.IsNullOrWhiteSpace(item.SequenceStepId)) ||
            allocationChecks.Select(item => item.AllocationId).Distinct(StringComparer.Ordinal).Count() != allocationChecks.Count)
        {
            throw new ToyDomainException(ToyErrorCodes.TestUnitPlanInvalid);
        }

        var knownSteps = testUnits
            .SelectMany(unit => unit.SequenceSteps.Select(step => (unit.TestUnitId, step.StepId)))
            .ToHashSet();
        if (allocationChecks.Any(item => !knownSteps.Contains((item.TestUnitId, item.SequenceStepId))))
            throw new ToyDomainException(ToyErrorCodes.TestUnitPlanInvalid);
    }

    private static void ValidateRequest(CreateToyTestUnitPlanRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ToyTestUnitPlanContract.RuleSetVersion, StringComparison.Ordinal) ||
            request.ObjectScope is null ||
            string.IsNullOrWhiteSpace(request.ObjectScope.LegalEntityId) ||
            string.IsNullOrWhiteSpace(request.ObjectScope.LaboratoryId) ||
            request.ExpectedCurrentVersion < 0 ||
            request.ProductVersion < 1 ||
            request.AgeGradeDecisionVersion < 1 ||
            request.AccessibilityAssessmentVersion < 1 ||
            string.IsNullOrWhiteSpace(request.ScopeMatrixId) ||
            request.ScopeMatrixVersion < 1 ||
            !ValidReferences(request.ScopeLineRefs) ||
            !ValidReferences(request.SampleRuleRefs) ||
            request.TestUnits is null || request.TestUnits.Count == 0 ||
            request.DemandInputs is null || request.DemandInputs.Count == 0)
        {
            throw new ToyDomainException(ToyErrorCodes.TestUnitPlanInvalid);
        }
    }

    private static void ValidateTestUnits(IReadOnlyList<CreateToyTestUnitInput> testUnits)
    {
        if (testUnits.Select(item => item.TestUnitId).Distinct(StringComparer.Ordinal).Count() != testUnits.Count ||
            testUnits.Select(item => item.ParallelNumber).Distinct().Count() != testUnits.Count)
        {
            throw new ToyDomainException(ToyErrorCodes.TestUnitPlanInvalid);
        }

        foreach (var unit in testUnits)
        {
            if (!Guid.TryParse(unit.TestUnitId, out _) ||
                unit.PhysicalObjectRef is null || !ValidReference(unit.PhysicalObjectRef) ||
                !ValidReferences(unit.HazardDomainRefs) ||
                unit.ParallelNumber < 1 ||
                unit.SequenceSteps is null || unit.SequenceSteps.Count == 0)
            {
                throw new ToyDomainException(ToyErrorCodes.TestUnitPlanInvalid);
            }

            var ordered = unit.SequenceSteps.Select(step => step.SequenceOrder).Order().ToArray();
            if (!ordered.SequenceEqual(Enumerable.Range(1, unit.SequenceSteps.Count)) ||
                unit.SequenceSteps.Select(step => step.StepId).Distinct(StringComparer.Ordinal).Count() != unit.SequenceSteps.Count)
            {
                throw new ToyDomainException(ToyErrorCodes.TestUnitPlanInvalid);
            }

            foreach (var step in unit.SequenceSteps)
            {
                var hasExclusiveGroup = !string.IsNullOrWhiteSpace(step.ExclusiveDestructiveGroupId);
                if (string.IsNullOrWhiteSpace(step.StepId) ||
                    step.TaskRef is null || !ValidReference(step.TaskRef) ||
                    (hasExclusiveGroup && !step.Destructive) ||
                    (step.Destructive && step.ShareRuleRef is not null) ||
                    (!step.Destructive && step.ShareRuleRef is not null && !ValidReference(step.ShareRuleRef)))
                {
                    throw new ToyDomainException(ToyErrorCodes.TestUnitPlanInvalid);
                }
            }

            var duplicateExclusiveGroup = unit.SequenceSteps
                .Where(step => step.Destructive && !string.IsNullOrWhiteSpace(step.ExclusiveDestructiveGroupId))
                .GroupBy(step => step.ExclusiveDestructiveGroupId!, StringComparer.Ordinal)
                .Any(group => group.Count() > 1);
            if (duplicateExclusiveGroup)
                throw new ToyDomainException(ToyErrorCodes.DestructiveTestUnitConflict);
        }
    }

    private static void ValidateDemand(CreateToyTestUnitPlanRequest request)
    {
        if (request.DemandInputs.Select(item => item.ComponentId).Distinct(StringComparer.Ordinal).Count() !=
            request.DemandInputs.Count ||
            ToySampleDemandKinds.All.Any(kind =>
                !request.DemandInputs.Any(item => string.Equals(item.Kind, kind, StringComparison.Ordinal))))
        {
            throw new ToyDomainException(ToyErrorCodes.SampleRequirementUnknown);
        }

        var knownUnits = request.TestUnits.Select(item => item.TestUnitId).ToHashSet(StringComparer.Ordinal);
        var knownHazards = request.TestUnits
            .SelectMany(item => item.HazardDomainRefs)
            .Select(ReferenceKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var input in request.DemandInputs)
        {
            var strictlyPositive = input.Kind is ToySampleDemandKinds.Base or ToySampleDemandKinds.ChemicalMinimum;
            if (string.IsNullOrWhiteSpace(input.ComponentId) ||
                !ToySampleDemandKinds.All.Contains(input.Kind, StringComparer.Ordinal) ||
                !string.Equals(input.Applicability, ToyApplicabilityDecisions.Allowed, StringComparison.Ordinal) ||
                (strictlyPositive ? input.Amount <= 0m : input.Amount < 0m) ||
                string.IsNullOrWhiteSpace(input.Dimension) ||
                string.IsNullOrWhiteSpace(input.Unit) ||
                input.SourceRuleRef is null || !ValidReference(input.SourceRuleRef) ||
                (input.HazardDomainRef is not null &&
                    (!ValidReference(input.HazardDomainRef) || !knownHazards.Contains(ReferenceKey(input.HazardDomainRef)))) ||
                (input.TestUnitId is not null && !knownUnits.Contains(input.TestUnitId)))
            {
                throw new ToyDomainException(ToyErrorCodes.SampleRequirementUnknown);
            }
        }

        var ruleUnitConflict = request.DemandInputs
            .GroupBy(item => ReferenceKey(item.SourceRuleRef), StringComparer.Ordinal)
            .Any(group => group.Select(item => (item.Dimension, item.Unit)).Distinct().Count() > 1);
        if (ruleUnitConflict)
            throw new ToyDomainException(ToyErrorCodes.SampleRequirementUnknown);
    }

    private static string HashCanonical(CreateToyTestUnitPlanRequest request)
    {
        var canonical = new
        {
            request.RuleSetVersion,
            request.ProductVersion,
            request.AgeGradeDecisionVersion,
            request.AccessibilityAssessmentVersion,
            request.ScopeMatrixId,
            request.ScopeMatrixVersion,
            ScopeLineRefs = request.ScopeLineRefs.OrderBy(ReferenceKey).ToArray(),
            SampleRuleRefs = request.SampleRuleRefs.OrderBy(ReferenceKey).ToArray(),
            TestUnits = request.TestUnits.OrderBy(item => item.TestUnitId, StringComparer.Ordinal).Select(unit => new
            {
                unit.TestUnitId,
                unit.PhysicalObjectRef,
                HazardDomainRefs = unit.HazardDomainRefs.OrderBy(ReferenceKey).ToArray(),
                unit.ParallelNumber,
                SequenceSteps = unit.SequenceSteps.OrderBy(step => step.SequenceOrder).ToArray()
            }).ToArray(),
            DemandInputs = request.DemandInputs.OrderBy(item => item.ComponentId, StringComparer.Ordinal).ToArray()
        };
        return ToyDomain.HashTarget(JsonSerializer.Serialize(canonical));
    }

    private static bool ValidReferences(IReadOnlyList<ToyVersionedReference>? references) =>
        references is { Count: > 0 } &&
        references.All(ValidReference) &&
        references.Select(ReferenceKey).Distinct(StringComparer.Ordinal).Count() == references.Count;

    private static bool ValidReference(ToyVersionedReference reference) =>
        !string.IsNullOrWhiteSpace(reference.Id) && reference.Version > 0;

    private static string ReferenceKey(ToyVersionedReference reference) =>
        $"{reference.Id}@{reference.Version}";
}

/// <summary>
/// DEV-024 toy pack rules: the separation of a customer's age claim from the
/// laboratory's own determination (OPS-TOY-001), re-determination by appended
/// version rather than in-place edit (OPS-TOY-002), and the accessibility
/// assessment whose newly exposed parts pull mechanical, chemical and labeling
/// scope back open (OPS-TOY-003).
/// </summary>
internal static class ToyDomain
{
    /// <summary>Nothing under one month reads as a real toy age grade.</summary>
    private const int MinimumAgeMonths = 0;
    private const int MaximumAgeMonths = 18 * 12;

    public static RecordAgeDeclarationRequest ValidateDeclaration(RecordAgeDeclarationRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ToyContract.RuleSetVersion, StringComparison.Ordinal) ||
            request.ObjectScope is null ||
            string.IsNullOrWhiteSpace(request.ObjectScope.LegalEntityId) ||
            string.IsNullOrWhiteSpace(request.ObjectScope.LaboratoryId) ||
            request.DeclaredMinimumAgeMonths < MinimumAgeMonths ||
            request.DeclaredMinimumAgeMonths > MaximumAgeMonths ||
            string.IsNullOrWhiteSpace(request.IntendedUse) ||
            string.IsNullOrWhiteSpace(request.DeclarationSource))
        {
            throw new ToyDomainException(ToyErrorCodes.ValidationFailed);
        }

        return request;
    }

    /// <summary>
    /// OPS-TOY-001: a determination without its basis, its standard or its
    /// approver is not a determination — it is an opinion, and it must not
    /// reach storage.
    /// </summary>
    public static RecordAgeGradeDecisionRequest ValidateDecision(RecordAgeGradeDecisionRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ToyContract.RuleSetVersion, StringComparison.Ordinal) ||
            request.ObjectScope is null ||
            string.IsNullOrWhiteSpace(request.ObjectScope.LegalEntityId) ||
            string.IsNullOrWhiteSpace(request.ObjectScope.LaboratoryId) ||
            request.MinimumAgeMonths < MinimumAgeMonths ||
            request.MinimumAgeMonths > MaximumAgeMonths ||
            string.IsNullOrWhiteSpace(request.Rationale) ||
            request.StandardRef is null ||
            string.IsNullOrWhiteSpace(request.StandardRef.Id) ||
            request.StandardRef.Version < 1 ||
            string.IsNullOrWhiteSpace(request.ApprovedBy))
        {
            throw new ToyDomainException(ToyErrorCodes.ValidationFailed);
        }

        return request;
    }

    public static RecordAccessibilityAssessmentRequest ValidateAssessment(
        RecordAccessibilityAssessmentRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ToyContract.RuleSetVersion, StringComparison.Ordinal) ||
            request.ObjectScope is null ||
            string.IsNullOrWhiteSpace(request.ObjectScope.LegalEntityId) ||
            string.IsNullOrWhiteSpace(request.ObjectScope.LaboratoryId) ||
            !ToyAssessmentStages.All.Contains(request.Stage, StringComparer.Ordinal) ||
            request.AccessibleParts is null ||
            request.AccessibleParts.Count == 0 ||
            request.AccessibleParts.Any(string.IsNullOrWhiteSpace) ||
            request.AccessibleParts.Distinct(StringComparer.Ordinal).Count() != request.AccessibleParts.Count)
        {
            throw new ToyDomainException(ToyErrorCodes.ValidationFailed);
        }

        // An abuse assessment that cannot name its abuse event is untraceable;
        // an initial one that names one is describing something it did not do.
        var isAbuse = string.Equals(request.Stage, ToyAssessmentStages.AfterAbuse, StringComparison.Ordinal);
        var abuseRefInvalid = isAbuse
            ? string.IsNullOrWhiteSpace(request.AbuseEventRef)
            : request.AbuseEventRef is not null;
        if (abuseRefInvalid)
            throw new ToyDomainException(ToyErrorCodes.ValidationFailed);

        return request;
    }

    /// <summary>
    /// OPS-TOY-003: the as-received state is the baseline every later stage is
    /// compared against, so it cannot be skipped or arrive late.
    /// </summary>
    public static void RequireInitialFirst(string stage, int nextVersionNumber)
    {
        var isInitial = string.Equals(stage, ToyAssessmentStages.Initial, StringComparison.Ordinal);
        if (isInitial != (nextVersionNumber == 1))
            throw new ToyDomainException(ToyErrorCodes.ValidationFailed);
    }

    public static void RequireFreezable(ToyAgeGradeDecisionEntry? decision)
    {
        if (decision is null)
            throw new ToyDomainException(ToyErrorCodes.DecisionNotFound);
        if (!string.Equals(decision.State, ToyDecisionStates.Draft, StringComparison.Ordinal))
            throw new ToyDomainException(ToyErrorCodes.DecisionFrozen);
    }

    public static void RequirePending(ToyReassessmentTriggerEntry? trigger)
    {
        if (trigger is null ||
            !string.Equals(trigger.State, ToyTriggerStates.Pending, StringComparison.Ordinal))
        {
            throw new ToyDomainException(ToyErrorCodes.ReassessmentNotPending);
        }
    }

    public static ResolveReassessmentTriggerRequest ValidateResolution(
        ResolveReassessmentTriggerRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ToyContract.RuleSetVersion, StringComparison.Ordinal) ||
            request.ResolutionRef is null ||
            string.IsNullOrWhiteSpace(request.ResolutionRef.Id) ||
            request.ResolutionRef.Version < 1)
        {
            throw new ToyDomainException(ToyErrorCodes.ValidationFailed);
        }

        return request;
    }

    /// <summary>
    /// The parts this assessment exposes that its predecessor did not. Parts
    /// that merely disappear are not a finding: losing access to something
    /// cannot bring new requirements with it.
    /// </summary>
    public static IReadOnlyList<string> NewlyExposedParts(
        IReadOnlyList<string> currentParts,
        ToyAccessibilityAssessmentEntry? previous)
    {
        if (previous is null)
            return [];
        var known = new HashSet<string>(previous.AccessibleParts, StringComparer.Ordinal);
        return [.. currentParts.Where(part => !known.Contains(part)).Order(StringComparer.Ordinal)];
    }

    public static string ResolveAccessibilityStatus(IEnumerable<ToyReassessmentTriggerEntry> triggers) =>
        triggers.Any(trigger => string.Equals(trigger.State, ToyTriggerStates.Pending, StringComparison.Ordinal))
            ? ToyAccessibilityStatuses.ReassessmentPending
            : ToyAccessibilityStatuses.Settled;

    public static ToyAgeGradeDecisionEntry? ResolveEffectiveDecision(
        IEnumerable<ToyAgeGradeDecisionEntry> decisions) =>
        decisions.LastOrDefault(decision =>
            string.Equals(decision.State, ToyDecisionStates.Effective, StringComparison.Ordinal));

    public static ToyAgeGradeStatusResult EvaluateStatus(
        ToyAgeGradeStatusRequest request,
        ToyProductOverview? overview)
    {
        if (!string.Equals(request.RuleSetVersion, ToyContract.RuleSetVersion, StringComparison.Ordinal))
            return Unknown(request, ToyAgeGradeReasons.RuleSetVersionUnknown, null);
        if (overview is null)
            return Unknown(request, ToyAgeGradeReasons.ToyUnavailable, null);
        if (request.ExpectedProductVersion != overview.Version)
            return Unknown(request, ToyAgeGradeReasons.VersionMismatch, overview.Version);

        var reasons = new List<string>();
        if (overview.EffectiveDecision is null)
            reasons.Add(ToyAgeGradeReasons.NoEffectiveDecision);
        if (string.Equals(
                overview.AccessibilityStatus,
                ToyAccessibilityStatuses.ReassessmentPending,
                StringComparison.Ordinal))
        {
            reasons.Add(ToyAgeGradeReasons.ReassessmentPending);
        }

        return new ToyAgeGradeStatusResult(
            reasons.Count == 0 ? ToyAgeGradeDecisions.Allowed : ToyAgeGradeDecisions.Blocked,
            reasons,
            request.ProductId,
            overview.Version,
            overview.EffectiveDecision?.VersionNumber,
            overview.EffectiveDecision?.MinimumAgeMonths,
            overview.AccessibilityStatus,
            ToyContract.RuleSetVersion);
    }

    public static string HashTarget(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ToyAgeGradeStatusResult Unknown(
        ToyAgeGradeStatusRequest request,
        string reason,
        long? currentVersion) => new(
        ToyAgeGradeDecisions.Unknown,
        [reason],
        request.ProductId,
        currentVersion,
        null,
        null,
        ToyAccessibilityStatuses.ReassessmentPending,
        ToyContract.RuleSetVersion);
}
