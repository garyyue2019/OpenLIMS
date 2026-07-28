using System.Net;
using System.Net.Http.Json;
using OpenLIMS.Contracts.Toy;
using OpenLIMS.Modules.Toy;
using Xunit;

namespace OpenLIMS.Toy.ContractTests;

[Trait("Profile", "toy")]
public sealed class ToyLabelReviewApiContractTests
{
    private const string ProductId = "00000000000000000000000000000200";
    private const string ArtifactId = "00000000000000000000000000000401";
    private const string ReviewId = "00000000000000000000000000000402";

    [Fact]
    public async Task Five_label_review_operations_expose_versioned_contracts()
    {
        using var factory = new ToyApiFactory();
        using var client = factory.CreateClient();

        using var artifact = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/label-artifacts",
            ArtifactRequest(),
            TestContext.Current.CancellationToken);
        using var version = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/label-artifacts/{ArtifactId}/versions",
            new AppendToyLabelArtifactVersionRequest(1, Hash('c'), Images('d', "v2")),
            TestContext.Current.CancellationToken);
        using var review = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/label-artifacts/{ArtifactId}/reviews",
            ReviewRequest(),
            TestContext.Current.CancellationToken);
        using var decision = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/label-reviews/{ReviewId}/decision",
            new DecideToyLabelReviewRequest(
                1, ToyLabelReviewDecisionValues.Approved, "reviewed against pinned evidence"),
            TestContext.Current.CancellationToken);
        using var status = await client.GetAsync(
            $"/api/v1/toy/products/{ProductId}/label-reviews/status" +
            "?productVersion=6&ageGradeDecisionVersion=2&market=CN&language=zh-CN" +
            $"&artifactType={ToyLabelArtifactTypes.Label}" +
            $"&ruleSetVersion={Uri.EscapeDataString(ToyLabelReviewContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, artifact.StatusCode);
        Assert.Equal(HttpStatusCode.Created, version.StatusCode);
        Assert.Equal(HttpStatusCode.Created, review.StatusCode);
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var statusBody = await status.Content.ReadFromJsonAsync<ToyLabelReviewStatusResult>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(statusBody);
        Assert.Equal(ToyLabelReviewStatusDecisions.Valid, statusBody.Decision);
        Assert.Equal(ToyLabelReviewContract.RuleSetVersion, statusBody.RuleSetVersion);
    }

    [Theory]
    [InlineData(ToyErrorCodes.LabelArtifactInvalid, HttpStatusCode.BadRequest)]
    [InlineData(ToyErrorCodes.LabelReviewInvalid, HttpStatusCode.BadRequest)]
    [InlineData(ToyErrorCodes.LabelImpactUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ToyErrorCodes.LabelReviewNotValid, HttpStatusCode.UnprocessableEntity)]
    public async Task Label_review_errors_map_to_stable_problem_contracts(
        string errorCode,
        HttpStatusCode expectedStatus)
    {
        using var factory = new ToyApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/toy/products/{ProductId}/label-artifacts",
            ArtifactRequest(),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Openapi_declares_all_five_label_review_operations()
    {
        using var factory = new ToyApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        foreach (var operation in new[]
        {
            "createToyLabelArtifact",
            "appendToyLabelArtifactVersion",
            "createToyLabelReview",
            "decideToyLabelReview",
            "getToyLabelReviewStatus"
        })
        {
            Assert.Contains(operation, content, StringComparison.Ordinal);
        }
    }

    private static CreateToyLabelArtifactRequest ArtifactRequest() => new(
        new ToyObjectContext("LEGAL-A", "LAB-A"),
        0,
        ToyLabelArtifactTypes.Label,
        "zh-CN",
        "CN",
        Hash('a'),
        Images('b', "v1"));

    private static CreateToyLabelReviewRequest ReviewRequest() => new(
        0,
        2,
        6,
        2,
        "CN",
        "zh-CN",
        [new ToyVersionedReference("AGE-CLAIM-ZH-CN", 1)],
        ToyLabelReviewContract.SupportedImpactRule,
        ToyLabelReviewContract.RuleSetVersion,
        null,
        null);

    private static IReadOnlyList<ToyLabelImageEvidenceInput> Images(char hash, string version) =>
        [new(new ToyImageObjectReference("toy-evidence", $"products/p1/label-{version}.png"), Hash(hash))];

    private static string Hash(char value) => new(value, 64);
}

