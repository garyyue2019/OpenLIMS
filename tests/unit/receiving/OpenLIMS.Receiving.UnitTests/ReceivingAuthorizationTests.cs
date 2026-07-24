using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Receiving;
using Xunit;

namespace OpenLIMS.Receiving.UnitTests;

[Trait("Profile", "receiving")]
public sealed class ReceivingAuthorizationTests
{
    [Fact]
    public async Task Exact_multidimensional_scope_is_allowed()
    {
        var port = Port(
            Claim("organization_group", "group-a"),
            Claim(ReceivingClaimTypes.Capability, ReceivingCapabilities.Register),
            Claim(ReceivingClaimTypes.LegalEntity, "legal-a"),
            Claim(ReceivingClaimTypes.Laboratory, "lab-a"),
            Claim(ReceivingClaimTypes.LaboratoryCode, "LAB-A"),
            Claim(ReceivingClaimTypes.Customer, "customer-a"),
            Claim(ReceivingClaimTypes.ServiceOrder, "order-a"),
            Claim(ReceivingClaimTypes.ReceivableServiceOrder, "order-a"));

        var decision = await port.AuthorizeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(ReceivingAuthorizationOutcome.Allowed, decision.Outcome);
        Assert.Equal("LAB-A", decision.LaboratoryCode);
    }

    [Fact]
    public async Task System_administrator_without_business_capability_is_denied()
    {
        var port = Port(
            Claim("organization_group", "group-a"),
            Claim("role", "system_admin"),
            Claim(ReceivingClaimTypes.LegalEntity, "legal-a"),
            Claim(ReceivingClaimTypes.Laboratory, "lab-a"),
            Claim(ReceivingClaimTypes.Customer, "customer-a"),
            Claim(ReceivingClaimTypes.ServiceOrder, "order-a"),
            Claim(ReceivingClaimTypes.ReceivableServiceOrder, "order-a"));

        var decision = await port.AuthorizeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(ReceivingAuthorizationOutcome.Denied, decision.Outcome);
    }

    [Fact]
    public async Task Authorized_scope_without_one_trusted_laboratory_code_is_denied()
    {
        var port = Port(
            Claim("organization_group", "group-a"),
            Claim(ReceivingClaimTypes.Capability, ReceivingCapabilities.Register),
            Claim(ReceivingClaimTypes.LegalEntity, "legal-a"),
            Claim(ReceivingClaimTypes.Laboratory, "lab-a"),
            Claim(ReceivingClaimTypes.Customer, "customer-a"),
            Claim(ReceivingClaimTypes.ServiceOrder, "order-a"),
            Claim(ReceivingClaimTypes.ReceivableServiceOrder, "order-a"));

        var decision = await port.AuthorizeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(ReceivingAuthorizationOutcome.Denied, decision.Outcome);
    }

    [Fact]
    public async Task Cross_laboratory_scope_does_not_expand_from_group_membership()
    {
        var port = Port(
            Claim("organization_group", "group-a"),
            Claim(ReceivingClaimTypes.Capability, ReceivingCapabilities.Register),
            Claim(ReceivingClaimTypes.LegalEntity, "legal-a"),
            Claim(ReceivingClaimTypes.Laboratory, "lab-b"),
            Claim(ReceivingClaimTypes.Customer, "customer-a"),
            Claim(ReceivingClaimTypes.ServiceOrder, "order-a"),
            Claim(ReceivingClaimTypes.ReceivableServiceOrder, "order-a"));

        var decision = await port.AuthorizeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(ReceivingAuthorizationOutcome.Denied, decision.Outcome);
    }

    [Fact]
    public async Task Authorized_scope_without_current_receivable_evidence_is_not_receivable()
    {
        var port = Port(
            Claim("organization_group", "group-a"),
            Claim(ReceivingClaimTypes.Capability, ReceivingCapabilities.Register),
            Claim(ReceivingClaimTypes.LegalEntity, "legal-a"),
            Claim(ReceivingClaimTypes.Laboratory, "lab-a"),
            Claim(ReceivingClaimTypes.Customer, "customer-a"),
            Claim(ReceivingClaimTypes.ServiceOrder, "order-a"));

        var decision = await port.AuthorizeAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(ReceivingAuthorizationOutcome.ServiceOrderNotReceivable, decision.Outcome);
    }

    private static HttpClaimsReceivingAuthorizationPort Port(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "test");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new HttpClaimsReceivingAuthorizationPort(new HttpContextAccessor { HttpContext = context });
    }

    private static ReceivingAuthorizationRequest Request() => new(
        "group-a",
        "actor-a",
        "legal-a",
        "lab-a",
        "customer-a",
        "order-a",
        ReceivingCapabilities.Register);

    private static Claim Claim(string type, string value) => new(type, value);
}
