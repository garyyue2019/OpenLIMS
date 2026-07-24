using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Receiving;
using Xunit;

namespace OpenLIMS.Receiving.ContractTests;

[Trait("Profile", "receiving")]
public sealed class ReceiptApiContractTests
{
    [Fact]
    public async Task Authorized_request_returns_declared_201_contract()
    {
        using var factory = new ReceivingApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(ValidRequest(), "contract-success");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ReceiptRegistrationResult>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("RCP-CONTRACT", result.ReceiptNumber);
        Assert.Equal("QUARANTINED", result.Containers[0].ReceivedItems[0].State);
    }

    [Fact]
    public async Task Anonymous_request_is_challenged_before_the_module_service()
    {
        using var factory = new ReceivingApiFactory(authenticated: false);
        using var client = factory.CreateClient();
        using var request = Request(ValidRequest(), "contract-anonymous");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("AUTH.AUTHENTICATION_REQUIRED", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_idempotency_header_returns_stable_validation_problem()
    {
        using var factory = new ReceivingApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, ReceivingContract.RegisterReceiptPath)
        {
            Content = JsonContent.Create(ValidRequest())
        };

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ReceivingErrorCodes.ValidationFailed, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_group_override_in_json_is_rejected_as_an_unknown_field()
    {
        using var factory = new ReceivingApiFactory();
        using var client = factory.CreateClient();
        const string json = """
            {
              "organizationGroupId": "other-group-secret",
              "legalEntityId": "legal-a",
              "laboratoryId": "lab-a",
              "customerId": "customer-a",
              "serviceOrderId": "order-a",
              "arrivalAt": "2026-07-24T00:55:00Z",
              "containers": []
            }
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, ReceivingContract.RegisterReceiptPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(ReceivingContract.IdempotencyHeader, "contract-group-override");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ReceivingErrorCodes.ValidationFailed, content, StringComparison.Ordinal);
        Assert.DoesNotContain("other-group-secret", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReceivingErrorCodes.AuthorizationDenied, HttpStatusCode.Forbidden)]
    [InlineData(ReceivingErrorCodes.ServiceOrderNotReceivable, HttpStatusCode.Conflict)]
    [InlineData(ReceivingErrorCodes.IdempotencyConflict, HttpStatusCode.Conflict)]
    [InlineData(ReceivingErrorCodes.IdentityGranularityUnresolved, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ReceivingErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Domain_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode expectedStatus)
    {
        using var factory = new ReceivingApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var request = Request(ValidRequest(), $"contract-{errorCode}");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Openapi_document_declares_the_receipt_operation()
    {
        using var factory = new ReceivingApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ReceivingContract.RegisterReceiptPath, content, StringComparison.Ordinal);
        Assert.Contains("registerReceipt", content, StringComparison.Ordinal);
    }

    private static HttpRequestMessage Request(RegisterReceiptRequest payload, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ReceivingContract.RegisterReceiptPath)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(ReceivingContract.IdempotencyHeader, idempotencyKey);
        return request;
    }

    private static RegisterReceiptRequest ValidRequest() => new(
        "legal-a",
        "lab-a",
        "customer-a",
        "order-a",
        new DateTimeOffset(2026, 7, 24, 0, 55, 0, TimeSpan.Zero),
        [
            new RegisterContainerRequest(
                "BOX-01",
                "carton",
                "intact",
                "seal intact",
                [new RegisterReceivedItemRequest(
                    "Hard plastic toy set",
                    "MODEL-001",
                    "BATCH-001",
                    "SERIAL-001",
                    "red",
                    "intact",
                    "sealed",
                    "intact",
                    1,
                    "set")])
        ]);
}

internal sealed class ReceivingApiFactory(
    string? errorCode = null,
    bool authenticated = true) : WebApplicationFactory<Program>
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
            if (authenticated)
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = ReceivingTestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = ReceivingTestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, ReceivingTestAuthenticationHandler>(
                        ReceivingTestAuthenticationHandler.SchemeName,
                        _ => { });
            }

            services.RemoveAll<IReceiptRegistrationService>();
            services.AddSingleton<IReceiptRegistrationService>(new StubReceiptRegistrationService(errorCode));
        });
    }
}

internal sealed class StubReceiptRegistrationService(string? errorCode) : IReceiptRegistrationService
{
    public Task<ReceiptRegistrationResult> RegisterAsync(
        RegisterReceiptRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (errorCode is not null)
        {
            throw new ReceivingDomainException(errorCode);
        }

        return Task.FromResult(new ReceiptRegistrationResult(
            "00000000000000000000000000000001",
            "RCP-CONTRACT",
            1,
            [new ContainerRegistrationResult(
                "00000000000000000000000000000002",
                "CNT-CONTRACT",
                [new ReceivedItemRegistrationResult(
                    "00000000000000000000000000000003",
                    "ITM-CONTRACT",
                    "QUARANTINED",
                    1)])]));
    }
}

internal sealed class ReceivingTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Receiving.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