internal sealed class StubToyLabelReviewService(string? errorCode) : IToyLabelReviewService
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 11, 0, 0, TimeSpan.Zero);

    public Task<ToyLabelArtifactResult> CreateArtifactAsync(
        string productId,
        CreateToyLabelArtifactRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        Artifact(productId, 1, cancellationToken);

    public Task<ToyLabelArtifactResult> AppendArtifactVersionAsync(
        string productId,
        string artifactId,
        AppendToyLabelArtifactVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        Artifact(productId, 2, cancellationToken);

    public Task<ToyLabelReviewResult> CreateReviewAsync(
        string productId,
        string artifactId,
        CreateToyLabelReviewRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        Review(productId, approved: false, cancellationToken);

    public Task<ToyLabelReviewResult> DecideReviewAsync(
        string productId,
        string reviewId,
        DecideToyLabelReviewRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        Review(productId, approved: true, cancellationToken);

    public Task<ToyLabelReviewStatusResult> GetStatusAsync(
        string productId,
        ToyLabelReviewStatusQuery query,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new ToyLabelReviewStatusResult(
            ToyLabelReviewStatusDecisions.Valid,
            [],
            productId,
            ToyLabelReviewApiContractTestsAccessor.ArtifactId,
            2,
            ToyLabelReviewApiContractTestsAccessor.ReviewId,
            1,
            query.ProductVersion,
            query.AgeGradeDecisionVersion,
            query.RuleSetVersion));
    }

    private Task<ToyLabelArtifactResult> Artifact(
        string productId,
        int versionCount,
        CancellationToken cancellationToken)
    {
        Throw(cancellationToken);
        var versions = Enumerable.Range(1, versionCount)
            .Select(version => new ToyLabelArtifactVersionEntry(
                version,
                new string((char)('a' + version - 1), 64),
                [new ToyLabelImageEvidenceEntry(
                    new ToyImageObjectReference("toy-evidence", $"products/p1/label-v{version}.png"),
                    new string((char)('c' + version - 1), 64))],
                "creator",
                Now))
            .ToArray();
        return Task.FromResult(new ToyLabelArtifactResult(
            ToyLabelReviewApiContractTestsAccessor.ArtifactId,
            productId,
            ToyLabelArtifactTypes.Label,
            "zh-CN",
            "CN",
            new ToyObjectContext("LEGAL-A", "LAB-A"),
            versions));
    }

    private Task<ToyLabelReviewResult> Review(
        string productId,
        bool approved,
        CancellationToken cancellationToken)
    {
        Throw(cancellationToken);
        var state = approved ? ToyLabelReviewStates.Approved : ToyLabelReviewStates.Draft;
        return Task.FromResult(new ToyLabelReviewResult(
            ToyLabelReviewApiContractTestsAccessor.ReviewId,
            productId,
            ToyLabelReviewApiContractTestsAccessor.ArtifactId,
            ToyLabelArtifactTypes.Label,
            new ToyObjectContext("LEGAL-A", "LAB-A"),
            [new ToyLabelReviewVersionEntry(
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
                approved
                    ? new ToyLabelReviewDecisionEntry(
                        ToyLabelReviewDecisionValues.Approved, "reviewer", Now, "reviewed")
                    : null,
                [],
                null,
                "creator",
                Now)]));
    }

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null)
            throw new ToyDomainException(errorCode);
    }
}

internal static class ToyLabelReviewApiContractTestsAccessor
{
    public const string ArtifactId = "00000000000000000000000000000401";
    public const string ReviewId = "00000000000000000000000000000402";
}
