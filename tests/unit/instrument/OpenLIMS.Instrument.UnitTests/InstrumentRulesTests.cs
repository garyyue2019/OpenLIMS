using OpenLIMS.Contracts.Instrument;
using OpenLIMS.Modules.Instrument;
using Xunit;

namespace OpenLIMS.Instrument.UnitTests;

[Trait("Profile", "instrument")]
public sealed class InstrumentRulesTests
{
    private static readonly string ValidHash = new('a', 64);

    [Fact]
    public void Registration_requires_rule_set_hash_source_system_and_pinned_references()
    {
        var normalized = InstrumentRules.ValidateRegistration(Registration());

        Assert.Equal("LEGAL-A", normalized.ObjectScope.LegalEntityId);
        Assert.Equal(ValidHash, normalized.Sha256);
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateRegistration(Registration() with { RuleSetVersion = "INST-IMPORT@latest" }));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateRegistration(Registration() with { Sha256 = "not-a-hash" }));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateRegistration(Registration() with { Sha256 = ValidHash.ToUpperInvariant() }));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateRegistration(Registration() with { SourceSystem = "SPREADSHEET" }));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateRegistration(Registration() with
            {
                InstrumentRef = new InstrumentVersionedReference("TENSILE-1", 0)
            }));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateRegistration(Registration() with { DeclaredRowCount = 0 }));
    }

    [Fact]
    public void Valid_rows_are_classified_as_facts_and_bad_rows_become_exceptions()
    {
        var (valid, exceptions) = InstrumentRules.ClassifyRows(
            Rows(
                Row(1),
                Row(2) with { SampleNumber = "bad sample" },
                Row(3) with { Unit = "milli metre" },
                Row(4) with { ParsedValue = "  " },
                Row(5) with { Qualifier = "less than" }),
            NoRows, NoRows);

        Assert.Single(valid);
        Assert.Equal(1, valid[0].RowNumber);
        Assert.Equal(
            [
                InstrumentExceptionReasons.UnknownSample,
                InstrumentExceptionReasons.IllegalUnit,
                InstrumentExceptionReasons.UnparsableValue,
                InstrumentExceptionReasons.QualifierConflict
            ],
            exceptions.Select(entry => entry.ReasonCode));
    }

    [Fact]
    public void Row_numbers_already_taken_become_duplicate_exceptions()
    {
        var (valid, exceptions) = InstrumentRules.ClassifyRows(Rows(Row(7), Row(7)), new HashSet<int> { 3 }, NoRows);
        var (_, againstExisting) = InstrumentRules.ClassifyRows(Rows(Row(3)), new HashSet<int> { 3 }, NoRows);

        Assert.Single(valid);
        Assert.Equal(InstrumentExceptionReasons.DuplicateRow, Assert.Single(exceptions).ReasonCode);
        Assert.Equal(InstrumentExceptionReasons.DuplicateRow, Assert.Single(againstExisting).ReasonCode);
    }

    [Fact]
    public void A_row_number_a_queued_exception_holds_is_rejected_instead_of_queued_twice()
    {
        // The exception queue is keyed by row number, so a second exception for
        // the same row could never be persisted — reject per the story's
        // 行号重复 → INS.VALIDATION_FAILED failure path instead.
        var againstQueued = Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ClassifyRows(Rows(Row(4)), NoRows, new HashSet<int> { 4 }));
        var secondOccurrenceInBatch = Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ClassifyRows(
                Rows(Row(2) with { Unit = "newton force" }, Row(2)), NoRows, NoRows));
        var afterDuplicateException = Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ClassifyRows(Rows(Row(3), Row(3), Row(3)), NoRows, NoRows));

        Assert.Equal(InstrumentErrorCodes.ValidationFailed, againstQueued.ErrorCode);
        Assert.Equal(InstrumentErrorCodes.ValidationFailed, secondOccurrenceInBatch.ErrorCode);
        Assert.Equal(InstrumentErrorCodes.ValidationFailed, afterDuplicateException.ErrorCode);
    }

    [Fact]
    public void A_row_number_a_parsed_fact_holds_still_queues_a_duplicate_exception()
    {
        var (valid, exceptions) = InstrumentRules.ClassifyRows(Rows(Row(5)), new HashSet<int> { 5 }, NoRows);

        Assert.Empty(valid);
        Assert.Equal(InstrumentExceptionReasons.DuplicateRow, Assert.Single(exceptions).ReasonCode);
    }

    [Fact]
    public void Raw_value_is_preserved_verbatim_in_both_outcomes()
    {
        const string raw = "  12.5000 mm  ";
        var (valid, exceptions) = InstrumentRules.ClassifyRows(
            Rows(
                Row(1) with { RawValue = raw },
                Row(2) with { RawValue = raw, SampleNumber = "unknown sample" }),
            NoRows, NoRows);

        Assert.Equal(raw, Assert.Single(valid).RawValue);
        Assert.Equal(raw, Assert.Single(exceptions).Row.RawValue);
    }

    [Fact]
    public void Row_submissions_reject_unknown_rule_sets_and_empty_batches()
    {
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ClassifyRows(Rows(Row(1)) with { RuleSetVersion = "INST-IMPORT@2" }, NoRows, NoRows));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ClassifyRows(new SubmitInstrumentRowsRequest(1, InstrumentContract.RuleSetVersion, []), NoRows, NoRows));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ClassifyRows(Rows(Row(1) with { RawValue = " " }), NoRows, NoRows));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ClassifyRows(Rows(Row(0)), NoRows, NoRows));
    }

    [Fact]
    public void Resolution_kinds_constrain_the_corrected_mapping()
    {
        InstrumentRules.ValidateResolution(Resolution(InstrumentResolutionKinds.AcceptWithMapping, Mapping()));
        InstrumentRules.ValidateResolution(Resolution(InstrumentResolutionKinds.RejectRow, null));

        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateResolution(Resolution(InstrumentResolutionKinds.AcceptWithMapping, null)));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateResolution(Resolution(InstrumentResolutionKinds.RejectRow, Mapping())));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateResolution(Resolution("OVERWRITE_RAW_VALUE", null)));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateResolution(Resolution(InstrumentResolutionKinds.RejectRow, null) with { Reason = "  " }));
        Assert.Throws<InstrumentDomainException>(() =>
            InstrumentRules.ValidateResolution(Resolution(
                InstrumentResolutionKinds.AcceptWithMapping, Mapping() with { Unit = "bad unit" })));
    }

    [Theory]
    [InlineData(3, 3, 0, 0, InstrumentFileStates.Completed)]
    [InlineData(3, 1, 2, 0, InstrumentFileStates.Blocked)]
    [InlineData(3, 1, 0, 2, InstrumentFileStates.Completed)]
    [InlineData(3, 1, 0, 0, InstrumentFileStates.Ingested)]
    public void File_state_follows_pending_exceptions_then_row_completeness(
        int declared, int validRows, int pending, int resolved, string expected) =>
        Assert.Equal(expected, InstrumentRules.ResolveFileState(declared, validRows, pending, resolved));

    [Fact]
    public void Status_is_allowed_only_for_completed_matching_versions()
    {
        var completed = File(InstrumentFileStates.Completed, rows: 2);

        var allowed = InstrumentRules.EvaluateStatus(Status(completed.Version), completed);
        var stale = InstrumentRules.EvaluateStatus(Status(completed.Version + 1), completed);
        var unknownRule = InstrumentRules.EvaluateStatus(
            Status(completed.Version) with { RuleSetVersion = "INST-IMPORT@latest" }, completed);
        var missing = InstrumentRules.EvaluateStatus(Status(1), null);

        Assert.Equal(InstrumentStatusDecisions.Allowed, allowed.Decision);
        Assert.Equal(2, allowed.CompletedRowCount);
        Assert.Equal(InstrumentStatusDecisions.Unknown, stale.Decision);
        Assert.Contains(InstrumentStatusReasons.VersionMismatch, stale.ReasonCodes);
        Assert.Equal(InstrumentStatusDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(InstrumentStatusReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
        Assert.Equal(InstrumentStatusDecisions.Blocked, missing.Decision);
        Assert.Contains(InstrumentStatusReasons.FileRequired, missing.ReasonCodes);
    }

    [Fact]
    public void Pending_exceptions_and_incomplete_imports_block_the_status_port()
    {
        var pending = File(InstrumentFileStates.Blocked, rows: 1, pendingExceptions: 1);
        var incomplete = File(InstrumentFileStates.Ingested, rows: 1);

        var blockedByExceptions = InstrumentRules.EvaluateStatus(Status(pending.Version), pending);
        var blockedByRows = InstrumentRules.EvaluateStatus(Status(incomplete.Version), incomplete);

        Assert.Equal(InstrumentStatusDecisions.Blocked, blockedByExceptions.Decision);
        Assert.Contains(InstrumentStatusReasons.PendingExceptions, blockedByExceptions.ReasonCodes);
        Assert.Equal(1, blockedByExceptions.PendingExceptionCount);
        Assert.Equal(InstrumentStatusDecisions.Blocked, blockedByRows.Decision);
        Assert.Contains(InstrumentStatusReasons.ImportIncomplete, blockedByRows.ReasonCodes);
    }

    private static readonly IReadOnlySet<int> NoRows = new HashSet<int>();

    private static RegisterInstrumentFileRequest Registration() => new(
        InstrumentContract.RuleSetVersion,
        new InstrumentObjectContext("LEGAL-A", "LAB-A"),
        new InstrumentVersionedReference("EXPORT-7", 1),
        new string('a', 64),
        InstrumentSourceSystems.Instrument,
        new InstrumentVersionedReference("TENSILE-1", 2),
        "PARSER-1.4",
        3);

    private static SubmitInstrumentRowsRequest Rows(params InstrumentRowInput[] rows) =>
        new(1, InstrumentContract.RuleSetVersion, rows);

    private static InstrumentRowInput Row(int rowNumber) => new(
        rowNumber, $"SPEC-{rowNumber}", $"POS-{rowNumber}", "TENSILE-STRENGTH", "NEWTON", null, "83.4", "83.4");

    private static ResolveImportExceptionRequest Resolution(string kind, InstrumentRowMapping? mapping) =>
        new(2, InstrumentContract.RuleSetVersion, kind, "operator corrected the sample map", mapping);

    private static InstrumentRowMapping Mapping() => new("SPEC-2", "POS-2", "TENSILE-STRENGTH", "NEWTON", null);

    private static InstrumentImportStatusRequest Status(long expectedVersion) => new(
        "group-a", "00000000000000000000000000000090", expectedVersion, InstrumentContract.RuleSetVersion);

    private static InstrumentFileResult File(string state, int rows, int pendingExceptions = 0) => new(
        "00000000000000000000000000000090",
        1 + rows + pendingExceptions,
        state,
        InstrumentContract.RuleSetVersion,
        new InstrumentObjectContext("LEGAL-A", "LAB-A"),
        new InstrumentVersionedReference("EXPORT-7", 1),
        new string('a', 64),
        InstrumentSourceSystems.Instrument,
        new InstrumentVersionedReference("TENSILE-1", 2),
        "PARSER-1.4",
        3,
        Enumerable.Range(1, rows).Select(number => new InstrumentParsedRowResult(
            $"row-{number}", "00000000000000000000000000000090", number,
            $"SPEC-{number}", $"POS-{number}", "TENSILE-STRENGTH", "NEWTON", null,
            "83.4", "83.4", "PARSER-1.4", "operator-a", DateTimeOffset.UnixEpoch)).ToList(),
        Enumerable.Range(1, pendingExceptions).Select(number => new InstrumentImportExceptionResult(
            $"exception-{number}", "00000000000000000000000000000090", 90 + number,
            InstrumentExceptionReasons.UnknownSample, "raw", InstrumentExceptionStates.Pending, null)).ToList(),
        "operator-a",
        DateTimeOffset.UnixEpoch);
}
