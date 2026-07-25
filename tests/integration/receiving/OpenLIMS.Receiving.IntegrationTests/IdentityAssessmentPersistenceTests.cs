using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Receiving;
using Xunit;

namespace OpenLIMS.Receiving.IntegrationTests;

[Collection("receiving-postgres")]
[Trait("Profile", "receiving")]
public sealed class IdentityAssessmentPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Observation_decision_and_three_actions_preserve_quarantine_and_append_evidence()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));
        var itemId = await RegisterOneAsync(provider);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IIdentityAssessmentService>();

        var initial = await service.GetAsync(itemId, "corr-view", TestContext.Current.CancellationToken);
        var observed = await service.AddObservationAsync(itemId, Observation(1), "corr-observe", TestContext.Current.CancellationToken);
        var decided = await service.SubmitDecisionAsync(itemId, Decision(2), "corr-decide", TestContext.Current.CancellationToken);

        Assert.Equal(IdentityAssessmentStates.NotStarted, initial.AssessmentState);
        Assert.Equal(IdentityAssessmentStates.InProgress, observed.AssessmentState);
        Assert.Equal(IdentityAssessmentStates.Matched, decided.AssessmentState);
        Assert.Equal("QUARANTINED", decided.CurrentState);
        Assert.Equal(3, decided.ItemVersion);
        Assert.Single(decided.Observations);
        Assert.Single(decided.Decisions);
        Assert.Equal(1, await CountAsync(connectionString, "receiving.identity_declaration_snapshot"));
        Assert.Equal(1, await CountAsync(connectionString, "receiving.identity_observation"));
        Assert.Equal(1, await CountAsync(connectionString, "receiving.identity_decision"));
        Assert.Equal("QUARANTINED", await ScalarStringAsync(connectionString, "select state from receiving.received_item limit 1"));

        var port = scope.ServiceProvider.GetRequiredService<IReceivingEligibilityPort>();
        foreach (var action in new[]
                 {
                     ReceivingEligibilityActions.Disassembly,
                     ReceivingEligibilityActions.SamplePreparation,
                     ReceivingEligibilityActions.TestAssignment
                 })
        {
            var eligibility = await port.EvaluateAsync(
                new ReceivingEligibilityRequest("lab-a", itemId, action, 3, IdentityAssessmentContract.RuleSetVersion),
                TestContext.Current.CancellationToken);
            Assert.Equal(ReceivingEligibilityDecisions.Blocked, eligibility.Decision);
            Assert.Contains(ReceivingEligibilityReasons.ReleaseDecisionRequired, eligibility.ReasonCodes);
            Assert.Equal(3, eligibility.ItemVersion);
        }

        var reopened = await service.AddObservationAsync(itemId, Observation(3), "corr-reopen", TestContext.Current.CancellationToken);
        var reopenedEligibility = await port.EvaluateAsync(
            new ReceivingEligibilityRequest("lab-a", itemId, ReceivingEligibilityActions.Disassembly, 4, IdentityAssessmentContract.RuleSetVersion),
            TestContext.Current.CancellationToken);
        Assert.Equal(IdentityAssessmentStates.InProgress, reopened.AssessmentState);
        Assert.Null(reopenedEligibility.IdentityDecisionId);
        Assert.Equal(ReceivingEligibilityDecisions.Blocked, reopenedEligibility.Decision);
    }

    [Fact]
    public async Task Concurrent_observations_with_one_expected_version_do_not_overwrite()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));
        var itemId = await RegisterOneAsync(provider);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IIdentityAssessmentService>()
            .AddObservationAsync(itemId, Observation(1), "corr-first", TestContext.Current.CancellationToken);
        var second = secondScope.ServiceProvider.GetRequiredService<IIdentityAssessmentService>()
            .AddObservationAsync(itemId, Observation(1), "corr-second", TestContext.Current.CancellationToken);
        var outcomes = await Task.WhenAll(CaptureAsync(first), CaptureAsync(second));

        Assert.Single(outcomes, outcome => outcome.Result is not null);
        var conflict = Assert.Single(outcomes, outcome => outcome.Error is not null).Error;
        Assert.Equal(ReceivingErrorCodes.ExpectedVersionConflict, conflict!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "receiving.identity_observation"));
        Assert.Equal("2", await ScalarStringAsync(connectionString, "select version::text from receiving.received_item limit 1"));
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_identity_fact_version_and_audit()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));
        var itemId = await RegisterOneAsync(provider);
        var auditBefore = await CountAsync(connectionString, "receiving.audit_pending");
        await ExecuteAsync(connectionString, """
            create or replace function receiving.fail_identity_outbox() returns trigger language plpgsql as $$
            begin
              if new.event_type = 'IDENTITY_OBSERVATION_RECORDED' then
                raise exception 'forced identity outbox failure';
              end if;
              return new;
            end;
            $$;
            create trigger trg_fail_identity_outbox before insert on receiving.outbox
            for each row execute function receiving.fail_identity_outbox();
            """);

        try
        {
            using var scope = provider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IIdentityAssessmentService>();
            var exception = await Assert.ThrowsAsync<ReceivingDomainException>(() =>
                service.AddObservationAsync(itemId, Observation(1), "corr-outbox", TestContext.Current.CancellationToken));

            Assert.Equal(ReceivingErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "receiving.identity_observation"));
            Assert.Equal(0, await CountAsync(connectionString, "receiving.identity_declaration_snapshot"));
            Assert.Equal(auditBefore, await CountAsync(connectionString, "receiving.audit_pending"));
            Assert.Equal("1", await ScalarStringAsync(connectionString, "select version::text from receiving.received_item limit 1"));
            Assert.Equal(1, await CountAsync(connectionString, "receiving.audit_attempt"));
        }
        finally
        {
            await ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_identity_outbox on receiving.outbox;
                drop function if exists receiving.fail_identity_outbox();
                """);
        }
    }

    [Fact]
    public async Task Unknown_rule_and_stale_version_fail_closed_without_allowed_result()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));
        var itemId = await RegisterOneAsync(provider);
        using var scope = provider.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IReceivingEligibilityPort>();

        var unknownRule = await port.EvaluateAsync(
            new ReceivingEligibilityRequest("lab-a", itemId, ReceivingEligibilityActions.Disassembly, 1, "latest"),
            TestContext.Current.CancellationToken);
        var staleVersion = await port.EvaluateAsync(
            new ReceivingEligibilityRequest("lab-a", itemId, ReceivingEligibilityActions.Disassembly, 999, IdentityAssessmentContract.RuleSetVersion),
            TestContext.Current.CancellationToken);

        Assert.Equal(ReceivingEligibilityDecisions.Unknown, unknownRule.Decision);
        Assert.Equal(ReceivingEligibilityDecisions.Unknown, staleVersion.Decision);
        Assert.DoesNotContain(ReceivingEligibilityDecisions.Allowed, new[] { unknownRule.Decision, staleVersion.Decision });
    }

    [Fact]
    public async Task Unauthorized_identity_access_records_only_hash_safe_attempt_evidence()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var registrationProvider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));
        var itemId = await RegisterOneAsync(registrationProvider);
        await using var deniedProvider = BuildProvider(connectionString, ReceivingAuthorizationDecision.Denied);
        using var scope = deniedProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IIdentityAssessmentService>();

        var exception = await Assert.ThrowsAsync<ReceivingDomainException>(() =>
            service.GetAsync(itemId, "corr-denied", TestContext.Current.CancellationToken));

        Assert.Equal(ReceivingErrorCodes.AuthorizationDenied, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "receiving.identity_observation"));
        Assert.Equal(1, await CountAsync(connectionString, "receiving.audit_attempt"));
        var target = await ScalarStringAsync(connectionString, "select target_hash from receiving.audit_attempt limit 1");
        Assert.Equal(64, target.Length);
        Assert.DoesNotContain(itemId, target, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Identity_fact_history_rejects_update_and_delete()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, ReceivingAuthorizationDecision.AllowedFor("LAB-A"));
        var itemId = await RegisterOneAsync(provider);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IIdentityAssessmentService>();
        await service.AddObservationAsync(itemId, Observation(1), "corr-append-only", TestContext.Current.CancellationToken);

        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "update receiving.identity_observation set appearance = 'rewritten'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "delete from receiving.identity_declaration_snapshot"));

        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
        Assert.Equal(1, await CountAsync(connectionString, "receiving.identity_observation"));
        Assert.Equal(1, await CountAsync(connectionString, "receiving.identity_declaration_snapshot"));
    }

    private static async Task<(IdentityAssessmentResult? Result, ReceivingDomainException? Error)> CaptureAsync(
        Task<IdentityAssessmentResult> task)
    {
        try
        {
            return (await task, null);
        }
        catch (ReceivingDomainException exception)
        {
            return (null, exception);
        }
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

    private static async Task<string> RegisterOneAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IReceiptRegistrationService>().RegisterAsync(
            new RegisterReceiptRequest(
                "legal-a",
                "lab-a",
                "customer-a",
                "order-a",
                Now.AddMinutes(-5),
                [new RegisterContainerRequest(
                    "BOX-IDENTITY",
                    "carton",
                    "intact",
                    "seal intact",
                    [new RegisterReceivedItemRequest(
                        "Hard plastic toy set",
                        "MODEL-001",
                        "BATCH-001",
                        "SERIAL-001",
                        "red",
                        "intact",
                        "sealed",
                        "intact",
                        1,
                        "set")])]),
            Guid.NewGuid().ToString("N"),
            "corr-register",
            TestContext.Current.CancellationToken);
        return result.Containers[0].ReceivedItems[0].ReceivedItemId;
    }

    private static CreateIdentityObservationRequest Observation(long expectedVersion) => new(
        expectedVersion,
        ["OUTER-LABEL-01"],
        "MODEL-001",
        "BATCH-001",
        "Intact red toy set",
        ["object://identity/photo-01"],
        [new string('a', 64)]);

    private static SubmitIdentityDecisionRequest Decision(long expectedVersion) => new(
        expectedVersion,
        1,
        1,
        IdentityDecisionOutcomes.Matched,
        "CONSISTENT",
        "All required evidence is consistent.",
        IdentityAssessmentContract.RuleSetVersion);

    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException("OPENLIMS_TEST_POSTGRES_CONNECTION is required for receiving integration tests.");

    private static async Task PrepareAsync(string connectionString)
    {
        await ReceivingMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ReceivingLabelIdentityMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ReceivingIdentityAssessmentMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              receiving.identity_decision,
              receiving.identity_observation,
              receiving.identity_assessment,
              receiving.identity_declaration_snapshot,
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
