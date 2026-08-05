using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Commercial;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Modules.Commercial;
using Xunit;

namespace OpenLIMS.Commercial.IntegrationTests;

[CollectionDefinition("commercial-postgres", DisableParallelization = true)]
public sealed class CommercialPostgresCollection;

[Collection("commercial-postgres")]
[Trait("Profile", "commercial")]
public sealed class CommercialPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_commercial_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Catalog_and_commercial_flow_atomically_persist_versions_audit_and_outbox()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, allowed: true);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICommercialService>();

        var catalog = await service.CreateCatalogAsync(CatalogRequest(0), "corr-cat-1", TestContext.Current.CancellationToken);
        await service.ReviseCatalogAsync(
            catalog.RecordId,
            CatalogRequest(1) with { DisplayName = "Method A revision" },
            "corr-cat-2",
            TestContext.Current.CancellationToken);
        var inquiry = await service.CreateInquiryAsync(InquiryRequest(), "corr-inq", TestContext.Current.CancellationToken);
        var reviewed = await service.RecordCapabilityReviewAsync(
            inquiry.InquiryId,
            ReviewRequest(inquiry.Version),
            "corr-review",
            TestContext.Current.CancellationToken);
        var quoted = await service.CreateQuoteVersionAsync(
            inquiry.InquiryId,
            QuoteRequest(reviewed.Version),
            "corr-quote",
            TestContext.Current.CancellationToken);
        var changed = await service.RecordChangeImpactAsync(
            inquiry.InquiryId,
            new RecordChangeImpactRequest(quoted.Version, CommercialChangeKinds.Scope, "scope changed"),
            "corr-change",
            TestContext.Current.CancellationToken);

        Assert.Equal(InquiryStates.ChangeReviewRequired, changed.State);
        Assert.Equal(2, await CountAsync(connectionString, "commercial.catalog_record_version"));
        Assert.Equal(4, await CountAsync(connectionString, "commercial.inquiry_version"));
        Assert.Equal(6, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(6, await CountAsync(connectionString, "platform.outbox"));
    }

    [Fact]
    public async Task Concurrent_review_with_same_expected_version_allows_only_one_writer()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string inquiryId;
        await using (var setup = BuildProvider(connectionString, allowed: true))
        {
            using var setupScope = setup.CreateScope();
            inquiryId = (await setupScope.ServiceProvider.GetRequiredService<ICommercialService>()
                .CreateInquiryAsync(InquiryRequest(), "corr-create", TestContext.Current.CancellationToken)).InquiryId;
        }

        await using var first = BuildProvider(connectionString, allowed: true, "reviewer-a");
        await using var second = BuildProvider(connectionString, allowed: true, "reviewer-b");
        using var firstScope = first.CreateScope();
        using var secondScope = second.CreateScope();
        var outcomes = await Task.WhenAll(
            CaptureAsync(firstScope.ServiceProvider.GetRequiredService<ICommercialService>()
                .RecordCapabilityReviewAsync(inquiryId, ReviewRequest(1), "corr-a", TestContext.Current.CancellationToken)),
            CaptureAsync(secondScope.ServiceProvider.GetRequiredService<ICommercialService>()
                .RecordCapabilityReviewAsync(inquiryId, ReviewRequest(1), "corr-b", TestContext.Current.CancellationToken)));

        Assert.Single(outcomes, outcome => outcome.Error is null);
        Assert.Equal(
            CommercialErrorCodes.ExpectedVersionConflict,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(2, await CountAsync(connectionString, "commercial.inquiry_version"));
        Assert.Equal(1, await CountAsync(connectionString, "commercial.audit_attempt"));
    }

    [Fact]
    public async Task Commercial_facts_reject_update_and_delete()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, allowed: true);
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICommercialService>()
            .CreateInquiryAsync(InquiryRequest(), "corr-create", TestContext.Current.CancellationToken);

        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "update commercial.inquiry_version set state = 'QUOTED'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "delete from commercial.inquiry_version"));

        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
    }

    [Fact]
    public async Task Authorization_denial_rolls_back_fact_and_appends_attempt()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, allowed: false);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<CommercialDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<ICommercialService>()
                .CreateInquiryAsync(InquiryRequest(), "corr-denied", TestContext.Current.CancellationToken));

        Assert.Equal(CommercialErrorCodes.NotAuthorized, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "commercial.inquiry_version"));
        Assert.Equal(0, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(0, await CountAsync(connectionString, "platform.outbox"));
        Assert.Equal(1, await CountAsync(connectionString, "commercial.audit_attempt"));
    }

    private static async Task<(InquiryResult? Result, CommercialDomainException? Error)> CaptureAsync(Task<InquiryResult> task)
    {
        try
        {
            return (await task, null);
        }
        catch (CommercialDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        bool allowed,
        string actorId = "operator-a")
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
        services.AddSingleton<ICurrentActorContext>(new FixedActorContext(new ActorContext(actorId, "group-a")));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        new CommercialModule(connectionString).AddApiServices(services);
        services.RemoveAll<ICommercialAuthorizationPort>();
        services.AddSingleton<ICommercialAuthorizationPort>(new FixedAuthorizationPort(allowed));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static SubmitCatalogRecordRequest CatalogRequest(long expectedVersion) => new(
        expectedVersion,
        CatalogRecordKinds.Method,
        "METHOD-A",
        "Method A",
        new DateOnly(2026, 1, 1),
        null,
        CatalogRecordStates.Active,
        new Dictionary<string, string> { ["matrix"] = "textile" },
        [new CommercialVersionedReference("REQ-A", 1)],
        ObjectScope());

    private static CreateInquiryRequest InquiryRequest() => new(
        new InquiryDetails(
            "Customer A", "TEXTILE", 2, "piece", "compliance", 10,
            [new CommercialVersionedReference("DOC", 1)]),
        ObjectScope());

    private static CapabilityReviewInput ReviewRequest(long version) => new(
        version, true, true, true, true, true, true,
        [new CommercialVersionedReference("CAPABILITY", 1)], "reviewed");

    private static SubmitQuoteVersionRequest QuoteRequest(long version) => new(
        version, 0,
        new CommercialVersionedReference("SCOPE", 1),
        new CommercialVersionedReference("CNY", 1),
        new CommercialVersionedReference("CONTRACT", 1),
        10, [], [new QuoteLineInput("LINE-1", "Testing", 1, 100)]);

    private static CommercialObjectContext ObjectScope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE");

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for commercial integration tests.");

    private static string ConnectionString() => new NpgsqlConnectionStringBuilder(AdminConnectionString())
    {
        Database = DedicatedDatabaseName
    }.ConnectionString;

    private static async Task EnsureDedicatedDatabaseAsync()
    {
        if (_databaseEnsured)
            return;
        await using var dataSource = NpgsqlDataSource.Create(AdminConnectionString());
        await using var exists = dataSource.CreateCommand("select 1 from pg_database where datname = $1");
        exists.Parameters.AddWithValue(DedicatedDatabaseName);
        if (await exists.ExecuteScalarAsync(TestContext.Current.CancellationToken) is null)
        {
            try
            {
                await using var create = dataSource.CreateCommand($"create database \"{DedicatedDatabaseName}\"");
                await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == "42P04")
            {
            }
        }
        _databaseEnsured = true;
    }

    private static async Task PrepareAsync(string connectionString)
    {
        await EnsureDedicatedDatabaseAsync();
        await PlatformMigrationRunner.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await CommercialMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              commercial.audit_attempt,
              commercial.inquiry_version,
              commercial.catalog_record_version,
              platform.audit_intent,
              platform.outbox
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

    private sealed class FixedAuthorizationPort(bool allowed) : ICommercialAuthorizationPort
    {
        public ValueTask<CommercialAuthorizationDecision> AuthorizeAsync(
            CommercialAuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed
                ? CommercialAuthorizationDecision.Permit
                : CommercialAuthorizationDecision.Deny);
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
