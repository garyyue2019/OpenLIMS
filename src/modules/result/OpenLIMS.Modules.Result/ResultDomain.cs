using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Result;

namespace OpenLIMS.Modules.Result;

internal sealed class ResultDomainException(string errorCode, string? gateSource = null) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
    public string? GateSource { get; } = gateSource;
}

internal static class ResultRules
{
    private static readonly Regex StableIdentifier = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Sha256Hex = new(
        "^[a-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static ResultObjectContext NormalizeObjectScope(ResultObjectContext? value)
    {
        if (value is null)
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        return new ResultObjectContext(
            Identifier(value.LegalEntityId),
            Identifier(value.LaboratoryId),
            Identifier(value.CustomerId),
            Identifier(value.ServiceOrderId),
            Identifier(value.ProductCategory));
    }

    public static void RequireRuleSet(string? value)
    {
        if (!string.Equals(value, ResultContract.RuleSetVersion, StringComparison.Ordinal))
            throw new ResultDomainException(ResultErrorCodes.ApplicabilityUnknown);
    }

    public static CreateResultGroupRequest ValidateGroup(CreateResultGroupRequest? request)
    {
        if (request is null)
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        RequireRuleSet(request.RuleSetVersion);
        if (request.ExpectedBatchVersion < 1)
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        return request with
        {
            ObjectScope = NormalizeObjectScope(request.ObjectScope),
            BatchId = Identifier(request.BatchId),
            MemberId = Identifier(request.MemberId),
            TestItem = Reference(request.TestItem),
            ScopeLineId = Identifier(request.ScopeLineId)
        };
    }

    public static AddResultObservationRequest ValidateObservation(
        AddResultObservationRequest? request,
        bool adoptionRuleExists)
    {
        if (request is null || request.ExpectedCurrentVersion < 1)
            throw new ResultDomainException(ResultErrorCodes.ExpectedVersionConflict);
        RequireRuleSet(request.RuleSetVersion);
        var kind = request.Kind?.Trim();
        if (kind is not (ResultObservationKinds.Initial or ResultObservationKinds.Duplicate or
            ResultObservationKinds.Retest or ResultObservationKinds.Supplement or
            ResultObservationKinds.RePreparation or ResultObservationKinds.ReSampling))
        {
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        }

        if (!string.Equals(kind, ResultObservationKinds.Initial, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(request.TriggerReason) || request.ApprovalRef is null))
        {
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        }

        if (string.Equals(kind, ResultObservationKinds.Retest, StringComparison.Ordinal) && !adoptionRuleExists)
            throw new ResultDomainException(ResultErrorCodes.AdoptionRuleRequired);

        return request with
        {
            Kind = kind,
            Value = Text(request.Value),
            Unit = Identifier(request.Unit),
            Evidence = Evidence(request.Evidence),
            TriggerReason = request.TriggerReason is null ? null : Text(request.TriggerReason),
            ApprovalRef = request.ApprovalRef is null ? null : Reference(request.ApprovalRef)
        };
    }

    public static AddResultDerivationRequest ValidateDerivation(
        AddResultDerivationRequest? request,
        IReadOnlySet<string> existingTargetIds)
    {
        if (request is null || request.ExpectedCurrentVersion < 1)
            throw new ResultDomainException(ResultErrorCodes.ExpectedVersionConflict);
        RequireRuleSet(request.RuleSetVersion);
        if (request.Inputs is null || request.Inputs.Count is < 1 or > 1000)
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inputs = new List<ResultDerivationInput>(request.Inputs.Count);
        foreach (var input in request.Inputs)
        {
            if (input is null)
                throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
            var targetId = Identifier(input.TargetId);
            if (!existingTargetIds.Contains(targetId) || !seen.Add(targetId))
                throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
            if (!input.Included && string.IsNullOrWhiteSpace(input.Rationale))
                throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
            inputs.Add(new ResultDerivationInput(
                targetId, input.Included, input.Rationale is null ? null : Text(input.Rationale)));
        }

        if (!inputs.Any(input => input.Included))
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);

        return request with
        {
            AggregationRule = Reference(request.AggregationRule),
            Value = Text(request.Value),
            Unit = Identifier(request.Unit),
            Inputs = inputs
        };
    }

    public static RecordAdoptionRuleRequest ValidateAdoptionRule(RecordAdoptionRuleRequest? request)
    {
        if (request is null || request.ExpectedCurrentVersion < 1)
            throw new ResultDomainException(ResultErrorCodes.ExpectedVersionConflict);
        RequireRuleSet(request.RuleSetVersion);
        if (request.Strategy?.Trim() is not
            (ResultAdoptionStrategies.RetestReplacesOriginal or ResultAdoptionStrategies.TechnicalReviewSelects))
        {
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        }
        return request with { Strategy = request.Strategy.Trim(), RuleRef = Reference(request.RuleRef) };
    }

