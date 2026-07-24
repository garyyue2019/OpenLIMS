using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Labeling;
using Xunit;

namespace OpenLIMS.Labeling.IntegrationTests;

[CollectionDefinition("labeling-postgres", DisableParallelization = true)]
public sealed class LabelingPostgresCollection;

[Collection("labeling-postgres")]
[Trait("Profile", "labeling")]
public sealed class LabelingPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 1, 0, 0, TimeSpan.Zero);
    private static readonly ReceivingLabelObjectSnapshot Snapshot = new(
        "RI",
        "00000000000000000000000000000011",
        1,
        "group-a",
        "legal-a",
        "lab-a",
        "LAB-A",
        "customer-a",
        "order-a",
        "LAB-A-RI-20260724-000001",
        "00000000000000000000000000000022",
        "OL1",
        "QUARANTINED");

    [Fact]
    public async Task Initial_print_is_idempotent_and_writes_append_only_evidence()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var serviceScope = provider.CreateScope();
        var service = serviceScope.ServiceProvider.GetRequiredService<ILabelingService>();
        var request = PrintRequest();

        var first = await service.CreateAsync(request, "idem-print", "corr-print", TestContext.Current.CancellationToken);
        var replay = await service.CreateAsync(request, "idem-print", "corr-replay", TestContext.Current.CancellationToken);

        Assert.Equivalent(first, replay, strict: true);
        Assert.Equal(LabelPrintJobStates.Requested, first.Jobs[0].Status);
        Assert.Equal(1, await CountAsync(connectionString, "labeling.print_job"));
        Assert.Equal(1, await CountAsync(connectionString, "labeling.print_event"));
        Assert.Equal(1, await CountAsync(connectionString, "labeling.audit_pending"));
        Assert.Equal(1, await CountAsync(connectionString, "labeling.outbox"));
    }

    [Fact]
    public async Task Printer_scope_mismatch_is_rejected_before_persistence()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, printerLaboratoryId: "lab-b");
        using var serviceScope = provider.CreateScope();
        var service = serviceScope.ServiceProvider.GetRequiredService<ILabelingService>();

        var exception = await Assert.ThrowsAsync<LabelingDomainException>(() =>
            service.CreateAsync(PrintRequest(), "idem-scope", "corr-scope", TestContext.Current.CancellationToken));

        Assert.Equal(LabelingErrorCodes.PrinterScopeMismatch, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "labeling.print_job"));
    }

    [Fact]
    public async Task Dispatched_or_unknown_job_becomes_verified_only_after_authorized_scan()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var serviceScope = provider.CreateScope();
        var service = serviceScope.ServiceProvider.GetRequiredService<ILabelingService>();
        var created = await service.CreateAsync(
            PrintRequest(),
            "idem-verify",
            "corr-create",
            TestContext.Current.CancellationToken);
        await SetDispatchOutcomeAsync(provider, LabelDispatchOutcome.Unknown);

        var resolution = await service.ResolveScanAsync(
            new ResolveLabelScanRequest(LabelBarcodeCodec.Create("RI", Guid.Parse(Snapshot.OpaqueReference))),
            "corr-scan",
            TestContext.Current.CancellationToken);
        var job = await service.GetAsync(created.Jobs[0].PrintJobId, TestContext.Current.CancellationToken);

        Assert.Equal(LabelPrintJobStates.Verified, resolution.PrintVerificationStatus);
        Assert.Equal(LabelPrintJobStates.Verified, job.Status);
        Assert.Equal("QUARANTINED", resolution.State);
        Assert.Equal(4, await CountAsync(connectionString, "labeling.print_event"));
        Assert.Equal(4, await CountAsync(connectionString, "labeling.audit_pending"));
        Assert.Equal(4, await CountAsync(connectionString, "labeling.outbox"));
    }

    [Fact]
    public async Task Fourth_successful_reprint_requires_override_capability()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var serviceScope = provider.CreateScope();
        var service = serviceScope.ServiceProvider.GetRequiredService<ILabelingService>();
        var initial = await service.CreateAsync(
            PrintRequest(),
            "idem-initial",
            "corr-initial",
            TestContext.Current.CancellationToken);
        await SetDispatchOutcomeAsync(provider, LabelDispatchOutcome.Dispatched);

        for (var index = 1; index <= 3; index++)
        {
            await service.ReprintAsync(
                initial.Jobs[0].PrintJobId,
                new ReprintLabelRequest("printer-a", $"damaged-{index}"),
                $"idem-reprint-{index}",
                $"corr-reprint-{index}",
                TestContext.Current.CancellationToken);
            await SetDispatchOutcomeAsync(provider, LabelDispatchOutcome.Dispatched);
        }

        var exception = await Assert.ThrowsAsync<LabelingDomainException>(() => service.ReprintAsync(
            initial.Jobs[0].PrintJobId,
            new ReprintLabelRequest("printer-a", "damaged-fourth"),
            "idem-reprint-4",
            "corr-reprint-4",
            TestContext.Current.CancellationToken));

        Assert.Equal(LabelingErrorCodes.ReprintLimitOverrideRequired, exception.ErrorCode);
        Assert.Equal(4, await CountAsync(connectionString, "labeling.print_job"));

        await using var overrideProvider = BuildProvider(connectionString, hasOverride: true);
        using var overrideScope = overrideProvider.CreateScope();
        var overrideService = overrideScope.ServiceProvider.GetRequiredService<ILabelingService>();
        var approved = await overrideService.ReprintAsync(
            initial.Jobs[0].PrintJobId,
            new ReprintLabelRequest("printer-a", "quality-approved"),
            "idem-reprint-override",
            "corr-reprint-override",
            TestContext.Current.CancellationToken);
        Assert.True(approved.Jobs[0].IsReprint);
        Assert.Equal(3, approved.Jobs[0].SuccessfulReprintCount);
    }

    [Fact]
    public async Task Expired_dispatch_lease_recovers_worker_interruption_to_unknown_without_resend()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var serviceScope = provider.CreateScope();
        var service = serviceScope.ServiceProvider.GetRequiredService<ILabelingService>();
        var created = await service.CreateAsync(
            PrintRequest(),
            "idem-worker-crash",
            "corr-worker-crash",
            TestContext.Current.CancellationToken);

        using (var firstWorkerScope = provider.CreateScope())
        {
            var store = firstWorkerScope.ServiceProvider.GetRequiredService<LabelingStore>();
            Assert.NotNull(await store.ClaimNextAsync(Now, TestContext.Current.CancellationToken));
        }

        using (var recoveryScope = provider.CreateScope())
        {
            var store = recoveryScope.ServiceProvider.GetRequiredService<LabelingStore>();
            Assert.Null(await store.ClaimNextAsync(Now.AddSeconds(31), TestContext.Current.CancellationToken));
        }

        var recovered = await service.GetAsync(created.Jobs[0].PrintJobId, TestContext.Current.CancellationToken);
        Assert.Equal(LabelPrintJobStates.Unknown, recovered.Status);
        Assert.Equal(1, await CountAsync(connectionString, "labeling.print_job"));
        Assert.Equal(3, await CountAsync(connectionString, "labeling.print_event"));
    }

    [Fact]
    public async Task Unauthorized_scan_returns_generic_denial_and_only_safe_attempt_evidence()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, allowObjectAccess: false);
        using var serviceScope = provider.CreateScope();
        var service = serviceScope.ServiceProvider.GetRequiredService<ILabelingService>();

        var exception = await Assert.ThrowsAsync<LabelingDomainException>(() => service.ResolveScanAsync(
            new ResolveLabelScanRequest(LabelBarcodeCodec.Create("RI", Guid.Parse(Snapshot.OpaqueReference))),
            "corr-denied-scan",
            TestContext.Current.CancellationToken));

        Assert.Equal(LabelingErrorCodes.ObjectNotAccessible, exception.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "labeling.scan_attempt"));
        Assert.Equal(0, await CountAsync(connectionString, "labeling.audit_pending"));
        var payloadHash = await ScalarStringAsync(connectionString, "select payload_hash from labeling.scan_attempt limit 1");
        Assert.Equal(64, payloadHash.Length);
        Assert.DoesNotContain(Snapshot.BusinessNumber, payloadHash, StringComparison.Ordinal);
    }

    private static async Task SetDispatchOutcomeAsync(
        ServiceProvider provider,
        LabelDispatchOutcome outcome)
    {
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<LabelingStore>();
        var job = await store.ClaimNextAsync(Now, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("Expected a requested print job.");
        await store.CompleteDispatchAsync(
            job,
            outcome,
            outcome == LabelDispatchOutcome.Dispatched ? null : LabelingErrorCodes.DeliveryUnknown,
            Now,
            TestContext.Current.CancellationToken);
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        string printerLaboratoryId = "lab-a",
        bool hasOverride = false,
        bool allowObjectAccess = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformDependencies(new PlatformDependencyOptions
        {
            PostgresConnectionString = connectionString,
            OidcAuthority = "https://issuer.invalid",
            OidcAudience = "openlims-api",
            ObjectStorageEndpoint = "https://storage.invalid",
            ObjectStorageBucket = "test",
            ObjectStorageAccessKey = "test",
            ObjectStorageSecretKey = "test",
            PostgresCommandTimeoutSeconds = 10,
            OidcMetadataTimeoutSeconds = 1,
            ObjectStorageProbeTimeoutSeconds = 1,
            DependencyProbeTimeoutSeconds = 2
        });
        services.AddSingleton<ICurrentOrganizationContext>(
            new DeploymentOrganizationContext(new OrganizationScope("group-a")));
        services.AddSingleton<ICurrentActorContext>(new FixedActorContext(new ActorContext("actor-a", "group-a")));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        var printer = new LogicalLabelPrinter(
            "printer-a",
            printerLaboratoryId,
            "Receiving printer",
            "printer-a.internal",
            9100,
            "TSPL2",
            "1.0.0",
            true);
        new LabelingModule(connectionString, [printer]).AddApiServices(services);
        services.RemoveAll<IReceivingLabelObjectPort>();
        services.AddSingleton<IReceivingLabelObjectPort>(new FixedReceivingPort(Snapshot));
        services.RemoveAll<ILabelingAuthorization>();
        services.AddSingleton<ILabelingAuthorization>(new FixedAuthorization(hasOverride, allowObjectAccess));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateLabelJobsRequest PrintRequest() => new(
        "printer-a",
        [new LabelPrintTarget(Snapshot.ObjectType, Snapshot.ObjectId, Snapshot.ObjectVersion)]);

    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException("OPENLIMS_TEST_POSTGRES_CONNECTION is required for labeling integration tests.");

    private static async Task PrepareAsync(string connectionString)
    {
        await LabelingMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              labeling.print_event,
              labeling.audit_pending,
              labeling.scan_attempt,
              labeling.outbox,
              labeling.idempotency,
              labeling.print_job
            cascade;
            """);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(string connectionString, string table)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand($"select count(*) from {table}");
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<string> ScalarStringAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return Convert.ToString(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private sealed class FixedReceivingPort(ReceivingLabelObjectSnapshot snapshot) : IReceivingLabelObjectPort
    {
        public ValueTask<ReceivingLabelObjectSnapshot?> GetAsync(
            string organizationGroupId,
            string objectType,
            string objectId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReceivingLabelObjectSnapshot?>(
                organizationGroupId == snapshot.OrganizationGroupId && objectType == snapshot.ObjectType && objectId == snapshot.ObjectId ? snapshot : null);

        public ValueTask<ReceivingLabelObjectSnapshot?> ResolveAsync(
            string organizationGroupId,
            string objectType,
            string opaqueReference,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ReceivingLabelObjectSnapshot?>(
                organizationGroupId == snapshot.OrganizationGroupId && objectType == snapshot.ObjectType && opaqueReference == snapshot.OpaqueReference ? snapshot : null);
    }

    private sealed class FixedAuthorization(bool hasOverride, bool allowObjectAccess) : ILabelingAuthorization
    {
        public bool IsAuthorized(ReceivingLabelObjectSnapshot snapshot, string capability) => allowObjectAccess;

        public bool HasCapability(string capability) =>
            capability != ReceivingCapabilities.LabelReprintOverride || hasOverride;
    }

    private sealed class FixedActorContext(ActorContext actor) : ICurrentActorContext
    {
        public ActorContext? Current { get; } = actor;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
