namespace OpenLIMS.Contracts.Ai;

public static class AiContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "AI-DOC-EXTRACTION@1.0.0";
}

public static class AiFactClasses
{
    public const string Observation = "OBSERVATION";
    public const string Assumption = "ASSUMPTION";
    public const string AiInference = "AI_INFERENCE";
    public const string VerifiedFact = "VERIFIED_FACT";
}

public static class AiDispositionKinds
{
    public const string Accept = "ACCEPT";
    public const string Modify = "MODIFY";
    public const string Split = "SPLIT";
    public const string Merge = "MERGE";
    public const string Reject = "REJECT";
}

public static class AiGapKinds
{
    public const string MissingInformation = "MISSING_INFORMATION";
    public const string Clarification = "CLARIFICATION";
}

public static class AiValidationDecisions
{
    public const string Accepted = "ACCEPTED";
    public const string Quarantined = "QUARANTINED";
}

public static class AiValidationErrorCodes
{
    public const string UnknownField = "UNKNOWN_FIELD";
    public const string IllegalUnit = "ILLEGAL_UNIT";
    public const string MissingSource = "MISSING_SOURCE";
    public const string DuplicateDeterminateField = "DUPLICATE_DETERMINATE_FIELD";
}

public static class AiErrorCodes
{
    public const string ValidationFailed = "AIX.VALIDATION_FAILED";
    public const string OutputQuarantined = "AIX.OUTPUT_QUARANTINED";
    public const string FactClassPromotionRejected = "AIX.FACT_CLASS_PROMOTION_REJECTED";
}

public sealed record AiVersionedReference(string Id, long Version);

public sealed record AiRunEnvelope(
    AiVersionedReference Model,
    string GatewayRoute,
    AiVersionedReference PromptTemplate,
    AiVersionedReference OutputSchema,
    IReadOnlyList<AiVersionedReference> InputRefs);

public sealed record AiSourceLocation(
    AiVersionedReference Document,
    int? Page = null,
    string? Region = null);

public sealed record AiFieldCandidate(
    string CandidateId,
    string TargetField,
    string Value,
    string FactClass,
    decimal Confidence,
    AiSourceLocation SourceLocation,
    string? Unit = null,
    bool Abstained = false,
    AiVersionedReference? AuthoritySource = null,
    AiVersionedReference? VerificationMethod = null);

public sealed record AiGapSuggestion(
    string GapId,
    string TargetField,
    string Kind,
    string Question);

public sealed record AiStructuredOutput(
    string RuleSetVersion,
    AiRunEnvelope Envelope,
    IReadOnlyList<AiFieldCandidate> Candidates,
    IReadOnlyList<AiGapSuggestion> Gaps);

public sealed record AiValidationError(string Field, string Code, string Detail);

public sealed record AiValidationResult(
    string Decision,
    IReadOnlyList<AiValidationError> Errors,
    IReadOnlyList<AiFieldCandidate> Candidates,
    IReadOnlyList<AiGapSuggestion> Gaps,
    string RuleSetVersion);

public sealed record AiDisposition(
    string DispositionId,
    string CandidateId,
    string Kind,
    string AiOriginalValue,
    string Reason,
    string ResponsibleActor,
    string? HumanValue = null);

public interface IAiOutputValidator
{
    AiValidationResult Validate(AiStructuredOutput output, IReadOnlySet<string> allowedFields, IReadOnlySet<string> allowedUnits);
    void ValidateDisposition(AiDisposition disposition, AiFieldCandidate candidate);
}
