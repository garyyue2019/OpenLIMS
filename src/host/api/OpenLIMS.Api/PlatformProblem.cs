namespace OpenLIMS.Api;

public static class PlatformErrorCodes
{
    public const string ConfigurationInvalid = "PLT.CONFIGURATION_INVALID";
    public const string GroupContextOverrideForbidden = "PLT.GROUP_CONTEXT_OVERRIDE_FORBIDDEN";
    public const string OrganizationGroupMismatch = "AUTH.ORGANIZATION_GROUP_MISMATCH";
    public const string AuthenticationRequired = "AUTH.AUTHENTICATION_REQUIRED";
    public const string AuthorizationForbidden = "AUTH.AUTHORIZATION_FORBIDDEN";
    public const string InvalidCorrelationId = "PLT.CORRELATION_ID_INVALID";
    public const string DependencyUnready = "PLT.DEPENDENCY_UNREADY";
    public const string Unexpected = "PLT.UNEXPECTED";
}
