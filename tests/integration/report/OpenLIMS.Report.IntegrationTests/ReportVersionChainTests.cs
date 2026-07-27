using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Instrument;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Qc;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Contracts.Report;
using OpenLIMS.Contracts.Result;
using OpenLIMS.Contracts.Scope;
using OpenLIMS.Modules.Report;
using Xunit;

namespace OpenLIMS.Report.IntegrationTests;

[Collection("report-postgres")]
[Trait("Profile", "report")]
public sealed class ReportVersionChainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_report_test";

    [Fact]
    public async Task Controlled_issuance_binds_the_signature_to_the_content_hash()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var report = await ReadyReportAsync(scope.ServiceProvider);
        var versions = scope.ServiceProvider.GetRequiredService<IReportVersionService>();

        var pending = await versions.GetPendingContentHashAsync(
            report.ReportId, "corr-hash", TestContext.Current.CancellationToken);
        var issued = await versions.IssueAsync(
            report.ReportId, Issue(report.Version, pending.ContentHash),
            "corr-issue", TestContext.Current.CancellationToken);

        Assert.Equal(1, pending.NextVersionNumber);
        Assert.Equal(1, issued.VersionNumber);
        Assert.Equal(ReportVersionStates.Issued, issued.State);
        Assert.Equal(pending.ContentHash, issued.Snapshot.ContentHash);
        Assert.Equal(pending.ContentHash, issued.Signature.ContentHash);
        Assert.Equal("I approve and sign this report", issued.Signature.SigningIntent);
        Assert.Equal("REAUTH-1", issued.Signature.ReauthenticationRef.Id);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from report.version_snapshot"));
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from report.version_signature"));
    }

    /// <summary>
    /// SEC-SIGN-002: once the signed content moves, the hash the signer agreed
    /// to no longer matches, so the signature cannot be completed.
    /// </summary>
    [Fact]
    public async Task A_hash_taken_before_the_content_changed_is_refused()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var versions = scope.ServiceProvider.GetRequiredService<IReportVersionService>();
        var report = await ReadyReportAsync(scope.ServiceProvider);
        var stale = await versions.GetPendingContentHashAsync(
            report.ReportId, "corr-hash", TestContext.Current.CancellationToken);

        // A second line changes what the report asserts, and therefore its hash.
        var extended = await reports.AddLineAsync(
            report.ReportId,
            Line(report.Version, 2) with { ResultGroupId = "GROUP-2", ScopeLineId = "SCOPE-LINE-2" },
            "corr-line-2", TestContext.Current.CancellationToken);
        var reEvaluated = await reports.EvaluateGateAsync(
            extended.ReportId,
            new EvaluateReportGateRequest(extended.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate-2", TestContext.Current.CancellationToken);
        var fresh = await versions.GetPendingContentHashAsync(
            reEvaluated.ReportId, "corr-hash-2", TestContext.Current.CancellationToken);

        var refused = await CaptureAsync(versions.IssueAsync(
            reEvaluated.ReportId, Issue(reEvaluated.Version, stale.ContentHash),
            "corr-stale", TestContext.Current.CancellationToken));
        var accepted = await versions.IssueAsync(
            reEvaluated.ReportId, Issue(reEvaluated.Version, fresh.ContentHash),
            "corr-fresh", TestContext.Current.CancellationToken);

        Assert.NotEqual(stale.ContentHash, fresh.ContentHash);
        Assert.Equal(ReportErrorCodes.ContentHashMismatch, refused.Error!.ErrorCode);
        Assert.Equal(fresh.ContentHash, accepted.Signature.ContentHash);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from report.version_signature"));
    }

    [Fact]
    public async Task Issuance_without_a_satisfied_gate_or_the_three_requirements_is_refused()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var versions = scope.ServiceProvider.GetRequiredService<IReportVersionService>();

        // A report with lines but no gate evaluation at all.
        var created = await reports.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        var withLine = await reports.AddLineAsync(
            created.ReportId, Line(created.Version, 1), "corr-line", TestContext.Current.CancellationToken);
        var pending = await versions.GetPendingContentHashAsync(
            withLine.ReportId, "corr-hash", TestContext.Current.CancellationToken);
        var noGate = await CaptureAsync(versions.IssueAsync(
            withLine.ReportId, Issue(withLine.Version, pending.ContentHash),
            "corr-nogate", TestContext.Current.CancellationToken));

        var ready = await reports.EvaluateGateAsync(
            withLine.ReportId,
            new EvaluateReportGateRequest(withLine.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);
        var freshHash = await versions.GetPendingContentHashAsync(
            ready.ReportId, "corr-hash-2", TestContext.Current.CancellationToken);
        var noIntent = await CaptureAsync(versions.IssueAsync(
            ready.ReportId, Issue(ready.Version, freshHash.ContentHash) with { SigningIntent = "  " },
            "corr-nointent", TestContext.Current.CancellationToken));

        Assert.Equal(ReportErrorCodes.IssuanceGateNotSatisfied, noGate.Error!.ErrorCode);
        Assert.Equal(ReportErrorCodes.SignatureRequirementsUnmet, noIntent.Error!.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from report.version_signature"));
    }

    /// <summary>
    /// AC-RPT-002 end to end: V1 issued, corrected into V2, and an old
    /// reference still returns V1 with its own content and history.
    /// </summary>
    [Fact]
    public async Task Correction_produces_v2_while_v1_stays_retrievable_and_unchanged()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var versions = scope.ServiceProvider.GetRequiredService<IReportVersionService>();
        var report = await ReadyReportAsync(scope.ServiceProvider);
        var v1Hash = (await versions.GetPendingContentHashAsync(
            report.ReportId, "corr-hash", TestContext.Current.CancellationToken)).ContentHash;
        var v1 = await versions.IssueAsync(
            report.ReportId, Issue(report.Version, v1Hash), "corr-issue", TestContext.Current.CancellationToken);

        // The sample description needs correcting, so a new line supersedes the
        // old picture and V1 is corrected rather than edited.
        var current = await reports.GetAsync(report.ReportId, "corr-read", TestContext.Current.CancellationToken);
        var missingAssessment = await CaptureAsync(versions.PerformControlledActionAsync(
            current.ReportId, Correction(current.Version) with { ImpactAssessmentRef = null },
            "corr-noassess", TestContext.Current.CancellationToken));
        var corrected = await versions.PerformControlledActionAsync(
            current.ReportId, Correction(current.Version), "corr-correct", TestContext.Current.CancellationToken);

        var afterCorrection = await reports.GetAsync(report.ReportId, "corr-read-2", TestContext.Current.CancellationToken);
        var amended = await reports.AddLineAsync(
            afterCorrection.ReportId,
            Line(afterCorrection.Version, 2) with { ResultGroupId = "GROUP-2", ScopeLineId = "SCOPE-LINE-2" },
            "corr-line-2", TestContext.Current.CancellationToken);
        var reGated = await reports.EvaluateGateAsync(
            amended.ReportId,
            new EvaluateReportGateRequest(amended.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate-2", TestContext.Current.CancellationToken);
        var v2Hash = (await versions.GetPendingContentHashAsync(
            reGated.ReportId, "corr-hash-2", TestContext.Current.CancellationToken)).ContentHash;
        var v2 = await versions.IssueAsync(
            reGated.ReportId, Issue(reGated.Version, v2Hash), "corr-issue-2", TestContext.Current.CancellationToken);

        var fetchedV1 = await versions.GetVersionAsync(
            report.ReportId, 1, "corr-v1", TestContext.Current.CancellationToken);
        var fetchedV2 = await versions.GetVersionAsync(
            report.ReportId, 2, "corr-v2", TestContext.Current.CancellationToken);
        var verification = await versions.GetVerificationAsync(
            report.ReportId, "corr-verify", TestContext.Current.CancellationToken);

        Assert.Equal(ReportErrorCodes.ImpactAssessmentRequired, missingAssessment.Error!.ErrorCode);
        Assert.Equal(2, v2.VersionNumber);
        Assert.NotEqual(v1Hash, v2Hash);

        // RPT-VERS-004: the old reference still yields V1's own content.
        Assert.Equal(v1Hash, fetchedV1.Snapshot.ContentHash);
        Assert.Equal(v1.Snapshot.CanonicalContent, fetchedV1.Snapshot.CanonicalContent);
        Assert.Equal(ReportVersionStates.Superseded, fetchedV1.State);
        Assert.Equal(v2Hash, fetchedV2.Snapshot.ContentHash);
        Assert.Equal(ReportVersionStates.Issued, fetchedV2.State);

        // RPT-VERS-003: the verification surface shows both and their relation.
        Assert.Equal(2, verification.CurrentVersionNumber);
        Assert.Equal(ReportChainStates.Active, verification.ChainState);
        Assert.Equal(2, verification.Versions.Count);
        var historical = Assert.Single(verification.Versions, entry => entry.VersionNumber == 1);
        Assert.Equal(ReportVersionStates.Superseded, historical.State);
        Assert.Equal(2, historical.SupersededBy);
        // Between the correction and V2's issuance there is deliberately no
        // current version: V1 is superseded and V2 does not exist yet.
        Assert.Null(corrected.CurrentVersionNumber);
        Assert.Equal(ReportVersionStates.Superseded, Assert.Single(corrected.Versions).State);
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from report.controlled_action where kind = 'CORRECTION'"));
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from report.controlled_action where impact_assessment_ref = 'IMPACT-1'"));
    }

    [Fact]
    public async Task Withdrawal_keeps_the_version_retrievable_and_cannot_repeat()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var versions = scope.ServiceProvider.GetRequiredService<IReportVersionService>();
        var report = await ReadyReportAsync(scope.ServiceProvider);
        var hash = (await versions.GetPendingContentHashAsync(
            report.ReportId, "corr-hash", TestContext.Current.CancellationToken)).ContentHash;
        await versions.IssueAsync(
            report.ReportId, Issue(report.Version, hash), "corr-issue", TestContext.Current.CancellationToken);

        var current = await reports.GetAsync(report.ReportId, "corr-read", TestContext.Current.CancellationToken);
        var withdrawn = await versions.PerformControlledActionAsync(
            current.ReportId,
            new PerformControlledActionRequest(
                current.Version, ReportContract.RuleSetVersion, 1,
                ReportControlledActionKinds.Withdrawal, "superseded by a client instruction"),
            "corr-withdraw", TestContext.Current.CancellationToken);
        var again = await CaptureAsync(versions.PerformControlledActionAsync(
            current.ReportId,
            new PerformControlledActionRequest(
                current.Version, ReportContract.RuleSetVersion, 1,
                ReportControlledActionKinds.Withdrawal, "second attempt"),
            "corr-withdraw-2", TestContext.Current.CancellationToken));
        var fetched = await versions.GetVersionAsync(
            report.ReportId, 1, "corr-v1", TestContext.Current.CancellationToken);

        Assert.Null(withdrawn.CurrentVersionNumber);
        Assert.Equal(ReportVersionStates.Withdrawn, Assert.Single(withdrawn.Versions).State);
        Assert.Equal(ReportErrorCodes.ValidationFailed, again.Error!.ErrorCode);
        // RULE-011: withdrawal stops reliance, it does not erase the record.
        Assert.Equal(hash, fetched.Snapshot.ContentHash);
        Assert.Equal(ReportVersionStates.Withdrawn, fetched.State);
    }

    [Fact]
    public async Task Voiding_closes_the_chain_to_every_further_action()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var versions = scope.ServiceProvider.GetRequiredService<IReportVersionService>();
        var report = await ReadyReportAsync(scope.ServiceProvider);
        var hash = (await versions.GetPendingContentHashAsync(
            report.ReportId, "corr-hash", TestContext.Current.CancellationToken)).ContentHash;
        await versions.IssueAsync(
            report.ReportId, Issue(report.Version, hash), "corr-issue", TestContext.Current.CancellationToken);
        var current = await reports.GetAsync(report.ReportId, "corr-read", TestContext.Current.CancellationToken);

        var voided = await versions.PerformControlledActionAsync(
            current.ReportId,
            new PerformControlledActionRequest(
                current.Version, ReportContract.RuleSetVersion, 1,
                ReportControlledActionKinds.Void, "issued against a cancelled order"),
            "corr-void", TestContext.Current.CancellationToken);
        var afterVoid = await reports.GetAsync(report.ReportId, "corr-read-2", TestContext.Current.CancellationToken);
        var furtherAction = await CaptureAsync(versions.PerformControlledActionAsync(
            afterVoid.ReportId,
            new PerformControlledActionRequest(
                afterVoid.Version, ReportContract.RuleSetVersion, 1,
                ReportControlledActionKinds.Withdrawal, "too late"),
            "corr-after-void", TestContext.Current.CancellationToken));

        Assert.Equal(ReportChainStates.Voided, voided.ChainState);
        Assert.Null(voided.CurrentVersionNumber);
        Assert.Equal(ReportVersionStates.Voided, Assert.Single(voided.Versions).State);
        Assert.Equal(ReportErrorCodes.VersionChainClosed, furtherAction.Error!.ErrorCode);
    }

    [Fact]
    public async Task Supersession_records_the_new_report_number()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var versions = scope.ServiceProvider.GetRequiredService<IReportVersionService>();
        var report = await ReadyReportAsync(scope.ServiceProvider);
        var hash = (await versions.GetPendingContentHashAsync(
            report.ReportId, "corr-hash", TestContext.Current.CancellationToken)).ContentHash;
        await versions.IssueAsync(
            report.ReportId, Issue(report.Version, hash), "corr-issue", TestContext.Current.CancellationToken);
        var current = await reports.GetAsync(report.ReportId, "corr-read", TestContext.Current.CancellationToken);

        var superseded = await versions.PerformControlledActionAsync(
            current.ReportId,
            new PerformControlledActionRequest(
                current.Version, ReportContract.RuleSetVersion, 1,
                ReportControlledActionKinds.Supersession, "scope changed materially",
                SupersedingReportNumber: "RPT-2026-0002"),
            "corr-supersede", TestContext.Current.CancellationToken);

        // BUS-RPT-005: a retry after a lost response must not append a second,
        // permanently uncorrectable supersession fact.
        var retry = await CaptureAsync(versions.PerformControlledActionAsync(
            current.ReportId,
            new PerformControlledActionRequest(
                current.Version, ReportContract.RuleSetVersion, 1,
                ReportControlledActionKinds.Supersession, "scope changed materially",
                SupersedingReportNumber: "RPT-2026-0002"),
            "corr-supersede-retry", TestContext.Current.CancellationToken));
        var other = await CaptureAsync(versions.PerformControlledActionAsync(
            current.ReportId,
            new PerformControlledActionRequest(
                current.Version, ReportContract.RuleSetVersion, 1,
                ReportControlledActionKinds.Supersession, "replaced again",
                SupersedingReportNumber: "RPT-2026-0009"),
            "corr-supersede-again", TestContext.Current.CancellationToken));
        var verification = await versions.GetVerificationAsync(
            current.ReportId, "corr-verify", TestContext.Current.CancellationToken);

        Assert.Equal("RPT-2026-0002", superseded.SupersedingReportNumber);
        Assert.Equal(ReportChainStates.Active, superseded.ChainState);
        Assert.Equal(ReportErrorCodes.ValidationFailed, retry.Error!.ErrorCode);
        Assert.Equal(ReportErrorCodes.ValidationFailed, other.Error!.ErrorCode);
        Assert.Equal("RPT-2026-0002", verification.SupersedingReportNumber);
        Assert.Equal(1, await CountAsync(
            connectionString,
            "select count(*) from report.controlled_action where kind = 'SUPERSESSION';"));
    }

    [Fact]
    public async Task A_blank_superseding_number_fails_validation_rather_than_the_check_constraint()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var versions = scope.ServiceProvider.GetRequiredService<IReportVersionService>();
        var report = await ReadyReportAsync(scope.ServiceProvider);
        var hash = (await versions.GetPendingContentHashAsync(
            report.ReportId, "corr-hash", TestContext.Current.CancellationToken)).ContentHash;
        await versions.IssueAsync(
            report.ReportId, Issue(report.Version, hash), "corr-issue", TestContext.Current.CancellationToken);
        var current = await reports.GetAsync(report.ReportId, "corr-read", TestContext.Current.CancellationToken);

        var blank = await CaptureAsync(versions.PerformControlledActionAsync(
            current.ReportId,
            new PerformControlledActionRequest(
                current.Version, ReportContract.RuleSetVersion, 1,
                ReportControlledActionKinds.Withdrawal, "customer asked",
                SupersedingReportNumber: "  "),
            "corr-blank", TestContext.Current.CancellationToken));

        // A caller mistake is a 400, not the 503 a CHECK violation would raise.
        Assert.Equal(ReportErrorCodes.ValidationFailed, blank.Error!.ErrorCode);
        Assert.Equal(0, await CountAsync(
            connectionString, "select count(*) from report.controlled_action;"));
    }

    [Fact]
    public async Task Version_facts_reject_mutation_and_the_chain_port_pins_the_current_version()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var versions = scope.ServiceProvider.GetRequiredService<IReportVersionService>();
        var report = await ReadyReportAsync(scope.ServiceProvider);
        var hash = (await versions.GetPendingContentHashAsync(
            report.ReportId, "corr-hash", TestContext.Current.CancellationToken)).ContentHash;
        await versions.IssueAsync(
            report.ReportId, Issue(report.Version, hash), "corr-issue", TestContext.Current.CancellationToken);

        var updateSnapshot = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update report.version_snapshot set content_hash = repeat('b', 64)"));
        var deleteSignature = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from report.version_signature"));
        var updateSignature = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update report.version_signature set signatory_id = 'someone-else'"));

        var port = scope.ServiceProvider.GetRequiredService<IReportVersionChainPort>();
        var allowed = await port.EvaluateAsync(new ReportVersionChainRequest(
            "group-a", report.ReportId, 1, ReportContract.RuleSetVersion)
        {
            CorrelationId = "corr-chain"
        }, TestContext.Current.CancellationToken);
        var stale = await port.EvaluateAsync(new ReportVersionChainRequest(
            "group-a", report.ReportId, 7, ReportContract.RuleSetVersion)
        {
            CorrelationId = "corr-chain-stale"
        }, TestContext.Current.CancellationToken);

        Assert.Equal("55000", updateSnapshot.SqlState);
        Assert.Equal("55000", deleteSignature.SqlState);
        Assert.Equal("55000", updateSignature.SqlState);
        Assert.Equal(ReportVersionChainDecisions.Allowed, allowed.Decision);
        Assert.Equal(1, allowed.CurrentVersionNumber);
        Assert.Equal(hash, allowed.ContentHash);
        Assert.Equal(ReportVersionChainDecisions.Unknown, stale.Decision);
        Assert.Contains(ReportVersionChainReasons.VersionMismatch, stale.ReasonCodes);
    }

    [Fact]
    public async Task Issuance_writes_platform_evidence_and_rolls_back_when_it_fails()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var versions = scope.ServiceProvider.GetRequiredService<IReportVersionService>();
        var report = await ReadyReportAsync(scope.ServiceProvider);
        var hash = (await versions.GetPendingContentHashAsync(
            report.ReportId, "corr-hash", TestContext.Current.CancellationToken)).ContentHash;
        var auditBefore = await CountAsync(connectionString, "select count(*) from platform.audit_intent");

        await ExecuteAsync(connectionString, """
            create or replace function platform.fail_report_issue() returns trigger language plpgsql as $$
            begin
              if new.action = 'ISSUE_REPORT_VERSION' then
                raise exception 'forced issuance audit failure';
              end if;
              return new;
            end;
            $$;
            create trigger trg_fail_report_issue before insert on platform.audit_intent
            for each row execute function platform.fail_report_issue();
            """);
        try
        {
            var failed = await CaptureAsync(versions.IssueAsync(
                report.ReportId, Issue(report.Version, hash), "corr-fail", TestContext.Current.CancellationToken));

            Assert.Equal(ReportErrorCodes.PersistenceUnavailable, failed.Error!.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from report.version_snapshot"));
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from report.version_signature"));
            Assert.Equal(auditBefore, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
            Assert.Equal(1, await CountAsync(connectionString,
                "select count(*) from report.audit_attempt where correlation_id = 'corr-fail'"));
        }
        finally
        {
            await ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_report_issue on platform.audit_intent;
                drop function if exists platform.fail_report_issue();
                """);
        }
    }

    private static async Task<ReportResult> ReadyReportAsync(IServiceProvider services)
    {
        var reports = services.GetRequiredService<IReportService>();
        var created = await reports.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        var withLine = await reports.AddLineAsync(
            created.ReportId, Line(created.Version, 1), "corr-line", TestContext.Current.CancellationToken);
        return await reports.EvaluateGateAsync(
            withLine.ReportId,
            new EvaluateReportGateRequest(withLine.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);
    }

    private static async Task<(object? Result, ReportDomainException? Error)> CaptureAsync<T>(Task<T> task)
    {
        try
        {
            return (await task, null);
        }
        catch (ReportDomainException exception)
        {
            return (null, exception);
        }
    }

    private static IssueReportRequest Issue(long expectedVersion, string contentHash) => new(
        expectedVersion, ReportContract.RuleSetVersion, new ReportVersionedReference("REAUTH-1", 1),
        "I approve and sign this report", contentHash, "signatory-a");

    private static PerformControlledActionRequest Correction(long expectedVersion) => new(
        expectedVersion, ReportContract.RuleSetVersion, 1, ReportControlledActionKinds.Correction,
        "sample description corrected",
        ImpactAssessmentRef: new ReportVersionedReference("IMPACT-1", 1));

    private static CreateReportRequest Report() => new(
        ReportContract.RuleSetVersion,
        new ReportObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        "RPT-2026-0001");

    private static AddReportLineRequest Line(long expectedVersion, int lineNumber) => new(
        expectedVersion, ReportContract.RuleSetVersion, lineNumber, "GROUP-1", 4, "SCOPE-LINE-1",
        ReportScopePartitions.ActualTested,
        new ReportTraceReferences("BATCH-1", "ALLOC-1", "ITEM-1", new ReportVersionedReference("REQ-SNAPSHOT-1", 1)),
        new AccreditationScopeReference("ACC-CNAS-1", 2, new string('a', 64)),
        new AccreditationClaim(
            "CNAS-L1234-S1", new ReportVersionedReference("METHOD-TENSILE", 3), "RIGID-PLASTIC", "0-500N",
            Now.AddYears(1), "signatory-a"),
        [new ReportVersionedReference("QC-RUN-1", 6)], "INST-FILE-1", 3, 2, "SCOPE-MATRIX-1", 2, 3, 4);

    private static ServiceProvider BuildProvider(string connectionString, string actorId = "operator-a")
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
        new ReportModule(connectionString).AddApiServices(services);
        services.RemoveAll<IReportAuthorizationPort>();
        services.AddSingleton<IReportAuthorizationPort>(new PermitAuthorizationPort());
        services.RemoveAll<IResultAdoptionPort>();
        services.AddSingleton<IResultAdoptionPort>(new AllowedResultAdoptionPort());
        services.RemoveAll<IQcReportabilityPort>();
        services.AddSingleton<IQcReportabilityPort>(new AllowedQcPort());
        services.RemoveAll<IReceivingEligibilityPortV2>();
        services.AddSingleton<IReceivingEligibilityPortV2>(new AllowedReceivingPort());
        services.RemoveAll<IScopeProductionEligibilityPort>();
        services.AddSingleton<IScopeProductionEligibilityPort>(new AllowedScopePort());
        services.RemoveAll<IAllocationStatusPort>();
        services.AddSingleton<IAllocationStatusPort>(new AllowedAllocationPort());
        services.RemoveAll<IBatchStatusPort>();
        services.AddSingleton<IBatchStatusPort>(new AllowedBatchPort());
        services.RemoveAll<IInstrumentImportPort>();
        services.AddSingleton<IInstrumentImportPort>(new AllowedInstrumentPort());
        services.RemoveAll<IAccreditationScopePort>();
        services.AddSingleton<IAccreditationScopePort>(new MatchingAccreditationScopePort());
        services.RemoveAll<ISignatoryAuthorityPort>();
        services.AddSingleton<ISignatoryAuthorityPort>(new AuthorizedSignatoryPort());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for report integration tests.");

    private static string ConnectionString() => new NpgsqlConnectionStringBuilder(AdminConnectionString())
    {
        Database = DedicatedDatabaseName
    }.ConnectionString;

    private static async Task PrepareAsync(string connectionString)
    {
        await using (var dataSource = NpgsqlDataSource.Create(AdminConnectionString()))
        {
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
        }

        await PlatformMigrationRunner.ApplyAsync(connectionString, TestContext.Current.CancellationToken);
        await new ReportModule(connectionString).ApplyMigrationAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              report.audit_attempt,
              report.controlled_action,
              report.version_signature,
              report.version_snapshot,
              report.approval_submission,
              report.accreditation_verdict,
              report.gate_blocker,
              report.gate_evaluation,
              report.report_line_qc_run,
              report.report_line,
              report.report,
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

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed class PermitAuthorizationPort : IReportAuthorizationPort
    {
        public ValueTask<ReportAuthorizationDecision> AuthorizeAsync(
            ReportAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReportAuthorizationDecision.Permit);
    }

    private sealed class AllowedResultAdoptionPort : IResultAdoptionPort
    {
        public ValueTask<ResultAdoptionStatusResult> EvaluateAsync(
            ResultAdoptionStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ResultAdoptionStatusResult(
                ResultAdoptionDecisions.Allowed, [], request.ResultGroupId, request.ExpectedGroupVersion,
                "adopted-target-1", 1, ResultContract.RuleSetVersion));
    }

    private sealed class AllowedQcPort : IQcReportabilityPort
    {
        public ValueTask<QcReportabilityResult> EvaluateAsync(
            QcReportabilityRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new QcReportabilityResult(
                QcReportabilityDecisions.Allowed, [], request.QcRunId, request.TargetId,
                request.ExpectedRunVersion, [], QcContract.RuleSetVersion));
    }

    private sealed class AllowedReceivingPort : IReceivingEligibilityPortV2
    {
        public ValueTask<ReceivingEligibilityV2Result> EvaluateAsync(
            ReceivingEligibilityV2Request request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReceivingEligibilityV2Result(
                "ALLOWED", "RELEASED", "MATCHED", "identity-1", "release-1", [],
                request.ExpectedItemVersion, 1, request.RuleSetVersion,
                [request.RequestedAction], [], Now.AddDays(30)));
    }

    private sealed class AllowedScopePort : IScopeProductionEligibilityPort
    {
        public ValueTask<ScopeProductionEligibilityResult> EvaluateAsync(
            ScopeProductionEligibilityRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ScopeProductionEligibilityResult(
                ScopeEligibilityDecisions.Allowed, [], request.ScopeMatrixId,
                request.ExpectedMatrixVersion, request.RuleSetVersion));
    }

    private sealed class AllowedAllocationPort : IAllocationStatusPort
    {
        public ValueTask<AllocationStatusResult> EvaluateAsync(
            AllocationStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AllocationStatusResult(
                AllocationStatusDecisions.Allowed, [], request.AllocationId, "RESERVED",
                request.ExpectedSubjectAllocationVersion, request.RuleSetVersion));
    }

    private sealed class AllowedBatchPort : IBatchStatusPort
    {
        public ValueTask<BatchStatusResult> EvaluateAsync(
            BatchStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new BatchStatusResult(
                BatchStatusDecisions.Allowed, [], request.BatchId, BatchStates.Active,
                request.ExpectedBatchVersion, BatchContract.RuleSetVersion));
    }

    private sealed class AllowedInstrumentPort : IInstrumentImportPort
    {
        public ValueTask<InstrumentImportStatusResult> EvaluateAsync(
            InstrumentImportStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new InstrumentImportStatusResult(
                InstrumentStatusDecisions.Allowed, [], request.FileRegistrationId,
                request.ExpectedFileVersion, 5, 0, InstrumentContract.RuleSetVersion));
    }

    private sealed class MatchingAccreditationScopePort : IAccreditationScopePort
    {
        public ValueTask<AccreditationScopeLookupResult?> ResolveAsync(
            AccreditationScopeLookupRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AccreditationScopeLookupResult?>(new AccreditationScopeLookupResult(
                "CNAS-L1234-S1", new ReportVersionedReference("METHOD-TENSILE", 3), "RIGID-PLASTIC", "0-500N",
                Now.AddYears(1), ["signatory-a"]));
    }

    private sealed class AuthorizedSignatoryPort : ISignatoryAuthorityPort
    {
        public ValueTask<SignatoryAuthorityDecision> EvaluateAsync(
            SignatoryAuthorityRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SignatoryAuthorityDecision(true, []));
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
