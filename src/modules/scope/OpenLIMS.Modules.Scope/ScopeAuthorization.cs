using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Scope;

namespace OpenLIMS.Modules.Scope;

internal sealed class HttpClaimsScopeAuthorizationPort(IHttpContextAccessor accessor) : IScopeAuthorizationPort
{
    public ValueTask<ScopeAuthorizationDecision> AuthorizeAsync(
        ScopeAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, ScopeClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, ScopeClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, ScopeClaimTypes.Laboratory, scope.LaboratoryId) &&
                      HasExactClaim(user, ScopeClaimTypes.Customer, scope.CustomerId) &&
                      HasExactClaim(user, ScopeClaimTypes.ServiceOrder, scope.ServiceOrderId) &&
                      HasExactClaim(user, ScopeClaimTypes.ProductCategory, scope.ProductCategory);
        return ValueTask.FromResult(allowed
            ? ScopeAuthorizationDecision.Permit
            : ScopeAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
