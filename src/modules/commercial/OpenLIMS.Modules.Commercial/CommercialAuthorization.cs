using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Commercial;

namespace OpenLIMS.Modules.Commercial;

internal sealed class HttpClaimsCommercialAuthorizationPort(IHttpContextAccessor accessor) : ICommercialAuthorizationPort
{
    public ValueTask<CommercialAuthorizationDecision> AuthorizeAsync(
        CommercialAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, CommercialClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, CommercialClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, CommercialClaimTypes.Laboratory, scope.LaboratoryId) &&
                      HasExactClaim(user, CommercialClaimTypes.Customer, scope.CustomerId) &&
                      HasExactClaim(user, CommercialClaimTypes.ServiceOrder, scope.ServiceOrderId) &&
                      HasExactClaim(user, CommercialClaimTypes.ProductCategory, scope.ProductCategory);
        return ValueTask.FromResult(allowed
            ? CommercialAuthorizationDecision.Permit
            : CommercialAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
