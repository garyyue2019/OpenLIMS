using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Scope;

namespace OpenLIMS.Modules.Scope;

internal sealed class ScopeDomainException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

internal static class ScopeRules
{
    private const int MaximumLines = 500;
    private static readonly Regex StableIdentifier = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static ScopeObjectContext NormalizeObjectScope(ScopeObjectContext? value)
    {
        if (value is null)
            throw new ScopeDomainException(ScopeErrorCodes.ValidationFailed);

        return new ScopeObjectContext(
            Identifier(value.LegalEntityId),
            Identifier(value.LaboratoryId),
            Identifier(value.CustomerId),
            Identifier(value.ServiceOrderId),
            Identifier(value.ProductCategory));
    }

    public static IReadOnlyList<ScopeLineResult> ValidateAndNormalize(
        SubmitScopeMatrixVersionRequest? request)
    {
        if (request is null || request.ExpectedCurrentVersion < 0 ||
            !string.Equals(request.RuleSetVersion, ScopeContract.RuleSetVersion, StringComparison.Ordinal) ||
            request.Lines is null || request.Lines.Count is < 1 or > MaximumLines)
        {
            throw new ScopeDomainException(request is not null &&
                !string.Equals(request.RuleSetVersion, ScopeContract.RuleSetVersion, StringComparison.Ordinal)
                    ? ScopeErrorCodes.ApplicabilityUnknown
                    : ScopeErrorCodes.ValidationFailed);
        }

        _ = NormalizeObjectScope(request.ObjectScope);
        var results = new List<ScopeLineResult>(request.Lines.Count);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in request.Lines)
        {
            var normalized = NormalizeLine(line);
            if (!identities.Add(normalized.ScopeLineId))
                throw new ScopeDomainException(ScopeErrorCodes.ValidationFailed);
            results.Add(normalized);
        }

