using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Textile;

namespace OpenLIMS.Modules.Textile;

internal sealed record TextileRequirementDraft(
    TextileSampleRequirementCalculation Calculation,
    TextileSampleRequirementResult Result,
    string InputHash);

internal static partial class TextileRuntimeDomain
{
    private static readonly Regex StableIdentifier = StableIdentifierPattern();
    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);

    internal static TextileRequirementDraft CalculateRequirement(
        CreateTextileSampleRequirementRequest request)
    {
        if (request is null ||
            !IsIdentifier(request.RequirementId) ||
            request.ExpectedCurrentVersion < 0 ||
            !IsScope(request.ObjectScope) ||
            request.Calculation is null)
        {
            throw new TextileOperationException(TextileErrorCodes.ValidationFailed);
        }

        var result = TextileSampleRequirementRules.Instance.Calculate(request.Calculation);
        return new TextileRequirementDraft(
            request.Calculation,
            result,
            Hash(request));
    }

    internal static void ValidatePlan(
        CreateTextileCuttingPlanRequest request,
        TextileSampleRequirementRecord requirement)
    {
        if (request is null ||
            requirement is null ||
            !IsIdentifier(request.CuttingPlanId) ||
            request.ExpectedCurrentVersion < 0 ||
            !IsIdentifier(request.SampleRequirementId) ||
            request.SampleRequirementVersion < 1 ||
            string.IsNullOrWhiteSpace(request.SampleRequirementInputHash) ||
            !string.Equals(request.CuttingPlanId, request.Plan?.CuttingPlanId, StringComparison.Ordinal) ||
            !string.Equals(request.SampleRequirementId, requirement.RequirementId, StringComparison.Ordinal) ||
            request.SampleRequirementVersion != requirement.Version ||
            !string.Equals(
                request.SampleRequirementInputHash,
                requirement.InputHash,
                StringComparison.Ordinal) ||
            !string.Equals(request.RuleSetVersion, requirement.Result.RuleSetVersion, StringComparison.Ordinal))
        {
            throw new TextileOperationException(TextileErrorCodes.ValidationFailed);
        }

        if (!string.Equals(request.RuleSetVersion, TextileContract.RuleSetVersion, StringComparison.Ordinal))
            throw new TextileOperationException(TextileErrorCodes.ApplicabilityUnknown);

        TextileSampleRequirementRules.ValidateCuttingPlan(request.Plan);
    }

    internal static string PlanInputHash(CreateTextileCuttingPlanRequest request) => Hash(request);

    internal static void RequireApprovable(
        TextileCuttingPlanResult plan,
        ApproveTextileCuttingPlanRequest request)
    {
        if (plan is null ||
            request is null ||
            request.ExpectedCurrentVersion != plan.Version ||
            !string.Equals(plan.State, TextileCuttingPlanStates.Draft, StringComparison.Ordinal) ||
            !string.Equals(
                request.SampleRequirementInputHash,
                plan.SampleRequirement.InputHash,
                StringComparison.Ordinal) ||
            !string.Equals(request.RuleSetVersion, plan.RuleSetVersion, StringComparison.Ordinal) ||
            !string.Equals(request.RuleSetVersion, TextileContract.RuleSetVersion, StringComparison.Ordinal))
        {
            throw new TextileOperationException(TextileErrorCodes.ValidationFailed);
        }

        if (!string.Equals(
                plan.SampleRequirement.Result.Decision,
                TextileCalculationDecisions.Sufficient,
                StringComparison.Ordinal))
        {
            throw new TextileOperationException(TextileErrorCodes.SampleRequirementNotApprovable);
        }
    }

    internal static TextileCuttingPlanStatusDecision EvaluateStatus(
        TextileCuttingPlanResult? plan,
        string ruleSetVersion)
    {
        if (plan is null ||
            !string.Equals(ruleSetVersion, TextileContract.RuleSetVersion, StringComparison.Ordinal) ||
            !string.Equals(plan.RuleSetVersion, ruleSetVersion, StringComparison.Ordinal))
        {
            return new TextileCuttingPlanStatusDecision(
                TextileStatusDecisions.Unknown,
                [TextileStatusReasons.EvidenceUnknown],
                plan?.CuttingPlanId ?? string.Empty,
                plan?.Version ?? 0,
                plan?.SampleRequirement.RequirementId,
                plan?.SampleRequirement.Version,
                ruleSetVersion);
        }

        var allowed = string.Equals(plan.State, TextileCuttingPlanStates.Approved, StringComparison.Ordinal) &&
                      plan.Approval is not null &&
                      string.Equals(
                          plan.SampleRequirement.Result.Decision,
                          TextileCalculationDecisions.Sufficient,
                          StringComparison.Ordinal) &&
                      string.Equals(
                          plan.Approval.SampleRequirementInputHash,
                          plan.SampleRequirement.InputHash,
                          StringComparison.Ordinal) &&
                      string.Equals(plan.Approval.RuleSetVersion, ruleSetVersion, StringComparison.Ordinal);

        return new TextileCuttingPlanStatusDecision(
            allowed ? TextileStatusDecisions.Allowed : TextileStatusDecisions.Blocked,
            [allowed ? TextileStatusReasons.PlanApproved : TextileStatusReasons.PlanNotApproved],
            plan.CuttingPlanId,
            plan.Version,
            plan.SampleRequirement.RequirementId,
            plan.SampleRequirement.Version,
            ruleSetVersion);
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, CanonicalJson)));

    private static bool IsScope(TextileObjectScope? value) =>
        value is not null && IsIdentifier(value.LegalEntityId) && IsIdentifier(value.LaboratoryId);

    private static bool IsIdentifier(string? value) =>
        value is not null && StableIdentifier.IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierPattern();
}
