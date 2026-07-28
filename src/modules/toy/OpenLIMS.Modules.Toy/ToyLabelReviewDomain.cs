using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed record ValidatedToyLabelArtifact(
    string ArtifactType,
    string Language,
    string Market,
    string ContentHash,
    IReadOnlyList<ToyLabelImageEvidenceInput> ImageEvidenceRefs);

internal sealed record ValidatedToyLabelArtifactVersion(
    string ContentHash,
    IReadOnlyList<ToyLabelImageEvidenceInput> ImageEvidenceRefs);

internal sealed record ToyLabelImpactAssessment(
    string Result,
    IReadOnlyList<ToyVersionedReference> MatchedScopeRefs,
    string Reason);

internal static partial class ToyLabelReviewDomain
{
    public static ValidatedToyLabelArtifact ValidateArtifact(CreateToyLabelArtifactRequest? request)
    {
        if (request is null ||
            request.ExpectedCurrentVersion != 0 ||
            request.ObjectScope is null ||
            string.IsNullOrWhiteSpace(request.ObjectScope.LegalEntityId) ||
            string.IsNullOrWhiteSpace(request.ObjectScope.LaboratoryId) ||
            !ToyLabelArtifactTypes.All.Contains(request.ArtifactType, StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(request.Language) ||
            string.IsNullOrWhiteSpace(request.Market))
        {
            throw InvalidArtifact();
        }

        var version = ValidateVersion(request.ContentHash, request.ImageEvidenceRefs);
        return new ValidatedToyLabelArtifact(
            request.ArtifactType.Trim(),
            request.Language.Trim(),
            request.Market.Trim(),
            version.ContentHash,
            version.ImageEvidenceRefs);
    }

    public static ValidatedToyLabelArtifactVersion ValidateArtifactVersion(
        AppendToyLabelArtifactVersionRequest? request)
    {
        if (request is null || request.ExpectedCurrentVersion < 1)
            throw InvalidArtifact();
        return ValidateVersion(request.ContentHash, request.ImageEvidenceRefs);
    }

    public static void ValidateReview(
        CreateToyLabelReviewRequest? request,
        ToyLabelArtifactResult artifact,
        ToyProductOverview product,
        long currentReviewVersion,
        ToyLabelReviewVersionEntry? previous)
    {
        if (request is null ||
            request.ExpectedCurrentVersion != currentReviewVersion ||
            request.ArtifactVersion != artifact.CurrentVersion ||
            request.ProductVersion != product.Version ||
            request.AgeGradeDecisionVersion != product.EffectiveDecision?.VersionNumber ||
            !string.Equals(request.Market, artifact.Market, StringComparison.Ordinal) ||
            !string.Equals(request.Language, artifact.Language, StringComparison.Ordinal) ||
            !string.Equals(request.RuleSetVersion, ToyLabelReviewContract.RuleSetVersion, StringComparison.Ordinal) ||
            !ValidReference(request.ImpactRuleRef) ||
            !ValidReferences(request.ReviewScopeRefs))
        {
            throw InvalidReview();
        }

        if (currentReviewVersion == 0)
        {
            if (request.PreviousReviewVersion is not null || request.TriggerChange is not null || previous is not null)
                throw InvalidReview();
            return;
        }

        if (previous is null ||
            previous.ReviewVersion != currentReviewVersion ||
            request.PreviousReviewVersion != currentReviewVersion ||
            request.TriggerChange is null ||
            !ToyLabelChangeTypes.All.Contains(request.TriggerChange.ChangeType, StringComparer.Ordinal) ||
            !ValidReference(request.TriggerChange.ChangeRef) ||
            !HasRecordedTrigger(previous, request.TriggerChange))
        {
            throw InvalidReview();
        }
    }

    public static void ValidateDecision(
        DecideToyLabelReviewRequest? request,
        ToyLabelReviewVersionEntry review)
    {
        if (request is null ||
            request.ExpectedCurrentVersion != review.ReviewVersion ||
            !string.Equals(review.State, ToyLabelReviewStates.Draft, StringComparison.Ordinal))
        {
            throw new ToyDomainException(ToyErrorCodes.ExpectedVersionConflict);
        }

        if (!ToyLabelReviewDecisionValues.All.Contains(request.Decision, StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(request.DecisionReason))
        {
            throw InvalidReview();
        }
    }

    public static ToyLabelImpactAssessment EvaluateImpact(
        ToyLabelReviewVersionEntry review,
        ToyLabelReviewImpactRequest? request,
        ToyProductOverview product)
    {
        if (request is null ||
            !string.Equals(review.State, ToyLabelReviewStates.Approved, StringComparison.Ordinal) ||
            review.Invalidation is not null)
        {
            return Unknown("REVIEW_NOT_EVALUABLE");
        }

        var changeScopes = request.ChangeScopeRefs ?? [];
        var determinable =
            ToyLabelChangeTypes.All.Contains(request.ChangeType, StringComparer.Ordinal) &&
            ValidReference(request.ChangeRef) &&
            ValidReferences(changeScopes) &&
            ValidReference(request.ImpactRuleRef) &&
            string.Equals(request.OrganizationGroupId, "", StringComparison.Ordinal) == false &&
            request.ResultingProductVersion == product.Version &&
            request.ResultingAgeGradeDecisionVersion == product.EffectiveDecision?.VersionNumber &&
            string.Equals(request.RuleSetVersion, ToyLabelReviewContract.RuleSetVersion, StringComparison.Ordinal) &&
            review.ImpactRuleRef == ToyLabelReviewContract.SupportedImpactRule &&
            request.ImpactRuleRef == ToyLabelReviewContract.SupportedImpactRule &&
            request.ImpactRuleRef == review.ImpactRuleRef;
        if (!determinable)
            return Unknown("IMPACT_RULE_OR_CONTEXT_UNKNOWN");

        var reviewScopes = review.ReviewScopeRefs.ToHashSet();
        var matched = changeScopes
            .Where(reviewScopes.Contains)
            .Distinct()
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Version)
            .ToArray();
        return matched.Length == 0
            ? new ToyLabelImpactAssessment(
                ToyLabelImpactResults.NotImpacted,
                [],
                "NO_EXACT_SCOPE_OVERLAP")
            : new ToyLabelImpactAssessment(
                ToyLabelImpactResults.Impacted,
                matched,
                "EXACT_SCOPE_OVERLAP");
    }

    public static ToyLabelReviewStatusResult EvaluateStatus(
        ToyLabelReviewStatusRequest request,
        ToyLabelArtifactResult? artifact,
        ToyLabelReviewVersionEntry? review,
        string? reviewId = null)
    {
        if (!string.Equals(request.RuleSetVersion, ToyLabelReviewContract.RuleSetVersion, StringComparison.Ordinal))
            return UnknownStatus(request, ToyLabelReviewStatusReasons.RuleSetVersionUnknown, artifact, review, reviewId);
        if (artifact is null ||
            !string.Equals(artifact.ProductId, request.ProductId, StringComparison.Ordinal) ||
            !string.Equals(artifact.ArtifactType, request.ArtifactType, StringComparison.Ordinal) ||
            !string.Equals(artifact.Market, request.Market, StringComparison.Ordinal) ||
            !string.Equals(artifact.Language, request.Language, StringComparison.Ordinal))
        {
            return UnknownStatus(request, ToyLabelReviewStatusReasons.ArtifactRequired, null, null, null);
        }
        if (review is null)
            return UnknownStatus(request, ToyLabelReviewStatusReasons.ReviewRequired, artifact, null, reviewId);
        if (review.Invalidation is not null ||
            string.Equals(review.State, ToyLabelReviewStates.Invalidated, StringComparison.Ordinal))
        {
            return Status(
                ToyLabelReviewStatusDecisions.ReReviewRequired,
                [ToyLabelReviewStatusReasons.ReviewInvalidated],
                request,
                artifact,
                review,
                reviewId);
        }
        if (string.Equals(review.State, ToyLabelReviewStates.Rejected, StringComparison.Ordinal))
        {
            return Status(
                ToyLabelReviewStatusDecisions.Rejected,
                [ToyLabelReviewStatusReasons.ReviewRejected],
                request,
                artifact,
                review,
                reviewId);
        }
        if (string.Equals(review.State, ToyLabelReviewStates.Draft, StringComparison.Ordinal))
            return UnknownStatus(request, ToyLabelReviewStatusReasons.ReviewNotDecided, artifact, review, reviewId);
        if (!string.Equals(review.State, ToyLabelReviewStates.Approved, StringComparison.Ordinal))
            return UnknownStatus(request, ToyLabelReviewStatusReasons.ImpactUnknown, artifact, review, reviewId);
        if (review.ArtifactVersion != artifact.CurrentVersion)
        {
            return Status(
                ToyLabelReviewStatusDecisions.ReReviewRequired,
                [ToyLabelReviewStatusReasons.ArtifactVersionChanged],
                request,
                artifact,
                review,
                reviewId);
        }

        // UNKNOWN is permanent for that immutable review version. A later,
        // unrelated NOT_IMPACTED evaluation cannot make an earlier
        // indeterminate change disappear; only a new review version can.
        var unknown = review.ImpactEvaluations
            .LastOrDefault(item =>
                string.Equals(item.Result, ToyLabelImpactResults.Unknown, StringComparison.Ordinal));
        if (unknown is not null)
            return UnknownStatus(request, ToyLabelReviewStatusReasons.ImpactUnknown, artifact, review, reviewId);

        var pinnedMatches = review.ProductVersion == request.ProductVersion &&
                            review.AgeGradeDecisionVersion == request.AgeGradeDecisionVersion;
        if (!pinnedMatches)
        {
            var evaluation = review.ImpactEvaluations.LastOrDefault(item =>
                item.ResultingProductVersion == request.ProductVersion &&
                item.ResultingAgeGradeDecisionVersion == request.AgeGradeDecisionVersion);
            if (evaluation is null)
                return UnknownStatus(request, ToyLabelReviewStatusReasons.ChangeNotEvaluated, artifact, review, reviewId);
            if (!string.Equals(evaluation.Result, ToyLabelImpactResults.NotImpacted, StringComparison.Ordinal))
                return UnknownStatus(request, ToyLabelReviewStatusReasons.ImpactUnknown, artifact, review, reviewId);
        }

        return Status(
            ToyLabelReviewStatusDecisions.Valid,
            [],
            request,
            artifact,
            review,
            reviewId);
    }

    private static ValidatedToyLabelArtifactVersion ValidateVersion(
        string? contentHash,
        IReadOnlyList<ToyLabelImageEvidenceInput>? evidence)
    {
        if (!ValidHash(contentHash) ||
            evidence is null ||
            evidence.Count == 0 ||
            evidence.Any(item =>
                item is null ||
                item.ObjectRef is null ||
                string.IsNullOrWhiteSpace(item.ObjectRef.Bucket) ||
                string.IsNullOrWhiteSpace(item.ObjectRef.ObjectKey) ||
                !ValidHash(item.Hash)) ||
            evidence.Select(item => (item.ObjectRef.Bucket, item.ObjectRef.ObjectKey))
                .Distinct()
                .Count() != evidence.Count)
        {
            throw InvalidArtifact();
        }

        return new ValidatedToyLabelArtifactVersion(
            contentHash!.ToLowerInvariant(),
            evidence.Select(item => new ToyLabelImageEvidenceInput(
                    new ToyImageObjectReference(item.ObjectRef.Bucket.Trim(), item.ObjectRef.ObjectKey.Trim()),
                    item.Hash.ToLowerInvariant()))
                .ToArray());
    }

    private static bool HasRecordedTrigger(
        ToyLabelReviewVersionEntry previous,
        ToyLabelReviewChangeReference trigger)
    {
        if (previous.Invalidation is not null &&
            string.Equals(previous.Invalidation.ChangeType, trigger.ChangeType, StringComparison.Ordinal) &&
            previous.Invalidation.ChangeRef == trigger.ChangeRef)
        {
            return true;
        }

        return previous.ImpactEvaluations.Any(item =>
            string.Equals(item.Result, ToyLabelImpactResults.Unknown, StringComparison.Ordinal) &&
            string.Equals(item.ChangeType, trigger.ChangeType, StringComparison.Ordinal) &&
            item.ChangeRef == trigger.ChangeRef);
    }

    private static bool ValidReferences(IReadOnlyList<ToyVersionedReference>? references) =>
        references is { Count: > 0 } &&
        references.All(ValidReference) &&
        references.Distinct().Count() == references.Count;

    private static bool ValidReference(ToyVersionedReference? reference) =>
        reference is not null && !string.IsNullOrWhiteSpace(reference.Id) && reference.Version > 0;

    private static bool ValidHash(string? value) =>
        value is not null && Sha256Pattern().IsMatch(value);

    private static ToyLabelImpactAssessment Unknown(string reason) =>
        new(ToyLabelImpactResults.Unknown, [], reason);

    private static ToyLabelReviewStatusResult UnknownStatus(
        ToyLabelReviewStatusRequest request,
        string reason,
        ToyLabelArtifactResult? artifact,
        ToyLabelReviewVersionEntry? review,
        string? reviewId) =>
        Status(
            ToyLabelReviewStatusDecisions.Unknown,
            [reason],
            request,
            artifact,
            review,
            reviewId);

    private static ToyLabelReviewStatusResult Status(
        string decision,
        IReadOnlyList<string> reasons,
        ToyLabelReviewStatusRequest request,
        ToyLabelArtifactResult? artifact,
        ToyLabelReviewVersionEntry? review,
        string? reviewId) => new(
        decision,
        reasons,
        request.ProductId,
        artifact?.ArtifactId,
        artifact?.CurrentVersion,
        reviewId,
        review?.ReviewVersion,
        review?.ProductVersion,
        review?.AgeGradeDecisionVersion,
        ToyLabelReviewContract.RuleSetVersion);

    private static ToyDomainException InvalidArtifact() =>
        new(ToyErrorCodes.LabelArtifactInvalid);

    private static ToyDomainException InvalidReview() =>
        new(ToyErrorCodes.LabelReviewInvalid);

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
