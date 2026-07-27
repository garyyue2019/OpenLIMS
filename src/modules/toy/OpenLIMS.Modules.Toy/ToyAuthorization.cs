using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class HttpClaimsToyAuthorizationPort(IHttpContextAccessor accessor) : IToyAuthorizationPort
{
    public ValueTask<ToyAuthorizationDecision> AuthorizeAsync(
        ToyAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, ToyClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, ToyClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, ToyClaimTypes.Laboratory, scope.LaboratoryId);
        return ValueTask.FromResult(allowed ? ToyAuthorizationDecision.Permit : ToyAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
