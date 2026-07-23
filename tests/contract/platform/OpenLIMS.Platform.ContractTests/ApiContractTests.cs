using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenLIMS.Api;
using OpenLIMS.BuildingBlocks.Platform;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;

namespace OpenLIMS.Platform.ContractTests;

public sealed class ApiContractTests : IClassFixture<ConfiguredApiFactory>
{
    private readonly HttpClient _client;

    public ApiContractTests(ConfiguredApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Liveness_returns_only_minimal_process_status()
    {
        using var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"live\"}", content);
    }

    [Fact]
    public async Task Forbidden_group_header_is_rejected_without_leaking_request_values()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Organization-Group-Id", "other-group-secret");
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PLT.GROUP_CONTEXT_OVERRIDE_FORBIDDEN", content, StringComparison.Ordinal);
        Assert.DoesNotContain("other-group-secret", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_supplied_group_claim_header_is_also_rejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Organization-Group-Claim", "test-group");
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PLT.GROUP_CONTEXT_OVERRIDE_FORBIDDEN", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_correlation_id_is_rejected_with_a_safe_problem()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "invalid id containing spaces");
        using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PLT.CORRELATION_ID_INVALID", content, StringComparison.Ordinal);
        Assert.DoesNotContain("invalid id containing spaces", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_system_status_returns_a_safe_authentication_problem()
    {
        using var response = await _client.GetAsync("/system/status", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("AUTH.AUTHENTICATION_REQUIRED", content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Authenticated_identity_from_another_group_is_rejected_before_dependency_access()
    {
        using var factory = new AuthenticatedApiFactory("other-group");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/system/status", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("AUTH.ORGANIZATION_GROUP_MISMATCH", content, StringComparison.Ordinal);
        Assert.DoesNotContain("other-group", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_identity_without_a_trusted_group_claim_is_rejected()
    {
        using var factory = new AuthenticatedApiFactory(null);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/system/status", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("AUTH.ORGANIZATION_GROUP_MISMATCH", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trusted_identity_for_the_bound_group_reaches_a_fresh_readiness_check()
    {
        using var factory = new AuthenticatedApiFactory("test-group");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/system/status", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("PLT.DEPENDENCY_UNREADY", content, StringComparison.Ordinal);
    }
}

public sealed class ConfiguredApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Platform:OrganizationGroupId", "test-group");
        builder.UseSetting("Platform:PostgresConnectionString", "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test");
        builder.UseSetting("Platform:OidcAuthority", "https://issuer.invalid/");
        builder.UseSetting("Platform:OidcAudience", "openlims-api");
        builder.UseSetting("Platform:ObjectStorageEndpoint", "http://127.0.0.1:1");
        builder.UseSetting("Platform:ObjectStorageBucket", "test");
        builder.UseSetting("Platform:ObjectStorageAccessKey", "test-access");
        builder.UseSetting("Platform:ObjectStorageSecretKey", "test-secret");
        builder.UseSetting("Platform:PostgresCommandTimeoutSeconds", "1");
        builder.UseSetting("Platform:OidcMetadataTimeoutSeconds", "1");
        builder.UseSetting("Platform:ObjectStorageProbeTimeoutSeconds", "1");
        builder.UseSetting("Platform:DependencyProbeTimeoutSeconds", "2");
        builder.UseSetting("Platform:AllowInsecureDevelopmentObjectStorage", "true");
    }
}

public sealed class AuthenticatedApiFactory(string? organizationGroupClaim) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Platform:OrganizationGroupId", "test-group");
        builder.UseSetting("Platform:PostgresConnectionString", "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test;Timeout=1");
        builder.UseSetting("Platform:OidcAuthority", "https://issuer.invalid/");
        builder.UseSetting("Platform:OidcAudience", "openlims-api");
        builder.UseSetting("Platform:ObjectStorageEndpoint", "http://127.0.0.1:1");
        builder.UseSetting("Platform:ObjectStorageBucket", "test");
        builder.UseSetting("Platform:ObjectStorageAccessKey", "test-access");
        builder.UseSetting("Platform:ObjectStorageSecretKey", "test-secret");
        builder.UseSetting("Platform:PostgresCommandTimeoutSeconds", "1");
        builder.UseSetting("Platform:OidcMetadataTimeoutSeconds", "1");
        builder.UseSetting("Platform:ObjectStorageProbeTimeoutSeconds", "1");
        builder.UseSetting("Platform:DependencyProbeTimeoutSeconds", "2");
        builder.UseSetting("Platform:AllowInsecureDevelopmentObjectStorage", "true");
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IPlatformDependencyProbe>();
            services.AddSingleton<IPlatformDependencyProbe>(new StubDependencyProbe(false));
            services.AddSingleton(new TestGroupClaim(organizationGroupClaim));
        });
    }
}

internal sealed record TestGroupClaim(string? Value);

internal sealed class StubDependencyProbe(bool ready) : IPlatformDependencyProbe
{
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(ready);
}

internal sealed class TestAuthenticationHandler(
    TestGroupClaim groupClaim,
    Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim> { new("sub", "contract-test-actor") };
        if (groupClaim.Value is not null)
        {
            claims.Add(new Claim("organization_group", groupClaim.Value));
        }
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
