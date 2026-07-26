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
using OpenLIMS.Contracts.Billing;
using OpenLIMS.Modules.Billing;
using Xunit;

namespace OpenLIMS.Billing.ContractTests;

[Trait("Profile", "billing")]
public sealed class BillingApiContractTests
{
    private const string EvidenceId = "00000000000000000000000000000080";

    [Fact]
    public async Task Four_billing_operations_expose_versioned_contracts()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync(
            BillingContract.CreateEvidencePath, EvidenceRequest(), TestContext.Current.CancellationToken);
        using var adjusted = await client.PostAsJsonAsync(
            $"/api/v1/billing-evidence/{EvidenceId}/adjustments",
            new AddBillingAdjustmentRequest(BillingContract.RuleSetVersion, -20m, "credit"),
            TestContext.Current.CancellationToken);
        using var read = await client.GetAsync(
            $"/api/v1/billing-evidence/{EvidenceId}", TestContext.Current.CancellationToken);
        using var status = await client.GetAsync(
            $"/api/v1/billing-evidence/{EvidenceId}/status?ruleSetVersion={Uri.EscapeDataString(BillingContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Created, adjusted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var gate = await status.Content.ReadFromJsonAsync<BillingEvidenceStatusResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(gate);
        Assert.Equal(BillingStatusDecisions.Allowed, gate.Decision);
    }

    [Theory]
    [InlineData(BillingErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(BillingErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(BillingErrorCodes.DuplicateBilling, HttpStatusCode.Conflict)]
    [InlineData(BillingErrorCodes.EligibilityBlocked, HttpStatusCode.UnprocessableEntity)]
    [InlineData(BillingErrorCodes.ApplicabilityUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(BillingErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Billing_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new BillingApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            BillingContract.CreateEvidencePath, EvidenceRequest(), TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_status_query_is_rejected()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/v1/billing-evidence/{EvidenceId}/status", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_billing_operations()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(BillingContract.CreateEvidencePath, content, StringComparison.Ordinal);
        foreach (var operation in new[]
        {
            "createBillingEvidence", "addBillingAdjustment", "getBillingEvidence", "getBillingEvidenceStatus"
        })
        {
            Assert.Contains(operation, content, StringComparison.Ordinal);
        }
    }

    private static CreateBillingEvidenceRequest EvidenceRequest() => new(
        BillingContract.RuleSetVersion,
        new BillingObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        "00000000000000000000000000000070", 5,
        new BillingVersionedReference("CONTRACT-7", 2),
        "ITEM-PB-TEST", "PRICE-2026Q3", 120.50m,
        new BillingVersionedReference("CNY", 1));
}

internal sealed class BillingApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = BillingTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = BillingTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, BillingTestAuthenticationHandler>(
                    BillingTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IBillingEvidenceService>();
            services.RemoveAll<IBillingEvidencePort>();
            services.AddSingleton<IBillingEvidenceService>(new StubBillingEvidenceService(errorCode));
            services.AddSingleton<IBillingEvidencePort>(new StubBillingEvidencePort(errorCode));
        });
    }
}

internal sealed class StubBillingEvidenceService(string? errorCode) : IBillingEvidenceService
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);

    public Task<BillingEvidenceResult> CreateAsync(CreateBillingEvidenceRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new BillingEvidenceResult(
            "00000000000000000000000000000080", BillingStages.BillableCandidate, BillingContract.RuleSetVersion,
            request.ObjectScope, request.ResultGroupId, request.ExpectedGroupVersion, "00000000000000000000000000000071",
            request.ContractBaseline, request.ChargeDimension, request.BillingRuleVersion,
            request.Amount, request.Currency, request.ZeroAmountReason, [], "contract-actor", Now));
    }

    public Task<BillingAdjustmentResult> AddAdjustmentAsync(string billingEvidenceId, AddBillingAdjustmentRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new BillingAdjustmentResult(
            "00000000000000000000000000000081", billingEvidenceId, request.Amount, request.Reason, "contract-actor", Now));
    }

    public Task<BillingEvidenceResult> GetAsync(string billingEvidenceId, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new BillingEvidenceResult(
            billingEvidenceId, BillingStages.BillableCandidate, BillingContract.RuleSetVersion,
            new BillingObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
            "00000000000000000000000000000070", 5, "00000000000000000000000000000071",
            new BillingVersionedReference("CONTRACT-7", 2), "ITEM-PB-TEST", "PRICE-2026Q3",
            120.50m, new BillingVersionedReference("CNY", 1), null, [], "contract-actor", Now));
    }

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new BillingDomainException(errorCode);
    }
}

internal sealed class StubBillingEvidencePort(string? errorCode) : IBillingEvidencePort
{
    public ValueTask<BillingEvidenceStatusResult> EvaluateAsync(BillingEvidenceStatusRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new BillingDomainException(errorCode);
        return ValueTask.FromResult(new BillingEvidenceStatusResult(
            BillingStatusDecisions.Allowed, [], request.BillingEvidenceId,
            BillingStages.BillableCandidate, 120.50m, 1, BillingContract.RuleSetVersion));
    }
}

internal sealed class BillingTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Billing.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
