using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Ai;

namespace OpenLIMS.Modules.Ai;

internal sealed class HttpClaimsAiAuthorizationPort(IHttpContextAccessor accessor) : IAiAuthorizationPort
{
    public ValueTask<AiAuthorizationDecision> AuthorizeAsync(
        AiAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var allowed = user?.Identity?.IsAuthenticated == true &&
            user.HasClaim("organization_group", request.OrganizationGroupId) &&
            user.HasClaim(AiClaimTypes.Capability, request.Capability) &&
            user.HasClaim(AiClaimTypes.LegalEntity, request.ObjectScope.LegalEntityId) &&
            user.HasClaim(AiClaimTypes.Laboratory, request.ObjectScope.LaboratoryId) &&
            user.HasClaim(AiClaimTypes.Customer, request.ObjectScope.CustomerId) &&
            user.HasClaim(AiClaimTypes.ServiceOrder, request.ObjectScope.ServiceOrderId) &&
            user.HasClaim(AiClaimTypes.ProductCategory, request.ObjectScope.ProductCategory);
        return ValueTask.FromResult(allowed ? AiAuthorizationDecision.Permit : AiAuthorizationDecision.Deny);
    }
}
