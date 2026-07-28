using OpenLIMS.Contracts.Toy;
using OpenLIMS.Modules.Toy;
using Xunit;

namespace OpenLIMS.Toy.UnitTests;

[Trait("Profile", "toy")]
public sealed class ToyLabelReviewDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ToyLabelArtifactTypes.Packaging)]
    [InlineData(ToyLabelArtifactTypes.Label)]
    [InlineData(ToyLabelArtifactTypes.Instruction)]
    [InlineData(ToyLabelArtifactTypes.MarketingAgeClaim)]
    public void Four_artifact_types_freeze_language_market_content_and_image_hashes(string artifactType)
    {
        var validated = ToyLabelReviewDomain.ValidateArtifact(ArtifactRequest(artifactType));

        Assert.Equal(artifactType, validated.ArtifactType);
        Assert.Equal("zh-CN", validated.Language);
        Assert.Equal("CN", validated.Market);
        Assert.Equal(Hash('a'), validated.ContentHash);
        Assert.Equal(Hash('b'), Assert.Single(validated.ImageEvidenceRefs).Hash);
    }

    [Theory]
    [InlineData("", "zh-CN", "CN", "hash", true)]
    [InlineData("PACKAGING", "", "CN", "hash", true)]
    [InlineData("PACKAGING", "zh-CN", "", "hash", true)]
    [InlineData("PACKAGING", "zh-CN", "CN", "", true)]
    [InlineData("PACKAGING", "zh-CN", "CN", "hash", false)]
    [InlineData("BROCHURE", "zh-CN", "CN", "hash", true)]
    public void Missing_or_unapproved_artifact_dimensions_fail_closed(
        string artifactType,
        string language,
        string market,
        string content,
        bool hasImage)
    {
        var request = ArtifactRequest(artifactType) with
        {
            Language = language,
            Market = market,
            ContentHash = content == "hash" ? Hash('a') : content,
            ImageEvidenceRefs = hasImage ? Images() : []
        };

        var exception = Assert.Throws<ToyDomainException>(() => ToyLabelReviewDomain.ValidateArtifact(request));

        Assert.Equal(ToyErrorCodes.LabelArtifactInvalid, exception.ErrorCode);
    }

    [Fact]
    public void Review_pins_artifact_product_age_scope_rule_and_re_review_cause()
    {
        var firstArtifact = Artifact(1);
        var artifact = Artifact(2);
        var product = Product();
        var first = ReviewRequest(0, 1, null, null);

        ToyLabelReviewDomain.ValidateReview(first, firstArtifact, product, 0, null);

        var prior = ReviewVersion(
            state: ToyLabelReviewStates.Invalidated,
            invalidation: new ToyLabelReviewInvalidationEntry(
                ToyLabelChangeTypes.AgeGradeDecision,
                new ToyVersionedReference("age-grade", 2),
                [new ToyVersionedReference("AGE-CLAIM-ZH-CN", 1)],
                ToyLabelReviewContract.SupportedImpactRule,
                "AGE_SCOPE_CHANGED",
                Now));
        var next = ReviewRequest(
            1,
            2,
            1,
            new ToyLabelReviewChangeReference(
                ToyLabelChangeTypes.AgeGradeDecision,
                new ToyVersionedReference("age-grade", 2)));

        ToyLabelReviewDomain.ValidateReview(next, artifact, product, 1, prior);

        Assert.Equal(ToyErrorCodes.LabelReviewInvalid,
            Assert.Throws<ToyDomainException>(() => ToyLabelReviewDomain.ValidateReview(
                next with { TriggerChange = null }, artifact, product, 1, prior)).ErrorCode);
        Assert.Equal(ToyErrorCodes.LabelReviewInvalid,
            Assert.Throws<ToyDomainException>(() => ToyLabelReviewDomain.ValidateReview(
                next with { ArtifactVersion = 1 }, artifact, product, 1, prior)).ErrorCode);
    }

    [Theory]
    [InlineData(ToyLabelReviewDecisionValues.Approved)]
    [InlineData(ToyLabelReviewDecisionValues.Rejected)]
    public void A_draft_accepts_one_terminal_immutable_decision(string decision)
    {
        var request = new DecideToyLabelReviewRequest(1, decision, "reviewed against pinned evidence");
        ToyLabelReviewDomain.ValidateDecision(request, ReviewVersion(state: ToyLabelReviewStates.Draft));

        Assert.Equal(ToyErrorCodes.ExpectedVersionConflict,
            Assert.Throws<ToyDomainException>(() => ToyLabelReviewDomain.ValidateDecision(
                request, ReviewVersion(state: ToyLabelReviewStates.Approved))).ErrorCode);
    }

    [Fact]
    public void Exact_scope_overlap_is_local_and_unsupported_rules_are_unknown()
    {
        var review = ReviewVersion();
        var product = Product();
        var chineseChange = ImpactRequest(
            [new ToyVersionedReference("AGE-CLAIM-ZH-CN", 1)],
            ToyLabelReviewContract.SupportedImpactRule);
        var englishChange = ImpactRequest(
            [new ToyVersionedReference("AGE-CLAIM-EN-US", 1)],
            ToyLabelReviewContract.SupportedImpactRule);
        var unknownRule = ImpactRequest(
            [new ToyVersionedReference("AGE-CLAIM-ZH-CN", 1)],
            new ToyVersionedReference("TOY-LABEL-SCOPE-OVERLAP", 99));
        var missingRule = ImpactRequest(
            [new ToyVersionedReference("AGE-CLAIM-ZH-CN", 1)], null);
        var missingScopes = ImpactRequest([], ToyLabelReviewContract.SupportedImpactRule);

        var impacted = ToyLabelReviewDomain.EvaluateImpact(review, chineseChange, product);
        var notImpacted = ToyLabelReviewDomain.EvaluateImpact(review, englishChange, product);
        var unknown = ToyLabelReviewDomain.EvaluateImpact(review, unknownRule, product);

        Assert.Equal(ToyLabelImpactResults.Impacted, impacted.Result);
        Assert.Equal("AGE-CLAIM-ZH-CN", Assert.Single(impacted.MatchedScopeRefs).Id);
        Assert.Equal(ToyLabelImpactResults.NotImpacted, notImpacted.Result);
        Assert.Empty(notImpacted.MatchedScopeRefs);
        Assert.Equal(ToyLabelImpactResults.Unknown, unknown.Result);
        Assert.Equal(ToyLabelImpactResults.Unknown,
            ToyLabelReviewDomain.EvaluateImpact(review, missingRule, product).Result);
        Assert.Equal(ToyLabelImpactResults.Unknown,
            ToyLabelReviewDomain.EvaluateImpact(review, missingScopes, product).Result);
    }

    [Fact]
    public void Status_rejects_invalidated_rejected_unknown_and_stale_unassessed_reviews()
    {
        var request = StatusRequest();
        var artifact = Artifact();

        Assert.Equal(ToyLabelReviewStatusDecisions.Valid,
            ToyLabelReviewDomain.EvaluateStatus(request, artifact, ReviewVersion()).Decision);
        Assert.Equal(ToyLabelReviewStatusDecisions.ReReviewRequired,
            ToyLabelReviewDomain.EvaluateStatus(
                request,
                artifact,
                ReviewVersion(
                    state: ToyLabelReviewStates.Invalidated,
                    invalidation: new ToyLabelReviewInvalidationEntry(
                        ToyLabelChangeTypes.AgeGradeDecision,
                        new ToyVersionedReference("age-grade", 2),
                        [new ToyVersionedReference("AGE-CLAIM-ZH-CN", 1)],
                        ToyLabelReviewContract.SupportedImpactRule,
                        "AGE_SCOPE_CHANGED",
                        Now))).Decision);
        Assert.Equal(ToyLabelReviewStatusDecisions.Rejected,
            ToyLabelReviewDomain.EvaluateStatus(
                request, artifact, ReviewVersion(state: ToyLabelReviewStates.Rejected)).Decision);
        Assert.Equal(ToyLabelReviewStatusDecisions.Unknown,
            ToyLabelReviewDomain.EvaluateStatus(
                request with { ProductVersion = 7 }, artifact, ReviewVersion()).Decision);
        var unknownReview = ReviewVersion(evaluations:
        [
            new ToyLabelImpactEvaluationEntry(
                ToyLabelChangeTypes.ProductVersion,
                new ToyVersionedReference("product-change", 2),
                5,
                1,
                [new ToyVersionedReference("PRODUCT-NAME", 1)],
                [],
                null,
                ToyLabelImpactResults.Unknown,
                "IMPACT_RULE_MISSING",
                Now)
        ]);
        Assert.Equal(ToyLabelReviewStatusDecisions.Unknown,
            ToyLabelReviewDomain.EvaluateStatus(request, artifact, unknownReview).Decision);
    }

    private static CreateToyLabelArtifactRequest ArtifactRequest(string artifactType) => new(
        new ToyObjectContext("LEGAL-A", "LAB-A"),
        0,
        artifactType,
        "zh-CN",
        "CN",
        Hash('a'),
        Images());

    private static IReadOnlyList<ToyLabelImageEvidenceInput> Images() =>
        [new(new ToyImageObjectReference("toy-evidence", "products/p1/label-v1.png"), Hash('b'))];

    private static ToyLabelArtifactResult Artifact(int versionCount = 2) => new(
        "00000000000000000000000000000401",
        "00000000000000000000000000000200",
        ToyLabelArtifactTypes.Label,
        "zh-CN",
        "CN",
        new ToyObjectContext("LEGAL-A", "LAB-A"),
        new[]
        {
            new ToyLabelArtifactVersionEntry(1, Hash('a'),
                [new ToyLabelImageEvidenceEntry(
                    new ToyImageObjectReference("toy-evidence", "products/p1/label-v1.png"), Hash('b'))],
                "author", Now),
            new ToyLabelArtifactVersionEntry(2, Hash('c'),
                [new ToyLabelImageEvidenceEntry(
                    new ToyImageObjectReference("toy-evidence", "products/p1/label-v2.png"), Hash('d'))],
                "author", Now)
        }.Take(versionCount).ToArray());

    private static ToyProductOverview Product() => new(
        "00000000000000000000000000000200",
        6,
        ToyContract.RuleSetVersion,
        new ToyObjectContext("LEGAL-A", "LAB-A"),
        new ToyAgeGradeDecisionEntry(
            "age-grade", "00000000000000000000000000000200", 2, 36, "rationale",
            new ToyVersionedReference("GB6675.2", 2), "approver", ToyDecisionStates.Effective, Now, Now),
        [],
        [],
        [],
        [],
        ToyAccessibilityStatuses.Settled);

    private static CreateToyLabelReviewRequest ReviewRequest(
        long expectedCurrentVersion,
        long artifactVersion,
        long? previousReviewVersion,
        ToyLabelReviewChangeReference? triggerChange) => new(
        expectedCurrentVersion,
        artifactVersion,
        6,
        2,
        "CN",
        "zh-CN",
        [new ToyVersionedReference("AGE-CLAIM-ZH-CN", 1)],
        ToyLabelReviewContract.SupportedImpactRule,
        ToyLabelReviewContract.RuleSetVersion,
        previousReviewVersion,
        triggerChange);

    private static ToyLabelReviewVersionEntry ReviewVersion(
        string state = ToyLabelReviewStates.Approved,
        ToyLabelReviewInvalidationEntry? invalidation = null,
        IReadOnlyList<ToyLabelImpactEvaluationEntry>? evaluations = null) => new(
        1,
        2,
        6,
        2,
        "CN",
        "zh-CN",
        [new ToyVersionedReference("AGE-CLAIM-ZH-CN", 1)],
        ToyLabelReviewContract.SupportedImpactRule,
        ToyLabelReviewContract.RuleSetVersion,
        null,
        null,
        state,
        state == ToyLabelReviewStates.Draft
            ? null
            : new ToyLabelReviewDecisionEntry(
                state == ToyLabelReviewStates.Rejected
                    ? ToyLabelReviewDecisionValues.Rejected
                    : ToyLabelReviewDecisionValues.Approved,
                "reviewer", Now, "reason"),
        evaluations ?? [],
        invalidation,
        "author",
        Now);

    private static ToyLabelReviewImpactRequest ImpactRequest(
        IReadOnlyList<ToyVersionedReference> scopes,
        ToyVersionedReference? rule) => new(
        "group-a",
        "00000000000000000000000000000200",
        ToyLabelChangeTypes.AgeGradeDecision,
        new ToyVersionedReference("age-grade", 2),
        6,
        2,
        scopes,
        rule,
        ToyLabelReviewContract.RuleSetVersion);

    private static ToyLabelReviewStatusRequest StatusRequest() => new(
        "group-a",
        "00000000000000000000000000000200",
        6,
        2,
        "CN",
        "zh-CN",
        ToyLabelArtifactTypes.Label,
        ToyLabelReviewContract.RuleSetVersion);

    private static string Hash(char value) => new(value, 64);
}
