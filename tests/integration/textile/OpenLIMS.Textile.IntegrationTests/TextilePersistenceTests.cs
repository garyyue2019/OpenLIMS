using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Textile;
using OpenLIMS.Modules.Textile;
using Xunit;

namespace OpenLIMS.Textile.IntegrationTests;

[CollectionDefinition("textile-postgres", DisableParallelization = true)]
public sealed class TextilePostgresCollection;

[Collection("textile-postgres")]
[Trait("Profile", "textile")]
public sealed class TextilePersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 13, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_textile_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Sufficient_requirement_plan_and_approval_are_atomic_and_reconstructable()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITextileRuntimeService>();

        var requirement = await service.CalculateSampleRequirementAsync(
            RequirementRequest(), "corr-requirement", TestContext.Current.CancellationToken);
        var plan = await service.CreateCuttingPlanAsync(
            PlanRequest(requirement), "corr-plan", TestContext.Current.CancellationToken);
        var approved = await service.ApproveCuttingPlanAsync(
            plan.CuttingPlanId,
            plan.Version,
            new ApproveTextileCuttingPlanRequest(
                plan.Version,
                requirement.InputHash,
                TextileContract.RuleSetVersion,
                "checked"),
            "corr-approve",
            TestContext.Current.CancellationToken);
        var loaded = await service.GetCuttingPlanAsync(
            plan.CuttingPlanId, plan.Version, "corr-read", TestContext.Current.CancellationToken);

        Assert.Equal(TextileCalculationDecisions.Sufficient, requirement.Result.Decision);
        Assert.Equal(TextileCuttingPlanStates.Approved, approved.State);
        Assert.NotNull(loaded.Approval);
        Assert.Equal(requirement.InputHash, loaded.Approval.SampleRequirementInputHash);
        Assert.Equal(1, await CountAsync(connectionString, "textile.sample_requirement"));
        Assert.Equal(1, await CountAsync(connectionString, "textile.cutting_plan"));
        Assert.Equal(1, await CountAsync(connectionString, "textile.cutting_plan_approval"));
        Assert.Equal(4, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(3, await CountAsync(connectionString, "platform.outbox"));
    }

    [Fact]
    public async Task Insufficient_requirement_persists_gap_and_blocks_approval()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITextileRuntimeService>();

        var requirement = await service.CalculateSampleRequirementAsync(
            RequirementRequest(availableArea: 10m), "corr-shortage", TestContext.Current.CancellationToken);
        var plan = await service.CreateCuttingPlanAsync(
            PlanRequest(requirement), "corr-short-plan", TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<TextileOperationException>(() =>
            service.ApproveCuttingPlanAsync(
                plan.CuttingPlanId,
                plan.Version,
                new ApproveTextileCuttingPlanRequest(
                    plan.Version,
                    requirement.InputHash,
                    TextileContract.RuleSetVersion,
                    "cannot approve"),
                "corr-short-approve",
                TestContext.Current.CancellationToken));

        Assert.Equal(TextileCalculationDecisions.Insufficient, requirement.Result.Decision);
        Assert.Equal(590m, Assert.Single(requirement.Result.Gaps).GapAreaSquareMm);
        Assert.Equal(TextileErrorCodes.SampleRequirementNotApprovable, error.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "textile.cutting_plan_approval"));
        Assert.Equal(1, await CountWhereAsync(
            connectionString,
            "platform.outbox",
            "message_type = 'TextileSampleShortageDetected.v1'"));
        Assert.Equal(1, await CountAsync(connectionString, "textile.audit_attempt"));
    }

    [Fact]
    public async Task Approval_requires_explicit_capability_and_records_failure_attempt()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, approveAllowed: false);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITextileRuntimeService>();
        var requirement = await service.CalculateSampleRequirementAsync(
            RequirementRequest(), "corr-auth-req", TestContext.Current.CancellationToken);
        var plan = await service.CreateCuttingPlanAsync(
            PlanRequest(requirement), "corr-auth-plan", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<TextileOperationException>(() =>
            service.ApproveCuttingPlanAsync(
                plan.CuttingPlanId,
                plan.Version,
                new ApproveTextileCuttingPlanRequest(
                    plan.Version, requirement.InputHash, TextileContract.RuleSetVersion),
                "corr-auth-approve",
                TestContext.Current.CancellationToken));

        Assert.Equal(TextileErrorCodes.NotAuthorized, error.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "textile.cutting_plan_approval"));
        Assert.Equal(1, await CountAsync(connectionString, "textile.audit_attempt"));
    }

    [Fact]
    public async Task Concurrent_plan_append_allows_exactly_one_writer()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        TextileSampleRequirementRecord requirement;
        await using (var setup = BuildProvider(connectionString))
        {
            using var setupScope = setup.CreateScope();
            requirement = await setupScope.ServiceProvider.GetRequiredService<ITextileRuntimeService>()
                .CalculateSampleRequirementAsync(
                    RequirementRequest(), "corr-concurrent-req", TestContext.Current.CancellationToken);
        }

        await using var firstProvider = BuildProvider(connectionString, actorId: "operator-a");
        await using var secondProvider = BuildProvider(connectionString, actorId: "operator-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        var outcomes = await Task.WhenAll(
            CaptureAsync(firstScope.ServiceProvider.GetRequiredService<ITextileRuntimeService>()
                .CreateCuttingPlanAsync(
                    PlanRequest(requirement), "corr-concurrent-a", TestContext.Current.CancellationToken)),
            CaptureAsync(secondScope.ServiceProvider.GetRequiredService<ITextileRuntimeService>()
                .CreateCuttingPlanAsync(
                    PlanRequest(requirement), "corr-concurrent-b", TestContext.Current.CancellationToken)));

        Assert.Single(outcomes, outcome => outcome.Error is null);
        Assert.Equal(
            TextileErrorCodes.ExpectedVersionConflict,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "textile.cutting_plan"));
        Assert.Equal(1, await CountAsync(connectionString, "textile.audit_attempt"));
    }

    [Fact]
    public async Task Published_textile_facts_reject_update_and_delete()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITextileRuntimeService>()
            .CalculateSampleRequirementAsync(
                RequirementRequest(), "corr-immutable", TestContext.Current.CancellationToken);

        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "update textile.sample_requirement set input_hash = 'changed'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "delete from textile.sample_requirement"));

        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
        Assert.Equal(1, await CountAsync(connectionString, "textile.sample_requirement"));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Evidence_failure_rolls_back_and_retry_creates_one_fact(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITextileRuntimeService>();
        try
        {
            var error = await Assert.ThrowsAsync<TextileOperationException>(() =>
                service.CalculateSampleRequirementAsync(
                    RequirementRequest(), "corr-retry", TestContext.Current.CancellationToken));

            Assert.Equal(TextileErrorCodes.PersistenceUnavailable, error.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "textile.sample_requirement"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.audit_intent"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "textile.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }

        var recovered = await service.CalculateSampleRequirementAsync(
            RequirementRequest(), "corr-retry", TestContext.Current.CancellationToken);
        Assert.Equal(1, recovered.Version);
        Assert.Equal(1, await CountAsync(connectionString, "textile.sample_requirement"));
        Assert.Equal(1, await CountAsync(connectionString, "textile.audit_attempt"));
    }

    [Fact]
    public async Task Status_port_allows_only_approved_exact_rule_set()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITextileRuntimeService>();
        var requirement = await service.CalculateSampleRequirementAsync(
            RequirementRequest(), "corr-status-req", TestContext.Current.CancellationToken);
        var plan = await service.CreateCuttingPlanAsync(
            PlanRequest(requirement), "corr-status-plan", TestContext.Current.CancellationToken);
        await service.ApproveCuttingPlanAsync(
            plan.CuttingPlanId,
            plan.Version,
            new ApproveTextileCuttingPlanRequest(
                plan.Version, requirement.InputHash, TextileContract.RuleSetVersion),
            "corr-status-approve",
            TestContext.Current.CancellationToken);
        var port = scope.ServiceProvider.GetRequiredService<ITextileCuttingPlanStatusPort>();

        var allowed = await port.EvaluateAsync(new TextileCuttingPlanStatusRequest(
            "group-a", plan.CuttingPlanId, plan.Version, TextileContract.RuleSetVersion),
            TestContext.Current.CancellationToken);
        var unknown = await port.EvaluateAsync(new TextileCuttingPlanStatusRequest(
            "group-a", plan.CuttingPlanId, plan.Version, "TEXTILE-SAMPLE-REQUIREMENT@latest"),
            TestContext.Current.CancellationToken);

        Assert.Equal(TextileStatusDecisions.Allowed, allowed.Decision);
        Assert.Equal(TextileStatusDecisions.Unknown, unknown.Decision);
    }

    private static async Task<(TextileCuttingPlanResult? Result, TextileOperationException? Error)>
        CaptureAsync(Task<TextileCuttingPlanResult> task)
    {
        try
        {
            return (await task, null);
        }
        catch (TextileOperationException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        bool manageAllowed = true,
        bool approveAllowed = true,
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
        new TextileModule(connectionString).AddApiServices(services);
        services.RemoveAll<ITextileAuthorizationPort>();
        services.AddSingleton<ITextileAuthorizationPort>(
            new FixedAuthorizationPort(manageAllowed, approveAllowed));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateTextileSampleRequirementRequest RequirementRequest(
        decimal availableArea = 1_000m) => new(
            "REQ-1", 0, Scope(),
            new TextileSampleRequirementCalculation(
                TextileContract.RuleSetVersion,
                [new TextileDemandLine(
                    Ref("STYLE"), Ref("RED"), Ref("FRONT"), Ref("COTTON"), "BODY",
                    TextileDirections.Warp, Ref("TENSILE"), 3, 1, 1, true, 10m, 12m,
                    ExclusiveDestructiveGroupId: "GROUP-A")],
                [new TextileAvailableFabric(
                    Ref("STYLE"), Ref("RED"), Ref("FRONT"), "BODY", availableArea)]));

    private static CreateTextileCuttingPlanRequest PlanRequest(
        TextileSampleRequirementRecord requirement) => new(
            "PLAN-1", 0, requirement.RequirementId, requirement.Version,
            requirement.InputHash, TextileContract.RuleSetVersion,
            new TextileCuttingPlan(
                "PLAN-1", Ref("FABRIC-LOT"), "BODY", TextileDirections.Warp,
                10m, 12m, 5, 20m, "TPL-1", "operator",
                ["SPEC-1", "SPEC-2", "SPEC-3", "SPEC-4", "SPEC-5"]));

    private static TextileObjectScope Scope() => new("LEGAL-A", "LAB-A");

    private static TextileVersionedReference Ref(string id) => new(id, 1);

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for textile integration tests.");

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
        await TextileMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              textile.audit_attempt,
              textile.cutting_plan_approval,
              textile.cutting_plan,
              textile.sample_requirement,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_textile_audit on platform.audit_intent;
                drop function if exists platform.fail_textile_audit();
                create or replace function platform.fail_textile_audit() returns trigger language plpgsql as $$
                begin
                  if new.action like '%TEXTILE%' then
                    raise exception 'forced textile audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_textile_audit before insert on platform.audit_intent
                for each row execute function platform.fail_textile_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_textile_outbox on platform.outbox;
                drop function if exists platform.fail_textile_outbox();
                create or replace function platform.fail_textile_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type like 'Textile%' then
                    raise exception 'forced textile outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_textile_outbox before insert on platform.outbox
                for each row execute function platform.fail_textile_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_textile_audit on platform.audit_intent;
                drop function if exists platform.fail_textile_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_textile_outbox on platform.outbox;
                drop function if exists platform.fail_textile_outbox();
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

    private static async Task<long> CountWhereAsync(
        string connectionString,
        string table,
        string predicate)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand($"select count(*) from {table} where {predicate}");
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FixedAuthorizationPort(bool manageAllowed, bool approveAllowed) :
        ITextileAuthorizationPort
    {
        public ValueTask<TextileAuthorizationDecision> AuthorizeAsync(
            TextileAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            var allowed = request.Capability switch
            {
                TextileCapabilities.SampleRequirementManage => manageAllowed,
                TextileCapabilities.CuttingPlanApprove => approveAllowed,
                _ => false
            };
            return ValueTask.FromResult(
                allowed ? TextileAuthorizationDecision.Permit : TextileAuthorizationDecision.Deny);
        }
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
