using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Billing;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Result;
using OpenLIMS.Modules.Billing;
using Xunit;

namespace OpenLIMS.Billing.IntegrationTests;

[CollectionDefinition("billing-postgres", DisableParallelization = true)]
public sealed class BillingPostgresCollection;

[Collection("billing-postgres")]
[Trait("Profile", "billing")]
public sealed class BillingPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_billing_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Adoption_gated_evidence_atomically_persists_facts_audit_and_outbox()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ResultAdoptionDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBillingEvidenceService>();

        var evidence = await service.CreateAsync(EvidenceRequest(), "corr-create", TestContext.Current.CancellationToken);
        var adjustment = await service.AddAdjustmentAsync(
            evidence.BillingEvidenceId,
            new AddBillingAdjustmentRequest(BillingContract.RuleSetVersion, -20m, "credit for repeat"),
            "corr-adjust", TestContext.Current.CancellationToken);

        Assert.Equal(BillingStages.BillableCandidate, evidence.Stage);
        Assert.Equal("adopted-target-1", evidence.AdoptionTargetId);
        Assert.Equal(-20m, adjustment.Amount);
        Assert.Equal(1, await CountAsync(connectionString, "billing.billing_evidence"));
        Assert.Equal(1, await CountAsync(connectionString, "billing.billing_adjustment"));
        Assert.Equal(2, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(2, await CountAsync(connectionString, "platform.outbox"));
    }

    [Fact]
    public async Task Duplicate_quadruple_is_rejected_sequentially_and_concurrently()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using (var setup = BuildProvider(connectionString, ResultAdoptionDecisions.Allowed))
        {
            using var setupScope = setup.CreateScope();
            await setupScope.ServiceProvider.GetRequiredService<IBillingEvidenceService>()
                .CreateAsync(EvidenceRequest(), "corr-first", TestContext.Current.CancellationToken);
        }

        await using var provider = BuildProvider(connectionString, ResultAdoptionDecisions.Allowed);
        using var scope = provider.CreateScope();
        var sequential = await CaptureAsync(scope.ServiceProvider.GetRequiredService<IBillingEvidenceService>()
            .CreateAsync(EvidenceRequest(), "corr-dup", TestContext.Current.CancellationToken));

        await using var firstProvider = BuildProvider(connectionString, ResultAdoptionDecisions.Allowed, "op-a");
        await using var secondProvider = BuildProvider(connectionString, ResultAdoptionDecisions.Allowed, "op-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        var alternate = EvidenceRequest() with { ChargeDimension = "ITEM-CD-TEST" };
        var outcomes = await Task.WhenAll(
            CaptureAsync(firstScope.ServiceProvider.GetRequiredService<IBillingEvidenceService>()
                .CreateAsync(alternate, "corr-c1", TestContext.Current.CancellationToken)),
            CaptureAsync(secondScope.ServiceProvider.GetRequiredService<IBillingEvidenceService>()
                .CreateAsync(alternate, "corr-c2", TestContext.Current.CancellationToken)));

        Assert.Equal(BillingErrorCodes.DuplicateBilling, sequential.Error!.ErrorCode);
        Assert.Single(outcomes, outcome => outcome.Error is null);
        Assert.Equal(BillingErrorCodes.DuplicateBilling,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(2, await CountAsync(connectionString, "billing.billing_evidence"));
    }

    [Fact]
    public async Task Zero_amount_requires_reason_and_billing_facts_reject_mutation()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ResultAdoptionDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBillingEvidenceService>();

        var missingReason = await CaptureAsync(service.CreateAsync(
            EvidenceRequest() with { Amount = 0m }, "corr-zero-bad", TestContext.Current.CancellationToken));
        var zero = await service.CreateAsync(
            EvidenceRequest() with { Amount = 0m, ZeroAmountReason = "contract free item" },
            "corr-zero", TestContext.Current.CancellationToken);
        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update billing.billing_evidence set amount = 999"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from billing.billing_evidence"));

        Assert.Equal(BillingErrorCodes.ValidationFailed, missingReason.Error!.ErrorCode);
        Assert.Equal("contract free item", zero.ZeroAmountReason);
        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
    }

    [Fact]
    public async Task Adoption_gate_blocked_fails_closed_without_evidence()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ResultAdoptionDecisions.Blocked);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<BillingDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<IBillingEvidenceService>().CreateAsync(
                EvidenceRequest(), "corr-blocked", TestContext.Current.CancellationToken));

        Assert.Equal(BillingErrorCodes.EligibilityBlocked, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "billing.billing_evidence"));
        Assert.Equal(1, await CountAsync(connectionString, "billing.audit_attempt"));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_back_billing_facts_and_appends_failure_attempt(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString, ResultAdoptionDecisions.Allowed);
            using var scope = provider.CreateScope();

            var exception = await Assert.ThrowsAsync<BillingDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<IBillingEvidenceService>().CreateAsync(
                    EvidenceRequest(), $"corr-{failedWriter}", TestContext.Current.CancellationToken));

            Assert.Equal(BillingErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "billing.billing_evidence"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.audit_intent"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "billing.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
    }

    [Fact]
    public async Task Status_port_pins_rule_set_and_reports_adjustment_chain()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ResultAdoptionDecisions.Allowed);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBillingEvidenceService>();
        var evidence = await service.CreateAsync(EvidenceRequest(), "corr-v1", TestContext.Current.CancellationToken);
        await service.AddAdjustmentAsync(
            evidence.BillingEvidenceId,
            new AddBillingAdjustmentRequest(BillingContract.RuleSetVersion, 15m, "supplement"),
            "corr-adj", TestContext.Current.CancellationToken);
        var port = scope.ServiceProvider.GetRequiredService<IBillingEvidencePort>();

        var allowed = await port.EvaluateAsync(
            new BillingEvidenceStatusRequest("group-a", evidence.BillingEvidenceId, BillingContract.RuleSetVersion)
            {
                CorrelationId = "corr-allowed"
            }, TestContext.Current.CancellationToken);
        var unknownRule = await port.EvaluateAsync(
            new BillingEvidenceStatusRequest("group-a", evidence.BillingEvidenceId, "BILLING-EVIDENCE@latest")
            {
                CorrelationId = "corr-unknown"
            }, TestContext.Current.CancellationToken);

        Assert.Equal(BillingStatusDecisions.Allowed, allowed.Decision);
        Assert.Equal(1, allowed.AdjustmentCount);
        Assert.Equal(BillingStatusDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(BillingStatusReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
    }

    private static async Task<(object? Result, BillingDomainException? Error)> CaptureAsync<T>(Task<T> task)
    {
        try
        {
            return (await task, null);
        }
        catch (BillingDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString, string adoptionDecision, string actorId = "operator-a")
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
        new BillingModule(connectionString).AddApiServices(services);
        services.RemoveAll<IBillingAuthorizationPort>();
        services.AddSingleton<IBillingAuthorizationPort>(new FixedAuthorizationPort(true));
        services.RemoveAll<IResultAdoptionPort>();
        services.AddSingleton<IResultAdoptionPort>(new FixedResultAdoptionPort(adoptionDecision));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateBillingEvidenceRequest EvidenceRequest() => new(
        BillingContract.RuleSetVersion,
        new BillingObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        "00000000000000000000000000000070", 5,
        new BillingVersionedReference("CONTRACT-7", 2),
        "ITEM-PB-TEST", "PRICE-2026Q3", 120.50m,
        new BillingVersionedReference("CNY", 1));

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for billing integration tests.");

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
        await BillingMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              billing.audit_attempt,
              billing.billing_adjustment,
              billing.billing_evidence,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_billing_audit on platform.audit_intent;
                drop function if exists platform.fail_billing_audit();
                create or replace function platform.fail_billing_audit() returns trigger language plpgsql as $$
                begin
                  if new.action like '%BILLING%' then
                    raise exception 'forced billing audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_billing_audit before insert on platform.audit_intent
                for each row execute function platform.fail_billing_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_billing_outbox on platform.outbox;
                drop function if exists platform.fail_billing_outbox();
                create or replace function platform.fail_billing_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type like 'Billing%' then
                    raise exception 'forced billing outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_billing_outbox before insert on platform.outbox
                for each row execute function platform.fail_billing_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_billing_audit on platform.audit_intent;
                drop function if exists platform.fail_billing_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_billing_outbox on platform.outbox;
                drop function if exists platform.fail_billing_outbox();
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

    private sealed class FixedAuthorizationPort(bool allowed) : IBillingAuthorizationPort
    {
        public ValueTask<BillingAuthorizationDecision> AuthorizeAsync(
            BillingAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed ? BillingAuthorizationDecision.Permit : BillingAuthorizationDecision.Deny);
    }

    private sealed class FixedResultAdoptionPort(string decision) : IResultAdoptionPort
    {
        public ValueTask<ResultAdoptionStatusResult> EvaluateAsync(
            ResultAdoptionStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ResultAdoptionStatusResult(
                decision,
                decision == ResultAdoptionDecisions.Allowed ? [] : [ResultAdoptionReasons.AdoptionRequired],
                request.ResultGroupId,
                request.ExpectedGroupVersion,
                decision == ResultAdoptionDecisions.Allowed ? "adopted-target-1" : null,
                decision == ResultAdoptionDecisions.Allowed ? 1 : null,
                ResultContract.RuleSetVersion));
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
