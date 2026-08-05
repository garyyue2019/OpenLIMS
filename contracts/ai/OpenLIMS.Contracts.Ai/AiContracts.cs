namespace OpenLIMS.Contracts.Ai;

public static class AiContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "AI-DOC-EXTRACTION@1.0.0";
    public const string RuntimeRuleSetVersion = "AI-RUNTIME@1.0.0";
    public const string CreateRunPath = "/api/v1/ai-runs";
    public const string GetRunPath = "/api/v1/ai-runs/{id}";
    public const string RecordDispositionPath = "/api/v1/ai-runs/{id}/dispositions";
    public const string ReviewQueuePath = "/api/v1/ai-review-queue";
}

public static class AiCapabilities
{
    public const string Run = "ai.run";
    public const string Review = "ai.review";
}

public static class AiClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ProductCategory = "product_category";
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
    public const string DuplicateCandidateId = "DUPLICATE_CANDIDATE_ID";
    public const string EnvelopeMismatch = "ENVELOPE_MISMATCH";
    public const string FactClassPromotion = "FACT_CLASS_PROMOTION";
    public const string ProviderResponseInvalid = "PROVIDER_RESPONSE_INVALID";
}

public static class AiErrorCodes
{
    public const string ValidationFailed = "AIX.VALIDATION_FAILED";
    public const string OutputQuarantined = "AIX.OUTPUT_QUARANTINED";
    public const string FactClassPromotionRejected = "AIX.FACT_CLASS_PROMOTION_REJECTED";
    public const string NotAuthorized = "AIX.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "AIX.OBJECT_NOT_ACCESSIBLE";
    public const string ExpectedVersionConflict = "AIX.EXPECTED_VERSION_CONFLICT";
    public const string IdempotencyConflict = "AIX.IDEMPOTENCY_CONFLICT";
    public const string ReviewNotAllowed = "AIX.REVIEW_NOT_ALLOWED";
    public const string CandidateNotFound = "AIX.CANDIDATE_NOT_FOUND";
    public const string PersistenceUnavailable = "AIX.PERSISTENCE_UNAVAILABLE";
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

public static class AiProviderStatuses
{
    public const string Pending = "PENDING";
    public const string Completed = "COMPLETED";
    public const string Disabled = "DISABLED";
    public const string Failed = "FAILED";
}

public static class AiRunStatuses
{
    public const string Pending = "PENDING";
    public const string Accepted = "ACCEPTED";
    public const string Quarantined = "QUARANTINED";
    public const string ProviderDisabled = "PROVIDER_DISABLED";
    public const string ProviderFailed = "PROVIDER_FAILED";

    public static readonly IReadOnlyList<string> Reviewable = [Accepted, Quarantined];
}

public sealed record AiObjectContext(
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory);

public sealed record CreateAiRunRequest(
    string RuleSetVersion,
    AiObjectContext ObjectScope,
    AiRunEnvelope Envelope,
    AiVersionedReference ValidationProfile,
    IReadOnlyList<string> AllowedFields,
    IReadOnlyList<string> AllowedUnits,
    string IdempotencyKey);

public sealed record AiProviderRequest(
    string RunId,
    AiRunEnvelope Envelope,
    AiVersionedReference ValidationProfile,
    IReadOnlyList<string> AllowedFields,
    IReadOnlyList<string> AllowedUnits);

public sealed record AiProviderResponse(
    string Status,
    AiStructuredOutput? Output = null,
    string? ExternalReference = null,
    string? FailureCode = null);

public interface IAiProviderPort
{
    ValueTask<AiProviderResponse> ExecuteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record RecordAiDispositionRequest(
    long ExpectedRunVersion,
    string RuleSetVersion,
    string CandidateId,
    string Kind,
    string Reason,
    string IdempotencyKey,
    string? HumanValue = null);

public sealed record AiReviewDispositionResult(
    AiDisposition Disposition,
    DateTimeOffset RecordedAt);

public sealed record AiRunResult(
    string RunId,
    long Version,
    string Status,
    AiObjectContext ObjectScope,
    AiRunEnvelope Envelope,
    AiVersionedReference ValidationProfile,
    IReadOnlyList<string> AllowedFields,
    IReadOnlyList<string> AllowedUnits,
    string ProviderStatus,
    string? ProviderExternalReference,
    string? ProviderFailureCode,
    AiStructuredOutput? OriginalOutput,
    AiValidationResult? Validation,
    IReadOnlyList<AiReviewDispositionResult> Dispositions,
    bool HumanReviewRequired,
    bool ManualFallbackRequired,
    string RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    string RuleSetVersion);

public sealed record AiReviewQueueResult(
    IReadOnlyList<AiRunResult> Runs,
    string RuleSetVersion);

public sealed record AiAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    AiObjectContext ObjectScope,
    string Capability);

public sealed record AiAuthorizationDecision(bool Allowed)
{
    public static AiAuthorizationDecision Permit { get; } = new(true);
    public static AiAuthorizationDecision Deny { get; } = new(false);
}

public interface IAiAuthorizationPort
{
    ValueTask<AiAuthorizationDecision> AuthorizeAsync(
        AiAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
