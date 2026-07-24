using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal sealed class HttpClaimsReceivingAuthorizationPort(IHttpContextAccessor accessor) : IReceivingAuthorizationPort
{
    public ValueTask<ReceivingAuthorizationDecision> AuthorizeAsync(
        ReceivingAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true ||
            !HasExactClaim(user, "organization_group", request.OrganizationGroupId) ||
            !HasExactClaim(user, ReceivingClaimTypes.Capability, request.Capability) ||
            !HasExactClaim(user, ReceivingClaimTypes.LegalEntity, request.LegalEntityId) ||
            !HasExactClaim(user, ReceivingClaimTypes.Laboratory, request.LaboratoryId) ||
            !HasExactClaim(user, ReceivingClaimTypes.Customer, request.CustomerId) ||
            !HasExactClaim(user, ReceivingClaimTypes.ServiceOrder, request.ServiceOrderId))
        {
            return ValueTask.FromResult(ReceivingAuthorizationDecision.Denied);
        }

        return ValueTask.FromResult(
            HasExactClaim(user, ReceivingClaimTypes.ReceivableServiceOrder, request.ServiceOrderId)
                ? ReceivingAuthorizationDecision.Allowed
                : ReceivingAuthorizationDecision.NotReceivable);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
