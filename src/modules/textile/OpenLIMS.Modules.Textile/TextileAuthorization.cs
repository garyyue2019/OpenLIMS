using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Textile;

namespace OpenLIMS.Modules.Textile;

internal sealed class HttpClaimsTextileAuthorizationPort(IHttpContextAccessor accessor) :
    ITextileAuthorizationPort
{
    public ValueTask<TextileAuthorizationDecision> AuthorizeAsync(
        TextileAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, TextileClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, TextileClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, TextileClaimTypes.Laboratory, scope.LaboratoryId);
        return ValueTask.FromResult(
            allowed ? TextileAuthorizationDecision.Permit : TextileAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
