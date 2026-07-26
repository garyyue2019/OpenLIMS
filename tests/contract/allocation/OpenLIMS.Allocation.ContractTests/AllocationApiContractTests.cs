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
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Modules.Allocation;
using Xunit;

namespace OpenLIMS.Allocation.ContractTests;

[Trait("Profile", "allocation")]
public sealed class AllocationApiContractTests
{
    private const string AllocationId = "00000000000000000000000000000040";

    [Fact]
    public async Task Four_allocation_operations_expose_versioned_contracts()
    {
        using var factory = new AllocationApiFactory();
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync(
            AllocationContract.CreateAllocationPath,
            AllocationRequest(),
            TestContext.Current.CancellationToken);
        using var released = await client.PostAsJsonAsync(
            $"/api/v1/test-object-allocations/{AllocationId}/release",
            new ReleaseTestObjectAllocationRequest("Superseded by revised plan"),
            TestContext.Current.CancellationToken);
        using var read = await client.GetAsync(
            $"/api/v1/test-object-allocations/{AllocationId}",
            TestContext.Current.CancellationToken);
        using var status = await client.GetAsync(
            $"/api/v1/test-object-allocations/{AllocationId}/status?expectedVersion=1&ruleSetVersion={Uri.EscapeDataString(AllocationContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Created, released.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var allocation = await created.Content.ReadFromJsonAsync<TestObjectAllocationResult>(
            TestContext.Current.CancellationToken);
        var gate = await status.Content.ReadFromJsonAsync<AllocationStatusResult>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(allocation);
        Assert.Equal(AllocationStates.Active, allocation.State);
        Assert.Equal("ALLOWED", allocation.ReceivingGate.Decision);
        Assert.Equal("ALLOWED", allocation.ScopeGate.Decision);
        Assert.Equal("ALLOWED", allocation.QuantityGate.Decision);
        Assert.NotNull(gate);
        Assert.Equal(AllocationStatusDecisions.Allowed, gate.Decision);
    }

    [Theory]
    [InlineData(AllocationErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(AllocationErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(AllocationErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(AllocationErrorCodes.DestructiveConflict, HttpStatusCode.Conflict)]
    [InlineData(AllocationErrorCodes.EligibilityBlocked, HttpStatusCode.UnprocessableEntity)]
    [InlineData(AllocationErrorCodes.ApplicabilityUnknown, HttpStatusCode.UnprocessableEntity)]
    [InlineData(AllocationErrorCodes.AllocationExpired, HttpStatusCode.UnprocessableEntity)]
    [InlineData(AllocationErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Allocation_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new AllocationApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            AllocationContract.CreateAllocationPath,
            AllocationRequest(),
            TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_status_query_is_rejected()
    {
        using var factory = new AllocationApiFactory();
        using var client = factory.CreateClient();
        using var missingRule = await client.GetAsync(
            $"/api/v1/test-object-allocations/{AllocationId}/status?expectedVersion=1",
            TestContext.Current.CancellationToken);
        using var invalidVersion = await client.GetAsync(
            $"/api/v1/test-object-allocations/{AllocationId}/status?expectedVersion=latest&ruleSetVersion={Uri.EscapeDataString(AllocationContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, missingRule.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidVersion.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_allocation_operations()
    {
        using var factory = new AllocationApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(AllocationContract.CreateAllocationPath, content, StringComparison.Ordinal);
        Assert.Contains("createTestObjectAllocation", content, StringComparison.Ordinal);
        Assert.Contains("releaseTestObjectAllocation", content, StringComparison.Ordinal);
        Assert.Contains("getTestObjectAllocation", content, StringComparison.Ordinal);
        Assert.Contains("getAllocationStatus", content, StringComparison.Ordinal);
    }

    internal static CreateTestObjectAllocationRequest AllocationRequest() => new(
        0,
        AllocationContract.RuleSetVersion,
        new AllocationObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        new AllocationSubjectReference(AllocationSubjectTypes.ReceivedItem, "ITEM-1", 1),
        new AllocationVersionedReference("SIA-1", 1),
        "ITEM-1",
        3,
        "00000000000000000000000000000030",
        2,
        new string('a', 64),
        new AllocationVersionedReference("PLAN-STEP-1", 1),
        "Tensile strength execution",
        1,
        false,
        "00000000000000000000000000000031",
        2,
        80.00m,
        "MASS",
        "GRAM",
        new AllocationVersionedReference("STORAGE-COND-1", 1),
        new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero));
}

internal sealed class AllocationApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
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
                    options.DefaultAuthenticateScheme = AllocationTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = AllocationTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, AllocationTestAuthenticationHandler>(
                    AllocationTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<ITestObjectAllocationService>();
            services.RemoveAll<IAllocationStatusPort>();
            services.AddSingleton<ITestObjectAllocationService>(new StubAllocationService(errorCode));
            services.AddSingleton<IAllocationStatusPort>(new StubAllocationStatusPort(errorCode));
        });
    }
}

internal sealed class StubAllocationService(string? errorCode) : ITestObjectAllocationService
{
    public Task<TestObjectAllocationResult> CreateAsync(
        CreateTestObjectAllocationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ResultAsync(request, cancellationToken);

    public Task<AllocationReleaseResult> ReleaseAsync(
        string allocationId,
        ReleaseTestObjectAllocationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new AllocationDomainException(errorCode);
        return Task.FromResult(new AllocationReleaseResult(
            allocationId,
            AllocationStates.Released,
            request.Reason,
            "contract-actor",
            new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero)));
    }

    public Task<TestObjectAllocationResult> GetAsync(
        string allocationId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        ResultAsync(AllocationApiContractTests.AllocationRequest(), cancellationToken);

    private Task<TestObjectAllocationResult> ResultAsync(
        CreateTestObjectAllocationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new AllocationDomainException(errorCode);
        return Task.FromResult(new TestObjectAllocationResult(
            "00000000000000000000000000000040",
            AllocationStates.Active,
            request.ExpectedCurrentVersion + 1,
            AllocationContract.RuleSetVersion,
            request.ObjectScope,
            request.Subject,
            request.IdentityAssignment,
            request.ScopeMatrixId,
            request.ScopeLineId,
            request.PlanStep,
            request.Purpose,
            request.SequenceOrder,
            request.Destructive,
            request.QuantityAccountId,
            request.RequestedAmount,
            request.Dimension,
            request.Unit,
            request.StorageCondition,
            request.ValidUntil,
            request.ReservationEntryId,
            new AllocationGateResult(AllocationGateSources.Receiving, "ALLOWED", 3, "REC-ELIGIBILITY@2.0.0", []),
            new AllocationGateResult(AllocationGateSources.Scope, "ALLOWED", 2, "SCOPE-LINE-GATE@1.0.0", []),
            new AllocationGateResult(AllocationGateSources.Quantity, "ALLOWED", 2, "SAMPLE-QUANTITY@1.0.0", []),
            "contract-actor",
            new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero),
            null,
            null,
            null));
    }
}

internal sealed class StubAllocationStatusPort(string? errorCode) : IAllocationStatusPort
{
    public ValueTask<AllocationStatusResult> EvaluateAsync(
        AllocationStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new AllocationDomainException(errorCode);
        return ValueTask.FromResult(new AllocationStatusResult(
            AllocationStatusDecisions.Allowed,
            [],
            request.AllocationId,
            AllocationStates.Active,
            request.ExpectedSubjectAllocationVersion,
            AllocationContract.RuleSetVersion));
    }
}

internal sealed class AllocationTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Allocation.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
