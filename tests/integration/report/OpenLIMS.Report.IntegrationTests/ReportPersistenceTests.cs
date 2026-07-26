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

[CollectionDefinition("report-postgres", DisableParallelization = true)]
public sealed class ReportPostgresCollection;

[Collection("report-postgres")]
[Trait("Profile", "report")]
public sealed class ReportPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
    private const string DedicatedDatabaseName = "openlims_report_test";
    private static bool _databaseEnsured;

    [Fact]
    public async Task Report_and_lines_persist_with_the_adoption_and_chain_pinned()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();

        var report = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        var withLine = await service.AddLineAsync(
            report.ReportId, Line(report.Version, 1), "corr-line", TestContext.Current.CancellationToken);

        Assert.Equal(ReportStates.Draft, report.State);
        var line = Assert.Single(withLine.Lines);
        Assert.Equal("adopted-target-1", line.AdoptionTargetId);
        Assert.Equal(ResultContract.RuleSetVersion, line.AdoptionRuleSetVersion);
        Assert.Equal("BATCH-1", line.TraceRefs.BatchId);
        var citedRun = Assert.Single(line.GateRefs.QcRuns);
        Assert.Equal("QC-RUN-1", citedRun.Id);
        Assert.Equal(6, citedRun.Version);
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
        Assert.Equal(2, await CountAsync(connectionString, "select count(*) from platform.outbox"));
    }

    [Fact]
    public async Task Blocked_adoption_prevents_the_line_from_existing_at_all()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, adoptionDecision: ResultAdoptionDecisions.Blocked);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var report = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);

        var blocked = await CaptureAsync(service.AddLineAsync(
            report.ReportId, Line(report.Version, 1), "corr-blocked", TestContext.Current.CancellationToken));

        Assert.Equal(ReportErrorCodes.EligibilityBlocked, blocked.Error!.ErrorCode);
        Assert.Equal(ReportGateSources.ResultAdoption, blocked.Error.GateSource);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from report.report_line"));
    }

    [Fact]
    public async Task Duplicate_attribution_and_reused_line_numbers_are_rejected()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var report = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        var withLine = await service.AddLineAsync(
            report.ReportId, Line(report.Version, 1), "corr-line", TestContext.Current.CancellationToken);

        var duplicate = await CaptureAsync(service.AddLineAsync(
            withLine.ReportId, Line(withLine.Version, 2), "corr-dup", TestContext.Current.CancellationToken));
        var reusedNumber = await CaptureAsync(service.AddLineAsync(
            withLine.ReportId,
            Line(withLine.Version, 1) with { ResultGroupId = "GROUP-2", ScopeLineId = "SCOPE-LINE-2" },
            "corr-reused", TestContext.Current.CancellationToken));

        Assert.Equal(ReportErrorCodes.DuplicateAttribution, duplicate.Error!.ErrorCode);
        Assert.Equal(ReportErrorCodes.ValidationFailed, reusedNumber.Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from report.report_line"));
    }

    /// <summary>
    /// AC-RPT-001: three different problems on three different lines must come
    /// back as three separate blockers, each naming its object, rule version and
    /// next step.
    /// </summary>
    [Fact]
    public async Task Three_distinct_problems_return_three_itemised_blockers()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(
            connectionString,
            receivingDecision: "BLOCKED",
            qcDecision: QcReportabilityDecisions.Blocked,
            signatoryAuthorized: false);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var current = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        current = await service.AddLineAsync(
            current.ReportId, Line(current.Version, 1), "corr-line", TestContext.Current.CancellationToken);

        var evaluated = await service.EvaluateGateAsync(
            current.ReportId,
            new EvaluateReportGateRequest(current.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);
        var evaluation = Assert.Single(evaluated.GateEvaluations);

        Assert.Equal(ReportGateDecisions.Blocked, evaluation.Decision);
        var receiving = Assert.Single(evaluation.Blockers, b => b.Source == ReportGateSources.ReceivingEligibility);
        var qc = Assert.Single(evaluation.Blockers, b => b.Source == ReportGateSources.QcReportability);
        var accreditation = Assert.Single(evaluation.Blockers, b => b.Source == ReportGateSources.Accreditation);
        Assert.Equal("ITEM-1", receiving.ObjectRef);
        Assert.Equal(ReceivingEligibilityV2Contract.RuleSetVersion, receiving.RuleSetVersion);
        Assert.Equal(ReportNextSteps.ResolveIdentityConflict, Assert.Single(receiving.AllowedNextSteps));
        Assert.Equal("QC-RUN-1", qc.ObjectRef);
        Assert.Equal(ReportNextSteps.ReleaseQcBlock, Assert.Single(qc.AllowedNextSteps));
        Assert.Equal(ReportNextSteps.AssignAuthorizedSignatory, Assert.Single(accreditation.AllowedNextSteps));
        Assert.All(evaluation.Blockers, blocker => Assert.Equal(1, blocker.LineNumber));

        var submitted = await CaptureAsync(service.SubmitForApprovalAsync(
            evaluated.ReportId,
            new SubmitReportForApprovalRequest(evaluated.Version, ReportContract.RuleSetVersion),
            "corr-submit", TestContext.Current.CancellationToken));

        Assert.Equal(ReportErrorCodes.AccreditationBlocked, submitted.Error!.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from report.approval_submission"));
    }

    /// <summary>
    /// AC-ACC-001: one line inside the accredited scope, one outside, judged
    /// independently — and an organisation-level flag has no way in.
    /// </summary>
    [Fact]
    public async Task Mixed_scope_report_judges_each_line_independently()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var current = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        current = await service.AddLineAsync(
            current.ReportId, Line(current.Version, 1), "corr-line-1", TestContext.Current.CancellationToken);
        // The second line names a method the accredited scope does not cover.
        current = await service.AddLineAsync(
            current.ReportId,
            Line(current.Version, 2) with
            {
                ResultGroupId = "GROUP-2",
                ScopeLineId = "SCOPE-LINE-2",
                AccreditationClaim = Claim() with { Method = new ReportVersionedReference("METHOD-FLAMMABILITY", 1) }
            },
            "corr-line-2", TestContext.Current.CancellationToken);

        var evaluated = await service.EvaluateGateAsync(
            current.ReportId,
            new EvaluateReportGateRequest(current.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);
        var evaluation = Assert.Single(evaluated.GateEvaluations);

        var first = Assert.Single(evaluation.AccreditationVerdicts, v => v.LineNumber == 1);
        var second = Assert.Single(evaluation.AccreditationVerdicts, v => v.LineNumber == 2);
        Assert.Equal(ReportAccreditationStatuses.Accredited, first.Status);
        Assert.Empty(first.FailedDimensions);
        Assert.Equal(ReportAccreditationStatuses.NotAccredited, second.Status);
        Assert.Contains(ReportAccreditationDimensions.MethodVersion, second.FailedDimensions);
        var blocker = Assert.Single(evaluation.Blockers, b => b.Source == ReportGateSources.Accreditation);
        Assert.Equal(2, blocker.LineNumber);
        Assert.Equal(ReportGateDecisions.Blocked, evaluation.Decision);
    }

    [Fact]
    public async Task A_clean_report_passes_the_gate_and_reaches_pending_approval()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var current = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        current = await service.AddLineAsync(
            current.ReportId, Line(current.Version, 1), "corr-line", TestContext.Current.CancellationToken);
        var evaluated = await service.EvaluateGateAsync(
            current.ReportId,
            new EvaluateReportGateRequest(current.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);

        Assert.Equal(ReportGateDecisions.Allowed, Assert.Single(evaluated.GateEvaluations).Decision);
        Assert.Empty(Assert.Single(evaluated.GateEvaluations).Blockers);

        var submitted = await service.SubmitForApprovalAsync(
            evaluated.ReportId,
            new SubmitReportForApprovalRequest(evaluated.Version, ReportContract.RuleSetVersion),
            "corr-submit", TestContext.Current.CancellationToken);

        Assert.Equal(ReportStates.PendingApproval, submitted.State);

        var gate = await scope.ServiceProvider.GetRequiredService<IReportIssuanceGatePort>().EvaluateAsync(
            new ReportIssuanceGateRequest(
                "group-a", submitted.ReportId, submitted.Version, ReportContract.RuleSetVersion)
            {
                CorrelationId = "corr-port"
            }, TestContext.Current.CancellationToken);

        Assert.Equal(ReportGateDecisions.Allowed, gate.Decision);
        Assert.Empty(gate.Blockers);
    }

    [Fact]
    public async Task A_laboratory_conclusion_line_blocks_pending_od_034()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var current = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        current = await service.AddLineAsync(
            current.ReportId,
            Line(current.Version, 1) with { ScopePartition = ReportScopePartitions.LaboratoryConclusion },
            "corr-line", TestContext.Current.CancellationToken);

        var evaluated = await service.EvaluateGateAsync(
            current.ReportId,
            new EvaluateReportGateRequest(current.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);
        var evaluation = Assert.Single(evaluated.GateEvaluations);
        var submitted = await CaptureAsync(service.SubmitForApprovalAsync(
            evaluated.ReportId,
            new SubmitReportForApprovalRequest(evaluated.Version, ReportContract.RuleSetVersion),
            "corr-submit", TestContext.Current.CancellationToken));

        Assert.Equal(ReportBlockerReasons.ConformityDecisionUnavailable,
            Assert.Single(evaluation.Blockers, b => b.Source == ReportGateSources.ConformityDecision).ReasonCode);
        Assert.Equal(ReportErrorCodes.ConformityDecisionUnavailable, submitted.Error!.ErrorCode);
    }

    [Fact]
    public async Task An_unknown_source_makes_the_gate_unknown_and_blocks_submission()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, batchDecision: BatchStatusDecisions.Unknown);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var current = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        current = await service.AddLineAsync(
            current.ReportId, Line(current.Version, 1), "corr-line", TestContext.Current.CancellationToken);

        var evaluated = await service.EvaluateGateAsync(
            current.ReportId,
            new EvaluateReportGateRequest(current.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);
        var evaluation = Assert.Single(evaluated.GateEvaluations);
        var submitted = await CaptureAsync(service.SubmitForApprovalAsync(
            evaluated.ReportId,
            new SubmitReportForApprovalRequest(evaluated.Version, ReportContract.RuleSetVersion),
            "corr-submit", TestContext.Current.CancellationToken));

        Assert.Equal(ReportGateDecisions.Unknown, evaluation.Decision);
        var blocker = Assert.Single(evaluation.Blockers, b => b.Source == ReportGateSources.BatchStatus);
        Assert.Equal(ReportBlockerReasons.SourceUnknown, blocker.ReasonCode);
        Assert.Contains(ReportNextSteps.RetryWhenSourceAvailable, blocker.AllowedNextSteps);
        Assert.Equal(ReportErrorCodes.ApplicabilityUnknown, submitted.Error!.ErrorCode);
    }

    [Fact]
    public async Task Gate_re_consults_the_adoption_port_and_blocks_a_superseded_target()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string reportId;
        long version;
        await using (var setup = BuildProvider(connectionString))
        {
            using var setupScope = setup.CreateScope();
            var service = setupScope.ServiceProvider.GetRequiredService<IReportService>();
            var created = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
            var withLine = await service.AddLineAsync(
                created.ReportId, Line(created.Version, 1), "corr-line", TestContext.Current.CancellationToken);
            reportId = withLine.ReportId;
            version = withLine.Version;
        }

        // The result group is corrected and re-adopted after the line was
        // appended, so the pinned target is no longer the effective one.
        await using var drifted = BuildProvider(connectionString, adoptionTargetId: "adopted-target-2");
        using var scope = drifted.CreateScope();
        var evaluated = await scope.ServiceProvider.GetRequiredService<IReportService>().EvaluateGateAsync(
            reportId, new EvaluateReportGateRequest(version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);
        var evaluation = Assert.Single(evaluated.GateEvaluations);
        var blocker = Assert.Single(evaluation.Blockers, b => b.Source == ReportGateSources.ResultAdoption);

        Assert.Equal(ReportGateDecisions.Blocked, evaluation.Decision);
        Assert.Equal(ReportNextSteps.RefreshAdoption, Assert.Single(blocker.AllowedNextSteps));
        Assert.Equal(ResultContract.RuleSetVersion, blocker.RuleSetVersion);
    }

    [Fact]
    public async Task Every_cited_qc_run_is_asked_and_one_blocked_run_blocks_the_line()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(
            connectionString, qcDecision: QcReportabilityDecisions.Blocked);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var created = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        var withLine = await service.AddLineAsync(
            created.ReportId,
            Line(created.Version, 1) with
            {
                QcRuns =
                [
                    new ReportVersionedReference("QC-RUN-1", 6),
                    new ReportVersionedReference("QC-RUN-2", 2),
                    new ReportVersionedReference("QC-RUN-3", 1)
                ]
            },
            "corr-line", TestContext.Current.CancellationToken);

        Assert.Equal(3, Assert.Single(withLine.Lines).GateRefs.QcRuns.Count);

        var evaluated = await service.EvaluateGateAsync(
            withLine.ReportId,
            new EvaluateReportGateRequest(withLine.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);
        var qcBlockers = Assert.Single(evaluated.GateEvaluations).Blockers
            .Where(b => b.Source == ReportGateSources.QcReportability)
            .ToList();

        // Every cited run is asked, so every failing run is named.
        Assert.Equal(3, qcBlockers.Count);
        Assert.Equal(
            ["QC-RUN-1", "QC-RUN-2", "QC-RUN-3"],
            qcBlockers.Select(b => b.ObjectRef).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Receiving_eligibility_is_asked_with_the_reports_laboratory_not_the_accreditation_site()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        var receivingPort = new FixedReceivingPort("ALLOWED");
        await using var provider = BuildProvider(connectionString, receivingPort: receivingPort);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var created = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        var withLine = await service.AddLineAsync(
            created.ReportId, Line(created.Version, 1), "corr-line", TestContext.Current.CancellationToken);
        await service.EvaluateGateAsync(
            withLine.ReportId,
            new EvaluateReportGateRequest(withLine.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);

        Assert.Equal("LAB-A", receivingPort.LastLaboratoryId);
        Assert.NotEqual(Claim().SiteId, receivingPort.LastLaboratoryId);
    }

    [Fact]
    public async Task Issuance_gate_refuses_to_replay_a_decision_taken_before_a_line_was_appended()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var current = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        current = await service.AddLineAsync(
            current.ReportId, Line(current.Version, 1), "corr-line-1", TestContext.Current.CancellationToken);
        current = await service.EvaluateGateAsync(
            current.ReportId,
            new EvaluateReportGateRequest(current.Version, ReportContract.RuleSetVersion, "signatory-a"),
            "corr-gate", TestContext.Current.CancellationToken);
        var port = scope.ServiceProvider.GetRequiredService<IReportIssuanceGatePort>();
        var beforeAppend = await port.EvaluateAsync(new ReportIssuanceGateRequest(
            "group-a", current.ReportId, current.Version, ReportContract.RuleSetVersion)
        {
            CorrelationId = "corr-before"
        }, TestContext.Current.CancellationToken);

        // A second line is appended after the decision was pinned; it was never
        // put to any source port, so the old ALLOWED must not be replayed.
        var appended = await service.AddLineAsync(
            current.ReportId,
            Line(current.Version, 2) with { ResultGroupId = "GROUP-2", ScopeLineId = "SCOPE-LINE-2" },
            "corr-line-2", TestContext.Current.CancellationToken);
        var afterAppend = await port.EvaluateAsync(new ReportIssuanceGateRequest(
            "group-a", appended.ReportId, appended.Version, ReportContract.RuleSetVersion)
        {
            CorrelationId = "corr-after"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(ReportGateDecisions.Allowed, beforeAppend.Decision);
        Assert.Equal(ReportGateDecisions.Blocked, afterAppend.Decision);
        Assert.Contains(ReportBlockerReasons.GateEvaluationRequired, afterAppend.ReasonCodes);
    }

    [Fact]
    public async Task Report_facts_reject_mutation_and_stale_versions()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var report = await service.CreateAsync(Report(), "corr-create", TestContext.Current.CancellationToken);
        await service.AddLineAsync(
            report.ReportId, Line(report.Version, 1), "corr-line", TestContext.Current.CancellationToken);

        var updateReport = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update report.report set report_number = 'TAMPERED'"));
        var deleteLine = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "delete from report.report_line"));
        var updateLine = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connectionString, "update report.report_line set scope_partition = 'ACTUAL_TESTED'"));
        var stale = await CaptureAsync(service.AddLineAsync(
            report.ReportId, Line(report.Version, 2), "corr-stale", TestContext.Current.CancellationToken));

        Assert.Equal("55000", updateReport.SqlState);
        Assert.Equal("55000", deleteLine.SqlState);
        Assert.Equal("55000", updateLine.SqlState);
        Assert.Equal(ReportErrorCodes.ExpectedVersionConflict, stale.Error!.ErrorCode);
    }

    [Fact]
    public async Task Concurrent_lines_at_one_expected_version_admit_exactly_one_writer()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        string reportId;
        long version;
        await using (var setup = BuildProvider(connectionString))
        {
            using var setupScope = setup.CreateScope();
            var created = await setupScope.ServiceProvider.GetRequiredService<IReportService>()
                .CreateAsync(Report(), "corr-setup", TestContext.Current.CancellationToken);
            reportId = created.ReportId;
            version = created.Version;
        }

        // Distinct line numbers and attributions, so only the advisory lock plus
        // the version check can pick a single winner.
        await using var firstProvider = BuildProvider(connectionString, actorId: "operator-a");
        await using var secondProvider = BuildProvider(connectionString, actorId: "operator-b");
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        var outcomes = await Task.WhenAll(
            CaptureAsync(firstScope.ServiceProvider.GetRequiredService<IReportService>().AddLineAsync(
                reportId, Line(version, 1), "corr-a", TestContext.Current.CancellationToken)),
            CaptureAsync(secondScope.ServiceProvider.GetRequiredService<IReportService>().AddLineAsync(
                reportId,
                Line(version, 2) with { ResultGroupId = "GROUP-2", ScopeLineId = "SCOPE-LINE-2" },
                "corr-b", TestContext.Current.CancellationToken)));

        Assert.Single(outcomes, outcome => outcome.Error is null);
        Assert.Equal(ReportErrorCodes.ExpectedVersionConflict,
            Assert.Single(outcomes, outcome => outcome.Error is not null).Error!.ErrorCode);
        Assert.Equal(1, await CountAsync(connectionString, "select count(*) from report.report_line"));
    }

    [Fact]
    public async Task Capability_denied_fails_closed_with_attempt_audit_only()
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await using var provider = BuildProvider(connectionString, permit: false);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<ReportDomainException>(() =>
            scope.ServiceProvider.GetRequiredService<IReportService>()
                .CreateAsync(Report(), "corr-denied", TestContext.Current.CancellationToken));

        Assert.Equal(ReportErrorCodes.NotAuthorized, exception.ErrorCode);
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from report.report"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
        Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.outbox"));
        Assert.Equal(1, await CountAsync(connectionString,
            "select count(*) from report.audit_attempt where correlation_id = 'corr-denied'"));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Platform_evidence_failure_rolls_back_report_facts(string failedWriter)
    {
        var connectionString = ConnectionString();
        await PrepareAsync(connectionString);
        await InstallFailureTriggerAsync(connectionString, failedWriter);
        try
        {
            await using var provider = BuildProvider(connectionString);
            using var scope = provider.CreateScope();

            var exception = await Assert.ThrowsAsync<ReportDomainException>(() =>
                scope.ServiceProvider.GetRequiredService<IReportService>()
                    .CreateAsync(Report(), $"corr-{failedWriter}", TestContext.Current.CancellationToken));

            Assert.Equal(ReportErrorCodes.PersistenceUnavailable, exception.ErrorCode);
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from report.report"));
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.audit_intent"));
            Assert.Equal(0, await CountAsync(connectionString, "select count(*) from platform.outbox"));
            Assert.Equal(1, await CountAsync(connectionString, "select count(*) from report.audit_attempt"));
        }
        finally
        {
            await RemoveFailureTriggerAsync(connectionString, failedWriter);
        }
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

    private static ServiceProvider BuildProvider(
        string connectionString,
        bool permit = true,
        string adoptionDecision = ResultAdoptionDecisions.Allowed,
        string adoptionTargetId = "adopted-target-1",
        string qcDecision = QcReportabilityDecisions.Allowed,
        string receivingDecision = "ALLOWED",
        string batchDecision = BatchStatusDecisions.Allowed,
        bool signatoryAuthorized = true,
        FixedReceivingPort? receivingPort = null,
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
        services.AddSingleton<ICurrentActorContext>(new FixedActorContext(new ActorContext(actorId, "group-a")));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        new ReportModule(connectionString).AddApiServices(services);
        services.RemoveAll<IReportAuthorizationPort>();
        services.AddSingleton<IReportAuthorizationPort>(new FixedAuthorizationPort(permit));
        services.RemoveAll<IResultAdoptionPort>();
        services.AddSingleton<IResultAdoptionPort>(
            new FixedResultAdoptionPort(adoptionDecision, adoptionTargetId));
        services.RemoveAll<IQcReportabilityPort>();
        services.AddSingleton<IQcReportabilityPort>(new FixedQcPort(qcDecision));
        services.RemoveAll<IReceivingEligibilityPortV2>();
        services.AddSingleton<IReceivingEligibilityPortV2>(receivingPort ?? new FixedReceivingPort(receivingDecision));
        services.RemoveAll<IScopeProductionEligibilityPort>();
        services.AddSingleton<IScopeProductionEligibilityPort>(new FixedScopePort());
        services.RemoveAll<IAllocationStatusPort>();
        services.AddSingleton<IAllocationStatusPort>(new FixedAllocationPort());
        services.RemoveAll<IBatchStatusPort>();
        services.AddSingleton<IBatchStatusPort>(new FixedBatchPort(batchDecision));
        services.RemoveAll<IInstrumentImportPort>();
        services.AddSingleton<IInstrumentImportPort>(new FixedInstrumentPort());
        services.RemoveAll<IAccreditationScopePort>();
        services.AddSingleton<IAccreditationScopePort>(new FixedAccreditationScopePort());
        services.RemoveAll<ISignatoryAuthorityPort>();
        services.AddSingleton<ISignatoryAuthorityPort>(new FixedSignatoryPort(signatoryAuthorized));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateReportRequest Report() => new(
        ReportContract.RuleSetVersion,
        new ReportObjectContext("LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS"),
        "RPT-2026-0001");

    private static AddReportLineRequest Line(long expectedVersion, int lineNumber) => new(
        expectedVersion, ReportContract.RuleSetVersion, lineNumber, "GROUP-1", 4, "SCOPE-LINE-1",
        ReportScopePartitions.ActualTested,
        new ReportTraceReferences("BATCH-1", "ALLOC-1", "ITEM-1", new ReportVersionedReference("REQ-SNAPSHOT-1", 1)),
        new AccreditationScopeReference("ACC-CNAS-1", 2, new string('a', 64)),
        Claim(), [new ReportVersionedReference("QC-RUN-1", 6)], "INST-FILE-1", 3, 2, "SCOPE-MATRIX-1", 2, 3, 4);

    // The accreditation SITE dimension is a registry-shaped identifier and is
    // deliberately NOT the laboratory id, so a mix-up cannot hide.
    private static AccreditationClaim Claim() => new(
        "CNAS-L1234-S1", new ReportVersionedReference("METHOD-TENSILE", 3), "RIGID-PLASTIC", "0-500N",
        Now.AddYears(1), "signatory-a");

    private static string AdminConnectionString() =>
        Environment.GetEnvironmentVariable("OPENLIMS_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "OPENLIMS_TEST_POSTGRES_CONNECTION is required for report integration tests.");

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
        await new ReportModule(connectionString).ApplyMigrationAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connectionString, """
            truncate table
              report.audit_attempt,
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

    private static Task InstallFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_report_audit on platform.audit_intent;
                drop function if exists platform.fail_report_audit();
                create or replace function platform.fail_report_audit() returns trigger language plpgsql as $$
                begin
                  if new.action like '%REPORT%' then
                    raise exception 'forced report audit failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_report_audit before insert on platform.audit_intent
                for each row execute function platform.fail_report_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_report_outbox on platform.outbox;
                drop function if exists platform.fail_report_outbox();
                create or replace function platform.fail_report_outbox() returns trigger language plpgsql as $$
                begin
                  if new.message_type like 'Report%' then
                    raise exception 'forced report outbox failure';
                  end if;
                  return new;
                end;
                $$;
                create trigger trg_fail_report_outbox before insert on platform.outbox
                for each row execute function platform.fail_report_outbox();
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(failedWriter))
        };

    private static Task RemoveFailureTriggerAsync(string connectionString, string failedWriter) =>
        failedWriter switch
        {
            "audit" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_report_audit on platform.audit_intent;
                drop function if exists platform.fail_report_audit();
                """),
            "outbox" => ExecuteAsync(connectionString, """
                drop trigger if exists trg_fail_report_outbox on platform.outbox;
                drop function if exists platform.fail_report_outbox();
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

    private sealed class FixedAuthorizationPort(bool allowed) : IReportAuthorizationPort
    {
        public ValueTask<ReportAuthorizationDecision> AuthorizeAsync(
            ReportAuthorizationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed ? ReportAuthorizationDecision.Permit : ReportAuthorizationDecision.Deny);
    }

    private sealed class FixedResultAdoptionPort(string decision, string targetId = "adopted-target-1")
        : IResultAdoptionPort
    {
        public ValueTask<ResultAdoptionStatusResult> EvaluateAsync(
            ResultAdoptionStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ResultAdoptionStatusResult(
                decision,
                decision == ResultAdoptionDecisions.Allowed ? [] : [ResultAdoptionReasons.AdoptionRequired],
                request.ResultGroupId,
                request.ExpectedGroupVersion,
                decision == ResultAdoptionDecisions.Allowed ? targetId : null,
                decision == ResultAdoptionDecisions.Allowed ? 1 : null,
                ResultContract.RuleSetVersion));
    }

    private sealed class FixedQcPort(string decision) : IQcReportabilityPort
    {
        public ValueTask<QcReportabilityResult> EvaluateAsync(
            QcReportabilityRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new QcReportabilityResult(
                decision,
                decision == QcReportabilityDecisions.Allowed ? [] : [QcReportabilityReasons.QcFailureUnreleased],
                request.QcRunId, request.TargetId, request.ExpectedRunVersion, [], QcContract.RuleSetVersion));
    }

    private sealed class FixedReceivingPort(string decision) : IReceivingEligibilityPortV2
    {
        public string? LastLaboratoryId { get; private set; }

        public ValueTask<ReceivingEligibilityV2Result> EvaluateAsync(
            ReceivingEligibilityV2Request request, CancellationToken cancellationToken = default)
        {
            LastLaboratoryId = request.LaboratoryId;
            return ValueTask.FromResult(new ReceivingEligibilityV2Result(
                decision, "RELEASED", "MATCHED", "identity-1", "release-1",
                decision == "ALLOWED" ? [] : ["IDENTITY_CONFLICT_UNRESOLVED"],
                request.ExpectedItemVersion, 1, request.RuleSetVersion,
                decision == "ALLOWED" ? [request.RequestedAction] : [], [], Now.AddDays(30)));
        }
    }

    private sealed class FixedScopePort : IScopeProductionEligibilityPort
    {
        public ValueTask<ScopeProductionEligibilityResult> EvaluateAsync(
            ScopeProductionEligibilityRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ScopeProductionEligibilityResult(
                ScopeEligibilityDecisions.Allowed, [], request.ScopeMatrixId,
                request.ExpectedMatrixVersion, request.RuleSetVersion));
    }

    private sealed class FixedAllocationPort : IAllocationStatusPort
    {
        public ValueTask<AllocationStatusResult> EvaluateAsync(
            AllocationStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AllocationStatusResult(
                AllocationStatusDecisions.Allowed, [], request.AllocationId, "RESERVED",
                request.ExpectedSubjectAllocationVersion, request.RuleSetVersion));
    }

    private sealed class FixedBatchPort(string decision) : IBatchStatusPort
    {
        public ValueTask<BatchStatusResult> EvaluateAsync(
            BatchStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new BatchStatusResult(
                decision,
                decision == BatchStatusDecisions.Allowed ? [] : [BatchStatusReasons.BatchFrozen],
                request.BatchId,
                decision == BatchStatusDecisions.Allowed ? BatchStates.Active : BatchStates.Frozen,
                request.ExpectedBatchVersion, BatchContract.RuleSetVersion));
    }

    private sealed class FixedInstrumentPort : IInstrumentImportPort
    {
        public ValueTask<InstrumentImportStatusResult> EvaluateAsync(
            InstrumentImportStatusRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new InstrumentImportStatusResult(
                InstrumentStatusDecisions.Allowed, [], request.FileRegistrationId,
                request.ExpectedFileVersion, 5, 0, InstrumentContract.RuleSetVersion));
    }

    private sealed class FixedAccreditationScopePort : IAccreditationScopePort
    {
        public ValueTask<AccreditationScopeLookupResult?> ResolveAsync(
            AccreditationScopeLookupRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AccreditationScopeLookupResult?>(new AccreditationScopeLookupResult(
                "CNAS-L1234-S1", new ReportVersionedReference("METHOD-TENSILE", 3), "RIGID-PLASTIC", "0-500N",
                Now.AddYears(1), ["signatory-a"]));
    }

    private sealed class FixedSignatoryPort(bool authorized) : ISignatoryAuthorityPort
    {
        public ValueTask<SignatoryAuthorityDecision> EvaluateAsync(
            SignatoryAuthorityRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SignatoryAuthorityDecision(
                authorized, authorized ? [] : [ReportBlockerReasons.SignatoryNotAuthorized]));
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
