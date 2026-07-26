using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Quantity;

namespace OpenLIMS.Modules.Quantity;

internal sealed class HttpClaimsQuantityAuthorizationPort(IHttpContextAccessor accessor) : IQuantityAuthorizationPort
{
    public ValueTask<QuantityAuthorizationDecision> AuthorizeAsync(
        QuantityAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, QuantityClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, QuantityClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, QuantityClaimTypes.Laboratory, scope.LaboratoryId) &&
                      HasExactClaim(user, QuantityClaimTypes.Customer, scope.CustomerId) &&
                      HasExactClaim(user, QuantityClaimTypes.ServiceOrder, scope.ServiceOrderId) &&
                      HasExactClaim(user, QuantityClaimTypes.ProductCategory, scope.ProductCategory);
        return ValueTask.FromResult(allowed
            ? QuantityAuthorizationDecision.Permit
            : QuantityAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
