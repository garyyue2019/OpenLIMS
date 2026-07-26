using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Billing;
using OpenLIMS.Modules.Billing;
using Xunit;

namespace OpenLIMS.Billing.UnitTests;

[Trait("Profile", "billing")]
public sealed class BillingRulesTests
{
    [Fact]
    public void Evidence_validation_normalizes_references_and_scope()
    {
        var validated = BillingRules.ValidateEvidence(Evidence(120.50m));

        Assert.Equal("CONTRACT-7", validated.ContractBaseline.Id);
        Assert.Equal("ITEM-PB-TEST", validated.ChargeDimension);
        Assert.Equal("CNY", validated.Currency.Id);
        Assert.Null(validated.ZeroAmountReason);
    }

    [Fact]
    public void Zero_amount_requires_reason_and_non_zero_forbids_it()
    {
        var zeroWithReason = BillingRules.ValidateEvidence(
            Evidence(0m) with { ZeroAmountReason = "contract free retest" });
        var zeroWithoutReason = Assert.Throws<BillingDomainException>(() =>
            BillingRules.ValidateEvidence(Evidence(0m)));
        var nonZeroWithReason = Assert.Throws<BillingDomainException>(() =>
            BillingRules.ValidateEvidence(Evidence(10m) with { ZeroAmountReason = "oops" }));

        Assert.Equal("contract free retest", zeroWithReason.ZeroAmountReason);
        Assert.Equal(BillingErrorCodes.ValidationFailed, zeroWithoutReason.ErrorCode);
        Assert.Equal(BillingErrorCodes.ValidationFailed, nonZeroWithReason.ErrorCode);
    }

    [Fact]
    public void Negative_amount_and_unknown_rule_set_fail_closed()
    {
        var negative = Assert.Throws<BillingDomainException>(() =>
            BillingRules.ValidateEvidence(Evidence(-5m)));
        var unknownRule = Assert.Throws<BillingDomainException>(() =>
            BillingRules.ValidateEvidence(Evidence(10m) with { RuleSetVersion = "BILLING-EVIDENCE@latest" }));

        Assert.Equal(BillingErrorCodes.ValidationFailed, negative.ErrorCode);
        Assert.Equal(BillingErrorCodes.ApplicabilityUnknown, unknownRule.ErrorCode);
    }

    [Fact]
    public void Adjustment_requires_non_zero_amount_and_reason()
    {
        var valid = BillingRules.ValidateAdjustment(new AddBillingAdjustmentRequest(
            BillingContract.RuleSetVersion, -20.5m, "credit for repeat"));
        var zero = Assert.Throws<BillingDomainException>(() =>
            BillingRules.ValidateAdjustment(new AddBillingAdjustmentRequest(
                BillingContract.RuleSetVersion, 0m, "reason")));
        var noReason = Assert.Throws<BillingDomainException>(() =>
            BillingRules.ValidateAdjustment(new AddBillingAdjustmentRequest(
                BillingContract.RuleSetVersion, 5m, " ")));

        Assert.Equal(-20.5m, valid.Amount);
        Assert.Equal(BillingErrorCodes.ValidationFailed, zero.ErrorCode);
        Assert.Equal(BillingErrorCodes.ValidationFailed, noReason.ErrorCode);
    }

    [Fact]
    public void Status_pins_rule_set_and_requires_evidence()
    {
        var evidence = new BillingEvidenceResult(
            "e1", BillingStages.BillableCandidate, BillingContract.RuleSetVersion, ObjectScope(),
            "g1", 5, "t1", new BillingVersionedReference("CONTRACT-7", 2),
            "ITEM-PB-TEST", "PRICE-2026Q3", 120.50m,
            new BillingVersionedReference("CNY", 1), null,
            [new BillingAdjustmentResult("a1", "e1", -20m, "credit", "actor", DateTimeOffset.MinValue)],
            "actor", DateTimeOffset.MinValue);

        var allowed = BillingRules.EvaluateStatus(Status(BillingContract.RuleSetVersion), evidence);
        var missing = BillingRules.EvaluateStatus(Status(BillingContract.RuleSetVersion), null);
        var unknownRule = BillingRules.EvaluateStatus(Status("BILLING-EVIDENCE@latest"), evidence);

        Assert.Equal(BillingStatusDecisions.Allowed, allowed.Decision);
        Assert.Equal(1, allowed.AdjustmentCount);
        Assert.Equal(BillingStatusDecisions.Blocked, missing.Decision);
        Assert.Contains(BillingStatusReasons.EvidenceRequired, missing.ReasonCodes);
        Assert.Equal(BillingStatusDecisions.Unknown, unknownRule.Decision);
    }

    [Fact]
    public async Task Authorization_requires_all_exact_scope_claims()
    {
        var context = new DefaultHttpContext { User = Principal(includeProductCategory: true) };
        var port = new HttpClaimsBillingAuthorizationPort(new HttpContextAccessor { HttpContext = context });

        var allowed = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);
        context.User = Principal(includeProductCategory: false);
        var denied = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    private static CreateBillingEvidenceRequest Evidence(decimal amount) => new(
        BillingContract.RuleSetVersion, ObjectScope(),
        "00000000000000000000000000000070", 5,
        new BillingVersionedReference("CONTRACT-7", 2),
        "ITEM-PB-TEST", "PRICE-2026Q3", amount,
        new BillingVersionedReference("CNY", 1));

    private static BillingObjectContext ObjectScope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS");

    private static BillingEvidenceStatusRequest Status(string ruleSetVersion) => new(
        "group-a", "00000000000000000000000000000080", ruleSetVersion);

    private static BillingAuthorizationRequest AuthRequest() => new(
        "group-a", "actor-a", ObjectScope(), BillingCapabilities.Record);

    private static ClaimsPrincipal Principal(bool includeProductCategory)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "actor-a"),
            new("organization_group", "group-a"),
            new(BillingClaimTypes.Capability, BillingCapabilities.Record),
            new(BillingClaimTypes.LegalEntity, "LEGAL-A"),
            new(BillingClaimTypes.Laboratory, "LAB-A"),
            new(BillingClaimTypes.Customer, "CUSTOMER-A"),
            new(BillingClaimTypes.ServiceOrder, "ORDER-A")
        };
        if (includeProductCategory) claims.Add(new Claim(BillingClaimTypes.ProductCategory, "TOYS"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