        return results;
    }

    public static bool IsComplete(ScopeMatrixVersionResult result)
    {
        try
        {
            if (!string.Equals(result.State, ScopeMatrixStates.Approved, StringComparison.Ordinal) ||
                !string.Equals(result.RuleSetVersion, ScopeContract.RuleSetVersion, StringComparison.Ordinal) ||
                result.Version < 1 || result.Lines.Count < 1)
            {
                return false;
            }

            _ = NormalizeObjectScope(result.ObjectScope);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in result.Lines)
            {
                var normalized = NormalizeLine(new ScopeLineInput(
                    line.SubjectType,
                    line.Subject,
                    line.TargetMarket,
                    line.RequirementClause,
                    line.TestItem,
                    line.Method,
                    line.MethodOption,
                    line.SampleRequirement,
                    line.EvaluationMode,
                    line.WorkCenter,
                    line.ReportPosition,
                    line.LimitRule,
                    line.DecisionRule,
                    line.NonEvaluationReason,
                    line.WaiverApproval));
                if (!string.Equals(normalized.ScopeLineId, line.ScopeLineId, StringComparison.Ordinal) ||
                    !identities.Add(normalized.ScopeLineId))
                {
                    return false;
                }
            }

            return true;
        }
        catch (ScopeDomainException)
        {
            return false;
        }
    }

    public static ScopeProductionEligibilityResult Evaluate(
        ScopeProductionEligibilityRequest request,
        ScopeMatrixVersionResult? current)
    {
        if (!string.Equals(request.RuleSetVersion, ScopeContract.RuleSetVersion, StringComparison.Ordinal))
            return Unknown(current, ScopeEligibilityReasons.RuleSetVersionUnknown);
        if (current is null)
            return Blocked(ScopeEligibilityReasons.ApprovedVersionRequired);
        if (request.ExpectedMatrixVersion != current.Version)
            return Unknown(current, ScopeEligibilityReasons.MatrixVersionMismatch);
        if (!IsComplete(current))
            return Unknown(current, ScopeEligibilityReasons.ScopeIncomplete);

        return new ScopeProductionEligibilityResult(
            ScopeEligibilityDecisions.Allowed,
            [],
            current.ScopeMatrixId,
            current.Version,
            ScopeContract.RuleSetVersion);
    }

    public static string HashTarget(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }

    private static ScopeLineResult NormalizeLine(ScopeLineInput? line)
    {
        if (line is null || !KnownSubjectType(line.SubjectType))
            throw new ScopeDomainException(ScopeErrorCodes.ValidationFailed);

        var subjectType = line.SubjectType.Trim();
        var subject = Reference(line.Subject);
        var targetMarket = Reference(line.TargetMarket);
        var requirementClause = Reference(line.RequirementClause);
        var testItem = Reference(line.TestItem);
        var method = Reference(line.Method);
        var sampleRequirement = Reference(line.SampleRequirement);
        var workCenter = Reference(line.WorkCenter);
        var methodOption = Identifier(line.MethodOption);
        var reportPosition = Identifier(line.ReportPosition);
        var evaluationMode = line.EvaluationMode?.Trim() ?? string.Empty;
        var limitRule = OptionalReference(line.LimitRule);
        var decisionRule = OptionalReference(line.DecisionRule);
        var waiver = OptionalReference(line.WaiverApproval);
        var reason = OptionalText(line.NonEvaluationReason);

        switch (evaluationMode)
        {
            case ScopeEvaluationModes.Evaluated:
                if (limitRule is null || decisionRule is null || waiver is not null || reason is not null)
                    throw new ScopeDomainException(ScopeErrorCodes.EvaluationIncomplete);
                break;
            case ScopeEvaluationModes.MeasuredOnly:
                if (limitRule is not null || decisionRule is not null || waiver is not null || reason is not null)
                    throw new ScopeDomainException(ScopeErrorCodes.EvaluationConflict);
                break;
            case ScopeEvaluationModes.NotEvaluated:
                if (limitRule is not null || decisionRule is not null || waiver is not null || reason is null)
                    throw new ScopeDomainException(ScopeErrorCodes.EvaluationConflict);
                break;
            case ScopeEvaluationModes.Waived:
                if (limitRule is not null || decisionRule is not null || waiver is null)
                    throw new ScopeDomainException(ScopeErrorCodes.EvaluationConflict);
                break;
            default:
                throw new ScopeDomainException(ScopeErrorCodes.ApplicabilityUnknown);
        }

        var identity = string.Join('|',
            subjectType,
            subject.Id,
            subject.Version,
            targetMarket.Id,
            targetMarket.Version,
            requirementClause.Id,
            requirementClause.Version,
            testItem.Id,
            testItem.Version);
        return new ScopeLineResult(
            HashTarget(identity),
            subjectType,
            subject,
            targetMarket,
            requirementClause,
            testItem,
            method,
            methodOption,
            sampleRequirement,
            evaluationMode,
            workCenter,
            reportPosition,
            limitRule,
            decisionRule,
            reason,
            waiver);
    }

    private static bool KnownSubjectType(string? value) => value?.Trim() is
        ScopeSubjectTypes.SubmissionItem or ScopeSubjectTypes.ProductVariant or ScopeSubjectTypes.FeatureNode;

    private static ScopeVersionedReference Reference(ScopeVersionedReference? value)
    {
        if (value is null || value.Version < 1)
            throw new ScopeDomainException(ScopeErrorCodes.ValidationFailed);
        return new ScopeVersionedReference(Identifier(value.Id), value.Version);
    }

    private static ScopeVersionedReference? OptionalReference(ScopeVersionedReference? value) =>
        value is null ? null : Reference(value);

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!StableIdentifier.IsMatch(trimmed))
            throw new ScopeDomainException(ScopeErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static string? OptionalText(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.Length is < 1 or > 500)
            throw new ScopeDomainException(ScopeErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static ScopeProductionEligibilityResult Blocked(string reason) => new(
        ScopeEligibilityDecisions.Blocked,
        [reason],
        null,
        null,
        ScopeContract.RuleSetVersion);

    private static ScopeProductionEligibilityResult Unknown(
        ScopeMatrixVersionResult? current,
        string reason) => new(
        ScopeEligibilityDecisions.Unknown,
        [reason],
        current?.ScopeMatrixId,
        current?.Version,
        ScopeContract.RuleSetVersion);
}
