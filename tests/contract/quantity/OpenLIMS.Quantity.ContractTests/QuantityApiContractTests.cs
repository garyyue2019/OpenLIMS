using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenLIMS.Contracts.Quantity;
using OpenLIMS.Modules.Quantity;
using Xunit;

namespace OpenLIMS.Quantity.ContractTests;

[Trait("Profile", "quantity")]
public sealed class QuantityApiContractTests
{
    private const string AccountId = "00000000000000000000000000000020";

    [Fact]
    public async Task Four_quantity_operations_expose_versioned_contracts()
    {
        using var factory = new QuantityApiFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync(
            QuantityContract.CreateAccountPath,
            AccountRequest(),
            TestContext.Current.CancellationToken);
        using var posted = await client.PostAsJsonAsync(
            $"/api/v1/quantity-accounts/{AccountId}/entries",
            EntryRequest(),
            TestContext.Current.CancellationToken);
        using var read = await client.GetAsync(
            $"/api/v1/quantity-accounts/{AccountId}",
            TestContext.Current.CancellationToken);
        using var availability = await client.GetAsync(
            $"/api/v1/quantity-accounts/{AccountId}/availability?expectedVersion=2&requestedAmount=10.50&ruleSetVersion={Uri.EscapeDataString(QuantityContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        var account = await created.Content.ReadFromJsonAsync<QuantityAccountResult>(
            TestContext.Current.CancellationToken);
        var entry = await posted.Content.ReadFromJsonAsync<QuantityEntryResult>(
            TestContext.Current.CancellationToken);
        var gate = await availability.Content.ReadFromJsonAsync<QuantityAvailabilityResult>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(account);
        Assert.Equal(1, account.Version);
        Assert.Equal(QuantityDimensions.Mass, account.Dimension);
        Assert.NotNull(entry);
        Assert.Equal(QuantityEntryTypes.Receipt, entry.EntryType);
        Assert.NotNull(gate);
        Assert.Equal(QuantityAvailabilityDecisions.Allowed, gate.Decision);
    }

    [Theory]
    [InlineData(QuantityErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(QuantityErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(QuantityErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(QuantityErrorCodes.InsufficientBalance, HttpStatusCode.UnprocessableEntity)]
    [InlineData(QuantityErrorCodes.DimensionMismatch, HttpStatusCode.UnprocessableEntity)]
    [InlineData(QuantityErrorCodes.NotQuantifiable, HttpStatusCode.UnprocessableEntity)]
    [InlineData(QuantityErrorCodes.ApplicabilityUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(QuantityErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Quantity_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new QuantityApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            QuantityContract.CreateAccountPath,
            AccountRequest(),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_availability_query_is_rejected()
    {
        using var factory = new QuantityApiFactory();
        using var client = factory.CreateClient();
        using var missingAmount = await client.GetAsync(
            $"/api/v1/quantity-accounts/{AccountId}/availability?expectedVersion=1&ruleSetVersion={Uri.EscapeDataString(QuantityContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);
        using var invalidVersion = await client.GetAsync(
            $"/api/v1/quantity-accounts/{AccountId}/availability?expectedVersion=latest&requestedAmount=10&ruleSetVersion={Uri.EscapeDataString(QuantityContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, missingAmount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidVersion.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_quantity_operations()
    {
        using var factory = new QuantityApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(QuantityContract.CreateAccountPath, content, StringComparison.Ordinal);
        Assert.Contains("createQuantityAccount", content, StringComparison.Ordinal);
        Assert.Contains("postQuantityEntry", content, StringComparison.Ordinal);
        Assert.Contains("getQuantityAccount", content, StringComparison.Ordinal);
        Assert.Contains("getQuantityAvailability", content, StringComparison.Ordinal);
    }

    internal static CreateQuantityAccountRequest AccountRequest() => new(
        QuantityContract.RuleSetVersion,
        new QuantityObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        new QuantitySubjectReference(QuantitySubjectTypes.ReceivedItem, "ITEM-1", 1),
        true,
        QuantityDimensions.Mass,
        "GRAM",
        2,
        0.20m);

    private static PostQuantityEntryRequest EntryRequest() => new(
        1,
        QuantityContract.RuleSetVersion,
        QuantityEntryTypes.Receipt,
        100.00m);
}

internal sealed class QuantityApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = QuantityTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = QuantityTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, QuantityTestAuthenticationHandler>(
                    QuantityTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IQuantityAccountService>();
            services.RemoveAll<IQuantityAvailabilityPort>();
            services.AddSingleton<IQuantityAccountService>(new StubQuantityAccountService(errorCode));
            services.AddSingleton<IQuantityAvailabilityPort>(new StubQuantityAvailabilityPort(errorCode));
        });
    }
}

internal sealed class StubQuantityAccountService(string? errorCode) : IQuantityAccountService
{
    public Task<QuantityAccountResult> CreateAsync(
        CreateQuantityAccountRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        AccountAsync(1, request, cancellationToken);

    public Task<QuantityEntryResult> PostEntryAsync(
        string quantityAccountId,
        PostQuantityEntryRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new QuantityDomainException(errorCode);
        return Task.FromResult(new QuantityEntryResult(
            "00000000000000000000000000000021",
            quantityAccountId,
            request.ExpectedCurrentVersion + 1,
            request.EntryType,
            request.Amount,
            request.Amount,
            0m,
            request.Amount,
            request.ReferencedEntryId,
            request.ReservationId,
            request.Reason,
            "contract-actor",
            new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero)));
    }

    public Task<QuantityAccountResult> GetAccountAsync(
        string quantityAccountId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        AccountAsync(2, QuantityApiContractTests.AccountRequest(), cancellationToken);

    private Task<QuantityAccountResult> AccountAsync(
        long version,
        CreateQuantityAccountRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new QuantityDomainException(errorCode);
        return Task.FromResult(new QuantityAccountResult(
            "00000000000000000000000000000020",
            version,
            QuantityContract.RuleSetVersion,
            request.ObjectScope,
            request.Subject,
            request.Dimension,
            request.Unit,
            request.PrecisionScale,
            request.ConservationTolerance,
            100.00m,
            0m,
            100.00m,
            "contract-actor",
            new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero)));
    }
}

internal sealed class StubQuantityAvailabilityPort(string? errorCode) : IQuantityAvailabilityPort
{
    public ValueTask<QuantityAvailabilityResult> EvaluateAsync(
        QuantityAvailabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new QuantityDomainException(errorCode);
        return ValueTask.FromResult(new QuantityAvailabilityResult(
            QuantityAvailabilityDecisions.Allowed,
            [],
            request.QuantityAccountId,
            request.ExpectedAccountVersion,
            100.00m,
            QuantityContract.RuleSetVersion));
    }
}

internal sealed class QuantityTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Quantity.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
