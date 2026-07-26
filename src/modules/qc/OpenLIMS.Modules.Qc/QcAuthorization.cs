using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Qc;

namespace OpenLIMS.Modules.Qc;

internal sealed class HttpClaimsQcAuthorizationPort(IHttpContextAccessor accessor) : IQcAuthorizationPort
{
    public ValueTask<QcAuthorizationDecision> AuthorizeAsync(
        QcAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, QcClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, QcClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, QcClaimTypes.Laboratory, scope.LaboratoryId);
        return ValueTask.FromResult(allowed ? QcAuthorizationDecision.Permit : QcAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
