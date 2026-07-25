using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Receiving;
using Xunit;

namespace OpenLIMS.Receiving.ContractTests;

[Trait("Profile", "receiving")]
public sealed class IdentityAssessmentApiContractTests
{
    private const string ItemId = "00000000000000000000000000000003";

    [Fact]
    public async Task Three_identity_operations_return_versioned_contracts()
    {
        using var factory = new IdentityApiFactory();
        using var client = factory.CreateClient();

        using var get = await client.GetAsync($"/api/v1/received-items/{ItemId}/identity-assessment", TestContext.Current.CancellationToken);
        using var observation = await client.PostAsJsonAsync(
            $"/api/v1/received-items/{ItemId}/identity-observations",
            Observation(),
            TestContext.Current.CancellationToken);
        using var decision = await client.PostAsJsonAsync(
            $"/api/v1/received-items/{ItemId}/identity-decisions",
            Decision(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.Created, observation.StatusCode);
        Assert.Equal(HttpStatusCode.Created, decision.StatusCode);
        var result = await decision.Content.ReadFromJsonAsync<IdentityAssessmentResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("QUARANTINED", result.CurrentState);
        Assert.Equal(IdentityAssessmentStates.Matched, result.AssessmentState);
    }

    [Fact]
    public async Task Client_group_override_is_rejected_without_echoing_the_value()
    {
        using var factory = new IdentityApiFactory();
        using var client = factory.CreateClient();
        const string json = """
            {
              "organizationGroupId": "other-group-secret",
              "expectedItemVersion": 1,
              "observedLabels": ["LABEL"],
              "observedModel": "MODEL-001",
              "observedBatch": "BATCH-001",
              "appearance": "intact",
              "attachmentRefs": ["object://photo"],
              "attachmentHashes": ["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"]
            }
            """;

        using var response = await client.PostAsync(
            $"/api/v1/received-items/{ItemId}/identity-observations",
            new StringContent(json, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(ReceivingErrorCodes.IdentityEvidenceIncomplete, content, StringComparison.Ordinal);
        Assert.DoesNotContain("other-group-secret", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReceivingErrorCodes.AuthorizationDenied, HttpStatusCode.Forbidden)]
    [InlineData(ReceivingErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(ReceivingErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(ReceivingErrorCodes.IdentityConflict, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReceivingErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Identity_domain_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new IdentityApiFactory(errorCode);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/v1/received-items/{ItemId}/identity-assessment",
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Openapi_document_declares_all_identity_operations()
    {
        using var factory = new IdentityApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(IdentityAssessmentContract.AssessmentPath, content, StringComparison.Ordinal);
        Assert.Contains(IdentityAssessmentContract.ObservationsPath, content, StringComparison.Ordinal);
        Assert.Contains(IdentityAssessmentContract.DecisionsPath, content, StringComparison.Ordinal);
    }

    private static CreateIdentityObservationRequest Observation() => new(
        1,
        ["OUTER-LABEL-01"],
        "MODEL-001",
        "BATCH-001",
        "intact red toy set",
        ["object://identity/photo-01"],
        [new string('a', 64)]);

    private static SubmitIdentityDecisionRequest Decision() => new(
        2,
        1,
        1,
        IdentityDecisionOutcomes.Matched,
        "CONSISTENT",
        "All required evidence is consistent.",
        IdentityAssessmentContract.RuleSetVersion);
}

internal sealed class IdentityApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    ReceivingTestAuthenticationHandler.SchemeName,
                    _ => { });
            services.RemoveAll<IIdentityAssessmentService>();
            services.AddSingleton<IIdentityAssessmentService>(new StubIdentityAssessmentService(errorCode));
        });
    }
}

internal sealed class StubIdentityAssessmentService(string? errorCode) : IIdentityAssessmentService
{
    public Task<IdentityAssessmentResult> GetAsync(
        string receivedItemId,
        string correlationId,
        CancellationToken cancellationToken = default) => Result(receivedItemId, IdentityAssessmentStates.NotStarted, 1);

    public Task<IdentityAssessmentResult> AddObservationAsync(
        string receivedItemId,
        CreateIdentityObservationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) => Result(receivedItemId, IdentityAssessmentStates.InProgress, 2);

    public Task<IdentityAssessmentResult> SubmitDecisionAsync(
        string receivedItemId,
        SubmitIdentityDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) => Result(receivedItemId, IdentityAssessmentStates.Matched, 3);

    private Task<IdentityAssessmentResult> Result(string receivedItemId, string state, long version)
    {
        if (errorCode is not null) throw new ReceivingDomainException(errorCode);
        return Task.FromResult(new IdentityAssessmentResult(
            receivedItemId,
            "ITM-CONTRACT",
            "QUARANTINED",
            version,
            state,
            version - 1,
            null,
            [],
            []));
    }
}
