using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Quantity;
using OpenLIMS.Modules.Quantity;
using Xunit;

namespace OpenLIMS.Quantity.IntegrationTests;

[CollectionDefinition("quantity-postgres", DisableParallelization = true)]
public sealed class QuantityPostgresCollection;

[Collection("quantity-postgres")]
[Trait("Profile", "quantity")]
public sealed class QuantityPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Account_creation_and_receipt_atomically_persist_facts_audit_and_outbox()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, QuantityAuthorizationDecision.Permit);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQuantityAccountService>();

        var account = await service.CreateAsync(AccountRequest(), "corr-create", TestContext.Current.CancellationToken);
        var receipt = await service.PostEntryAsync(
            account.QuantityAccountId,
            Entry(1, QuantityEntryTypes.Receipt, 100.00m),
            "corr-receipt",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, account.Version);
        Assert.Equal(2, receipt.AccountVersion);
        Assert.Equal(100.00m, receipt.ResultingBalance);
        Assert.Equal(0m, receipt.ResultingReserved);
        Assert.Equal(1, await CountAsync(connectionString, "quantity.quantity_account"));
        Assert.Equal(1, await CountAsync(connectionString, "quantity.quantity_entry"));
        Assert.Equal(2, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(2, await CountAsync(connectionString, "platform.outbox"));
        Assert.Equal(
            "true",
            await ScalarStringAsync(connectionString, "select exists (select 1 from quantity.quantity_entry e join platform.outbox o on o.id = e.event_id)::text"));
    }

    [Fact]
    public async Task Concurrent_allocations_with_one_expected_version_never_exceed_available()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string accountId;
        await using (var setupProvider = BuildProvider(connectionString, QuantityAuthorizationDecision.Permit, "operator-setup"))
        {
            using var setupScope = setupProvider.CreateScope();
            var service = setupScope.ServiceProvider.GetRequiredService<IQuantityAccountService>();
            accountId = (await service.CreateAsync(AccountRequest(), "corr-setup", TestContext.Current.CancellationToken))
                .QuantityAccountId;
            await service.PostEntryAsync(
                accountId,
                Entry(1, QuantityEntryTypes.Receipt, 100.00m),
                "corr-stock",
                TestContext.Current.CancellationToken);
        }

        await using var firstProvider = BuildProvider(connectionString, QuantityAuthorizationDecision.Permit, "operator-a");
        await using var secondProvider = BuildProvider(connectionString, QuantityAuthorizationDecision.Permit, "operator-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IQuantityAccountService>().PostEntryAsync(
            accountId,
            Entry(2, QuantityEntryTypes.Allocate, 80.00m),
            "corr-alloc-a",
            TestContext.Current.CancellationToken);
        var second = secondScope.ServiceProvider.GetRequiredService<IQuantityAccountService>().PostEntryAsync(
            accountId,
            Entry(2, QuantityEntryTypes.Allocate, 80.00m),
            "corr-alloc-b",
            TestContext.Current.CancellationToken);

        var outcomes = await Task.WhenAll(CaptureAsync(first), CaptureAsync(second));

        var succeeded = Assert.Single(outcomes, outcome => outcome.Result is not null).Result!;
        var failed = Assert.Single(outcomes, outcome => outcome.Error is not null).Error!;
        Assert.Equal(20.00m, succeeded.ResultingBalance);
        Assert.Contains(
            failed.ErrorCode,
            new[] { QuantityErrorCodes.ExpectedVersionConflict, QuantityErrorCodes.InsufficientBalance });
        Assert.Equal(2, await CountAsync(connectionString, "quantity.quantity_entry"));
        Assert.Equal(1, await CountAsync(connectionString, "quantity.audit_attempt"));
        Assert.Equal(
            "true",
            await ScalarStringAsync(connectionString, "select (resulting_balance = 20)::text from quantity.quantity_entry order by account_version desc limit 1"));
    }

    [Fact]
    public async Task Posted_account_and_entries_reject_update_and_delete()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, QuantityAuthorizationDecision.Permit);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQuantityAccountService>();
        var account = await service.CreateAsync(AccountRequest(), "corr-immutable", TestContext.Current.CancellationToken);
        await service.PostEntryAsync(
            account.QuantityAccountId,
            Entry(1, QuantityEntryTypes.Receipt, 50.00m),
            "corr-entry",
            TestContext.Current.CancellationToken);

        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "update quantity.quantity_entry set amount = 999"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "delete from quantity.quantity_account"));

        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
        Assert.Equal(1, await CountAsync(connectionString, "quantity.quantity_account"));
        Assert.Equal(1, await CountAsync(connectionString, "quantity.quantity_entry"));
    }

    [Fact]
    public async Task Availability_authorization_denial_is_hash_audited_after_transaction_rollback()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string accountId;
        await using (var creatorProvider = BuildProvider(connectionString, QuantityAuthorizationDecision.Permit))
        {
            using var creatorScope = creatorProvider.CreateScope();
            accountId = (await creatorScope.ServiceProvider.GetRequiredService<IQuantityAccountService>().CreateAsync(
                AccountRequest(),
                "corr-create",
                TestContext.Current.CancellationToken)).QuantityAccountId;
        }

        await using var deniedProvider = BuildProvider(connectionString, QuantityAuthorizationDecision.Deny, "denied-actor");
        using var deniedScope = deniedProvider.CreateScope();
        var request = Availability(accountId, 1, 10.00m) with { CorrelationId = "corr-denied" };

        var exception = await Assert.ThrowsAsync<QuantityDomainException>(async () =>
            await deniedScope.ServiceProvider.GetRequiredService<IQuantityAvailabilityPort>()
                .EvaluateAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(QuantityErrorCodes.NotAuthorized, exception.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "quantity.audit_attempt"));
        Assert.Equal(
            "corr-denied",
            await ScalarStringAsync(connectionString, "select correlation_id from quantity.audit_attempt limit 1"));
        var targetHash = await ScalarStringAsync(connectionString, "select target_hash from quantity.audit_attempt limit 1");
        Assert.Equal(64, targetHash.Length);
        Assert.DoesNotContain(accountId, targetHash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_back_quantity_facts_and_appends_failure_attempt(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString, QuantityAuthorizationDecision.Permit);
            using var scope = provider.CreateScope();

            var exception = await Assert.ThrowsAsync<QuantityDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<IQuantityAccountService>().CreateAsync(
                    AccountRequest(),
                    $"corr-{failedWriter}-failure",
                    TestContext.Current.CancellationToken));

            Assert.Equal(QuantityErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "quantity.quantity_account"));
            Assert.Equal(0, await CountAsync(connectionString, "quantity.quantity_entry"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.audit_intent"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "quantity.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
    }

    [Fact]
    public async Task Reservation_lifecycle_and_correction_chain_stay_append_only()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, QuantityAuthorizationDecision.Permit);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQuantityAccountService>();
        var account = await service.CreateAsync(AccountRequest(), "corr-v1", TestContext.Current.CancellationToken);
        var accountId = account.QuantityAccountId;
        await service.PostEntryAsync(
            accountId, Entry(1, QuantityEntryTypes.Receipt, 100.00m), "corr-receipt", TestContext.Current.CancellationToken);
        var reserve = await service.PostEntryAsync(
            accountId, Entry(2, QuantityEntryTypes.Reserve, 80.00m), "corr-reserve", TestContext.Current.CancellationToken);

        var overAllocation = await CaptureAsync(service.PostEntryAsync(
            accountId, Entry(3, QuantityEntryTypes.Allocate, 30.00m), "corr-over", TestContext.Current.CancellationToken));
        var consume = await service.PostEntryAsync(
            accountId,
            Entry(3, QuantityEntryTypes.Consume, 80.00m) with { ReservationId = reserve.EntryId },
            "corr-consume",
            TestContext.Current.CancellationToken);
        var loss = await service.PostEntryAsync(
            accountId, Entry(4, QuantityEntryTypes.Loss, 5.00m), "corr-loss", TestContext.Current.CancellationToken);
        var reversal = await service.PostEntryAsync(
            accountId,
            Entry(5, QuantityEntryTypes.Reversal, 5.00m) with { ReferencedEntryId = loss.EntryId },
            "corr-reversal",
            TestContext.Current.CancellationToken);
        var restate = await service.PostEntryAsync(
            accountId,
            Entry(6, QuantityEntryTypes.Restate, 3.00m) with { ReferencedEntryId = reversal.EntryId },
            "corr-restate",
            TestContext.Current.CancellationToken);

        Assert.Equal(QuantityErrorCodes.InsufficientBalance, overAllocation.Error!.ErrorCode);
        Assert.Equal(20.00m, consume.ResultingBalance);
        Assert.Equal(0m, consume.ResultingReserved);
        Assert.Equal(20.00m, reversal.ResultingBalance);
        Assert.Equal(17.00m, restate.ResultingBalance);
        Assert.Equal(6, await CountAsync(connectionString, "quantity.quantity_entry"));
    }

    [Fact]
    public async Task Current_version_is_allowed_while_stale_version_and_unknown_rule_are_unknown()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, QuantityAuthorizationDecision.Permit);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQuantityAccountService>();
        var account = await service.CreateAsync(AccountRequest(), "corr-v1", TestContext.Current.CancellationToken);
        await service.PostEntryAsync(
            account.QuantityAccountId,
            Entry(1, QuantityEntryTypes.Receipt, 100.00m),
            "corr-v2",
            TestContext.Current.CancellationToken);
        var port = scope.ServiceProvider.GetRequiredService<IQuantityAvailabilityPort>();

        var allowed = await port.EvaluateAsync(
            Availability(account.QuantityAccountId, 2, 70.00m) with { CorrelationId = "corr-allowed" },
            TestContext.Current.CancellationToken);
        var insufficient = await port.EvaluateAsync(
            Availability(account.QuantityAccountId, 2, 100.50m) with { CorrelationId = "corr-insufficient" },
            TestContext.Current.CancellationToken);
        var stale = await port.EvaluateAsync(
            Availability(account.QuantityAccountId, 1, 10.00m) with { CorrelationId = "corr-stale" },
            TestContext.Current.CancellationToken);
        var unknownRule = await port.EvaluateAsync(
            Availability(account.QuantityAccountId, 2, 10.00m, "SAMPLE-QUANTITY@latest") with
            {
                CorrelationId = "corr-unknown-rule"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(QuantityAvailabilityDecisions.Allowed, allowed.Decision);
        Assert.Equal(100.00m, allowed.AvailableAmount);
        Assert.Equal(QuantityAvailabilityDecisions.Blocked, insufficient.Decision);
        Assert.Contains(QuantityAvailabilityReasons.InsufficientAvailable, insufficient.ReasonCodes);
        Assert.Equal(QuantityAvailabilityDecisions.Unknown, stale.Decision);
        Assert.Contains(QuantityAvailabilityReasons.AccountVersionMismatch, stale.ReasonCodes);
        Assert.Equal(QuantityAvailabilityDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(QuantityAvailabilityReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
    }

    private static async Task<(QuantityEntryResult? Result, QuantityDomainException? Error)> CaptureAsync(
        Task<QuantityEntryResult> task)
    {
        try
        {
            return (await task, null);
        }
        catch (QuantityDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        QuantityAuthorizationDecision authorizationDecision,
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
        services.AddSingleton<ICurrentActorContext>(
            new FixedActorContext(new ActorContext(actorId, "group-a")));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        new QuantityModule(connectionString).AddApiServices(services);
        services.RemoveAll<IQuantityAuthorizationPort>();
        services.AddSingleton<IQuantityAuthorizationPort>(new FixedAuthorizationPort(authorizationDecision));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateQuantityAccountRequest AccountRequest() => new(
        QuantityContract.RuleSetVersion,
        new QuantityObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        new QuantitySubjectReference(QuantitySubjectTypes.ReceivedItem, "ITEM-1", 1),
        true,
        QuantityDimensions.Mass,
        "GRAM",
        2,
        0.20m);

    private static PostQuantityEntryRequest Entry(long expectedVersion, string entryType, decimal amount) => new(
        expectedVersion,
        QuantityContract.RuleSetVersion,
        entryType,
        amount);

    private static QuantityAvailabilityRequest Availability(
        string accountId,
        long expectedVersion,
        decimal requestedAmount,
        string ruleSetVersion = QuantityContract.RuleSetVersion) => new(
        "group-a",
        accountId,
        expectedVersion,
        ruleSetVersion,
        requestedAmount);

    private const string DedicatedDatabaseName = "openlims_quantity_test";
    private static bool _databaseEnsured;

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for quantity integration tests.");

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
        await QuantityMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              quantity.audit_attempt,
              quantity.quantity_entry,
              quantity.quantity_account,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_quantity_audit on platform.audit_intent;
                drop function if exists platform.fail_quantity_audit();
                create or replace function platform.fail_quantity_audit() returns trigger language plpgsql as $$
                begin
                  if new.action in ('CREATE_QUANTITY_ACCOUNT', 'POST_QUANTITY_ENTRY') then
                    raise exception 'forced quantity audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_quantity_audit before insert on platform.audit_intent
                for each row execute function platform.fail_quantity_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_quantity_outbox on platform.outbox;
                drop function if exists platform.fail_quantity_outbox();
                create or replace function platform.fail_quantity_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type in ('QuantityAccountCreated.v1', 'QuantityEntryPosted.v1') then
                    raise exception 'forced quantity outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_quantity_outbox before insert on platform.outbox
                for each row execute function platform.fail_quantity_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_quantity_audit on platform.audit_intent;
                drop function if exists platform.fail_quantity_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_quantity_outbox on platform.outbox;
                drop function if exists platform.fail_quantity_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

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

    private sealed class FixedAuthorizationPort(QuantityAuthorizationDecision decision) : IQuantityAuthorizationPort
    {
        public ValueTask<QuantityAuthorizationDecision> AuthorizeAsync(
            QuantityAuthorizationRequest request,
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
