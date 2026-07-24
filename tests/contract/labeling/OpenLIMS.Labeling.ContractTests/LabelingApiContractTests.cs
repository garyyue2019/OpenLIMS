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
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Modules.Labeling;
using Xunit;

namespace OpenLIMS.Labeling.ContractTests;

[Trait("Profile", "labeling")]
public sealed class LabelingApiContractTests
{
    [Fact]
    public async Task Authorized_batch_print_returns_202_with_real_state_semantics()
    {
        using var factory = new LabelingApiFactory();
        using var client = factory.CreateClient();
        using var request = PrintRequest("contract-print");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<CreateLabelJobsResult>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(LabelPrintJobStates.Requested, result.Jobs[0].Status);
        Assert.False(result.Jobs[0].IsReprint);
    }

    [Fact]
    public async Task Anonymous_print_is_challenged_before_module_service()
    {
        using var factory = new LabelingApiFactory(authenticated: false);
        using var client = factory.CreateClient();
        using var request = PrintRequest("contract-anonymous");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_a_stable_validation_problem()
    {
        using var factory = new LabelingApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            LabelingContract.CreateJobsPath,
            ValidPrintRequest(),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(LabelingErrorCodes.ValidationFailed, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_resolution_returns_object_type_and_verification_without_sensitive_fields()
    {
        using var factory = new LabelingApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            LabelingContract.ResolveScanPath,
            new ResolveLabelScanRequest("OL1:RI:00112233445566778899aabbccddeeff:00000000"),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("LAB-A-RI-20260724-000001", content, StringComparison.Ordinal);
        Assert.DoesNotContain("customer-secret", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LabelingErrorCodes.ObjectNotAccessible, HttpStatusCode.Forbidden)]
    [InlineData(LabelingErrorCodes.PrinterScopeMismatch, HttpStatusCode.Forbidden)]
    [InlineData(LabelingErrorCodes.IdempotencyConflict, HttpStatusCode.Conflict)]
    [InlineData(LabelingErrorCodes.ReprintLimitOverrideRequired, HttpStatusCode.Conflict)]
    [InlineData(LabelingErrorCodes.BarcodeVersionUnsupported, HttpStatusCode.UnprocessableEntity)]
    [InlineData(LabelingErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Domain_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode expectedStatus)
    {
        using var factory = new LabelingApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var request = PrintRequest($"contract-{errorCode}");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Openapi_document_declares_label_and_scan_operations()
    {
        using var factory = new LabelingApiFactory();
        using var client = factory.CreateClient();

        var content = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Contains(LabelingContract.CreateJobsPath, content, StringComparison.Ordinal);
        Assert.Contains(LabelingContract.ResolveScanPath, content, StringComparison.Ordinal);
        Assert.Contains("createLabelPrintJobs", content, StringComparison.Ordinal);
        Assert.Contains("resolveLabelScan", content, StringComparison.Ordinal);
    }

    private static HttpRequestMessage PrintRequest(string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, LabelingContract.CreateJobsPath)
        {
            Content = JsonContent.Create(ValidPrintRequest())
        };
        request.Headers.Add(LabelingContract.IdempotencyHeader, key);
        return request;
    }

    private static CreateLabelJobsRequest ValidPrintRequest() => new(
        "printer-a",
        [new LabelPrintTarget("RI", "00000000000000000000000000000001", 1)]);
}

internal sealed class LabelingApiFactory(
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
                        options.DefaultAuthenticateScheme = LabelingTestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = LabelingTestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, LabelingTestAuthenticationHandler>(
                        LabelingTestAuthenticationHandler.SchemeName,
                        _ => { });
            }

            services.RemoveAll<ILabelingService>();
            services.AddSingleton<ILabelingService>(new StubLabelingService(errorCode));
        });
    }
}

internal sealed class StubLabelingService(string? errorCode) : ILabelingService
{
    private static readonly LabelPrintJobResult Job = new(
        "10000000000000000000000000000001",
        "RI",
        "00000000000000000000000000000001",
        "LAB-A-RI-20260724-000001",
        "REC-RI-50X30@1.0.0",
        "printer-a",
        LabelPrintJobStates.Requested,
        false,
        0,
        new DateTimeOffset(2026, 7, 24, 1, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 24, 1, 0, 0, TimeSpan.Zero));

    public Task<CreateLabelJobsResult> CreateAsync(
        CreateLabelJobsRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult(new CreateLabelJobsResult([Job]));
    }

    public Task<LabelPrintJobResult> GetAsync(string printJobId, CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult(Job);
    }

    public Task<CreateLabelJobsResult> ReprintAsync(
        string printJobId,
        ReprintLabelRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult(new CreateLabelJobsResult([Job with { IsReprint = true }]));
    }

    public Task<LabelScanResolution> ResolveScanAsync(
        ResolveLabelScanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfConfigured();
        return Task.FromResult(new LabelScanResolution(
            "RI",
            Job.ObjectId,
            Job.BusinessNumber,
            "QUARANTINED",
            LabelPrintJobStates.Verified,
            ["reprint"]));
    }

    private void ThrowIfConfigured()
    {
        if (errorCode is not null)
        {
            throw new LabelingDomainException(errorCode);
        }
    }
}

internal sealed class LabelingTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Labeling.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
