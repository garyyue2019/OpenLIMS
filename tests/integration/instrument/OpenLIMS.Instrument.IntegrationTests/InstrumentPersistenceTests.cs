using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Instrument;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Modules.Instrument;
using Xunit;

namespace OpenLIMS.Instrument.IntegrationTests;

[CollectionDefinition("instrument-postgres", DisableParallelization = true)]
public sealed class InstrumentPostgresCollection;

[Collection("instrument-postgres")]
[Trait("Profile", "instrument")]
public sealed class InstrumentPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_instrument_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Registration_atomically_persists_facts_audit_and_outbox()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInstrumentImportService>();

        var file = await service.RegisterFileAsync(Registration(), "corr-register", TestContext.Current.CancellationToken);

        Assert.Equal(InstrumentFileStates.Ingested, file.State);
        Assert.Equal(Hash("export-7"), file.Sha256);
        Assert.Equal("PARSER-1.4", file.ParserVersion);
        Assert.Equal(1, file.Version);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from instrument.file_registration"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from platform.outbox"));
    }

    [Fact]
    public async Task Duplicate_content_hash_is_rejected_sequentially_and_concurrently()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using (var setup = BuildProvider(connectionString))
        {
            using var setupScope = setup.CreateScope();
            await setupScope.ServiceProvider.GetRequiredService<IInstrumentImportService>()
                .RegisterFileAsync(Registration(), "corr-first", TestContext.Current.CancellationToken);
        }

        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var sequential = await CaptureAsync(scope.ServiceProvider.GetRequiredService<IInstrumentImportService>()
            .RegisterFileAsync(Registration(), "corr-dup", TestContext.Current.CancellationToken));

        await using var firstProvider = BuildProvider(connectionString, actorId: "operator-a");
        await using var secondProvider = BuildProvider(connectionString, actorId: "operator-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        var alternate = Registration() with { Sha256 = Hash("export-8"), ExternalRef = new InstrumentVersionedReference("EXPORT-8", 1) };
        var outcomes = await Task.WhenAll(
            CaptureAsync(firstScope.ServiceProvider.GetRequiredService<IInstrumentImportService>()
                .RegisterFileAsync(alternate, "corr-c1", TestContext.Current.CancellationToken)),
            CaptureAsync(secondScope.ServiceProvider.GetRequiredService<IInstrumentImportService>()
                .RegisterFileAsync(alternate, "corr-c2", TestContext.Current.CancellationToken)));

        Assert.Equal(InstrumentErrorCodes.DuplicateFile, sequential.Error!.ErrorCode);
        Assert.Single(outcomes, outcome => outcome.Error is null);
        Assert.Equal(InstrumentErrorCodes.DuplicateFile,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from instrument.file_registration"));
    }

    [Fact]
    public async Task Valid_rows_become_immutable_facts_and_bad_rows_block_the_import()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInstrumentImportService>();
        var file = await service.RegisterFileAsync(Registration(), "corr-register", TestContext.Current.CancellationToken);

        var submitted = await service.SubmitRowsAsync(
            file.FileRegistrationId,
            new SubmitInstrumentRowsRequest(file.Version, InstrumentContract.RuleSetVersion,
                [Row(1), Row(2) with { SampleNumber = "unknown sample", RawValue = "  77.1 raw  " }, Row(3)]),
            "corr-rows", TestContext.Current.CancellationToken);

        Assert.Equal(InstrumentFileStates.Blocked, submitted.State);
        Assert.Equal(2, submitted.Rows.Count);
        var queued = Assert.Single(submitted.Exceptions);
        Assert.Equal(InstrumentExceptionReasons.UnknownSample, queued.ReasonCode);
        Assert.Equal(InstrumentExceptionStates.Pending, queued.State);
        Assert.Contains("  77.1 raw  ", queued.RawContent, StringComparison.Ordinal);
        Assert.All(submitted.Rows, row => Assert.Equal("PARSER-1.4", row.ParserVersion));
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from instrument.parsed_row"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from instrument.import_exception"));

        var status = await scope.ServiceProvider.GetRequiredService<IInstrumentImportPort>().EvaluateAsync(
            new InstrumentImportStatusRequest(
                "group-a", submitted.FileRegistrationId, submitted.Version, InstrumentContract.RuleSetVersion)
            {
                CorrelationId = "corr-blocked"
            }, TestContext.Current.CancellationToken);

        Assert.Equal(InstrumentStatusDecisions.Blocked, status.Decision);
        Assert.Contains(InstrumentStatusReasons.PendingExceptions, status.ReasonCodes);
    }

    [Fact]
    public async Task Human_resolution_preserves_raw_values_and_completes_the_import()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInstrumentImportService>();
        var file = await service.RegisterFileAsync(
            Registration() with { DeclaredRowCount = 3 }, "corr-register", TestContext.Current.CancellationToken);
        var submitted = await service.SubmitRowsAsync(
            file.FileRegistrationId,
            new SubmitInstrumentRowsRequest(file.Version, InstrumentContract.RuleSetVersion,
                [Row(1), Row(2) with { Unit = "newton force" }, Row(3) with { ParsedValue = " " }]),
            "corr-rows", TestContext.Current.CancellationToken);
        var illegalUnit = Assert.Single(submitted.Exceptions,
            entry => entry.ReasonCode == InstrumentExceptionReasons.IllegalUnit);
        var unparsable = Assert.Single(submitted.Exceptions,
            entry => entry.ReasonCode == InstrumentExceptionReasons.UnparsableValue);
        var rawContentBefore = illegalUnit.RawContent;

        var accepted = await service.ResolveExceptionAsync(
            submitted.FileRegistrationId, illegalUnit.ExceptionId,
            new ResolveImportExceptionRequest(
                submitted.Version, InstrumentContract.RuleSetVersion,
                InstrumentResolutionKinds.AcceptWithMapping, "unit corrected against the method",
                new InstrumentRowMapping("SPEC-2", "POS-2", "TENSILE-STRENGTH", "NEWTON", null)),
            "corr-accept", TestContext.Current.CancellationToken);
        var completed = await service.ResolveExceptionAsync(
            accepted.FileRegistrationId, unparsable.ExceptionId,
            new ResolveImportExceptionRequest(
                accepted.Version, InstrumentContract.RuleSetVersion,
                InstrumentResolutionKinds.RejectRow, "instrument emitted an empty value"),
            "corr-reject", TestContext.Current.CancellationToken);

        var resolvedIllegalUnit = Assert.Single(completed.Exceptions,
            entry => entry.ExceptionId == illegalUnit.ExceptionId);
        Assert.Equal(rawContentBefore, resolvedIllegalUnit.RawContent);
        Assert.Equal(InstrumentExceptionStates.Resolved, resolvedIllegalUnit.State);
        Assert.Equal(InstrumentResolutionKinds.AcceptWithMapping, resolvedIllegalUnit.Resolution!.Kind);
        Assert.Equal("NEWTON", resolvedIllegalUnit.Resolution.CorrectedMapping!.Unit);
        Assert.Equal("operator-a", resolvedIllegalUnit.Resolution.ResolvedBy);
        var resolvedUnparsable = Assert.Single(completed.Exceptions,
            entry => entry.ExceptionId == unparsable.ExceptionId);
        Assert.Null(resolvedUnparsable.Resolution!.CorrectedMapping);
        Assert.Equal(InstrumentFileStates.Completed, completed.State);

        var duplicateResolution = await CaptureAsync(service.ResolveExceptionAsync(
            completed.FileRegistrationId, illegalUnit.ExceptionId,
            new ResolveImportExceptionRequest(
                completed.Version, InstrumentContract.RuleSetVersion,
                InstrumentResolutionKinds.RejectRow, "second attempt"),
            "corr-again", TestContext.Current.CancellationToken));

        Assert.Equal(InstrumentErrorCodes.ExceptionAlreadyResolved, duplicateResolution.Error!.ErrorCode);

        var status = await scope.ServiceProvider.GetRequiredService<IInstrumentImportPort>().EvaluateAsync(
            new InstrumentImportStatusRequest(
                "group-a", completed.FileRegistrationId, completed.Version, InstrumentContract.RuleSetVersion)
            {
                CorrelationId = "corr-allowed"
            }, TestContext.Current.CancellationToken);
        var staleStatus = await scope.ServiceProvider.GetRequiredService<IInstrumentImportPort>().EvaluateAsync(
            new InstrumentImportStatusRequest(
                "group-a", completed.FileRegistrationId, completed.Version + 5, InstrumentContract.RuleSetVersion)
            {
                CorrelationId = "corr-stale"
            }, TestContext.Current.CancellationToken);

        Assert.Equal(InstrumentStatusDecisions.Allowed, status.Decision);
        Assert.Equal(1, status.CompletedRowCount);
        Assert.Equal(InstrumentStatusDecisions.Unknown, staleStatus.Decision);
        Assert.Contains(InstrumentStatusReasons.VersionMismatch, staleStatus.ReasonCodes);
    }

    [Fact]
    public async Task Instrument_facts_reject_mutation_and_stale_versions()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInstrumentImportService>();
        var file = await service.RegisterFileAsync(Registration(), "corr-register", TestContext.Current.CancellationToken);
        await service.SubmitRowsAsync(
            file.FileRegistrationId,
            new SubmitInstrumentRowsRequest(file.Version, InstrumentContract.RuleSetVersion, [Row(1)]),
            "corr-rows", TestContext.Current.CancellationToken);

        var updateRegistration = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update instrument.file_registration set parser_version = 'PARSER-9'"));
        var deleteRow = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from instrument.parsed_row"));
        var updateRow = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update instrument.parsed_row set raw_value = 'tampered'"));
        var staleSubmission = await CaptureAsync(service.SubmitRowsAsync(
            file.FileRegistrationId,
            new SubmitInstrumentRowsRequest(file.Version, InstrumentContract.RuleSetVersion, [Row(2)]),
            "corr-stale", TestContext.Current.CancellationToken));

        Assert.Equal("55000", updateRegistration.SqlState);
        Assert.Equal("55000", deleteRow.SqlState);
        Assert.Equal("55000", updateRow.SqlState);
        Assert.Equal(InstrumentErrorCodes.ExpectedVersionConflict, staleSubmission.Error!.ErrorCode);
    }

    [Fact]
    public async Task Row_submissions_cannot_exceed_the_declared_row_count()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInstrumentImportService>();
        var file = await service.RegisterFileAsync(
            Registration() with { DeclaredRowCount = 2 }, "corr-register", TestContext.Current.CancellationToken);

        var overflow = await CaptureAsync(service.SubmitRowsAsync(
            file.FileRegistrationId,
            new SubmitInstrumentRowsRequest(file.Version, InstrumentContract.RuleSetVersion, [Row(1), Row(2), Row(3)]),
            "corr-overflow", TestContext.Current.CancellationToken));

        Assert.Equal(InstrumentErrorCodes.ValidationFailed, overflow.Error!.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from instrument.parsed_row"));
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from instrument.audit_attempt where outcome = 'INS.VALIDATION_FAILED'"));
    }

    [Fact]
    public async Task Resubmitting_a_row_number_the_exception_queue_holds_is_a_validation_failure()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInstrumentImportService>();
        var file = await service.RegisterFileAsync(Registration(), "corr-register", TestContext.Current.CancellationToken);
        var blocked = await service.SubmitRowsAsync(
            file.FileRegistrationId,
            new SubmitInstrumentRowsRequest(file.Version, InstrumentContract.RuleSetVersion,
                [Row(1) with { Unit = "newton force" }]),
            "corr-rows", TestContext.Current.CancellationToken);

        // The operator fixes the source file and resubmits the same row number.
        // The queue is keyed by row number, so this must fail validation rather
        // than attempt a second exception and surface as a version conflict.
        var resubmitted = await CaptureAsync(service.SubmitRowsAsync(
            blocked.FileRegistrationId,
            new SubmitInstrumentRowsRequest(blocked.Version, InstrumentContract.RuleSetVersion, [Row(1)]),
            "corr-resubmit", TestContext.Current.CancellationToken));
        var twoExceptionsForOneRow = await CaptureAsync(service.SubmitRowsAsync(
            blocked.FileRegistrationId,
            new SubmitInstrumentRowsRequest(blocked.Version, InstrumentContract.RuleSetVersion,
                [Row(2) with { SampleNumber = "unknown sample" }, Row(2)]),
            "corr-two-exceptions", TestContext.Current.CancellationToken));

        Assert.Equal(InstrumentErrorCodes.ValidationFailed, resubmitted.Error!.ErrorCode);
        Assert.Equal(InstrumentErrorCodes.ValidationFailed, twoExceptionsForOneRow.Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from instrument.import_exception"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from instrument.parsed_row"));
        Assert.Equal(2, await CountAsync(connectionString,
            "select count(*) from instrument.audit_attempt where outcome = 'INS.VALIDATION_FAILED'"));
        Assert.Equal(0, await CountAsync(connectionString,
            "select count(*) from instrument.audit_attempt where outcome = 'INS.EXPECTED_VERSION_CONFLICT'"));
    }

    [Fact]
    public async Task Concurrent_submissions_at_one_expected_version_admit_exactly_one_writer()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string fileId;
        long version;
        await using (var setup = BuildProvider(connectionString))
        {
            using var setupScope = setup.CreateScope();
            var registered = await setupScope.ServiceProvider.GetRequiredService<IInstrumentImportService>()
                .RegisterFileAsync(Registration(), "corr-setup", TestContext.Current.CancellationToken);
            fileId = registered.FileRegistrationId;
            version = registered.Version;
        }

        // Disjoint row numbers, so only the advisory lock plus the expected-version
        // check can produce a single winner — not the row-number unique index.
        await using var firstProvider = BuildProvider(connectionString, actorId: "operator-a");
        await using var secondProvider = BuildProvider(connectionString, actorId: "operator-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        var outcomes = await Task.WhenAll(
            CaptureAsync(firstScope.ServiceProvider.GetRequiredService<IInstrumentImportService>().SubmitRowsAsync(
                fileId, new SubmitInstrumentRowsRequest(version, InstrumentContract.RuleSetVersion, [Row(1)]),
                "corr-a", TestContext.Current.CancellationToken)),
            CaptureAsync(secondScope.ServiceProvider.GetRequiredService<IInstrumentImportService>().SubmitRowsAsync(
                fileId, new SubmitInstrumentRowsRequest(version, InstrumentContract.RuleSetVersion, [Row(2)]),
                "corr-b", TestContext.Current.CancellationToken)));

        Assert.Single(outcomes, outcome => outcome.Error is null);
        Assert.Equal(InstrumentErrorCodes.ExpectedVersionConflict,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from instrument.parsed_row"));
    }

    [Fact]
    public async Task Capability_denied_fails_closed_with_attempt_audit_only()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, permit: false);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<InstrumentDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<IInstrumentImportService>()
                .RegisterFileAsync(Registration(), "corr-denied", TestContext.Current.CancellationToken));

        Assert.Equal(InstrumentErrorCodes.NotAuthorized, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from instrument.file_registration"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.outbox"));
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from instrument.audit_attempt where correlation_id = 'corr-denied'"));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_back_instrument_facts(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString);
            using var scope = provider.CreateScope();

            var exception = await Assert.ThrowsAsync<InstrumentDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<IInstrumentImportService>()
                    .RegisterFileAsync(Registration(), $"corr-{failedWriter}", TestContext.Current.CancellationToken));

            Assert.Equal(InstrumentErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from instrument.file_registration"));
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "select count(*) from instrument.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
    }

    private static async Task<(object? Result, InstrumentDomainException? Error)> CaptureAsync<T>(Task<T> task)
    {
        try
        {
            return (await task, null);
        }
        catch (InstrumentDomainException exception)
        {
            return (null, exception);
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString, bool permit = true, string actorId = "operator-a")
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
        new InstrumentModule(connectionString).AddApiServices(services);
        services.RemoveAll<IInstrumentAuthorizationPort>();
        services.AddSingleton<IInstrumentAuthorizationPort>(new FixedAuthorizationPort(permit));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static RegisterInstrumentFileRequest Registration() => new(
        InstrumentContract.RuleSetVersion,
        new InstrumentObjectContext("LEGAL-A", "LAB-A"),
        new InstrumentVersionedReference("EXPORT-7", 1),
        Hash("export-7"),
        InstrumentSourceSystems.Instrument,
        new InstrumentVersionedReference("TENSILE-1", 2),
        "PARSER-1.4",
        5);

    private static InstrumentRowInput Row(int rowNumber) => new(
        rowNumber, $"SPEC-{rowNumber}", $"POS-{rowNumber}", "TENSILE-STRENGTH", "NEWTON", null, "83.4", "83.4");

    private static string Hash(string seed) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed)));

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for instrument integration tests.");

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
        await new InstrumentModule(connectionString).ApplyMigrationAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              instrument.audit_attempt,
              instrument.exception_resolution,
              instrument.import_exception,
              instrument.parsed_row,
              instrument.file_registration,
              platform.audit_intent,
              platform.outbox
            cascade;
            """);
    }

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_instrument_audit on platform.audit_intent;
                drop function if exists platform.fail_instrument_audit();
                create or replace function platform.fail_instrument_audit() returns trigger language plpgsql as $$
                begin
                  if new.action like '%INSTRUMENT%' then
                    raise exception 'forced instrument audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_instrument_audit before insert on platform.audit_intent
                for each row execute function platform.fail_instrument_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_instrument_outbox on platform.outbox;
                drop function if exists platform.fail_instrument_outbox();
                create or replace function platform.fail_instrument_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type like 'Instrument%' then
                    raise exception 'forced instrument outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_instrument_outbox before insert on platform.outbox
                for each row execute function platform.fail_instrument_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_instrument_audit on platform.audit_intent;
                drop function if exists platform.fail_instrument_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_instrument_outbox on platform.outbox;
                drop function if exists platform.fail_instrument_outbox();
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

    private sealed class FixedAuthorizationPort(bool allowed) : IInstrumentAuthorizationPort
    {
        public ValueTask<InstrumentAuthorizationDecision> AuthorizeAsync(
            InstrumentAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed
                ? InstrumentAuthorizationDecision.Permit
                : InstrumentAuthorizationDecision.Deny);
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
