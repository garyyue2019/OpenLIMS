using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;
using OpenLIMS.Modules.Toy;
using Xunit;

namespace OpenLIMS.Toy.IntegrationTests;

[CollectionDefinition("toy-postgres", DisableParallelization = true)]
public sealed class ToyPostgresCollection;

[Collection("toy-postgres")]
[Trait("Profile", "toy")]
public sealed class ToyPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_toy_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Customer_claim_and_laboratory_determination_are_stored_apart()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();

        var declared = await service.RecordDeclarationAsync(
            productId, Declaration(0), "corr-declare", TestContext.Current.CancellationToken);
        var decided = await service.RecordDecisionAsync(
            productId, Decision(declared.Version, 36), "corr-decide", TestContext.Current.CancellationToken);

        // OPS-TOY-001: the claim says 36 months because the customer said so;
        // the determination says 36 months because the laboratory decided it,
        // and the two live in different tables.
        var declaration = Assert.Single(decided.Declarations);
        Assert.Equal(36, declaration.DeclaredMinimumAgeMonths);
        Assert.Equal("CUSTOMER_SUBMISSION", declaration.DeclarationSource);
        var decision = Assert.Single(decided.Decisions);
        Assert.Equal(ToyDecisionStates.Draft, decision.State);
        Assert.Equal("APPROVER-1", decision.ApprovedBy);
        Assert.Equal(2, decision.StandardRef.Version);
        Assert.Null(decided.EffectiveDecision);

        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.age_declaration"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.age_grade_decision"));
        // Product registration, declaration and determination: three facts.
        Assert.Equal(3, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from platform.outbox"));
    }

    [Fact]
    public async Task Determination_rejects_a_missing_rationale_standard_or_approver()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();
        var declared = await service.RecordDeclarationAsync(
            productId, Declaration(0), "corr-declare", TestContext.Current.CancellationToken);

        var failures = new List<string>();
        foreach (var broken in new[]
        {
            Decision(declared.Version, 36) with { Rationale = "  " },
            Decision(declared.Version, 36) with { StandardRef = new ToyVersionedReference(" ", 2) },
            Decision(declared.Version, 36) with { ApprovedBy = "" },
            Decision(declared.Version, 400)
        })
        {
            var attempt = await CaptureAsync(service.RecordDecisionAsync(
                productId, broken, "corr-broken", TestContext.Current.CancellationToken));
            failures.Add(attempt.Error!.ErrorCode);
        }

        Assert.All(failures, code => Assert.Equal(ToyErrorCodes.ValidationFailed, code));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from toy.age_grade_decision"));
        Assert.Equal(4, await CountAsync(connectionString, "select count(*) from toy.audit_attempt"));
    }

    /// <summary>
    /// AC-TOY-001, first half: the customer changes their mind, and the original
    /// determination survives it intact (OPS-TOY-002).
    /// </summary>
    [Fact]
    public async Task Re_determination_appends_a_version_and_leaves_the_first_one_intact()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();

        var declared = await service.RecordDeclarationAsync(
            productId, Declaration(0), "corr-declare", TestContext.Current.CancellationToken);
        var decided = await service.RecordDecisionAsync(
            productId, Decision(declared.Version, 36), "corr-decide", TestContext.Current.CancellationToken);
        var frozen = await service.FreezeDecisionAsync(
            productId, 1, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, decided.Version),
            "corr-freeze", TestContext.Current.CancellationToken);
        var firstEffective = frozen.EffectiveDecision;

        var reDeclared = await service.RecordDeclarationAsync(
            productId,
            Declaration(frozen.Version) with { DeclaredMinimumAgeMonths = 18, IntendedUse = "学步推车" },
            "corr-redeclare", TestContext.Current.CancellationToken);
        var reDecided = await service.RecordDecisionAsync(
            productId,
            Decision(reDeclared.Version, 18) with { Rationale = "改判为 18 个月及以上" },
            "corr-redecide", TestContext.Current.CancellationToken);
        var reFrozen = await service.FreezeDecisionAsync(
            productId, 2, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, reDecided.Version),
            "corr-refreeze", TestContext.Current.CancellationToken);

        Assert.Equal(36, firstEffective!.MinimumAgeMonths);
        Assert.Equal(ToyDecisionStates.Effective, firstEffective.State);

        var v1 = reFrozen.Decisions.Single(entry => entry.VersionNumber == 1);
        var v2 = reFrozen.Decisions.Single(entry => entry.VersionNumber == 2);
        Assert.Equal(ToyDecisionStates.Superseded, v1.State);
        Assert.Equal(ToyDecisionStates.Effective, v2.State);
        // V1 still answers with its own rationale, approver and age — nothing
        // about it was rewritten by the re-determination.
        Assert.Equal(36, v1.MinimumAgeMonths);
        Assert.Equal(firstEffective.DecisionId, v1.DecisionId);
        Assert.Equal(firstEffective.Rationale, v1.Rationale);
        Assert.Equal(18, v2.MinimumAgeMonths);
        Assert.Equal(2, reFrozen.EffectiveDecision!.VersionNumber);
        Assert.Equal(2, reFrozen.Declarations.Count);
        Assert.Single(reFrozen.Decisions, entry =>
            string.Equals(entry.State, ToyDecisionStates.Effective, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_frozen_determination_cannot_be_frozen_again_or_rewritten()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();
        var decided = await service.RecordDecisionAsync(
            productId, Decision(0, 36), "corr-decide", TestContext.Current.CancellationToken);
        var frozen = await service.FreezeDecisionAsync(
            productId, 1, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, decided.Version),
            "corr-freeze", TestContext.Current.CancellationToken);

        var again = await CaptureAsync(service.FreezeDecisionAsync(
            productId, 1, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, frozen.Version),
            "corr-freeze-again", TestContext.Current.CancellationToken));
        var missing = await CaptureAsync(service.FreezeDecisionAsync(
            productId, 9, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, frozen.Version),
            "corr-freeze-missing", TestContext.Current.CancellationToken));
        var rewrite = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update toy.age_grade_decision set minimum_age_months = 6;"));
        var erase = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from toy.age_grade_freeze;"));

        Assert.Equal(ToyErrorCodes.DecisionFrozen, again.Error!.ErrorCode);
        Assert.Equal(ToyErrorCodes.DecisionNotFound, missing.Error!.ErrorCode);
        Assert.Equal("55000", rewrite.SqlState);
        Assert.Equal("55000", erase.SqlState);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.age_grade_freeze"));
    }

    /// <summary>
    /// AC-TOY-001, second half: an abuse event exposes a part that was not
    /// reachable before, and that opens all three scopes (OPS-TOY-003).
    /// </summary>
    [Fact]
    public async Task A_newly_exposed_part_opens_mechanical_chemical_and_labeling_reassessment()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();

        var initial = await service.RecordAssessmentAsync(
            productId, Assessment(0, ToyAssessmentStages.Initial, ["shell", "wheels"]),
            "corr-initial", TestContext.Current.CancellationToken);
        var afterAbuse = await service.RecordAssessmentAsync(
            productId,
            Assessment(initial.Version, ToyAssessmentStages.AfterAbuse, ["shell", "wheels", "battery-compartment"])
                with { AbuseEventRef = "DROP-TEST-1" },
            "corr-abuse", TestContext.Current.CancellationToken);

        Assert.Empty(initial.Triggers);
        Assert.Equal(ToyAccessibilityStatuses.Settled, initial.AccessibilityStatus);
        Assert.Equal(3, afterAbuse.Triggers.Count);
        Assert.Equal(
            ["CHEMICAL", "LABELING", "MECHANICAL"],
            afterAbuse.Triggers.Select(trigger => trigger.Scope).Order(StringComparer.Ordinal));
        Assert.All(afterAbuse.Triggers, trigger =>
        {
            Assert.Equal(ToyTriggerStates.Pending, trigger.State);
            Assert.Equal(["battery-compartment"], trigger.NewlyExposedParts);
            Assert.Equal(2, trigger.AssessmentVersion);
        });
        Assert.Equal(ToyAccessibilityStatuses.ReassessmentPending, afterAbuse.AccessibilityStatus);
        // The initial assessment keeps its own part set; it is not back-filled.
        var recordedInitial = afterAbuse.Assessments.Single(entry => entry.VersionNumber == 1);
        Assert.Equal(["shell", "wheels"], recordedInitial.AccessibleParts);
        Assert.Null(recordedInitial.AbuseEventRef);
        Assert.Equal("DROP-TEST-1", afterAbuse.Assessments.Single(entry => entry.VersionNumber == 2).AbuseEventRef);
    }

    [Fact]
    public async Task An_assessment_that_exposes_nothing_new_raises_nothing()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();

        var initial = await service.RecordAssessmentAsync(
            productId, Assessment(0, ToyAssessmentStages.Initial, ["shell", "wheels"]),
            "corr-initial", TestContext.Current.CancellationToken);
        var same = await service.RecordAssessmentAsync(
            productId, Assessment(initial.Version, ToyAssessmentStages.AfterNormalUse, ["wheels", "shell"]),
            "corr-same", TestContext.Current.CancellationToken);
        // Losing access to a part is not a finding either.
        var fewer = await service.RecordAssessmentAsync(
            productId, Assessment(same.Version, ToyAssessmentStages.AfterNormalUse, ["shell"]),
            "corr-fewer", TestContext.Current.CancellationToken);

        Assert.Empty(fewer.Triggers);
        Assert.Equal(ToyAccessibilityStatuses.Settled, fewer.AccessibilityStatus);
        Assert.Equal(3, fewer.Assessments.Count);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from toy.reassessment_trigger"));
    }

    [Fact]
    public async Task Assessment_stage_rules_are_enforced_by_the_domain_and_the_database()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var productId = NewProductId();

        var notInitialFirst = await CaptureAsync(service.RecordAssessmentAsync(
            productId, Assessment(0, ToyAssessmentStages.AfterNormalUse, ["shell"]),
            "corr-not-initial", TestContext.Current.CancellationToken));
        var initial = await service.RecordAssessmentAsync(
            productId, Assessment(0, ToyAssessmentStages.Initial, ["shell"]),
            "corr-initial", TestContext.Current.CancellationToken);
        var secondInitial = await CaptureAsync(service.RecordAssessmentAsync(
            productId, Assessment(initial.Version, ToyAssessmentStages.Initial, ["shell"]),
            "corr-second-initial", TestContext.Current.CancellationToken));
        var abuseWithoutEvent = await CaptureAsync(service.RecordAssessmentAsync(
            productId, Assessment(initial.Version, ToyAssessmentStages.AfterAbuse, ["shell"]),
            "corr-abuse-no-event", TestContext.Current.CancellationToken));
        var normalWithEvent = await CaptureAsync(service.RecordAssessmentAsync(
            productId,
            Assessment(initial.Version, ToyAssessmentStages.AfterNormalUse, ["shell"])
                with { AbuseEventRef = "DROP-TEST-1" },
            "corr-normal-with-event", TestContext.Current.CancellationToken));

        Assert.Equal(ToyErrorCodes.ValidationFailed, notInitialFirst.Error!.ErrorCode);
        Assert.Equal(ToyErrorCodes.ValidationFailed, secondInitial.Error!.ErrorCode);
        Assert.Equal(ToyErrorCodes.ValidationFailed, abuseWithoutEvent.Error!.ErrorCode);
        Assert.Equal(ToyErrorCodes.ValidationFailed, normalWithEvent.Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.accessibility_assessment"));

        // The same two rules are also CHECK constraints, so no future caller can
        // reach a bad row by another route.
        var directBadStage = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, """
            insert into toy.accessibility_assessment (
                assessment_id, product_id, version_number, stage, abuse_event_ref,
                assessed_by, assessed_at, event_id, correlation_id)
            select gen_random_uuid(), product_id, 2, 'AFTER_ABUSE', null,
                   'x', now(), 'evt-bad-stage', 'corr'
            from toy.product limit 1;
            """));
        Assert.Equal("23514", directBadStage.SqlState);
    }

    [Fact]
    public async Task Triggers_settle_once_and_the_status_port_pins_the_version()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
        var port = scope.ServiceProvider.GetRequiredService<IToyAgeGradeStatusPort>();
        var productId = NewProductId();

        var decided = await service.RecordDecisionAsync(
            productId, Decision(0, 36), "corr-decide", TestContext.Current.CancellationToken);
        var frozen = await service.FreezeDecisionAsync(
            productId, 1, new FreezeAgeGradeDecisionRequest(ToyContract.RuleSetVersion, decided.Version),
            "corr-freeze", TestContext.Current.CancellationToken);
        var initial = await service.RecordAssessmentAsync(
            productId, Assessment(frozen.Version, ToyAssessmentStages.Initial, ["shell"]),
            "corr-initial", TestContext.Current.CancellationToken);
        var current = await service.RecordAssessmentAsync(
            productId,
            Assessment(initial.Version, ToyAssessmentStages.AfterAbuse, ["shell", "spring"])
                with { AbuseEventRef = "TORQUE-TEST-1" },
            "corr-abuse", TestContext.Current.CancellationToken);

        var blocked = await port.EvaluateAsync(new ToyAgeGradeStatusRequest(
            "group-a", productId, current.Version, ToyContract.RuleSetVersion)
        {
            CorrelationId = "corr-status-blocked"
        }, TestContext.Current.CancellationToken);

        var firstTriggerId = current.Triggers[0].TriggerId;
        foreach (var trigger in current.Triggers)
        {
            current = await service.ResolveTriggerAsync(
                productId, trigger.TriggerId,
                ToyResolution(current.Version), "corr-resolve", TestContext.Current.CancellationToken);
        }

        var repeat = await CaptureAsync(service.ResolveTriggerAsync(
            productId, firstTriggerId, ToyResolution(current.Version),
            "corr-resolve-again", TestContext.Current.CancellationToken));
        var allowed = await port.EvaluateAsync(new ToyAgeGradeStatusRequest(
            "group-a", productId, current.Version, ToyContract.RuleSetVersion)
        {
            CorrelationId = "corr-status-allowed"
        }, TestContext.Current.CancellationToken);
        var stale = await port.EvaluateAsync(new ToyAgeGradeStatusRequest(
            "group-a", productId, current.Version - 1, ToyContract.RuleSetVersion)
        {
            CorrelationId = "corr-status-stale"
        }, TestContext.Current.CancellationToken);
        var unknownRuleSet = await port.EvaluateAsync(new ToyAgeGradeStatusRequest(
            "group-a", productId, current.Version, "TOY-AGE-GRADE@0.0.1")
        {
            CorrelationId = "corr-status-unknown"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(ToyAgeGradeDecisions.Blocked, blocked.Decision);
        Assert.Equal([ToyAgeGradeReasons.ReassessmentPending], blocked.ReasonCodes);
        Assert.Equal(ToyErrorCodes.ReassessmentNotPending, repeat.Error!.ErrorCode);
        Assert.Equal(ToyAccessibilityStatuses.Settled, current.AccessibilityStatus);
        Assert.Equal(ToyAgeGradeDecisions.Allowed, allowed.Decision);
        Assert.Equal(1, allowed.EffectiveDecisionVersion);
        Assert.Equal(36, allowed.MinimumAgeMonths);
        Assert.Equal(ToyAgeGradeDecisions.Unknown, stale.Decision);
        Assert.Equal([ToyAgeGradeReasons.VersionMismatch], stale.ReasonCodes);
        Assert.Equal(ToyAgeGradeDecisions.Unknown, unknownRuleSet.Decision);
        Assert.Equal([ToyAgeGradeReasons.RuleSetVersionUnknown], unknownRuleSet.ReasonCodes);
        // A second resolution row for the same trigger is refused by the
        // database too, not only by the derived state.
        Assert.Equal(3, await CountAsync(connectionString, "select count(*) from toy.reassessment_resolution"));
    }

    [Fact]
    public async Task Stale_expected_version_and_denied_authorization_both_fail_closed()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var productId = NewProductId();
        await using (var provider = BuildProvider(connectionString))
        {
            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();
            var declared = await service.RecordDeclarationAsync(
                productId, Declaration(0), "corr-declare", TestContext.Current.CancellationToken);
            var stale = await CaptureAsync(service.RecordDecisionAsync(
                productId, Decision(declared.Version - 1, 36), "corr-stale",
                TestContext.Current.CancellationToken));
            // A later command may not re-point the product at another laboratory.
            var rescoped = await CaptureAsync(service.RecordDecisionAsync(
                productId,
                Decision(declared.Version, 36) with { ObjectScope = new ToyObjectContext("LEGAL-A", "LAB-B") },
                "corr-rescope", TestContext.Current.CancellationToken));

            Assert.Equal(ToyErrorCodes.ExpectedVersionConflict, stale.Error!.ErrorCode);
            Assert.Equal(ToyErrorCodes.ValidationFailed, rescoped.Error!.ErrorCode);
        }

        await using var denied = BuildProvider(connectionString, permit: false);
        using var deniedScope = denied.CreateScope();
        var deniedService = deniedScope.ServiceProvider.GetRequiredService<IToyProductService>();
        var refused = await CaptureAsync(deniedService.GetOverviewAsync(
            productId, "corr-denied", TestContext.Current.CancellationToken));

        Assert.Equal(ToyErrorCodes.NotAuthorized, refused.Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.age_declaration"));
        Assert.Equal(3, await CountAsync(connectionString, "select count(*) from toy.audit_attempt"));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_the_whole_command_back(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString);
            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IToyProductService>();

            var attempt = await CaptureAsync(service.RecordDecisionAsync(
                NewProductId(), Decision(0, 36), "corr-fail", TestContext.Current.CancellationToken));

            Assert.Equal(ToyErrorCodes.PersistenceUnavailable, attempt.Error!.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from toy.age_grade_decision"));
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from toy.product"));
            // The attempt log is written on its own connection, so it survives
            // the rollback that erased the business facts.
            Assert.Equal(1, await CountAsync(connectionString, "select count(*) from toy.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
    }

    private static async Task<(object? Result, ToyDomainException? Error)> CaptureAsync<T>(Task<T> task)
    {
        try
        {
            return (await task, null);
        }
        catch (ToyDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString, bool permit = true, string actorId = "technician-a")
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
        new ToyModule(connectionString).AddApiServices(services);
        services.RemoveAll<IToyAuthorizationPort>();
        services.AddSingleton<IToyAuthorizationPort>(new FixedAuthorizationPort(permit));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string NewProductId() => Guid.NewGuid().ToString("N");

    private static RecordAgeDeclarationRequest Declaration(long expectedVersion) => new(
        ToyContract.RuleSetVersion, new ToyObjectContext("LEGAL-A", "LAB-A"), expectedVersion,
        36, "室内地板玩具车", "CUSTOMER_SUBMISSION");

    private static RecordAgeGradeDecisionRequest Decision(long expectedVersion, int months) => new(
        ToyContract.RuleSetVersion, new ToyObjectContext("LEGAL-A", "LAB-A"), expectedVersion,
        months, "无可分离小零件", new ToyVersionedReference("GB6675.2", 2), "APPROVER-1");

    private static RecordAccessibilityAssessmentRequest Assessment(
        long expectedVersion, string stage, IReadOnlyList<string> parts) => new(
        ToyContract.RuleSetVersion, new ToyObjectContext("LEGAL-A", "LAB-A"), expectedVersion,
        stage, null, parts);

    private static ResolveReassessmentTriggerRequest ToyResolution(long expectedVersion) => new(
        ToyContract.RuleSetVersion, expectedVersion, new ToyVersionedReference("REASSESS-1", 1));

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for toy integration tests.");

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
        await new ToyModule(connectionString).ApplyMigrationAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              toy.audit_attempt,
              toy.reassessment_resolution,
              toy.reassessment_trigger,
              toy.accessible_part,
              toy.accessibility_assessment,
              toy.age_grade_freeze,
              toy.age_grade_decision,
              toy.age_declaration,
              toy.product,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_toy_audit on platform.audit_intent;
                drop function if exists platform.fail_toy_audit();
                create or replace function platform.fail_toy_audit() returns trigger language plpgsql as $$
                begin
                  if new.action like '%TOY%' then
                    raise exception 'forced toy audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_toy_audit before insert on platform.audit_intent
                for each row execute function platform.fail_toy_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_toy_outbox on platform.outbox;
                drop function if exists platform.fail_toy_outbox();
                create or replace function platform.fail_toy_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type like 'Toy%' then
                    raise exception 'forced toy outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_toy_outbox before insert on platform.outbox
                for each row execute function platform.fail_toy_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_toy_audit on platform.audit_intent;
                drop function if exists platform.fail_toy_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_toy_outbox on platform.outbox;
                drop function if exists platform.fail_toy_outbox();
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

    private sealed class FixedAuthorizationPort(bool allowed) : IToyAuthorizationPort
    {
        public ValueTask<ToyAuthorizationDecision> AuthorizeAsync(
            ToyAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed ? ToyAuthorizationDecision.Permit : ToyAuthorizationDecision.Deny);
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
