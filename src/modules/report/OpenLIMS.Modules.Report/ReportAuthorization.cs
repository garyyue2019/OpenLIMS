using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

internal sealed class HttpClaimsReportAuthorizationPort(IHttpContextAccessor accessor) : IReportAuthorizationPort
{
    public ValueTask<ReportAuthorizationDecision> AuthorizeAsync(
        ReportAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var scope = request.ObjectScope;
        var allowed = user?.Identity?.IsAuthenticated == true &&
                      HasExactClaim(user, "organization_group", request.OrganizationGroupId) &&
                      HasExactClaim(user, ReportClaimTypes.Capability, request.Capability) &&
                      HasExactClaim(user, ReportClaimTypes.LegalEntity, scope.LegalEntityId) &&
                      HasExactClaim(user, ReportClaimTypes.Laboratory, scope.LaboratoryId) &&
                      HasExactClaim(user, ReportClaimTypes.Customer, scope.CustomerId) &&
                      HasExactClaim(user, ReportClaimTypes.ServiceOrder, scope.ServiceOrderId) &&
                      HasExactClaim(user, ReportClaimTypes.ProductCategory, scope.ProductCategory);
        return ValueTask.FromResult(allowed
            ? ReportAuthorizationDecision.Permit
            : ReportAuthorizationDecision.Deny);
    }

    private static bool HasExactClaim(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}

/// <summary>
/// Default accreditation-scope resolver. The authoritative source is OD-012,
/// still open, so out of the box nothing resolves and every line that claims
/// accreditation is judged NOT_ACCREDITED. A deployment plugs in the real
/// source by registering its own <see cref="IAccreditationScopePort"/>.
/// </summary>
internal sealed class UnresolvedAccreditationScopePort : IAccreditationScopePort
{
    public ValueTask<AccreditationScopeLookupResult?> ResolveAsync(
        AccreditationScopeLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<AccreditationScopeLookupResult?>(null);
    }
}

/// <summary>
/// Signatory authority derived from the caller's claims. The authoritative
/// personnel-qualification source is OD-012, still open, so this stays behind
/// the port and fails closed when the claim is absent.
/// </summary>
internal sealed class HttpClaimsSignatoryAuthorityPort(IHttpContextAccessor accessor) : ISignatoryAuthorityPort
{
    public ValueTask<SignatoryAuthorityDecision> EvaluateAsync(
        SignatoryAuthorityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = accessor.HttpContext?.User;
        var authorized = user?.Identity?.IsAuthenticated == true &&
                         user.FindAll("signatory_scope").Any(claim => string.Equals(
                             claim.Value,
                             $"{request.SiteId}|{request.Method.Id}@{request.Method.Version}|{request.ParameterRange}",
                             StringComparison.Ordinal));
        return ValueTask.FromResult(new SignatoryAuthorityDecision(
            authorized,
            authorized ? [] : [ReportBlockerReasons.SignatoryNotAuthorized]));
    }
}
