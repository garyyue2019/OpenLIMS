using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Qc;
using OpenLIMS.Modules.Qc;
using Xunit;

namespace OpenLIMS.Qc.IntegrationTests;

[CollectionDefinition("qc-postgres", DisableParallelization = true)]
public sealed class QcPostgresCollection;

[Collection("qc-postgres")]
[Trait("Profile", "qc")]
public sealed class QcPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_qc_test";
    private const string BatchId = "00000000000000000000000000000040";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Passing_run_pins_versions_and_persists_facts_audit_and_outbox()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQcRunService>();

        var run = await service.OpenRunAsync(Run(), "corr-open", TestContext.Current.CancellationToken);
        var withResult = await service.AddResultAsync(
            run.QcRunId, Result(run.Version, QcVerdicts.Pass), "corr-result", TestContext.Current.CancellationToken);
        var verdict = await service.RecordVerdictAsync(
            withResult.QcRunId, new RecordQcVerdictRequest(withResult.Version, QcContract.RuleSetVersion),
            "corr-verdict", TestContext.Current.CancellationToken);

        Assert.Equal(QcRunStates.Open, run.State);
        Assert.Equal(BatchStatusDecisions.Allowed, run.BatchGateDecision);
        Assert.Equal(BatchContract.RuleSetVersion, run.BatchGateRuleSetVersion);
        Assert.Equal(3, run.Method.Version);
        Assert.Equal(QcRunStates.Passed, verdict.State);
        Assert.Equal(3, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
        Assert.Equal(3, await CountAsync(connectionString, "select count(*) from platform.outbox"));

        var reportability = await scope.ServiceProvider.GetRequiredService<IQcReportabilityPort>().EvaluateAsync(
            new QcReportabilityRequest(
                "group-a", verdict.QcRunId, verdict.Version, QcContract.RuleSetVersion, "GROUP-1")
            {
                CorrelationId = "corr-passed"
            }, TestContext.Current.CancellationToken);

        Assert.Equal(QcReportabilityDecisions.Allowed, reportability.Decision);
    }

    [Fact]
    public async Task Failed_run_blocks_every_impacted_target_and_deviation_approval_does_not_release()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQcRunService>();
        var port = scope.ServiceProvider.GetRequiredService<IQcReportabilityPort>();
        var failed = await FailedRunAsync(service, ["GROUP-1", "GROUP-2", "GROUP-3"]);

        var deviated = await service.RecordDeviationApprovalAsync(
            failed.QcRunId,
            new RecordQcDeviationApprovalRequest(
                failed.Version, QcContract.RuleSetVersion,
                new QcVersionedReference("DEV-APPROVAL-1", 1), "quality lead approved the deviation"),
            "corr-deviation", TestContext.Current.CancellationToken);

        // AC-QC-001: the deviation is approved but impact scope and validity
        // decision are not recorded, so every impacted result stays unreportable.
        var release = await CaptureAsync(service.ReleaseAsync(
            deviated.QcRunId, new ReleaseQcBlockRequest(deviated.Version, QcContract.RuleSetVersion),
            "corr-early-release", TestContext.Current.CancellationToken));

        Assert.Equal(QcErrorCodes.ReleaseGateIncomplete, release.Error!.ErrorCode);
        Assert.Single(deviated.DeviationApprovals);
        Assert.Empty(deviated.Gates);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from qc.qc_release"));

        foreach (var target in new[] { "GROUP-1", "GROUP-2", "GROUP-3" })
        {
            var decision = await port.EvaluateAsync(new QcReportabilityRequest(
                "group-a", deviated.QcRunId, deviated.Version, QcContract.RuleSetVersion, target)
            {
                CorrelationId = $"corr-blocked-{target}"
            }, TestContext.Current.CancellationToken);

            Assert.Equal(QcReportabilityDecisions.Blocked, decision.Decision);
            Assert.Contains(QcReportabilityReasons.QcFailureUnreleased, decision.ReasonCodes);
            Assert.Equal(5, decision.OutstandingGates.Count);
        }
    }

    [Fact]
    public async Task Release_requires_all_five_gates_and_then_unblocks_every_target()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQcRunService>();
        var current = await FailedRunAsync(service, ["GROUP-1", "GROUP-2"]);

        var partialFailures = new List<string>();
        foreach (var kind in QcReleaseGateKinds.Required)
        {
            var premature = await CaptureAsync(service.ReleaseAsync(
                current.QcRunId, new ReleaseQcBlockRequest(current.Version, QcContract.RuleSetVersion),
                $"corr-release-before-{kind}", TestContext.Current.CancellationToken));
            partialFailures.Add(premature.Error!.ErrorCode);
            current = await service.SatisfyGateAsync(
                current.QcRunId,
                new SatisfyQcReleaseGateRequest(
                    current.Version, QcContract.RuleSetVersion, kind, new QcVersionedReference($"EVIDENCE-{kind}", 1)),
                $"corr-gate-{kind}", TestContext.Current.CancellationToken);
        }

        var released = await service.ReleaseAsync(
            current.QcRunId, new ReleaseQcBlockRequest(current.Version, QcContract.RuleSetVersion),
            "corr-release", TestContext.Current.CancellationToken);
        var duplicateGate = await CaptureAsync(service.SatisfyGateAsync(
            released.QcRunId,
            new SatisfyQcReleaseGateRequest(
                released.Version, QcContract.RuleSetVersion,
                QcReleaseGateKinds.Investigation, new QcVersionedReference("EVIDENCE-AGAIN", 1)),
            "corr-gate-again", TestContext.Current.CancellationToken));

        Assert.Equal(5, partialFailures.Count);
        Assert.All(partialFailures, code => Assert.Equal(QcErrorCodes.ReleaseGateIncomplete, code));
        Assert.Equal(QcRunStates.Released, released.State);
        Assert.Equal(5, released.Gates.Count);
        Assert.Equal("reviewer-a", released.ReleasedBy);
        Assert.Equal(QcErrorCodes.ValidationFailed, duplicateGate.Error!.ErrorCode);

        var port = scope.ServiceProvider.GetRequiredService<IQcReportabilityPort>();
        foreach (var target in new[] { "GROUP-1", "GROUP-2" })
        {
            var decision = await port.EvaluateAsync(new QcReportabilityRequest(
                "group-a", released.QcRunId, released.Version, QcContract.RuleSetVersion, target)
            {
                CorrelationId = $"corr-allowed-{target}"
            }, TestContext.Current.CancellationToken);
            Assert.Equal(QcReportabilityDecisions.Allowed, decision.Decision);
        }
    }

    [Fact]
    public async Task Empty_and_duplicate_impact_scopes_are_rejected()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQcRunService>();
        var run = await service.OpenRunAsync(Run(), "corr-open", TestContext.Current.CancellationToken);
        var withResult = await service.AddResultAsync(
            run.QcRunId, Result(run.Version, QcVerdicts.Fail), "corr-result", TestContext.Current.CancellationToken);
        var failed = await service.RecordVerdictAsync(
            withResult.QcRunId, new RecordQcVerdictRequest(withResult.Version, QcContract.RuleSetVersion),
            "corr-verdict", TestContext.Current.CancellationToken);

        var empty = await CaptureAsync(service.RecordImpactAsync(
            failed.QcRunId, new RecordQcImpactRequest(failed.Version, QcContract.RuleSetVersion, []),
            "corr-empty", TestContext.Current.CancellationToken));
        var duplicateInBatch = await CaptureAsync(service.RecordImpactAsync(
            failed.QcRunId,
            new RecordQcImpactRequest(failed.Version, QcContract.RuleSetVersion, [Target("GROUP-1"), Target("GROUP-1")]),
            "corr-dup", TestContext.Current.CancellationToken));
        var recorded = await service.RecordImpactAsync(
            failed.QcRunId,
            new RecordQcImpactRequest(failed.Version, QcContract.RuleSetVersion, [Target("GROUP-1")]),
            "corr-impact", TestContext.Current.CancellationToken);
        var duplicateAcrossCalls = await CaptureAsync(service.RecordImpactAsync(
            recorded.QcRunId,
            new RecordQcImpactRequest(recorded.Version, QcContract.RuleSetVersion, [Target("GROUP-1")]),
            "corr-dup-again", TestContext.Current.CancellationToken));

        Assert.Equal(QcErrorCodes.ValidationFailed, empty.Error!.ErrorCode);
        Assert.Equal(QcErrorCodes.ValidationFailed, duplicateInBatch.Error!.ErrorCode);
        Assert.Equal(QcErrorCodes.ValidationFailed, duplicateAcrossCalls.Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from qc.qc_impact"));
    }

    [Fact]
    public async Task Batch_gate_blocked_or_unknown_fails_closed()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var blockedProvider = BuildProvider(connectionString, batchDecision: BatchStatusDecisions.Blocked);
        await using var unknownProvider = BuildProvider(connectionString, batchDecision: BatchStatusDecisions.Unknown);
        using var blockedScope = blockedProvider.CreateScope();
        using var unknownScope = unknownProvider.CreateScope();

        var blocked = await Assert.ThrowsAsync<QcDomainException>(() =>
            blockedScope.ServiceProvider.GetRequiredService<IQcRunService>()
                .OpenRunAsync(Run(), "corr-blocked", TestContext.Current.CancellationToken));
        var unknown = await Assert.ThrowsAsync<QcDomainException>(() =>
            unknownScope.ServiceProvider.GetRequiredService<IQcRunService>()
                .OpenRunAsync(Run(), "corr-unknown", TestContext.Current.CancellationToken));

        Assert.Equal(QcErrorCodes.EligibilityBlocked, blocked.ErrorCode);
        Assert.Equal(QcErrorCodes.ApplicabilityUnknown, unknown.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from qc.qc_run"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from qc.audit_attempt"));
    }

    [Fact]
    public async Task Qc_facts_reject_mutation_and_stale_versions()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IQcRunService>();
        var run = await service.OpenRunAsync(Run(), "corr-open", TestContext.Current.CancellationToken);
        await service.AddResultAsync(
            run.QcRunId, Result(run.Version, QcVerdicts.Fail), "corr-result", TestContext.Current.CancellationToken);

        var updateRun = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update qc.qc_run set method_version = 99"));
        var deleteResult = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from qc.qc_result"));
        var updateResult = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update qc.qc_result set verdict = 'PASS'"));
        var stale = await CaptureAsync(service.AddResultAsync(
            run.QcRunId, Result(run.Version, QcVerdicts.Pass), "corr-stale", TestContext.Current.CancellationToken));

        Assert.Equal("55000", updateRun.SqlState);
        Assert.Equal("55000", deleteResult.SqlState);
        Assert.Equal("55000", updateResult.SqlState);
        Assert.Equal(QcErrorCodes.ExpectedVersionConflict, stale.Error!.ErrorCode);
    }

    [Fact]
    public async Task Concurrent_results_at_one_expected_version_admit_exactly_one_writer()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string runId;
        long version;
        await using (var setup = BuildProvider(connectionString))
        {
            using var setupScope = setup.CreateScope();
            var run = await setupScope.ServiceProvider.GetRequiredService<IQcRunService>()
                .OpenRunAsync(Run(), "corr-setup", TestContext.Current.CancellationToken);
            runId = run.QcRunId;
            version = run.Version;
        }

        // Distinct rules, so the (run, rule) unique index cannot be what picks a
        // single winner — only the advisory lock plus the version check can.
        await using var firstProvider = BuildProvider(connectionString, actorId: "operator-a");
        await using var secondProvider = BuildProvider(connectionString, actorId: "operator-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        var outcomes = await Task.WhenAll(
            CaptureAsync(firstScope.ServiceProvider.GetRequiredService<IQcRunService>().AddResultAsync(
                runId, Result(version, QcVerdicts.Pass) with { Rule = new QcVersionedReference("RULE-A", 1) },
                "corr-a", TestContext.Current.CancellationToken)),
            CaptureAsync(secondScope.ServiceProvider.GetRequiredService<IQcRunService>().AddResultAsync(
                runId, Result(version, QcVerdicts.Pass) with { Rule = new QcVersionedReference("RULE-B", 1) },
                "corr-b", TestContext.Current.CancellationToken)));

        Assert.Single(outcomes, outcome => outcome.Error is null);
        Assert.Equal(QcErrorCodes.ExpectedVersionConflict,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from qc.qc_result"));
    }

    [Fact]
    public async Task Capability_denied_fails_closed_with_attempt_audit_only()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, permit: false);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<QcDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<IQcRunService>()
                .OpenRunAsync(Run(), "corr-denied", TestContext.Current.CancellationToken));

        Assert.Equal(QcErrorCodes.NotAuthorized, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from qc.qc_run"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.outbox"));
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from qc.audit_attempt where correlation_id = 'corr-denied'"));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_back_qc_facts(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString);
            using var scope = provider.CreateScope();

            var exception = await Assert.ThrowsAsync<QcDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<IQcRunService>()
                    .OpenRunAsync(Run(), $"corr-{failedWriter}", TestContext.Current.CancellationToken));

            Assert.Equal(QcErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from qc.qc_run"));
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "select count(*) from qc.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
    }

    private static async Task<QcRunResult> FailedRunAsync(IQcRunService service, IReadOnlyList<string> targets)
    {
        var run = await service.OpenRunAsync(Run(), "corr-open", TestContext.Current.CancellationToken);
        var withResult = await service.AddResultAsync(
            run.QcRunId, Result(run.Version, QcVerdicts.Fail), "corr-result", TestContext.Current.CancellationToken);
        var failed = await service.RecordVerdictAsync(
            withResult.QcRunId, new RecordQcVerdictRequest(withResult.Version, QcContract.RuleSetVersion),
            "corr-verdict", TestContext.Current.CancellationToken);
        return await service.RecordImpactAsync(
            failed.QcRunId,
            new RecordQcImpactRequest(failed.Version, QcContract.RuleSetVersion, [.. targets.Select(Target)]),
            "corr-impact", TestContext.Current.CancellationToken);
    }

    private static async Task<(object? Result, QcDomainException? Error)> CaptureAsync<T>(Task<T> task)
    {
        try
        {
            return (await task, null);
        }
        catch (QcDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        bool permit = true,
        string batchDecision = BatchStatusDecisions.Allowed,
        string actorId = "reviewer-a")
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
        new QcModule(connectionString).AddApiServices(services);
        services.RemoveAll<IQcAuthorizationPort>();
        services.AddSingleton<IQcAuthorizationPort>(new FixedAuthorizationPort(permit));
        services.RemoveAll<IBatchStatusPort>();
        services.AddSingleton<IBatchStatusPort>(new FixedBatchStatusPort(batchDecision));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateQcRunRequest Run() => new(
        QcContract.RuleSetVersion,
        new QcObjectContext("LEGAL-A", "LAB-A"),
        BatchId,
        2,
        new QcVersionedReference("METHOD-TENSILE", 3),
        new QcVersionedReference("QC-RULESET-TOY", 2));

    private static AddQcResultRequest Result(long expectedVersion, string verdict) => new(
        expectedVersion, QcContract.RuleSetVersion, new QcVersionedReference("RULE-BLANK", 1),
        QcControlTypes.Blank, "0.02", verdict,
        verdict == QcVerdicts.Fail ? "blank exceeded tolerance" : "within blank tolerance");

    private static QcImpactTarget Target(string id) => new(QcImpactTargetTypes.ResultGroup, id, 3);

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for qc integration tests.");

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
        await new QcModule(connectionString).ApplyMigrationAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              qc.audit_attempt,
              qc.qc_release,
              qc.qc_deviation_approval,
              qc.qc_release_gate,
              qc.qc_impact,
              qc.qc_verdict,
              qc.qc_result,
              qc.qc_run,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_qc_audit on platform.audit_intent;
                drop function if exists platform.fail_qc_audit();
                create or replace function platform.fail_qc_audit() returns trigger language plpgsql as $$
                begin
                  if new.action like '%QC%' then
                    raise exception 'forced qc audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_qc_audit before insert on platform.audit_intent
                for each row execute function platform.fail_qc_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_qc_outbox on platform.outbox;
                drop function if exists platform.fail_qc_outbox();
                create or replace function platform.fail_qc_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type like 'Qc%' then
                    raise exception 'forced qc outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_qc_outbox before insert on platform.outbox
                for each row execute function platform.fail_qc_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_qc_audit on platform.audit_intent;
                drop function if exists platform.fail_qc_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_qc_outbox on platform.outbox;
                drop function if exists platform.fail_qc_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FixedAuthorizationPort(bool allowed) : IQcAuthorizationPort
    {
        public ValueTask<QcAuthorizationDecision> AuthorizeAsync(
            QcAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed ? QcAuthorizationDecision.Permit : QcAuthorizationDecision.Deny);
    }

    private sealed class FixedBatchStatusPort(string decision) : IBatchStatusPort
    {
        public ValueTask<BatchStatusResult> EvaluateAsync(
            BatchStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new BatchStatusResult(
                decision,
                decision == BatchStatusDecisions.Allowed ? [] : [BatchStatusReasons.BatchFrozen],
                request.BatchId,
                decision == BatchStatusDecisions.Allowed ? BatchStates.Active : BatchStates.Frozen,
                request.ExpectedBatchVersion,
                BatchContract.RuleSetVersion));
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
