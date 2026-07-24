using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Receiving;
using Xunit;

namespace OpenLIMS.Receiving.IntegrationTests;

[CollectionDefinition("receiving-postgres", DisableParallelization = true)]
public sealed class ReceivingPostgresCollection;

[Collection("receiving-postgres")]
[Trait("Profile", "receiving")]
public sealed class ReceiptRegistrationPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Registration_writes_fact_state_history_audit_and_outbox_atomically()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));

        var result = await RegisterAsync(provider, ValidRequest(), "idem-normal");

        Assert.Equal(2, result.Containers[0].ReceivedItems.Count);
        Assert.All(result.Containers[0].ReceivedItems, item => Assert.Equal("QUARANTINED", item.State));
        var containerLabel = Assert.IsType<LabelIdentityResult>(result.Containers[0].LabelIdentity);
        Assert.StartsWith("LAB-A-CT-20260724-", containerLabel.BusinessNumber, StringComparison.Ordinal);
        Assert.All(result.Containers[0].ReceivedItems, item =>
        {
            var itemLabel = Assert.IsType<LabelIdentityResult>(item.LabelIdentity);
            Assert.StartsWith("LAB-A-RI-20260724-", itemLabel.BusinessNumber, StringComparison.Ordinal);
            Assert.True(LabelBarcodeCodec.TryParse(itemLabel.BarcodePayload, out var barcode, out _));
            Assert.Equal("RI", barcode!.ObjectType);
        });
        Assert.Equal(1, await CountAsync(connectionString, "receiving.receipt"));
        Assert.Equal(1, await CountAsync(connectionString, "receiving.container"));
        Assert.Equal(2, await CountAsync(connectionString, "receiving.received_item"));
        Assert.Equal(4, await CountAsync(connectionString, "receiving.received_item_state_history"));
        Assert.Equal(3, await CountAsync(connectionString, "receiving.label_identity"));
        Assert.Equal(6, await CountAsync(connectionString, "receiving.audit_pending"));
        Assert.Equal(6, await CountAsync(connectionString, "receiving.outbox"));
    }

    [Fact]
    public async Task Same_idempotency_key_and_payload_replays_first_result_without_duplicates()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));

        var first = await RegisterAsync(provider, ValidRequest(), "idem-replay");
        var second = await RegisterAsync(provider, ValidRequest(), "idem-replay");

        Assert.Equivalent(first, second, strict: true);
        Assert.Equal(1, await CountAsync(connectionString, "receiving.receipt"));
        Assert.Equal(2, await CountAsync(connectionString, "receiving.received_item"));
        Assert.Equal(3, await CountAsync(connectionString, "receiving.label_identity"));
        Assert.Equal(6, await CountAsync(connectionString, "receiving.outbox"));
    }

    [Fact]
    public async Task Same_idempotency_key_with_different_payload_is_audited_and_rejected()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));
        await RegisterAsync(provider, ValidRequest(), "idem-conflict");

        var exception = await Assert.ThrowsAsync<ReceivingDomainException>(() =>
            RegisterAsync(provider, ValidRequest() with { CustomerId = "customer-b" }, "idem-conflict"));

        Assert.Equal(ReceivingErrorCodes.IdempotencyConflict, exception.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "receiving.receipt"));
        Assert.Equal(1, await CountAsync(connectionString, "receiving.audit_attempt"));
    }

    [Fact]
    public async Task Authorization_denial_creates_no_business_fact_and_records_safe_attempt()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.Denied);

        var exception = await Assert.ThrowsAsync<ReceivingDomainException>(() =>
            RegisterAsync(provider, ValidRequest(), "idem-denied"));

        Assert.Equal(ReceivingErrorCodes.AuthorizationDenied, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "receiving.receipt"));
        Assert.Equal(1, await CountAsync(connectionString, "receiving.audit_attempt"));
        var target = await ScalarStringAsync(connectionString, "select target_hash from receiving.audit_attempt limit 1");
        Assert.Equal(64, target.Length);
        Assert.DoesNotContain("order-a", target, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_all_business_facts_and_is_recorded_separately()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await ExecuteAsync(connectionString, """
            create or replace function receiving.fail_outbox_insert() returns trigger language plpgsql as $$
            begin
              raise exception 'forced outbox failure';
            end;
            $$;
            create trigger trg_fail_outbox before insert on receiving.outbox
            for each row execute function receiving.fail_outbox_insert();
            """);

        try
        {
            await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));
            var exception = await Assert.ThrowsAsync<ReceivingDomainException>(() =>
                RegisterAsync(provider, ValidRequest(), "idem-outbox-failure"));

            Assert.Equal(ReceivingErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "receiving.receipt"));
            Assert.Equal(0, await CountAsync(connectionString, "receiving.received_item"));
            Assert.Equal(0, await CountAsync(connectionString, "receiving.audit_pending"));
            Assert.Equal(0, await CountAsync(connectionString, "receiving.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "receiving.audit_attempt"));
        }
        finally
        {
            await ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_outbox on receiving.outbox;
                drop function if exists receiving.fail_outbox_insert();
                """);
        }
    }

    [Fact]
    public async Task Concurrent_identical_requests_create_one_receipt_and_return_one_identity()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var request = ValidRequest();

        var firstTask = firstScope.ServiceProvider.GetRequiredService<IReceiptRegistrationService>()
            .RegisterAsync(request, "idem-concurrent", "corr-first", TestContext.Current.CancellationToken);
        var secondTask = secondScope.ServiceProvider.GetRequiredService<IReceiptRegistrationService>()
            .RegisterAsync(request, "idem-concurrent", "corr-second", TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(results[0].ReceiptId, results[1].ReceiptId);
        Assert.Equal(1, await CountAsync(connectionString, "receiving.receipt"));
        Assert.Equal(2, await CountAsync(connectionString, "receiving.received_item"));
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        ReceivingAuthorizationDecision authorizationDecision)
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
        services.AddSingleton<ICurrentOrganizationContext>(new DeploymentOrganizationContext(new OrganizationScope("group-a")));
        services.AddSingleton<ICurrentActorContext>(new FixedActorContext(new ActorContext("actor-a", "group-a")));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        new ReceivingModule(connectionString).AddApiServices(services);
        services.RemoveAll<IReceivingAuthorizationPort>();
        services.AddSingleton<IReceivingAuthorizationPort>(new FixedAuthorizationPort(authorizationDecision));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<ReceiptRegistrationResult> RegisterAsync(
        IServiceProvider provider,
        RegisterReceiptRequest request,
        string key)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IReceiptRegistrationService>()
            .RegisterAsync(request, key, $"corr-{key}", TestContext.Current.CancellationToken);
    }

    private static RegisterReceiptRequest ValidRequest() => new(
        "legal-a",
        "lab-a",
        "customer-a",
        "order-a",
        Now.AddMinutes(-5),
        [
            new RegisterContainerRequest(
                "BOX-01",
                "carton",
                "intact",
                "seal intact",
                [
                    Item("SERIAL-001", "red"),
                    Item("SERIAL-002", "blue")
                ])
        ]);

    private static RegisterReceivedItemRequest Item(string serial, string color) => new(
        "Hard plastic toy set",
        "MODEL-001",
        "BATCH-001",
        serial,
        color,
        "intact",
        "sealed",
        "intact",
        1,
        "set");

    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException("OPENLIMS_TEST_POSTGRES_CONNECTION is required for receiving integration tests.");

    private static async Task PrepareAsync(string connectionString)
    {
        await ReceivingMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ReceivingLabelIdentityMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              receiving.label_identity,
              receiving.label_sequence,
              receiving.received_item_state_history,
              receiving.audit_pending,
              receiving.audit_attempt,
              receiving.outbox,
              receiving.idempotency,
              receiving.received_item,
              receiving.container,
              receiving.receipt
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

    private sealed class FixedAuthorizationPort(ReceivingAuthorizationDecision decision) : IReceivingAuthorizationPort
    {
        public ValueTask<ReceivingAuthorizationDecision> AuthorizeAsync(
            ReceivingAuthorizationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(decision);
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
