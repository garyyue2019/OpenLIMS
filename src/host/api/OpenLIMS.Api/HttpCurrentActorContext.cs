using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Api;

public sealed class HttpCurrentActorContext(IHttpContextAccessor httpContextAccessor) : ICurrentActorContext
{
    public ActorContext? Current
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var actorId = principal.FindFirst("sub")?.Value;
            var organizationGroupId = principal.FindFirst("organization_group")?.Value;
            return string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(organizationGroupId)
                ? null
                : new ActorContext(actorId, organizationGroupId);
        }
    }
}
