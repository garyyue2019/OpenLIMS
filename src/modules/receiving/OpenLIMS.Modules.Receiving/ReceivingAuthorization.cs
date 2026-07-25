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
            !HasExactClaim(user, ReceivingClaimTypes.ServiceOrder, request.ServiceOrderId) ||
            (request.ProductCategory is not null &&
             !HasExactClaim(user, ReceivingClaimTypes.ProductCategory, request.ProductCategory)))
        {
            return ValueTask.FromResult(ReceivingAuthorizationDecision.Denied);
        }

        if (!HasExactClaim(user, ReceivingClaimTypes.ReceivableServiceOrder, request.ServiceOrderId))
        {
            return ValueTask.FromResult(ReceivingAuthorizationDecision.NotReceivable);
        }

        var laboratoryCodes = user.FindAll(ReceivingClaimTypes.LaboratoryCode)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return ValueTask.FromResult(laboratoryCodes.Length == 1
            ? ReceivingAuthorizationDecision.AllowedFor(laboratoryCodes[0])
            : ReceivingAuthorizationDecision.Denied);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
