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
using OpenLIMS.Contracts.Commercial;
using OpenLIMS.Modules.Commercial;
using Xunit;

namespace OpenLIMS.Commercial.ContractTests;

[Trait("Profile", "commercial")]
public sealed class CommercialApiContractTests
{
    private const string RecordId = "00000000000000000000000000000031";
    private const string InquiryId = "00000000000000000000000000000032";

    [Fact]
    public async Task Commercial_operations_expose_versioned_http_contracts()
    {
        using var factory = new CommercialApiFactory();
        using var client = factory.CreateClient();
        using var createdCatalog = await client.PostAsJsonAsync(
            CommercialContract.CreateCatalogRecordPath,
            CatalogRequest(0),
            TestContext.Current.CancellationToken);
        using var revisedCatalog = await client.PostAsJsonAsync(
            $"/api/v1/catalog-records/{RecordId}/versions",
            CatalogRequest(1),
            TestContext.Current.CancellationToken);
        using var readCatalog = await client.GetAsync(
            $"/api/v1/catalog-records/{RecordId}/versions/1",
            TestContext.Current.CancellationToken);
        using var createdInquiry = await client.PostAsJsonAsync(
            CommercialContract.CreateInquiryPath,
            InquiryRequest(),
            TestContext.Current.CancellationToken);
        using var readInquiry = await client.GetAsync(
            $"/api/v1/inquiries/{InquiryId}",
            TestContext.Current.CancellationToken);
        using var resolved = await client.PostAsJsonAsync(
            $"/api/v1/inquiries/{InquiryId}/gaps/{InquiryGapCodes.CustomerName}/resolution",
            new ResolveInquiryGapRequest(1, "Customer A"),
            TestContext.Current.CancellationToken);
        using var reviewed = await client.PostAsJsonAsync(
            $"/api/v1/inquiries/{InquiryId}/capability-reviews",
            ReviewRequest(),
            TestContext.Current.CancellationToken);
        using var quoted = await client.PostAsJsonAsync(
            $"/api/v1/inquiries/{InquiryId}/quote-versions",
            QuoteRequest(),
            TestContext.Current.CancellationToken);
        using var changed = await client.PostAsJsonAsync(
            $"/api/v1/inquiries/{InquiryId}/change-impacts",
            new RecordChangeImpactRequest(1, CommercialChangeKinds.Scope, "scope changed"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, createdCatalog.StatusCode);
        Assert.Equal(HttpStatusCode.Created, revisedCatalog.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readCatalog.StatusCode);
        Assert.Equal(HttpStatusCode.Created, createdInquiry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readInquiry.StatusCode);
        Assert.Equal(HttpStatusCode.Created, resolved.StatusCode);
        Assert.Equal(HttpStatusCode.Created, reviewed.StatusCode);
        Assert.Equal(HttpStatusCode.Created, quoted.StatusCode);
        Assert.Equal(HttpStatusCode.Created, changed.StatusCode);
    }

    [Theory]
    [InlineData(CommercialErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(CommercialErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(CommercialErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(CommercialErrorCodes.InquiryGapsOpen, HttpStatusCode.UnprocessableEntity)]
    [InlineData(CommercialErrorCodes.CapabilityReviewRequired, HttpStatusCode.UnprocessableEntity)]
    [InlineData(CommercialErrorCodes.CapabilityReviewBlocked, HttpStatusCode.UnprocessableEntity)]
    [InlineData(CommercialErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Commercial_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new CommercialApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            CommercialContract.CreateInquiryPath,
            InquiryRequest(),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_commercial_body_is_rejected()
    {
        using var factory = new CommercialApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(
            CommercialContract.CreateInquiryPath,
            new StringContent("{"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_commercial_operations()
    {
        using var factory = new CommercialApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        foreach (var operation in new[]
        {
            "createCatalogRecord", "reviseCatalogRecord", "getCatalogRecordVersion", "createInquiry",
            "getInquiry", "resolveInquiryGap", "recordCapabilityReview", "createQuoteVersion",
            "recordCommercialChangeImpact"
        })
        {
            Assert.Contains(operation, content, StringComparison.Ordinal);
        }
    }

    private static SubmitCatalogRecordRequest CatalogRequest(long expectedVersion) => new(
        expectedVersion,
        CatalogRecordKinds.Method,
        "METHOD-A",
        "Method A",
        new DateOnly(2026, 1, 1),
        null,
        CatalogRecordStates.Active,
        new Dictionary<string, string>(),
        [Ref("REQ-A", 1)],
        Scope());

    private static CreateInquiryRequest InquiryRequest() => new(
        new InquiryDetails("Customer A", "TEXTILE", 1, "piece", "compliance", 10, [Ref("DOC", 1)]),
        Scope());

    private static CapabilityReviewInput ReviewRequest() => new(
        1, true, true, true, true, true, true, [Ref("CAP", 1)], "reviewed");

    private static SubmitQuoteVersionRequest QuoteRequest() => new(
        1, 0, Ref("SCOPE", 1), Ref("CNY", 1), Ref("CONTRACT", 1), 10, [],
        [new QuoteLineInput("LINE-1", "Testing", 1, 100)]);

    private static CommercialVersionedReference Ref(string id, long version) => new(id, version);

    private static CommercialObjectContext Scope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE");
}

internal sealed class CommercialApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = CommercialTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = CommercialTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, CommercialTestAuthenticationHandler>(
                    CommercialTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<ICommercialService>();
            services.AddSingleton<ICommercialService>(new StubCommercialService(errorCode));
        });
    }
}

internal sealed class StubCommercialService(string? errorCode) : ICommercialService
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    public Task<CatalogRecordResult> CreateCatalogAsync(SubmitCatalogRecordRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        Catalog(request, 1, cancellationToken);

    public Task<CatalogRecordResult> ReviseCatalogAsync(string recordId, SubmitCatalogRecordRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        Catalog(request, 2, cancellationToken);

    public Task<CatalogRecordResult> GetCatalogAsync(string recordId, long version, string correlationId, CancellationToken cancellationToken = default) =>
        Catalog(CatalogRequest(), version, cancellationToken);

    public Task<InquiryResult> CreateInquiryAsync(CreateInquiryRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        Inquiry(request, cancellationToken);

    public Task<InquiryResult> GetInquiryAsync(string inquiryId, string correlationId, CancellationToken cancellationToken = default) =>
        Inquiry(InquiryRequest(), cancellationToken);

    public Task<InquiryResult> ResolveGapAsync(string inquiryId, string gapId, ResolveInquiryGapRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        Inquiry(InquiryRequest(), cancellationToken);

    public Task<InquiryResult> RecordCapabilityReviewAsync(string inquiryId, CapabilityReviewInput request, string correlationId, CancellationToken cancellationToken = default) =>
        Inquiry(InquiryRequest(), cancellationToken);

    public Task<InquiryResult> CreateQuoteVersionAsync(string inquiryId, SubmitQuoteVersionRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        Inquiry(InquiryRequest(), cancellationToken);

    public Task<InquiryResult> RecordChangeImpactAsync(string inquiryId, RecordChangeImpactRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        Inquiry(InquiryRequest(), cancellationToken);

    private Task<CatalogRecordResult> Catalog(SubmitCatalogRecordRequest request, long version, CancellationToken cancellationToken)
    {
        Throw(cancellationToken);
        return Task.FromResult(new CatalogRecordResult(
            "00000000000000000000000000000031", version, CommercialContract.RuleSetVersion,
            request.Kind, request.Code, request.DisplayName, request.ValidFrom, request.ValidTo, request.State,
            request.Attributes, request.References, request.ObjectScope, "contract-actor", Now));
    }

    private Task<InquiryResult> Inquiry(CreateInquiryRequest request, CancellationToken cancellationToken)
    {
        Throw(cancellationToken);
        return Task.FromResult(new InquiryResult(
            "00000000000000000000000000000032", "INQ-CONTRACT", 1,
            CommercialContract.RuleSetVersion, InquiryStates.ReadyForReview, request.Details, request.ObjectScope,
            [], [], [], [], "contract-actor", Now));
    }

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null)
            throw new CommercialDomainException(errorCode);
    }

    private static SubmitCatalogRecordRequest CatalogRequest() => new(
        0, CatalogRecordKinds.Method, "METHOD-A", "Method A", new DateOnly(2026, 1, 1), null,
        CatalogRecordStates.Active, new Dictionary<string, string>(), [new CommercialVersionedReference("REQ-A", 1)],
        new CommercialObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE"));

    private static CreateInquiryRequest InquiryRequest() => new(
        new InquiryDetails("Customer A", "TEXTILE", 1, "piece", "compliance", 10,
            [new CommercialVersionedReference("DOC", 1)]),
        new CommercialObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE"));
}

internal sealed class CommercialTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Commercial.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
