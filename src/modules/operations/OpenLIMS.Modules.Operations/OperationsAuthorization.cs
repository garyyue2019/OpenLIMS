using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Operations;

namespace OpenLIMS.Modules.Operations;

internal sealed class HttpClaimsOperationsAuthorizationPort(IHttpContextAccessor accessor) : IOperationsAuthorizationPort
{
    public ValueTask<OperationsAuthorizationDecision> AuthorizeAsync(
        OperationsAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, OperationsClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, OperationsClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, OperationsClaimTypes.Laboratory, scope.LaboratoryId) &&
                      HasExactClaim(user, OperationsClaimTypes.Customer, scope.CustomerId) &&
                      HasExactClaim(user, OperationsClaimTypes.ServiceOrder, scope.ServiceOrderId) &&
                      HasExactClaim(user, OperationsClaimTypes.ProductCategory, scope.ProductCategory);
        return ValueTask.FromResult(allowed
            ? OperationsAuthorizationDecision.Permit
            : OperationsAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
