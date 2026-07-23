namespace OpenLIMS.Api;

public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    public string? OrganizationGroupId { get; init; }
    public string? PostgresConnectionString { get; init; }
    public string? OidcAuthority { get; init; }
    public string? OidcAudience { get; init; }
    public string? ObjectStorageEndpoint { get; init; }
    public string? ObjectStorageBucket { get; init; }
    public string? ObjectStorageAccessKey { get; init; }
    public string? ObjectStorageSecretKey { get; init; }
    public int PostgresCommandTimeoutSeconds { get; init; }
    public int OidcMetadataTimeoutSeconds { get; init; }
    public int ObjectStorageProbeTimeoutSeconds { get; init; }
    public int DependencyProbeTimeoutSeconds { get; init; }
    public bool AllowInsecureDevelopmentOidc { get; init; }
    public bool AllowInsecureDevelopmentObjectStorage { get; init; }
}
