using System.Security.Cryptography;
using System.Text;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

public sealed class ToyDomainException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
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
