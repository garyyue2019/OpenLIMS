using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using OpenLIMS.Contracts.Result;

namespace OpenLIMS.Modules.Result;

internal sealed class ResultDomainException(string errorCode, string? gateSource = null) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
    public string? GateSource { get; } = gateSource;
}

internal sealed record ResultCalculationExecution(
    IReadOnlyList<ResultCalculationResolvedInput> Inputs,
    ResultCalculationRule Rule,
    decimal ExactValue,
    decimal RoundedValue,
    string ReportedValue,
    string Qualification,
    string LimitDecision);

internal sealed record ResultAccreditationEvaluation(
    RecordResultAccreditationAssessmentRequest Request,
    string Decision,
    IReadOnlyList<string> ReasonCodes);

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

    public static ResultCalculationExecution ExecuteCalculation(
        ExecuteResultCalculationRequest? request,
        ResultGroupResult group)
    {
        if (request is null || request.ExpectedCurrentVersion < 1)
            throw new ResultDomainException(ResultErrorCodes.ExpectedVersionConflict);
        if (!string.Equals(request.RuleSetVersion, ResultContract.CalculationRuleSetVersion, StringComparison.Ordinal))
            throw new ResultDomainException(ResultErrorCodes.ApplicabilityUnknown);
        if (request.Inputs is null || request.Inputs.Count is < 1 or > 100)
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);

        var rule = NormalizeCalculationRule(request.Rule);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inputs = new List<ResultCalculationResolvedInput>(request.Inputs.Count);
        foreach (var input in request.Inputs)
        {
            if (input is null || input.Coefficient == 0)
                throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
            var targetId = Identifier(input.TargetId);
            if (!seen.Add(targetId))
                throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
            var target = ResolveNumericTarget(group, targetId);
            if (target is null)
                throw new ResultDomainException(ResultErrorCodes.CalculationFailed);
            if (!string.Equals(target.Value.Unit, rule.InputUnit, StringComparison.Ordinal))
                throw new ResultDomainException(ResultErrorCodes.CalculationFailed);
            inputs.Add(new ResultCalculationResolvedInput(
                targetId, target.Value.Value, target.Value.Unit, input.Coefficient));
        }

        try
        {
            var weighted = inputs.Aggregate(0m, (current, input) => checked(current + input.Value * input.Coefficient));
            var converted = checked(weighted * rule.UnitMultiplier + rule.UnitOffset);
            var exact = checked(converted * rule.DilutionFactor * rule.QuantityFactor);
            var rounded = Round(exact, rule.DecimalPlaces, rule.RoundingMode);
            var qualification = rule.Lod is not null && exact < rule.Lod.Value
                ? ResultDetectionQualifications.BelowLod
                : rule.Loq is not null && exact < rule.Loq.Value
                    ? ResultDetectionQualifications.BelowLoq
                    : ResultDetectionQualifications.Quantified;
            var reported = qualification switch
            {
                ResultDetectionQualifications.BelowLod => "<LOD",
                ResultDetectionQualifications.BelowLoq => "<LOQ",
                _ => CanonicalDecimal(rounded)
            };
            var limitDecision = EvaluateLimit(rule, exact, rounded, qualification);
            return new ResultCalculationExecution(
                inputs, rule, exact, rounded, reported, qualification, limitDecision);
        }
        catch (OverflowException)
        {
            throw new ResultDomainException(ResultErrorCodes.CalculationFailed);
        }
    }

    public static ResultAccreditationEvaluation EvaluateAccreditationAssessment(
        RecordResultAccreditationAssessmentRequest? request,
        ResultGroupResult group,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null || request.ExpectedCurrentVersion < 1)
            throw new ResultDomainException(ResultErrorCodes.ExpectedVersionConflict);
        if (!string.Equals(request.RuleSetVersion, ResultContract.AccreditationRuleSetVersion, StringComparison.Ordinal))
            throw new ResultDomainException(ResultErrorCodes.ApplicabilityUnknown);
        var stage = request.Stage?.Trim();
        if (stage is not (ResultAccreditationStages.Execution or ResultAccreditationStages.Result) ||
            request.ValidFrom > request.ValidTo || request.RangeLower > request.RangeUpper ||
            request.AuthorizedActorIds is null || request.AuthorizedActorIds.Count is < 1 or > 100)
        {
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        }

        string? targetId = null;
        if (string.Equals(stage, ResultAccreditationStages.Execution, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(request.TargetId))
                throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        }
        else
        {
            targetId = Identifier(request.TargetId);
        }

        var actors = request.AuthorizedActorIds.Select(Identifier).ToArray();
        if (actors.Distinct(StringComparer.Ordinal).Count() != actors.Length)
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);

        var normalized = request with
        {
            Stage = stage,
            TargetId = targetId,
            Accreditation = Reference(request.Accreditation),
            Method = Reference(request.Method),
            SiteId = Identifier(request.SiteId),
            ProductOrMatrix = Identifier(request.ProductOrMatrix),
            Parameter = Identifier(request.Parameter),
            RangeUnit = Identifier(request.RangeUnit),
            AuthorizedActorIds = actors
        };
        var reasons = new List<string>();
        if (!string.Equals(normalized.SiteId, group.ObjectScope.LaboratoryId, StringComparison.Ordinal))
            reasons.Add(ResultAccreditationReasons.SiteMismatch);
        if (!string.Equals(normalized.ProductOrMatrix, group.ObjectScope.ProductCategory, StringComparison.Ordinal))
            reasons.Add(ResultAccreditationReasons.ProductMatrixMismatch);
        if (!string.Equals(normalized.Parameter, group.TestItem.Id, StringComparison.Ordinal))
            reasons.Add(ResultAccreditationReasons.ParameterMismatch);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (today < normalized.ValidFrom)
            reasons.Add(ResultAccreditationReasons.NotYetValid);
        if (today > normalized.ValidTo)
            reasons.Add(ResultAccreditationReasons.Expired);
        if (!normalized.AuthorizedActorIds.Contains(actorId, StringComparer.Ordinal))
            reasons.Add(ResultAccreditationReasons.ActorUnauthorized);

        if (string.Equals(stage, ResultAccreditationStages.Result, StringComparison.Ordinal))
        {
            var effectiveTarget = group.Adoptions
                .OrderByDescending(adoption => adoption.AdoptionVersion)
                .FirstOrDefault()?.TargetId;
            if (effectiveTarget is null)
            {
                reasons.Add(ResultAccreditationReasons.TargetRequired);
            }
            else if (!string.Equals(normalized.TargetId, effectiveTarget, StringComparison.Ordinal))
            {
                reasons.Add(ResultAccreditationReasons.TargetNotEffective);
            }
            else
            {
                var target = ResolveNumericTarget(group, effectiveTarget);
                if (target is null)
                {
                    reasons.Add(ResultAccreditationReasons.TargetNotNumeric);
                }
                else if (!string.Equals(target.Value.Unit, normalized.RangeUnit, StringComparison.Ordinal))
                {
                    reasons.Add(ResultAccreditationReasons.RangeUnitMismatch);
                }
                else if (target.Value.Value < normalized.RangeLower || target.Value.Value > normalized.RangeUpper)
                {
                    reasons.Add(ResultAccreditationReasons.OutsideRange);
                }
            }
        }

        return new ResultAccreditationEvaluation(
            normalized,
            reasons.Count == 0 ? ResultAccreditationDecisions.Eligible : ResultAccreditationDecisions.Blocked,
            reasons);
    }

    public static ResultAccreditationEligibilityResult EvaluateAccreditationEligibility(
        ResultAccreditationEligibilityRequest request,
        ResultGroupResult? group,
        string actorId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.RuleSetVersion, ResultContract.AccreditationRuleSetVersion, StringComparison.Ordinal))
            return UnknownAccreditation(group, ResultAccreditationEligibilityReasons.RuleSetVersionUnknown);
        if (group is null)
            return BlockedAccreditation(null, null, null, ResultAccreditationEligibilityReasons.GroupRequired);
        if (request.ExpectedGroupVersion != group.Version)
            return UnknownAccreditation(group, ResultAccreditationEligibilityReasons.GroupVersionMismatch);

        var execution = group.AccreditationAssessments
            .Where(candidate => string.Equals(candidate.Stage, ResultAccreditationStages.Execution, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.GroupVersion)
            .FirstOrDefault();
        var result = group.AccreditationAssessments
            .Where(candidate => string.Equals(candidate.Stage, ResultAccreditationStages.Result, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.GroupVersion)
            .FirstOrDefault();
        if (execution is null)
            return BlockedAccreditation(group, null, result, ResultAccreditationEligibilityReasons.ExecutionAssessmentRequired);
        if (result is null)
            return BlockedAccreditation(group, execution, null, ResultAccreditationEligibilityReasons.ResultAssessmentRequired);

        var reasons = new List<string>();
        if (!string.Equals(execution.Decision, ResultAccreditationDecisions.Eligible, StringComparison.Ordinal) ||
            !string.Equals(result.Decision, ResultAccreditationDecisions.Eligible, StringComparison.Ordinal))
        {
            reasons.Add(ResultAccreditationEligibilityReasons.AssessmentBlocked);
        }
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (today < execution.ValidFrom || today > execution.ValidTo ||
            today < result.ValidFrom || today > result.ValidTo)
        {
            reasons.Add(ResultAccreditationEligibilityReasons.AssessmentExpired);
        }
        if (execution.Accreditation != result.Accreditation || execution.Method != result.Method ||
            !string.Equals(execution.SiteId, result.SiteId, StringComparison.Ordinal) ||
            !string.Equals(execution.ProductOrMatrix, result.ProductOrMatrix, StringComparison.Ordinal) ||
            !string.Equals(execution.Parameter, result.Parameter, StringComparison.Ordinal) ||
            !string.Equals(execution.RangeUnit, result.RangeUnit, StringComparison.Ordinal) ||
            execution.RangeLower != result.RangeLower || execution.RangeUpper != result.RangeUpper)
        {
            reasons.Add(ResultAccreditationEligibilityReasons.EvidenceMismatch);
        }
        var effectiveTarget = group.Adoptions
            .OrderByDescending(adoption => adoption.AdoptionVersion)
            .FirstOrDefault()?.TargetId;
        if (effectiveTarget is null || !string.Equals(result.TargetId, effectiveTarget, StringComparison.Ordinal))
            reasons.Add(ResultAccreditationEligibilityReasons.EffectiveTargetMismatch);
        if (!result.AuthorizedActorIds.Contains(actorId, StringComparer.Ordinal))
            reasons.Add(ResultAccreditationEligibilityReasons.CurrentActorUnauthorized);

        return new ResultAccreditationEligibilityResult(
            reasons.Count == 0 ? ResultAccreditationDecisions.Eligible : ResultAccreditationDecisions.Blocked,
            reasons,
            group.ResultGroupId,
            group.Version,
            execution.AssessmentId,
            result.AssessmentId,
            effectiveTarget,
            ResultContract.AccreditationRuleSetVersion);
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
        var calculation = group.Calculations.FirstOrDefault(candidate =>
            string.Equals(candidate.CalculationId, adoption.TargetId, StringComparison.Ordinal));
        if (!isObservation && derivation is null && calculation is null)
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
                    var adoptsCalculationIncludingIt = calculation is not null &&
                        CalculationIncludesTarget(group, calculation, latestRetest.ObservationId, []);
                    if (!adoptsLatestRetest && !adoptsDerivationIncludingIt && !adoptsCalculationIncludingIt)
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

    public static ResultConclusionEvidenceResult EvaluateConclusionEvidence(
        ResultConclusionEvidenceRequest request,
        ResultGroupResult? group)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.RuleSetVersion, ResultContract.RuleSetVersion, StringComparison.Ordinal))
        {
            return UnknownConclusionEvidence(
                group,
                request.AdoptionVersion,
                ResultConclusionEvidenceReasons.RuleSetVersionUnknown);
        }

        if (group is null)
        {
            return UnknownConclusionEvidence(
                null,
                request.AdoptionVersion,
                ResultConclusionEvidenceReasons.GroupUnavailable);
        }

        var adoption = group.Adoptions.SingleOrDefault(candidate =>
            candidate.AdoptionVersion == request.AdoptionVersion);
        if (adoption is null)
        {
            return new ResultConclusionEvidenceResult(
                ResultConclusionEvidenceDecisions.Blocked,
                [ResultConclusionEvidenceReasons.AdoptionVersionMissing],
                group.ResultGroupId,
                group.Version,
                request.AdoptionVersion,
                null,
                null,
                null,
                group.ObjectScope,
                ResultContract.RuleSetVersion);
        }

        var observation = group.Observations.SingleOrDefault(candidate =>
            string.Equals(candidate.ObservationId, adoption.TargetId, StringComparison.Ordinal));
        if (observation is not null)
        {
            return new ResultConclusionEvidenceResult(
                ResultConclusionEvidenceDecisions.Allowed,
                [],
                group.ResultGroupId,
                group.Version,
                adoption.AdoptionVersion,
                adoption.TargetId,
                observation.Kind,
                observation.RecordedBy,
                group.ObjectScope,
                ResultContract.RuleSetVersion);
        }

        var derivation = group.Derivations.SingleOrDefault(candidate =>
            string.Equals(candidate.DerivationId, adoption.TargetId, StringComparison.Ordinal));
        if (derivation is not null)
        {
            return new ResultConclusionEvidenceResult(
                ResultConclusionEvidenceDecisions.Allowed,
                [],
                group.ResultGroupId,
                group.Version,
                adoption.AdoptionVersion,
                adoption.TargetId,
                "DERIVATION",
                derivation.RecordedBy,
                group.ObjectScope,
                ResultContract.RuleSetVersion);
        }

        var calculation = group.Calculations.SingleOrDefault(candidate =>
            string.Equals(candidate.CalculationId, adoption.TargetId, StringComparison.Ordinal));
        if (calculation is not null)
        {
            return new ResultConclusionEvidenceResult(
                ResultConclusionEvidenceDecisions.Allowed,
                [],
                group.ResultGroupId,
                group.Version,
                adoption.AdoptionVersion,
                adoption.TargetId,
                "CALCULATION",
                calculation.ExecutedBy,
                group.ObjectScope,
                ResultContract.RuleSetVersion);
        }

        return UnknownConclusionEvidence(
            group,
            adoption.AdoptionVersion,
            ResultConclusionEvidenceReasons.TargetUnavailable);
    }

    public static string HashTarget(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ResultCalculationRule NormalizeCalculationRule(ResultCalculationRule? rule)
    {
        if (rule is null || rule.UnitMultiplier <= 0 || rule.DilutionFactor <= 0 ||
            rule.QuantityFactor <= 0 || rule.DecimalPlaces is < 0 or > 12 ||
            rule.RoundingMode?.Trim() is not (ResultRoundingModes.ToEven or
                ResultRoundingModes.AwayFromZero or ResultRoundingModes.TowardZero or
                ResultRoundingModes.Floor or ResultRoundingModes.Ceiling) ||
            rule.Lod is < 0 || rule.Loq is < 0 ||
            (rule.Lod is not null && rule.Loq is not null && rule.Lod > rule.Loq))
        {
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        }

        var limitOperator = rule.LimitOperator?.Trim();
        var evaluationBasis = rule.LimitEvaluationBasis?.Trim();
        if (limitOperator is not (ResultLimitOperators.None or ResultLimitOperators.LessThanOrEqual or
                ResultLimitOperators.GreaterThanOrEqual or ResultLimitOperators.BetweenInclusive) ||
            evaluationBasis is not (ResultLimitEvaluationBases.Exact or ResultLimitEvaluationBases.Rounded))
        {
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);
        }
        var validLimits = limitOperator switch
        {
            ResultLimitOperators.None => rule.LowerLimit is null && rule.UpperLimit is null,
            ResultLimitOperators.LessThanOrEqual => rule.LowerLimit is null && rule.UpperLimit is not null,
            ResultLimitOperators.GreaterThanOrEqual => rule.LowerLimit is not null && rule.UpperLimit is null,
            ResultLimitOperators.BetweenInclusive => rule.LowerLimit is not null && rule.UpperLimit is not null &&
                                                     rule.LowerLimit <= rule.UpperLimit,
            _ => false
        };
        if (!validLimits)
            throw new ResultDomainException(ResultErrorCodes.ValidationFailed);

        return rule with
        {
            CalculationRule = Reference(rule.CalculationRule),
            UnitConversionRule = Reference(rule.UnitConversionRule),
            InputUnit = Identifier(rule.InputUnit),
            OutputUnit = Identifier(rule.OutputUnit),
            RoundingMode = rule.RoundingMode.Trim(),
            LimitOperator = limitOperator,
            LimitEvaluationBasis = evaluationBasis
        };
    }

    private static (decimal Value, string Unit)? ResolveNumericTarget(ResultGroupResult group, string targetId)
    {
        var observation = group.Observations.FirstOrDefault(candidate =>
            string.Equals(candidate.ObservationId, targetId, StringComparison.Ordinal));
        if (observation is not null)
            return ParseNumeric(observation.Value, observation.Unit);
        var derivation = group.Derivations.FirstOrDefault(candidate =>
            string.Equals(candidate.DerivationId, targetId, StringComparison.Ordinal));
        if (derivation is not null)
            return ParseNumeric(derivation.Value, derivation.Unit);
        var calculation = group.Calculations.FirstOrDefault(candidate =>
            string.Equals(candidate.CalculationId, targetId, StringComparison.Ordinal));
        return calculation is null ? null : (calculation.ExactValue, calculation.Unit);
    }

    private static (decimal Value, string Unit)? ParseNumeric(string value, string unit) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? (parsed, unit)
            : null;

    private static decimal Round(decimal value, int places, string mode)
    {
        if (mode == ResultRoundingModes.ToEven)
            return decimal.Round(value, places, MidpointRounding.ToEven);
        if (mode == ResultRoundingModes.AwayFromZero)
            return decimal.Round(value, places, MidpointRounding.AwayFromZero);
        var scale = DecimalScale(places);
        var scaled = checked(value * scale);
        var integral = mode switch
        {
            ResultRoundingModes.TowardZero => decimal.Truncate(scaled),
            ResultRoundingModes.Floor => decimal.Floor(scaled),
            ResultRoundingModes.Ceiling => decimal.Ceiling(scaled),
            _ => throw new ResultDomainException(ResultErrorCodes.ValidationFailed)
        };
        return integral / scale;
    }

    private static decimal DecimalScale(int places)
    {
        var scale = 1m;
        for (var index = 0; index < places; index++)
            scale *= 10m;
        return scale;
    }

    private static string EvaluateLimit(
        ResultCalculationRule rule,
        decimal exact,
        decimal rounded,
        string qualification)
    {
        if (rule.LimitOperator == ResultLimitOperators.None)
            return ResultLimitDecisions.NotEvaluated;
        if (qualification != ResultDetectionQualifications.Quantified)
            return ResultLimitDecisions.Unknown;
        var value = rule.LimitEvaluationBasis == ResultLimitEvaluationBases.Exact ? exact : rounded;
        var passed = rule.LimitOperator switch
        {
            ResultLimitOperators.LessThanOrEqual => value <= rule.UpperLimit!.Value,
            ResultLimitOperators.GreaterThanOrEqual => value >= rule.LowerLimit!.Value,
            ResultLimitOperators.BetweenInclusive => value >= rule.LowerLimit!.Value && value <= rule.UpperLimit!.Value,
            _ => throw new ResultDomainException(ResultErrorCodes.ValidationFailed)
        };
        return passed ? ResultLimitDecisions.Pass : ResultLimitDecisions.Fail;
    }

    private static string CanonicalDecimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static bool CalculationIncludesTarget(
        ResultGroupResult group,
        ResultCalculationResult calculation,
        string targetId,
        HashSet<string> visited)
    {
        if (!visited.Add(calculation.CalculationId))
            return false;
        foreach (var input in calculation.Inputs)
        {
            if (string.Equals(input.TargetId, targetId, StringComparison.Ordinal))
                return true;
            var nested = group.Calculations.FirstOrDefault(candidate =>
                string.Equals(candidate.CalculationId, input.TargetId, StringComparison.Ordinal));
            if (nested is not null && CalculationIncludesTarget(group, nested, targetId, visited))
                return true;
        }
        return false;
    }

    private static ResultAccreditationEligibilityResult BlockedAccreditation(
        ResultGroupResult? group,
        ResultAccreditationAssessmentResult? execution,
        ResultAccreditationAssessmentResult? result,
        string reason) => new(
        ResultAccreditationDecisions.Blocked,
        [reason],
        group?.ResultGroupId,
        group?.Version,
        execution?.AssessmentId,
        result?.AssessmentId,
        group?.Adoptions.OrderByDescending(candidate => candidate.AdoptionVersion).FirstOrDefault()?.TargetId,
        ResultContract.AccreditationRuleSetVersion);

    private static ResultAccreditationEligibilityResult UnknownAccreditation(
        ResultGroupResult? group,
        string reason) => new(
        ResultAccreditationDecisions.Unknown,
        [reason],
        group?.ResultGroupId,
        group?.Version,
        null,
        null,
        null,
        ResultContract.AccreditationRuleSetVersion);

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

    private static ResultConclusionEvidenceResult UnknownConclusionEvidence(
        ResultGroupResult? group,
        long adoptionVersion,
        string reason) => new(
        ResultConclusionEvidenceDecisions.Unknown,
        [reason],
        group?.ResultGroupId,
        group?.Version,
        adoptionVersion,
        null,
        null,
        null,
        group?.ObjectScope,
        ResultContract.RuleSetVersion);
}
