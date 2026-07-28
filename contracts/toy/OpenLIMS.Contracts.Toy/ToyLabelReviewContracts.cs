namespace OpenLIMS.Contracts.Toy;

public static class ToyLabelReviewContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "TOY-LABEL-REVIEW@1.0.0";
    public const string ArtifactPath = "/api/v1/toy/products/{id}/label-artifacts";
    public const string ArtifactVersionPath =
        "/api/v1/toy/products/{id}/label-artifacts/{artifactId}/versions";
    public const string ReviewPath =
        "/api/v1/toy/products/{id}/label-artifacts/{artifactId}/reviews";
    public const string DecisionPath =
        "/api/v1/toy/products/{id}/label-reviews/{reviewId}/decision";
    public const string StatusPath = "/api/v1/toy/products/{id}/label-reviews/status";

    public static ToyVersionedReference SupportedImpactRule { get; } =
        new("TOY-LABEL-SCOPE-OVERLAP", 1);
}

public static class ToyLabelArtifactTypes
{
    public const string Packaging = "PACKAGING";
    public const string Label = "LABEL";
    public const string Instruction = "INSTRUCTION";
    public const string MarketingAgeClaim = "MARKETING_AGE_CLAIM";

    public static IReadOnlyList<string> All { get; } =
        [Packaging, Label, Instruction, MarketingAgeClaim];
}

public static class ToyLabelReviewStates
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Invalidated = "INVALIDATED";
}

public static class ToyLabelReviewDecisionValues
{
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";

    public static IReadOnlyList<string> All { get; } = [Approved, Rejected];
}

public static class ToyLabelChangeTypes
{
    public const string ProductVersion = "PRODUCT_VERSION";
    public const string AgeGradeDecision = "AGE_GRADE_DECISION";

    public static IReadOnlyList<string> All { get; } = [ProductVersion, AgeGradeDecision];
}

public static class ToyLabelImpactResults
{
    public const string Impacted = "IMPACTED";
    public const string NotImpacted = "NOT_IMPACTED";
    public const string Unknown = "UNKNOWN";
}

public static class ToyLabelReviewStatusDecisions
{
    public const string Valid = "VALID";
    public const string ReReviewRequired = "RE_REVIEW_REQUIRED";
    public const string Rejected = "REJECTED";
    public const string Unknown = "UNKNOWN";
}

public static class ToyLabelReviewStatusReasons
{
    public const string ArtifactRequired = "LABEL_ARTIFACT_REQUIRED";
    public const string ReviewRequired = "LABEL_REVIEW_REQUIRED";
    public const string ReviewNotDecided = "LABEL_REVIEW_NOT_DECIDED";
    public const string ArtifactVersionChanged = "LABEL_ARTIFACT_VERSION_CHANGED";
    public const string ReviewInvalidated = "LABEL_REVIEW_INVALIDATED";
    public const string ReviewRejected = "LABEL_REVIEW_REJECTED";
    public const string ImpactUnknown = "LABEL_IMPACT_UNKNOWN";
    public const string ChangeNotEvaluated = "LABEL_CHANGE_NOT_EVALUATED";
    public const string RuleSetVersionUnknown = "LABEL_RULE_SET_VERSION_UNKNOWN";
    public const string ToyUnavailable = "TOY_UNAVAILABLE";
}

public sealed record ToyImageObjectReference(string Bucket, string ObjectKey);

public sealed record ToyLabelImageEvidenceInput(
    ToyImageObjectReference ObjectRef,
    string Hash);

public sealed record CreateToyLabelArtifactRequest(
    ToyObjectContext ObjectScope,
    long ExpectedCurrentVersion,
    string ArtifactType,
    string Language,
    string Market,
    string ContentHash,
    IReadOnlyList<ToyLabelImageEvidenceInput> ImageEvidenceRefs);

public sealed record AppendToyLabelArtifactVersionRequest(
    long ExpectedCurrentVersion,
    string ContentHash,
    IReadOnlyList<ToyLabelImageEvidenceInput> ImageEvidenceRefs);

public sealed record ToyLabelImageEvidenceEntry(
    ToyImageObjectReference ObjectRef,
    string Hash);

