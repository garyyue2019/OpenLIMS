using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Scope;
using OpenLIMS.Modules.Scope;
using Xunit;

namespace OpenLIMS.Scope.IntegrationTests;

[CollectionDefinition("scope-postgres", DisableParallelization = true)]
public sealed class ScopePostgresCollection;

[Collection("scope-postgres")]
[Trait("Profile", "scope")]
public sealed class ScopePersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Initial_approval_atomically_persists_four_modes_audit_and_outbox()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ScopeAuthorizationDecision.Permit);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IScopeMatrixService>().CreateAsync(
            Request(0),
            "corr-initial",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Version);
        Assert.Equal(ScopeMatrixStates.Approved, result.State);
        Assert.Equal(ScopeContract.RuleSetVersion, result.RuleSetVersion);
        Assert.Equal(4, result.Lines.Count);
        Assert.Equal(1, await CountAsync(connectionString, "scope.scope_matrix_version"));
        Assert.Equal(4, await CountAsync(connectionString, "scope.scope_line_version"));
        Assert.Equal(1, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(1, await CountAsync(connectionString, "platform.outbox"));
        Assert.Equal(
            "EVALUATED,MEASURED_ONLY,NOT_EVALUATED,WAIVED",
            await ScalarStringAsync(connectionString, "select string_agg(evaluation_mode, ',' order by evaluation_mode) from scope.scope_line_version"));
        Assert.Equal(
            "true",
            await ScalarStringAsync(connectionString, "select exists (select 1 from scope.scope_matrix_version m join platform.outbox o on o.id = m.event_id)::text"));
    }

    [Fact]
    public async Task Concurrent_revisions_with_one_expected_version_append_only_once()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var setupProvider = BuildProvider(connectionString, ScopeAuthorizationDecision.Permit, "approver-setup");
        string matrixId;
        using (var setupScope = setupProvider.CreateScope())
        {
            matrixId = (await setupScope.ServiceProvider.GetRequiredService<IScopeMatrixService>().CreateAsync(
                Request(0),
                "corr-setup",
                TestContext.Current.CancellationToken)).ScopeMatrixId;
        }

        await using var firstProvider = BuildProvider(connectionString, ScopeAuthorizationDecision.Permit, "approver-a");
        await using var secondProvider = BuildProvider(connectionString, ScopeAuthorizationDecision.Permit, "approver-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IScopeMatrixService>().ReviseAsync(
            matrixId,
            Request(1, 10),
            "corr-revise-a",
            TestContext.Current.CancellationToken);
        var second = secondScope.ServiceProvider.GetRequiredService<IScopeMatrixService>().ReviseAsync(
            matrixId,
            Request(1, 20),
            "corr-revise-b",
            TestContext.Current.CancellationToken);

        var outcomes = await Task.WhenAll(CaptureAsync(first), CaptureAsync(second));

        Assert.Equal(2, Assert.Single(outcomes, outcome => outcome.Result is not null).Result!.Version);
        Assert.Equal(
            ScopeErrorCodes.ExpectedVersionConflict,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(2, await CountAsync(connectionString, "scope.scope_matrix_version"));
        Assert.Equal(8, await CountAsync(connectionString, "scope.scope_line_version"));
        Assert.Equal(1, await CountAsync(connectionString, "scope.audit_attempt"));
    }

    [Fact]
    public async Task Approved_matrix_and_lines_reject_update_and_delete()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ScopeAuthorizationDecision.Permit);
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IScopeMatrixService>().CreateAsync(
            Request(0),
            "corr-immutable",
            TestContext.Current.CancellationToken);

        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "update scope.scope_line_version set report_position = 'REWRITTEN'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "delete from scope.scope_matrix_version"));

        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
        Assert.Equal(1, await CountAsync(connectionString, "scope.scope_matrix_version"));
        Assert.Equal(4, await CountAsync(connectionString, "scope.scope_line_version"));
    }

    [Fact]
    public async Task Eligibility_authorization_denial_is_hash_audited_after_transaction_rollback()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var creatorProvider = BuildProvider(connectionString, ScopeAuthorizationDecision.Permit);
        string matrixId;
        using (var creatorScope = creatorProvider.CreateScope())
        {
            matrixId = (await creatorScope.ServiceProvider.GetRequiredService<IScopeMatrixService>().CreateAsync(
                Request(0),
                "corr-create",
                TestContext.Current.CancellationToken)).ScopeMatrixId;
        }

        await using var deniedProvider = BuildProvider(connectionString, ScopeAuthorizationDecision.Deny, "denied-actor");
        using var deniedScope = deniedProvider.CreateScope();
        var request = Eligibility(matrixId, 1) with { CorrelationId = "corr-denied" };

        var exception = await Assert.ThrowsAsync<ScopeDomainException>(async () =>
            await deniedScope.ServiceProvider.GetRequiredService<IScopeProductionEligibilityPort>()
                .EvaluateAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(ScopeErrorCodes.NotAuthorized, exception.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "scope.audit_attempt"));
        Assert.Equal(
            "corr-denied",
            await ScalarStringAsync(connectionString, "select correlation_id from scope.audit_attempt limit 1"));
        var targetHash = await ScalarStringAsync(connectionString, "select target_hash from scope.audit_attempt limit 1");
        Assert.Equal(64, targetHash.Length);
        Assert.DoesNotContain(matrixId, targetHash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_back_scope_facts_and_appends_failure_attempt(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString, ScopeAuthorizationDecision.Permit);
            using var scope = provider.CreateScope();

            var exception = await Assert.ThrowsAsync<ScopeDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<IScopeMatrixService>().CreateAsync(
                    Request(0),
                    $"corr-{failedWriter}-failure",
                    TestContext.Current.CancellationToken));

            Assert.Equal(ScopeErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "scope.scope_matrix_version"));
            Assert.Equal(0, await CountAsync(connectionString, "scope.scope_line_version"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.audit_intent"));
            Assert.Equal(0, await CountAsync(connectionString, "platform.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "scope.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
    }

    [Fact]
    public async Task Current_version_is_allowed_while_stale_version_and_unknown_rule_are_unknown()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ScopeAuthorizationDecision.Permit);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IScopeMatrixService>();
        var initial = await service.CreateAsync(Request(0), "corr-v1", TestContext.Current.CancellationToken);
        var current = await service.ReviseAsync(
            initial.ScopeMatrixId,
            Request(1, 10),
            "corr-v2",
            TestContext.Current.CancellationToken);
        var port = scope.ServiceProvider.GetRequiredService<IScopeProductionEligibilityPort>();

        var allowed = await port.EvaluateAsync(
            Eligibility(initial.ScopeMatrixId, current.Version) with { CorrelationId = "corr-allowed" },
            TestContext.Current.CancellationToken);
        var stale = await port.EvaluateAsync(
            Eligibility(initial.ScopeMatrixId, initial.Version) with { CorrelationId = "corr-stale" },
            TestContext.Current.CancellationToken);
        var unknownRule = await port.EvaluateAsync(
            Eligibility(initial.ScopeMatrixId, current.Version, "SCOPE-LINE-GATE@latest") with
            {
                CorrelationId = "corr-unknown-rule"
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(ScopeEligibilityDecisions.Allowed, allowed.Decision);
        Assert.Equal(ScopeEligibilityDecisions.Unknown, stale.Decision);
        Assert.Contains(ScopeEligibilityReasons.MatrixVersionMismatch, stale.ReasonCodes);
        Assert.Equal(ScopeEligibilityDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(ScopeEligibilityReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
    }

    private static async Task<(ScopeMatrixVersionResult? Result, ScopeDomainException? Error)> CaptureAsync(
        Task<ScopeMatrixVersionResult> task)
    {
        try
        {
            return (await task, null);
        }
        catch (ScopeDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        ScopeAuthorizationDecision authorizationDecision,
        string actorId = "approver-a")
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
        new ScopeModule(connectionString).AddApiServices(services);
        services.RemoveAll<IScopeAuthorizationPort>();
        services.AddSingleton<IScopeAuthorizationPort>(new FixedAuthorizationPort(authorizationDecision));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static SubmitScopeMatrixVersionRequest Request(long expectedVersion, int suffix = 0) => new(
        expectedVersion,
        ScopeContract.RuleSetVersion,
        ObjectScope(),
        [
            Line(ScopeEvaluationModes.MeasuredOnly, suffix + 1),
            Line(ScopeEvaluationModes.Evaluated, suffix + 2),
            Line(ScopeEvaluationModes.NotEvaluated, suffix + 3),
            Line(ScopeEvaluationModes.Waived, suffix + 4)
        ]);

    private static ScopeLineInput Line(string mode, int suffix) => new(
        ScopeSubjectTypes.FeatureNode,
        Ref($"FEATURE-{suffix}"),
        Ref("MARKET-CN"),
        Ref($"REQ-{suffix}"),
        Ref($"ITEM-{suffix}"),
        Ref($"METHOD-{suffix}"),
        "OPTION-A",
        Ref($"SAMPLE-REQ-{suffix}"),
        mode,
        Ref("WC-A"),
        $"REPORT-{suffix}",
        mode == ScopeEvaluationModes.Evaluated ? Ref($"LIMIT-{suffix}") : null,
        mode == ScopeEvaluationModes.Evaluated ? Ref($"DECISION-{suffix}") : null,
        mode == ScopeEvaluationModes.NotEvaluated ? "Documented non-evaluation basis." : null,
        mode == ScopeEvaluationModes.Waived ? Ref($"WAIVER-{suffix}") : null);

    private static ScopeObjectContext ObjectScope() => new(
        "LEGAL-A",
        "LAB-A",
        "CUSTOMER-A",
        "ORDER-A",
        "TOYS");

    private static ScopeVersionedReference Ref(string id) => new(id, 1);

    private static ScopeProductionEligibilityRequest Eligibility(
        string matrixId,
        long expectedVersion,
        string ruleSetVersion = ScopeContract.RuleSetVersion) => new(
        "group-a",
        matrixId,
        expectedVersion,
        ruleSetVersion);

    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for scope integration tests.");

    private static async Task PrepareAsync(string connectionString)
    {
        await PlatformMigrationRunner.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ScopeMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              scope.audit_attempt,
              scope.scope_line_version,
              scope.scope_matrix_version,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_scope_audit on platform.audit_intent;
                drop function if exists platform.fail_scope_audit();
                create or replace function platform.fail_scope_audit() returns trigger language plpgsql as $$
                begin
                  if new.action in ('APPROVE_SCOPE_MATRIX', 'APPROVE_SCOPE_MATRIX_REVISION') then
                    raise exception 'forced scope audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_scope_audit before insert on platform.audit_intent
                for each row execute function platform.fail_scope_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_scope_outbox on platform.outbox;
                drop function if exists platform.fail_scope_outbox();
                create or replace function platform.fail_scope_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type = 'ScopeMatrixApproved.v1' then
                    raise exception 'forced scope outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_scope_outbox before insert on platform.outbox
                for each row execute function platform.fail_scope_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_scope_audit on platform.audit_intent;
                drop function if exists platform.fail_scope_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_scope_outbox on platform.outbox;
                drop function if exists platform.fail_scope_outbox();
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

    private sealed class FixedAuthorizationPort(ScopeAuthorizationDecision decision) : IScopeAuthorizationPort
    {
        public ValueTask<ScopeAuthorizationDecision> AuthorizeAsync(
            ScopeAuthorizationRequest request,
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
