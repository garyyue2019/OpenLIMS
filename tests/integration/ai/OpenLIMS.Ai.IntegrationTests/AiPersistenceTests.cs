using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Ai;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Modules.Ai;
using Xunit;

namespace OpenLIMS.Ai.IntegrationTests;

[CollectionDefinition("ai-postgres", DisableParallelization = true)]
public sealed class AiPostgresCollection;

[Collection("ai-postgres")]
[Trait("Profile", "ai")]
public sealed class AiPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_ai_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Disabled_provider_persists_manual_fallback_and_idempotent_recovery()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, allowed: true);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAiRunService>();

        var first = await service.CreateAsync(
            Request("disabled-1"), "corr-disabled-1", TestContext.Current.CancellationToken);
        var retry = await service.CreateAsync(
            Request("disabled-1"), "corr-disabled-2", TestContext.Current.CancellationToken);

        Assert.Equal(first.RunId, retry.RunId);
        Assert.Equal(AiRunStatuses.ProviderDisabled, first.Status);
        Assert.Equal(AiProviderStatuses.Disabled, first.ProviderStatus);
        Assert.True(first.ManualFallbackRequired);
        Assert.False(first.HumanReviewRequired);
        Assert.Equal(1, await CountAsync(connectionString, "ai.run_request"));
        Assert.Equal(1, await CountAsync(connectionString, "ai.run_outcome"));
        Assert.Equal(3, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(2, await CountAsync(connectionString, "platform.outbox"));
    }

    [Fact]
    public async Task Accepted_output_and_human_disposition_preserve_original_and_human_values()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(
            connectionString, allowed: true, provider: AcceptedProvider());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAiRunService>();

        var run = await service.CreateAsync(
            Request("accepted-1"), "corr-accepted", TestContext.Current.CancellationToken);
        var disposition = await service.RecordDispositionAsync(
            run.RunId,
            new RecordAiDispositionRequest(
                run.Version, AiContract.RuntimeRuleSetVersion, "candidate-1",
                AiDispositionKinds.Modify, "verified against label", "review-1", "STY-1002"),
            "corr-review",
            TestContext.Current.CancellationToken);
        var loaded = await service.GetAsync(
            run.RunId, "corr-read", TestContext.Current.CancellationToken);

        Assert.Equal(AiRunStatuses.Accepted, run.Status);
        Assert.Equal("provider-job-1", run.ProviderExternalReference);
        Assert.Equal("STY-1001", disposition.Disposition.AiOriginalValue);
        Assert.Equal("STY-1002", disposition.Disposition.HumanValue);
        Assert.Equal("operator-a", disposition.Disposition.ResponsibleActor);
        Assert.Equal(2, loaded.Version);
        Assert.Equal("STY-1001", Assert.Single(loaded.OriginalOutput!.Candidates).Value);
        Assert.Equal("STY-1002", Assert.Single(loaded.Dispositions).Disposition.HumanValue);
        Assert.Equal(1, await CountAsync(connectionString, "ai.disposition"));
    }

    [Fact]
    public async Task Unknown_provider_fields_quarantine_output_and_remain_visible_for_review()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var quarantineProvider = new DelegateProvider(request => new AiProviderResponse(
            AiProviderStatuses.Completed,
            Output(request.Envelope) with
            {
                Candidates = [Candidate() with { TargetField = "unknown-field" }]
            },
            "provider-job-quarantine"));
        await using var provider = BuildProvider(
            connectionString, allowed: true, provider: quarantineProvider);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAiRunService>();

        var run = await service.CreateAsync(
            Request("quarantine-1"), "corr-quarantine", TestContext.Current.CancellationToken);
        var queue = await service.GetReviewQueueAsync(
            null, "corr-queue", TestContext.Current.CancellationToken);

        Assert.Equal(AiRunStatuses.Quarantined, run.Status);
        Assert.NotNull(run.OriginalOutput);
        Assert.Empty(run.Validation!.Candidates);
        Assert.Contains(run.Validation.Errors, error => error.Code == AiValidationErrorCodes.UnknownField);
        Assert.Equal(run.RunId, Assert.Single(queue.Runs).RunId);
    }

    [Fact]
    public async Task Concurrent_dispositions_with_same_expected_version_allow_only_one_writer()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string runId;
        await using (var setup = BuildProvider(
            connectionString, allowed: true, provider: AcceptedProvider()))
        {
            using var setupScope = setup.CreateScope();
            runId = (await setupScope.ServiceProvider.GetRequiredService<IAiRunService>().CreateAsync(
                Request("concurrent-1"), "corr-create", TestContext.Current.CancellationToken)).RunId;
        }

        await using var first = BuildProvider(connectionString, allowed: true, actorId: "reviewer-a");
        await using var second = BuildProvider(connectionString, allowed: true, actorId: "reviewer-b");
        using var firstScope = first.CreateScope();
        using var secondScope = second.CreateScope();
        var outcomes = await Task.WhenAll(
            CaptureAsync(firstScope.ServiceProvider.GetRequiredService<IAiRunService>().RecordDispositionAsync(
                runId, AcceptDisposition("review-a"), "corr-a", TestContext.Current.CancellationToken)),
            CaptureAsync(secondScope.ServiceProvider.GetRequiredService<IAiRunService>().RecordDispositionAsync(
                runId, AcceptDisposition("review-b"), "corr-b", TestContext.Current.CancellationToken)));

        Assert.Single(outcomes, outcome => outcome.Error is null);
        Assert.Equal(
            AiErrorCodes.ExpectedVersionConflict,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "ai.disposition"));
        Assert.Equal(1, await CountAsync(connectionString, "ai.audit_attempt"));
    }

    [Fact]
    public async Task Idempotency_key_cannot_be_reused_for_a_different_object_scope()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var countingProvider = AcceptedProvider();
        await using var provider = BuildProvider(
            connectionString, allowed: true, provider: countingProvider);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAiRunService>();
        await service.CreateAsync(
            Request("scope-1"), "corr-first", TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<AiDomainException>(() => service.CreateAsync(
            Request("scope-1") with
            {
                ObjectScope = ObjectScope() with { CustomerId = "CUSTOMER-B" }
            },
            "corr-second",
            TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.IdempotencyConflict, exception.ErrorCode);
        Assert.Equal(1, countingProvider.CallCount);
        Assert.Equal(1, await CountAsync(connectionString, "ai.run_request"));
    }

    [Fact]
    public async Task Authorization_denial_writes_attempt_without_calling_provider_or_persisting_facts()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var countingProvider = AcceptedProvider();
        await using var provider = BuildProvider(
            connectionString, allowed: false, provider: countingProvider);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<AiDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<IAiRunService>().CreateAsync(
                Request("denied-1"), "corr-denied", TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCodes.NotAuthorized, exception.ErrorCode);
        Assert.Equal(0, countingProvider.CallCount);
        Assert.Equal(0, await CountAsync(connectionString, "ai.run_request"));
        Assert.Equal(0, await CountAsync(connectionString, "platform.audit_intent"));
        Assert.Equal(0, await CountAsync(connectionString, "platform.outbox"));
        Assert.Equal(1, await CountAsync(connectionString, "ai.audit_attempt"));
    }

    [Fact]
    public async Task Provider_exception_is_persisted_as_terminal_failure_with_manual_fallback()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var failingProvider = new DelegateProvider(_ => throw new InvalidOperationException("provider unavailable"));
        await using var provider = BuildProvider(
            connectionString, allowed: true, provider: failingProvider);
        using var scope = provider.CreateScope();

        var run = await scope.ServiceProvider.GetRequiredService<IAiRunService>().CreateAsync(
            Request("provider-failed-1"), "corr-provider-failed", TestContext.Current.CancellationToken);

        Assert.Equal(AiRunStatuses.ProviderFailed, run.Status);
        Assert.Equal(AiProviderStatuses.Failed, run.ProviderStatus);
        Assert.Equal("PROVIDER_INVOCATION_FAILED", run.ProviderFailureCode);
        Assert.True(run.ManualFallbackRequired);
        Assert.Equal(1, failingProvider.CallCount);
        Assert.Equal(1, await CountAsync(connectionString, "ai.run_outcome"));
    }

    [Fact]
    public async Task Ai_runtime_facts_reject_update_and_delete()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, allowed: true);
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAiRunService>().CreateAsync(
            Request("append-only-1"), "corr-create", TestContext.Current.CancellationToken);

        var update = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "update ai.run_request set requested_by = 'tampered'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString,
            "delete from ai.run_outcome"));

        Assert.Equal("55000", update.SqlState);
        Assert.Equal("55000", delete.SqlState);
    }

    private static async Task<(AiReviewDispositionResult? Result, AiDomainException? Error)> CaptureAsync(
        Task<AiReviewDispositionResult> task)
    {
        try
        {
            return (await task, null);
        }
        catch (AiDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        bool allowed,
        string actorId = "operator-a",
        DelegateProvider? provider = null)
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
        new AiModule(connectionString).AddApiServices(services);
        services.RemoveAll<IAiAuthorizationPort>();
        services.AddSingleton<IAiAuthorizationPort>(new FixedAuthorizationPort(allowed));
        if (provider is not null)
        {
            services.RemoveAll<IAiProviderPort>();
            services.AddSingleton<IAiProviderPort>(provider);
        }
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static DelegateProvider AcceptedProvider() => new(request => new AiProviderResponse(
        AiProviderStatuses.Completed,
        Output(request.Envelope),
        "provider-job-1"));

    private static RecordAiDispositionRequest AcceptDisposition(string idempotencyKey) => new(
        1,
        AiContract.RuntimeRuleSetVersion,
        "candidate-1",
        AiDispositionKinds.Accept,
        "accepted after source review",
        idempotencyKey);

    private static CreateAiRunRequest Request(string idempotencyKey) => new(
        AiContract.RuntimeRuleSetVersion,
        ObjectScope(),
        Envelope(),
        new AiVersionedReference("VALIDATION-PROFILE", 1),
        ["style-number"],
        [],
        idempotencyKey);

    private static AiObjectContext ObjectScope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE");

    private static AiRunEnvelope Envelope() => new(
        new AiVersionedReference("MODEL-A", 1),
        "gateway-primary",
        new AiVersionedReference("PROMPT-A", 1),
        new AiVersionedReference("SCHEMA-A", 1),
        [new AiVersionedReference("DOC-A", 1)]);

    private static AiStructuredOutput Output(AiRunEnvelope envelope) => new(
        AiContract.RuleSetVersion,
        envelope,
        [Candidate()],
        [new AiGapSuggestion(
            "gap-1", "target-market", AiGapKinds.MissingInformation, "Which market applies?")]);

    private static AiFieldCandidate Candidate() => new(
        "candidate-1", "style-number", "STY-1001", AiFactClasses.AiInference, 0.94m,
        new AiSourceLocation(new AiVersionedReference("DOC-A", 1), 2, "top-right"));

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for AI integration tests.");

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
        await AiMigrator.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              ai.audit_attempt,
              ai.disposition,
              ai.run_outcome,
              ai.run_request,
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

    private static async Task<int> CountAsync(string connectionString, string table)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand($"select count(*) from {table}");
        return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FixedActorContext(ActorContext actor) : ICurrentActorContext
    {
        public ActorContext? Current { get; } = actor;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedAuthorizationPort(bool allowed) : IAiAuthorizationPort
    {
        public ValueTask<AiAuthorizationDecision> AuthorizeAsync(
            AiAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(allowed ? AiAuthorizationDecision.Permit : AiAuthorizationDecision.Deny);
        }
    }
}

internal sealed class DelegateProvider(Func<AiProviderRequest, AiProviderResponse> handler) : IAiProviderPort
{
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public ValueTask<AiProviderResponse> ExecuteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _callCount);
        return ValueTask.FromResult(handler(request));
    }
}
