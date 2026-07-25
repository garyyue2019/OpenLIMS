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
public sealed class ReceivingExceptionApiContractTests
{
    private const string ItemId = "00000000000000000000000000000003";
    private const string ExceptionId = "00000000000000000000000000000004";

    [Fact]
    public async Task Three_exception_operations_preserve_quarantine_contract()
    {
        using var factory = new ExceptionApiFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync(
            ReceivingExceptionContract.CreatePath, Create(), TestContext.Current.CancellationToken);
        using var get = await client.GetAsync(
            $"/api/v1/exceptions/{ExceptionId}", TestContext.Current.CancellationToken);
        using var decision = await client.PostAsJsonAsync(
            $"/api/v1/exceptions/{ExceptionId}/decisions", Decision(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.Created, decision.StatusCode);
        var result = await decision.Content.ReadFromJsonAsync<ReceivingExceptionResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("QUARANTINED", result.ItemState);
        Assert.Equal(ReceivingExceptionStatuses.ConditionallyAccepted, result.Status);
    }

    [Theory]
    [InlineData(ReceivingErrorCodes.DecisionNotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(ReceivingErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(ReceivingErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(ReceivingErrorCodes.ConditionalAcceptConstraintsRequired, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReceivingErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Exception_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new ExceptionApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/v1/exceptions/{ExceptionId}", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Openapi_declares_exception_operations()
    {
        using var factory = new ExceptionApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(ReceivingExceptionContract.CreatePath, content, StringComparison.Ordinal);
        Assert.Contains(ReceivingExceptionContract.DetailPath, content, StringComparison.Ordinal);
        Assert.Contains(ReceivingExceptionContract.DecisionsPath, content, StringComparison.Ordinal);
    }

    private static CreateReceivingExceptionRequest Create() => new(
        ItemId, 3, ReceivingExceptionTypes.QuantityShortage,
        new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero),
        "Insufficient quantity observed.", ["object://exception/evidence"], [new string('a', 64)]);

    private static SubmitReceivingExceptionDecisionRequest Decision() => new(
        1, ReceivingExceptionDecisionTypes.ConditionalAccept,
        [ReceivingEligibilityActions.Disassembly], [ReceivingEligibilityActions.SamplePreparation],
        new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
        ["object://exception/decision"], [new string('b', 64)],
        "Impact reviewed.", "Explicit constraints approved.", ReceivingExceptionContract.MatrixVersion);
}

internal sealed class ExceptionApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
            services.RemoveAll<IReceivingExceptionService>();
            services.AddSingleton<IReceivingExceptionService>(new StubReceivingExceptionService(errorCode));
        });
    }
}

internal sealed class StubReceivingExceptionService(string? errorCode) : IReceivingExceptionService
{
    private const string StubItemId = "00000000000000000000000000000003";
    private const string StubExceptionId = "00000000000000000000000000000004";
    public Task<ReceivingExceptionResult> CreateAsync(
        CreateReceivingExceptionRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        Result(ReceivingExceptionStatuses.Open, 1);

    public Task<ReceivingExceptionResult> GetAsync(
        string exceptionId, string correlationId, CancellationToken cancellationToken = default) =>
        Result(ReceivingExceptionStatuses.Open, 1);

    public Task<ReceivingExceptionResult> SubmitDecisionAsync(
        string exceptionId, SubmitReceivingExceptionDecisionRequest request, string correlationId,
        CancellationToken cancellationToken = default) =>
        Result(ReceivingExceptionStatuses.ConditionallyAccepted, 2);

    private Task<ReceivingExceptionResult> Result(string status, long version)
    {
        if (errorCode is not null) throw new ReceivingDomainException(errorCode);
        return Task.FromResult(new ReceivingExceptionResult(
            StubExceptionId, StubItemId, "ITM-CONTRACT", 3 + version, "QUARANTINED",
            ReceivingExceptionTypes.QuantityShortage, ReceivingExceptionSeverities.Standard,
            "Insufficient quantity observed.", new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero),
            ["object://exception/evidence"], [new string('a', 64)], "creator", new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero),
            status, version, []));
    }
}
