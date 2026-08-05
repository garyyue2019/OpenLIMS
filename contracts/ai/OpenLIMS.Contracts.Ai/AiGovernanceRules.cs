using System.Text.RegularExpressions;

namespace OpenLIMS.Contracts.Ai;

/// <summary>
/// Pure, deterministic governance rules for the AI future-fit contract slice.
/// No model calls, no IO, no clock — every unknown semantic fails closed and
/// AI_INFERENCE can never be promoted to VERIFIED_FACT without both an
/// authority source and a verification method.
/// </summary>
public sealed partial class AiGovernanceRules : IAiOutputValidator
{
    private static readonly Regex StableIdentifier = StableIdentifierPattern();

    public static AiGovernanceRules Instance { get; } = new();

    public AiValidationResult Validate(
        AiStructuredOutput output,
        IReadOnlySet<string> allowedFields,
        IReadOnlySet<string> allowedUnits)
    {
        if (output is null || allowedFields is null || allowedUnits is null ||
            output.Candidates is null || output.Gaps is null)
        {
            throw new AiContractException(AiErrorCodes.ValidationFailed);
        }
        if (!string.Equals(output.RuleSetVersion, AiContract.RuleSetVersion, StringComparison.Ordinal))
            throw new AiContractException(AiErrorCodes.ValidationFailed);
        ValidateEnvelope(output.Envelope);

        var errors = new List<AiValidationError>();
        var determinateFields = new HashSet<string>(StringComparer.Ordinal);
        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in output.Candidates)
        {
            ValidateCandidateShape(candidate);
            if (!candidateIds.Add(candidate.CandidateId))
                errors.Add(new AiValidationError(candidate.TargetField, AiValidationErrorCodes.DuplicateCandidateId, "candidate id is repeated"));
            if (!allowedFields.Contains(candidate.TargetField))
                errors.Add(new AiValidationError(candidate.TargetField, AiValidationErrorCodes.UnknownField, "target field is not in the output schema"));
            if (candidate.Unit is not null && !allowedUnits.Contains(candidate.Unit))
                errors.Add(new AiValidationError(candidate.TargetField, AiValidationErrorCodes.IllegalUnit, $"unit '{candidate.Unit}' is not allowed"));
            if (candidate.SourceLocation is null || candidate.SourceLocation.Document is null)
                errors.Add(new AiValidationError(candidate.TargetField, AiValidationErrorCodes.MissingSource, "source location is required"));
            if (!candidate.Abstained && !determinateFields.Add($"{candidate.TargetField}|{candidate.Value}") &&
                output.Candidates.Count(other =>
                    !other.Abstained &&
                    string.Equals(other.TargetField, candidate.TargetField, StringComparison.Ordinal) &&
                    string.Equals(other.Value, candidate.Value, StringComparison.Ordinal)) > 1)
            {
                errors.Add(new AiValidationError(candidate.TargetField, AiValidationErrorCodes.DuplicateDeterminateField, "identical determinate candidate repeated"));
            }
        }

        foreach (var gap in output.Gaps)
        {
            if (gap is null || !IsIdentifier(gap.GapId) || !IsIdentifier(gap.TargetField) ||
                gap.Kind is not (AiGapKinds.MissingInformation or AiGapKinds.Clarification) ||
                string.IsNullOrWhiteSpace(gap.Question))
            {
                throw new AiContractException(AiErrorCodes.ValidationFailed);
            }
        }

        return errors.Count > 0
            ? new AiValidationResult(AiValidationDecisions.Quarantined, errors, [], [], AiContract.RuleSetVersion)
            : new AiValidationResult(AiValidationDecisions.Accepted, [], output.Candidates, output.Gaps, AiContract.RuleSetVersion);
    }

    public void ValidateDisposition(AiDisposition disposition, AiFieldCandidate candidate)
    {
        if (disposition is null || candidate is null ||
            !IsIdentifier(disposition.DispositionId) ||
            !string.Equals(disposition.CandidateId, candidate.CandidateId, StringComparison.Ordinal) ||
            disposition.Kind is not (AiDispositionKinds.Accept or AiDispositionKinds.Modify or
                AiDispositionKinds.Split or AiDispositionKinds.Merge or AiDispositionKinds.Reject) ||
            !string.Equals(disposition.AiOriginalValue, candidate.Value, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(disposition.Reason) ||
            !IsIdentifier(disposition.ResponsibleActor))
        {
            throw new AiContractException(AiErrorCodes.ValidationFailed);
        }

        var isModify = string.Equals(disposition.Kind, AiDispositionKinds.Modify, StringComparison.Ordinal);
        if (isModify && string.IsNullOrWhiteSpace(disposition.HumanValue))
            throw new AiContractException(AiErrorCodes.ValidationFailed);
        if (!isModify && disposition.HumanValue is not null)
            throw new AiContractException(AiErrorCodes.ValidationFailed);
    }

    public static void RequireNoPromotion(AiFieldCandidate candidate, string requestedFactClass)
    {
        if (!string.Equals(requestedFactClass, AiFactClasses.VerifiedFact, StringComparison.Ordinal))
            return;
        if (candidate.AuthoritySource is null || candidate.VerificationMethod is null)
            throw new AiContractException(AiErrorCodes.FactClassPromotionRejected);
    }

    private static void ValidateEnvelope(AiRunEnvelope? envelope)
    {
        if (envelope is null ||
            !IsReference(envelope.Model) ||
            !IsIdentifier(envelope.GatewayRoute) ||
            !IsReference(envelope.PromptTemplate) ||
            !IsReference(envelope.OutputSchema) ||
            envelope.InputRefs is null || envelope.InputRefs.Count is < 1 or > 1000 ||
            envelope.InputRefs.Any(reference => !IsReference(reference)))
        {
            throw new AiContractException(AiErrorCodes.ValidationFailed);
        }
    }

    private static void ValidateCandidateShape(AiFieldCandidate? candidate)
    {
        if (candidate is null ||
            !IsIdentifier(candidate.CandidateId) ||
            !IsIdentifier(candidate.TargetField) ||
            string.IsNullOrWhiteSpace(candidate.Value) ||
            candidate.Confidence is < 0 or > 1 ||
            (candidate.Unit is not null && !IsIdentifier(candidate.Unit)) ||
            (candidate.AuthoritySource is not null && !IsReference(candidate.AuthoritySource)) ||
            (candidate.VerificationMethod is not null && !IsReference(candidate.VerificationMethod)))
        {
            throw new AiContractException(AiErrorCodes.ValidationFailed);
        }

        if (candidate.FactClass is not (AiFactClasses.Observation or AiFactClasses.Assumption or
            AiFactClasses.AiInference or AiFactClasses.VerifiedFact))
        {
            throw new AiContractException(AiErrorCodes.ValidationFailed);
        }

        if (string.Equals(candidate.FactClass, AiFactClasses.VerifiedFact, StringComparison.Ordinal) &&
            (candidate.AuthoritySource is null || candidate.VerificationMethod is null))
        {
            throw new AiContractException(AiErrorCodes.FactClassPromotionRejected);
        }
    }

    private static bool IsReference(AiVersionedReference? value) =>
        value is not null && value.Version >= 1 && IsIdentifier(value.Id);

    private static bool IsIdentifier(string? value) =>
        value is not null && StableIdentifier.IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierPattern();
}

public sealed class AiContractException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
