using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Result;

namespace OpenLIMS.Modules.Result;

internal sealed class HttpClaimsResultAuthorizationPort(IHttpContextAccessor accessor) : IResultAuthorizationPort
{
    public ValueTask<ResultAuthorizationDecision> AuthorizeAsync(
        ResultAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, ResultClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, ResultClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, ResultClaimTypes.Laboratory, scope.LaboratoryId) &&
                      HasExactClaim(user, ResultClaimTypes.Customer, scope.CustomerId) &&
                      HasExactClaim(user, ResultClaimTypes.ServiceOrder, scope.ServiceOrderId) &&
                      HasExactClaim(user, ResultClaimTypes.ProductCategory, scope.ProductCategory);
        return ValueTask.FromResult(allowed
            ? ResultAuthorizationDecision.Permit
            : ResultAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
