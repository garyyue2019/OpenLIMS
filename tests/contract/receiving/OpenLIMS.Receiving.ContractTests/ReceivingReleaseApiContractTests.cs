using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Receiving;
using Xunit;

namespace OpenLIMS.Receiving.ContractTests;

[Trait("Profile", "receiving")]
public sealed class ReceivingReleaseApiContractTests
{
    private const string ItemId = "00000000000000000000000000000003";

    [Fact]
    public async Task Release_operation_returns_immutable_versioned_contract()
    {
        using var factory = new ReleaseApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/received-items/{ItemId}/release-decisions",
            new SubmitReceivingReleaseDecisionRequest(3, ReceivingReleaseContract.RuleSetVersion, "Quality review complete."),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ReceivingReleaseDecisionResult>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(ReceivingReleaseOutcomes.Released, result.Outcome);
        Assert.Equal(ReceivingReleaseStates.Accepted, result.State);
        Assert.Equal(3, result.BoundItemVersion);
        Assert.Equal(4, result.ItemVersion);
        Assert.Equal(ReceivingReleaseContract.RuleSetVersion, result.ReleaseRuleVersion);
    }

    [Theory]
    [InlineData(ReceivingErrorCodes.ReleaseNotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(ReceivingErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(ReceivingErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(ReceivingErrorCodes.IdentityNotMatched, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReceivingErrorCodes.BlockingException, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReceivingErrorCodes.ReleaseApplicabilityUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReceivingErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Release_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new ReleaseApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/received-items/{ItemId}/release-decisions",
            new SubmitReceivingReleaseDecisionRequest(3, ReceivingReleaseContract.RuleSetVersion, "Quality review complete."),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Openapi_declares_release_operation()
    {
        using var factory = new ReleaseApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(ReceivingReleaseContract.DecisionsPath, content, StringComparison.Ordinal);
        Assert.Contains("submitReceivingReleaseDecision", content, StringComparison.Ordinal);
    }
}

internal sealed class ReleaseApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = ReceivingTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = ReceivingTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ReceivingTestAuthenticationHandler>(
                    ReceivingTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IReceivingReleaseService>();
            services.AddSingleton<IReceivingReleaseService>(new StubReceivingReleaseService(errorCode));
        });
    }
}

internal sealed class StubReceivingReleaseService(string? errorCode) : IReceivingReleaseService
{
    public Task<ReceivingReleaseDecisionResult> SubmitAsync(
        string receivedItemId,
        SubmitReceivingReleaseDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (errorCode is not null) throw new ReceivingDomainException(errorCode);
        return Task.FromResult(new ReceivingReleaseDecisionResult(
            "00000000000000000000000000000009",
            1,
            receivedItemId,
            "ITM-CONTRACT",
            request.ExpectedItemVersion,
            request.ExpectedItemVersion + 1,
            ReceivingReleaseStates.Accepted,
            "00000000000000000000000000000008",
            1,
            [],
            request.RuleSetVersion,
            ReceivingReleaseContract.ExceptionMatrixVersion,
            ReceivingReleaseOutcomes.Released,
            [
                ReceivingEligibilityActions.Disassembly,
                ReceivingEligibilityActions.SamplePreparation,
                ReceivingEligibilityActions.TestAssignment
            ],
            [],
            null,
            request.Rationale,
            new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero),
            "quality-a"));
    }
}
