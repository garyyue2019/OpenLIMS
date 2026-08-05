using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Commercial;
using OpenLIMS.Modules.Commercial;
using Xunit;

namespace OpenLIMS.Commercial.UnitTests;

[Trait("Profile", "commercial")]
public sealed class CommercialRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Catalog_versions_preserve_identity_and_reject_stale_revision()
    {
        var created = CommercialRules.CreateCatalog(Id(1), CatalogRequest(0), "actor-a", Now);
        var revised = CommercialRules.ReviseCatalog(
            created,
            CatalogRequest(1) with { DisplayName = "Method A revision" },
            "actor-b",
            Now.AddMinutes(1));
        var stale = Assert.Throws<CommercialDomainException>(() =>
            CommercialRules.ReviseCatalog(created, CatalogRequest(2), "actor-b", Now));

        Assert.Equal(2, revised.Version);
        Assert.Equal(created.RecordId, revised.RecordId);
        Assert.Equal(created.Code, revised.Code);
        Assert.Equal(CommercialErrorCodes.ExpectedVersionConflict, stale.ErrorCode);
    }

    [Fact]
    public void Catalog_identity_and_scope_cannot_change_during_revision()
    {
        var created = CommercialRules.CreateCatalog(Id(1), CatalogRequest(0), "actor-a", Now);
        var changedCode = Assert.Throws<CommercialDomainException>(() =>
            CommercialRules.ReviseCatalog(created, CatalogRequest(1) with { Code = "METHOD-B" }, "actor-b", Now));
        var changedScope = Assert.Throws<CommercialDomainException>(() =>
            CommercialRules.ReviseCatalog(
                created,
                CatalogRequest(1) with { ObjectScope = Scope() with { LaboratoryId = "LAB-B" } },
                "actor-b",
                Now));

        Assert.Equal(CommercialErrorCodes.ValidationFailed, changedCode.ErrorCode);
        Assert.Equal(CommercialErrorCodes.ValidationFailed, changedScope.ErrorCode);
    }

    [Fact]
    public void Incomplete_inquiry_creates_explicit_gap_queue()
    {
        var inquiry = CommercialRules.CreateInquiry(
            Id(2),
            new CreateInquiryRequest(new InquiryDetails(null, null, null, null, null, null, []), Scope()),
            "actor-a",
            Now);

        Assert.Equal(InquiryStates.GapsOpen, inquiry.State);
        Assert.Equal(7, inquiry.Gaps.Count);
        Assert.Contains(inquiry.Gaps, gap => gap.Code == InquiryGapCodes.SourceDocument);
    }

    [Fact]
    public void Gap_resolution_is_versioned_and_stale_resolution_fails()
    {
        var inquiry = CommercialRules.CreateInquiry(
            Id(2),
            new CreateInquiryRequest(new InquiryDetails(null, "TEXTILE", 2, "piece", "compliance", 10, [Ref("DOC", 1)]), Scope()),
            "actor-a",
            Now);
        var resolved = CommercialRules.ResolveGap(
            inquiry,
            InquiryGapCodes.CustomerName,
            new ResolveInquiryGapRequest(1, "Customer A"),
            "actor-b",
            Now.AddMinutes(1));
        var stale = Assert.Throws<CommercialDomainException>(() =>
            CommercialRules.ResolveGap(
                resolved,
                InquiryGapCodes.CustomerName,
                new ResolveInquiryGapRequest(1, "Customer B"),
                "actor-c",
                Now));

        Assert.Equal(2, resolved.Version);
        Assert.Equal(InquiryStates.ReadyForReview, resolved.State);
        Assert.Equal("Customer A", resolved.Details.CustomerName);
        Assert.Equal(CommercialErrorCodes.ExpectedVersionConflict, stale.ErrorCode);
    }

    [Fact]
    public void Blocked_capability_review_prevents_quote_issue()
    {
        var inquiry = CompleteInquiry();
        var reviewed = CommercialRules.RecordReview(
            inquiry,
            Review(inquiry.Version) with { AccreditationConfirmed = false },
            Id(3),
            "reviewer",
            Now.AddMinutes(1));
        var blocked = Assert.Throws<CommercialDomainException>(() =>
            CommercialRules.AddQuote(reviewed, Quote(reviewed.Version, 0), Id(4), "sales", Now));

        Assert.Equal(InquiryStates.ReviewBlocked, reviewed.State);
        Assert.Contains("ACCREDITATION_UNCONFIRMED", reviewed.CapabilityReviews[^1].BlockingReasons);
        Assert.Equal(CommercialErrorCodes.CapabilityReviewBlocked, blocked.ErrorCode);
    }

    [Fact]
    public void Passed_review_allows_deterministic_immutable_quote_versions()
    {
        var inquiry = CompleteInquiry();
        var reviewed = CommercialRules.RecordReview(
            inquiry,
            Review(inquiry.Version),
            Id(3),
            "reviewer",
            Now.AddMinutes(1));
        var quoted = CommercialRules.AddQuote(
            reviewed,
            Quote(reviewed.Version, 0),
            Id(4),
            "sales",
            Now.AddMinutes(2));
        var revised = CommercialRules.AddQuote(
            quoted,
            Quote(quoted.Version, 1) with
            {
                Lines = [new QuoteLineInput("LINE-1", "Testing", 3, 40.005m)]
            },
            Id(4),
            "sales",
            Now.AddMinutes(3));

        Assert.Equal(120.015m, quoted.QuoteVersions[0].TotalAmount);
        Assert.Equal(2, revised.QuoteVersions.Count);
        Assert.Equal(1, revised.QuoteVersions[0].Version);
        Assert.Equal(2, revised.QuoteVersions[1].Version);
        Assert.Equal(120.015m, revised.QuoteVersions[0].TotalAmount);
    }

    [Theory]
    [InlineData(CommercialChangeKinds.Quantity, true, false, true, true, false)]
    [InlineData(CommercialChangeKinds.Turnaround, false, true, false, true, false)]
    [InlineData(CommercialChangeKinds.Method, true, true, true, true, true)]
    public void Change_impacts_are_explicit_and_deterministic(
        string kind,
        bool price,
        bool turnaround,
        bool sample,
        bool work,
        bool report)
    {
        var inquiry = CompleteInquiry();
        var changed = CommercialRules.AddChangeImpact(
            inquiry,
            new RecordChangeImpactRequest(inquiry.Version, kind, "customer change"),
            Id(5),
            "actor-a",
            Now);
        var impact = changed.ChangeImpacts.Single();

        Assert.Equal(price, impact.PriceAffected);
        Assert.Equal(turnaround, impact.TurnaroundAffected);
        Assert.Equal(sample, impact.SampleRequirementAffected);
        Assert.Equal(work, impact.WorkInProgressAffected);
        Assert.Equal(report, impact.ReportAffected);
        Assert.Equal(InquiryStates.ChangeReviewRequired, changed.State);
    }

    [Fact]
    public async Task Authorization_requires_every_exact_scope_claim()
    {
        var context = new DefaultHttpContext { User = Principal(includeProductCategory: true) };
        var port = new HttpClaimsCommercialAuthorizationPort(new HttpContextAccessor { HttpContext = context });

        var allowed = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);
        context.User = Principal(includeProductCategory: false);
        var denied = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    private static InquiryResult CompleteInquiry() => CommercialRules.CreateInquiry(
        Id(2),
        new CreateInquiryRequest(
            new InquiryDetails("Customer A", "TEXTILE", 2, "piece", "compliance", 10, [Ref("DOC", 1)]),
            Scope()),
        "actor-a",
        Now);

    private static SubmitCatalogRecordRequest CatalogRequest(long expectedVersion) => new(
        expectedVersion,
        CatalogRecordKinds.Method,
        "METHOD-A",
        "Method A",
        new DateOnly(2026, 1, 1),
        null,
        CatalogRecordStates.Active,
        new Dictionary<string, string> { ["matrix"] = "textile" },
        [Ref("REQ-A", 1)],
        Scope());

    private static CapabilityReviewInput Review(long expectedVersion) => new(
        expectedVersion,
        true,
        true,
        true,
        true,
        true,
        true,
        [Ref("CAPABILITY", 2)],
        "reviewed");

    private static SubmitQuoteVersionRequest Quote(long inquiryVersion, long quoteVersion) => new(
        inquiryVersion,
        quoteVersion,
        Ref("SCOPE", 3),
        Ref("CNY", 1),
        Ref("CONTRACT", 2),
        10,
        ["shipping excluded"],
        [new QuoteLineInput("LINE-1", "Testing", 3, 40.005m)]);

    private static CommercialObjectContext Scope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE");

    private static CommercialVersionedReference Ref(string id, long version) => new(id, version);

    private static string Id(int value) => value.ToString("x32");

    private static CommercialAuthorizationRequest AuthRequest() => new(
        "group-a", "actor-a", Scope(), CommercialCapabilities.Write);

    private static ClaimsPrincipal Principal(bool includeProductCategory)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "actor-a"),
            new("organization_group", "group-a"),
            new(CommercialClaimTypes.Capability, CommercialCapabilities.Write),
            new(CommercialClaimTypes.LegalEntity, "LEGAL-A"),
            new(CommercialClaimTypes.Laboratory, "LAB-A"),
            new(CommercialClaimTypes.Customer, "CUSTOMER-A"),
            new(CommercialClaimTypes.ServiceOrder, "ORDER-A")
        };
        if (includeProductCategory)
            claims.Add(new Claim(CommercialClaimTypes.ProductCategory, "TEXTILE"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
