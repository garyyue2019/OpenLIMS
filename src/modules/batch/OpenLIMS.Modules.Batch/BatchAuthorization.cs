using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Batch;

namespace OpenLIMS.Modules.Batch;

internal sealed class HttpClaimsBatchAuthorizationPort(IHttpContextAccessor accessor) : IBatchAuthorizationPort
{
    public ValueTask<BatchAuthorizationDecision> AuthorizeAsync(
        BatchAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, BatchClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, BatchClaimTypes.LegalEntity, request.ObjectScope.LegalEntityId) &&
                      HasExactClaim(user, BatchClaimTypes.Laboratory, request.ObjectScope.LaboratoryId);
        return ValueTask.FromResult(allowed
            ? BatchAuthorizationDecision.Permit
            : BatchAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
