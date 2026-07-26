using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Billing;

namespace OpenLIMS.Modules.Billing;

internal sealed class HttpClaimsBillingAuthorizationPort(IHttpContextAccessor accessor) : IBillingAuthorizationPort
{
    public ValueTask<BillingAuthorizationDecision> AuthorizeAsync(
        BillingAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, BillingClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, BillingClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, BillingClaimTypes.Laboratory, scope.LaboratoryId) &&
                      HasExactClaim(user, BillingClaimTypes.Customer, scope.CustomerId) &&
                      HasExactClaim(user, BillingClaimTypes.ServiceOrder, scope.ServiceOrderId) &&
                      HasExactClaim(user, BillingClaimTypes.ProductCategory, scope.ProductCategory);
        return ValueTask.FromResult(allowed
            ? BillingAuthorizationDecision.Permit
            : BillingAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