public sealed record ToyLabelArtifactVersionEntry(
    long VersionNumber,
    string ContentHash,
    IReadOnlyList<ToyLabelImageEvidenceEntry> ImageEvidenceRefs,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record ToyLabelArtifactResult(
    string ArtifactId,
    string ProductId,
    string ArtifactType,
    string Language,
    string Market,
    ToyObjectContext ObjectScope,
    IReadOnlyList<ToyLabelArtifactVersionEntry> Versions)
{
    public long CurrentVersion => Versions.Count == 0 ? 0 : Versions.Max(item => item.VersionNumber);
}

public sealed record ToyLabelReviewChangeReference(
    string ChangeType,
    ToyVersionedReference ChangeRef);

public sealed record CreateToyLabelReviewRequest(
    long ExpectedCurrentVersion,
    long ArtifactVersion,
    long ProductVersion,
    long AgeGradeDecisionVersion,
    string Market,
    string Language,
    IReadOnlyList<ToyVersionedReference> ReviewScopeRefs,
    ToyVersionedReference ImpactRuleRef,
    string RuleSetVersion,
    long? PreviousReviewVersion,
    ToyLabelReviewChangeReference? TriggerChange);

public sealed record DecideToyLabelReviewRequest(
    long ExpectedCurrentVersion,
    string Decision,
    string DecisionReason);

public sealed record ToyLabelReviewDecisionEntry(
    string Decision,
    string ReviewedBy,
    DateTimeOffset ReviewedAt,
    string DecisionReason);

public sealed record ToyLabelImpactEvaluationEntry(
    string ChangeType,
    ToyVersionedReference ChangeRef,
    long ResultingProductVersion,
    long ResultingAgeGradeDecisionVersion,
    IReadOnlyList<ToyVersionedReference> ChangeScopeRefs,
    IReadOnlyList<ToyVersionedReference> MatchedScopeRefs,
    ToyVersionedReference? ImpactRuleRef,
    string Result,
    string Reason,
    DateTimeOffset EvaluatedAt);

public sealed record ToyLabelReviewInvalidationEntry(
    string ChangeType,
    ToyVersionedReference ChangeRef,
    IReadOnlyList<ToyVersionedReference> MatchedScopeRefs,
    ToyVersionedReference ImpactRuleRef,
    string Reason,
    DateTimeOffset InvalidatedAt);

public sealed record ToyLabelReviewVersionEntry(
    long ReviewVersion,
    long ArtifactVersion,
    long ProductVersion,
    long AgeGradeDecisionVersion,
    string Market,
    string Language,
    IReadOnlyList<ToyVersionedReference> ReviewScopeRefs,
    ToyVersionedReference ImpactRuleRef,
    string RuleSetVersion,
    long? PreviousReviewVersion,
    ToyLabelReviewChangeReference? TriggerChange,
    string State,
    ToyLabelReviewDecisionEntry? Decision,
    IReadOnlyList<ToyLabelImpactEvaluationEntry> ImpactEvaluations,
    ToyLabelReviewInvalidationEntry? Invalidation,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record ToyLabelReviewResult(
    string ReviewId,
    string ProductId,
    string ArtifactId,
    string ArtifactType,
    ToyObjectContext ObjectScope,
    IReadOnlyList<ToyLabelReviewVersionEntry> Versions)
{
    public long CurrentVersion => Versions.Count == 0 ? 0 : Versions.Max(item => item.ReviewVersion);
}

public sealed record ToyLabelReviewImpactRequest(
    string OrganizationGroupId,
    string ProductId,
    string ChangeType,
    ToyVersionedReference ChangeRef,
    long ResultingProductVersion,
    long ResultingAgeGradeDecisionVersion,
    IReadOnlyList<ToyVersionedReference>? ChangeScopeRefs,
    ToyVersionedReference? ImpactRuleRef,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record ToyLabelReviewImpactResult(
    string ProductId,
    string ChangeType,
    ToyVersionedReference ChangeRef,
    IReadOnlyList<ToyLabelImpactEvaluationEntry> Evaluations,
    string RuleSetVersion);

public sealed record ToyLabelReviewStatusQuery(
    long ProductVersion,
    long AgeGradeDecisionVersion,
    string Market,
    string Language,
    string ArtifactType,
    string RuleSetVersion);

public sealed record ToyLabelReviewStatusRequest(
    string OrganizationGroupId,
    string ProductId,
    long ProductVersion,
    long AgeGradeDecisionVersion,
    string Market,
    string Language,
    string ArtifactType,
    string RuleSetVersion)
{
    public string? CorrelationId { get; init; }
}

public sealed record ToyLabelReviewStatusResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string ProductId,
    string? ArtifactId,
    long? ArtifactVersion,
    string? ReviewId,
    long? ReviewVersion,
    long? ProductVersion,
    long? AgeGradeDecisionVersion,
    string RuleSetVersion);

public interface IToyLabelReviewStatusPort
{
    ValueTask<ToyLabelReviewStatusResult> EvaluateAsync(
        ToyLabelReviewStatusRequest request,
        CancellationToken cancellationToken = default);
}

public interface IToyLabelReviewImpactPort
{
    ValueTask<ToyLabelReviewImpactResult> EvaluateAsync(
        ToyLabelReviewImpactRequest request,
        CancellationToken cancellationToken = default);
}

public interface IToyLabelReviewService
{
    Task<ToyLabelArtifactResult> CreateArtifactAsync(
        string productId,
        CreateToyLabelArtifactRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyLabelArtifactResult> AppendArtifactVersionAsync(
        string productId,
        string artifactId,
        AppendToyLabelArtifactVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyLabelReviewResult> CreateReviewAsync(
        string productId,
        string artifactId,
        CreateToyLabelReviewRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyLabelReviewResult> DecideReviewAsync(
        string productId,
        string reviewId,
        DecideToyLabelReviewRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ToyLabelReviewStatusResult> GetStatusAsync(
        string productId,
        ToyLabelReviewStatusQuery query,
        string correlationId,
        CancellationToken cancellationToken = default);
}
