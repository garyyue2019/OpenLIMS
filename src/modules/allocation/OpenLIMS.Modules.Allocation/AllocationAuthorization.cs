using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Allocation;

namespace OpenLIMS.Modules.Allocation;

internal sealed class HttpClaimsAllocationAuthorizationPort(IHttpContextAccessor accessor) : IAllocationAuthorizationPort
{
    public ValueTask<AllocationAuthorizationDecision> AuthorizeAsync(
        AllocationAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, AllocationClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, AllocationClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, AllocationClaimTypes.Laboratory, scope.LaboratoryId) &&
                      HasExactClaim(user, AllocationClaimTypes.Customer, scope.CustomerId) &&
                      HasExactClaim(user, AllocationClaimTypes.ServiceOrder, scope.ServiceOrderId) &&
                      HasExactClaim(user, AllocationClaimTypes.ProductCategory, scope.ProductCategory);
        return ValueTask.FromResult(allowed
            ? AllocationAuthorizationDecision.Permit
            : AllocationAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