    public static AdoptResultRequest ValidateAdoption(AdoptResultRequest? request)
    {
        if (request is null || request.ExpectedCurrentVersion < 1)
            throw new ResultDomainException(ResultErrorCodes.ExpectedVersionConflict);
        RequireRuleSet(request.RuleSetVersion);
        return request with
        {
            TargetId = Identifier(request.TargetId),
            ReviewApprovalRef = request.ReviewApprovalRef is null ? null : Reference(request.ReviewApprovalRef)
        };
    }

    public static void RequireStrategyCompliance(
        AdoptResultRequest adoption,
        AdoptionRuleResult rule,
        ResultGroupResult group)
    {
        var isObservation = group.Observations.Any(observation =>
            string.Equals(observation.ObservationId, adoption.TargetId, StringComparison.Ordinal));
        var derivation = group.Derivations.FirstOrDefault(candidate =>
            string.Equals(candidate.DerivationId, adoption.TargetId, StringComparison.Ordinal));
        if (!isObservation && derivation is null)
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);

        switch (rule.Strategy)
        {
            case ResultAdoptionStrategies.RetestReplacesOriginal:
                var latestRetest = group.Observations
                    .Where(observation => string.Equals(
                        observation.Kind, ResultObservationKinds.Retest, StringComparison.Ordinal))
                    .OrderByDescending(observation => observation.GroupVersion)
                    .FirstOrDefault();
                if (latestRetest is not null)
                {
                    var adoptsLatestRetest = string.Equals(
                        adoption.TargetId, latestRetest.ObservationId, StringComparison.Ordinal);
                    var adoptsDerivationIncludingIt = derivation is not null && derivation.Inputs.Any(input =>
                        input.Included && string.Equals(
                            input.TargetId, latestRetest.ObservationId, StringComparison.Ordinal));
                    if (!adoptsLatestRetest && !adoptsDerivationIncludingIt)
                        throw new ResultDomainException(ResultErrorCodes.AdoptionStrategyViolation);
                }
                break;
            case ResultAdoptionStrategies.TechnicalReviewSelects:
                if (adoption.ReviewApprovalRef is null)
                    throw new ResultDomainException(ResultErrorCodes.AdoptionStrategyViolation);
                break;
            default:
                throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        }
    }

    public static void RequireVersion(long expected, long current)
    {
        if (expected != current)
            throw new ResultDomainException(ResultErrorCodes.ExpectedVersionConflict);
    }

    public static ResultAdoptionStatusResult EvaluateStatus(
        ResultAdoptionStatusRequest request,
        ResultGroupResult? group)
    {
        if (!string.Equals(request.RuleSetVersion, ResultContract.RuleSetVersion, StringComparison.Ordinal))
            return Unknown(group, ResultAdoptionReasons.RuleSetVersionUnknown);
        if (group is null)
            return Blocked(null, ResultAdoptionReasons.GroupRequired);
        if (request.ExpectedGroupVersion != group.Version)
            return Unknown(group, ResultAdoptionReasons.GroupVersionMismatch);
        var effective = group.Adoptions.OrderByDescending(adoption => adoption.AdoptionVersion).FirstOrDefault();
        if (effective is null)
            return Blocked(group, ResultAdoptionReasons.AdoptionRequired);

        return new ResultAdoptionStatusResult(
            ResultAdoptionDecisions.Allowed,
            [],
            group.ResultGroupId,
            group.Version,
            effective.TargetId,
            effective.AdoptionVersion,
            ResultContract.RuleSetVersion);
    }

    public static string HashTarget(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ResultEvidence Evidence(ResultEvidence? value)
    {
        if (value is null ||
            value.SourceSystem?.Trim() is not (ResultEvidenceSources.Cds or ResultEvidenceSources.Eln or
                ResultEvidenceSources.Instrument or ResultEvidenceSources.Manual))
        {
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        }
        var sha = value.Sha256?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Sha256Hex.IsMatch(sha))
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        return new ResultEvidence(
            value.SourceSystem.Trim(),
            Reference(value.ExternalRef),
            sha,
            Identifier(value.ParserVersion));
    }

    private static ResultVersionedReference Reference(ResultVersionedReference? value)
    {
        if (value is null || value.Version < 1)
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        return new ResultVersionedReference(Identifier(value.Id), value.Version);
    }

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!StableIdentifier.IsMatch(trimmed))
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static string Text(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > 500)
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static ResultAdoptionStatusResult Blocked(ResultGroupResult? group, string reason) => new(
        ResultAdoptionDecisions.Blocked, [reason], group?.ResultGroupId, group?.Version, null, null,
        ResultContract.RuleSetVersion);

    private static ResultAdoptionStatusResult Unknown(ResultGroupResult? group, string reason) => new(
        ResultAdoptionDecisions.Unknown, [reason], group?.ResultGroupId, group?.Version, null, null,
        ResultContract.RuleSetVersion);
}
