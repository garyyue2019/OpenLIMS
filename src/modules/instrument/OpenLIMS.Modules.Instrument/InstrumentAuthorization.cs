using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Instrument;

namespace OpenLIMS.Modules.Instrument;

internal sealed class HttpClaimsInstrumentAuthorizationPort(IHttpContextAccessor accessor) : IInstrumentAuthorizationPort
{
    public ValueTask<InstrumentAuthorizationDecision> AuthorizeAsync(
        InstrumentAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, InstrumentClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, InstrumentClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, InstrumentClaimTypes.Laboratory, scope.LaboratoryId);
        return ValueTask.FromResult(allowed
            ? InstrumentAuthorizationDecision.Permit
            : InstrumentAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
