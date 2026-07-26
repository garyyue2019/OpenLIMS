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
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Modules.Batch;
using Xunit;

namespace OpenLIMS.Batch.ContractTests;

[Trait("Profile", "batch")]
public sealed class BatchApiContractTests
{
    private const string BatchId = "00000000000000000000000000000050";

    [Fact]
    public async Task Six_batch_operations_expose_versioned_contracts()
    {
        using var factory = new BatchApiFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync(
            BatchContract.CreateBatchPath, CreateRequest(), TestContext.Current.CancellationToken);
        using var member = await client.PostAsJsonAsync(
            $"/api/v1/batches/{BatchId}/members", MemberRequest(), TestContext.Current.CancellationToken);
        using var evidence = await client.PostAsJsonAsync(
            $"/api/v1/batches/{BatchId}/evidence", EvidenceRequest(), TestContext.Current.CancellationToken);
        using var freeze = await client.PostAsJsonAsync(
            $"/api/v1/batches/{BatchId}/freeze", FreezeRequest(), TestContext.Current.CancellationToken);
        using var read = await client.GetAsync(
            $"/api/v1/batches/{BatchId}", TestContext.Current.CancellationToken);
        using var status = await client.GetAsync(
            $"/api/v1/batches/{BatchId}/status?expectedVersion=2&ruleSetVersion={Uri.EscapeDataString(BatchContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Created, member.StatusCode);
        Assert.Equal(HttpStatusCode.Created, evidence.StatusCode);
        Assert.Equal(HttpStatusCode.Created, freeze.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var batch = await created.Content.ReadFromJsonAsync<BatchResult>(TestContext.Current.CancellationToken);
        var gate = await status.Content.ReadFromJsonAsync<BatchStatusResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(batch);
        Assert.Equal(BatchTypes.Analytical, batch.BatchType);
        Assert.NotNull(gate);
        Assert.Equal(BatchStatusDecisions.Allowed, gate.Decision);
    }

    [Theory]
    [InlineData(BatchErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(BatchErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(BatchErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(BatchErrorCodes.BatchFrozen, HttpStatusCode.Conflict)]
    [InlineData(BatchErrorCodes.EligibilityBlocked, HttpStatusCode.UnprocessableEntity)]
    [InlineData(BatchErrorCodes.ApplicabilityUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(BatchErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Batch_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new BatchApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            BatchContract.CreateBatchPath, CreateRequest(), TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_status_query_is_rejected()
    {
        using var factory = new BatchApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            $"/api/v1/batches/{BatchId}/status?expectedVersion=latest",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_batch_operations()
    {
        using var factory = new BatchApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(BatchContract.CreateBatchPath, content, StringComparison.Ordinal);
        Assert.Contains("createBatch", content, StringComparison.Ordinal);
        Assert.Contains("addBatchMember", content, StringComparison.Ordinal);
        Assert.Contains("addBatchEvidence", content, StringComparison.Ordinal);
        Assert.Contains("freezeBatch", content, StringComparison.Ordinal);
        Assert.Contains("getBatch", content, StringComparison.Ordinal);
        Assert.Contains("getBatchStatus", content, StringComparison.Ordinal);
    }

    private static CreateBatchRequest CreateRequest() => new(
        BatchContract.RuleSetVersion, new BatchObjectContext("LEGAL-A", "LAB-A"), BatchTypes.Analytical);

    private static AddBatchMemberRequest MemberRequest() => new(
        1, BatchContract.RuleSetVersion, BatchMemberTypes.QcSample,
        "CUSTOMER-QC", "ORDER-QC", "TOYS",
        QcRef: new BatchVersionedReference("QC-CTRL-7", 1));

    private static AddBatchEvidenceRequest EvidenceRequest() => new(
        2, BatchContract.RuleSetVersion, BatchEvidenceSources.Cds,
        new BatchVersionedReference("CDS-SEQ-9", 3), new string('a', 64));

    private static FreezeBatchRequest FreezeRequest() => new(
        3, BatchContract.RuleSetVersion, BatchFreezeCauses.QcFailure);
}

internal sealed class BatchApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = BatchTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = BatchTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, BatchTestAuthenticationHandler>(
                    BatchTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IBatchService>();
            services.RemoveAll<IBatchStatusPort>();
            services.AddSingleton<IBatchService>(new StubBatchService(errorCode));
            services.AddSingleton<IBatchStatusPort>(new StubBatchStatusPort(errorCode));
        });
    }
}

internal sealed class StubBatchService(string? errorCode) : IBatchService
{
    public Task<BatchResult> CreateAsync(CreateBatchRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new BatchResult(
            "00000000000000000000000000000050", request.BatchType, BatchStates.Active, 1,
            BatchContract.RuleSetVersion, request.ObjectScope, [], [], null,
            "contract-actor", new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero)));
    }

    public Task<BatchMemberResult> AddMemberAsync(string batchId, AddBatchMemberRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new BatchMemberResult(
            "00000000000000000000000000000051", batchId, request.ExpectedCurrentVersion + 1,
            request.MemberType, request.AllocationId, request.ExpectedSubjectAllocationVersion,
            null, null, request.QcRef, request.CustomerId, request.ServiceOrderId, request.ProductCategory,
            "contract-actor", new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero)));
    }

    public Task<BatchEvidenceResult> AddEvidenceAsync(string batchId, AddBatchEvidenceRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new BatchEvidenceResult(
            "00000000000000000000000000000052", batchId, request.ExpectedCurrentVersion + 1,
            request.SourceSystem, request.ExternalRef, request.Sha256,
            "contract-actor", new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero)));
    }

    public Task<BatchFreezeResult> FreezeAsync(string batchId, FreezeBatchRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new BatchFreezeResult(
            "00000000000000000000000000000053", batchId, request.ExpectedCurrentVersion + 1,
            request.Cause, 2, request.ApprovedFollowUpRef,
            "contract-actor", new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero)));
    }

    public Task<BatchResult> GetAsync(string batchId, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(new BatchResult(
            batchId, BatchTypes.Analytical, BatchStates.Active, 2,
            BatchContract.RuleSetVersion, new BatchObjectContext("LEGAL-A", "LAB-A"), [], [], null,
            "contract-actor", new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero)));
    }

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new BatchDomainException(errorCode);
    }
}

internal sealed class StubBatchStatusPort(string? errorCode) : IBatchStatusPort
{
    public ValueTask<BatchStatusResult> EvaluateAsync(BatchStatusRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new BatchDomainException(errorCode);
        return ValueTask.FromResult(new BatchStatusResult(
            BatchStatusDecisions.Allowed, [], request.BatchId, BatchStates.Active,
            request.ExpectedBatchVersion, BatchContract.RuleSetVersion));
    }
}

internal sealed class BatchTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Batch.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
