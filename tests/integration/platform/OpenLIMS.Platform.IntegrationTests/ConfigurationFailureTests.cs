using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenLIMS.Api;
using Xunit;

namespace OpenLIMS.Platform.IntegrationTests;

public sealed class ConfigurationFailureTests
{
    [Fact]
    public void Missing_deployment_group_fails_closed()
    {
        using var factory = new WebApplicationFactory<Program>();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("PLT.CONFIGURATION_INVALID", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Non_loopback_http_identity_provider_fails_closed_even_in_development()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Platform:OrganizationGroupId", "test-group");
            builder.UseSetting("Platform:PostgresConnectionString", "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test");
            builder.UseSetting("Platform:OidcAuthority", "http://identity.example.test/realms/openlims");
            builder.UseSetting("Platform:OidcAudience", "openlims-api");
            builder.UseSetting("Platform:ObjectStorageEndpoint", "https://storage.example.test");
            builder.UseSetting("Platform:ObjectStorageBucket", "test");
            builder.UseSetting("Platform:ObjectStorageAccessKey", "test-access");
            builder.UseSetting("Platform:ObjectStorageSecretKey", "test-secret");
            builder.UseSetting("Platform:PostgresCommandTimeoutSeconds", "1");
            builder.UseSetting("Platform:OidcMetadataTimeoutSeconds", "1");
            builder.UseSetting("Platform:ObjectStorageProbeTimeoutSeconds", "1");
            builder.UseSetting("Platform:DependencyProbeTimeoutSeconds", "2");
            builder.UseSetting("Platform:AllowInsecureDevelopmentOidc", "true");
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("PLT.CONFIGURATION_INVALID", exception.ToString(), StringComparison.Ordinal);
    }
}
