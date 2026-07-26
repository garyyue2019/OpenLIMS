using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Modules.Batch;
using Xunit;

namespace OpenLIMS.Batch.UnitTests;

[Trait("Profile", "batch")]
public sealed class BatchRulesTests
{
    [Theory]
    [InlineData(BatchTypes.Preparation)]
    [InlineData(BatchTypes.Preconditioning)]
    [InlineData(BatchTypes.Analytical)]
    [InlineData(BatchTypes.InstrumentRun)]
    public void Explicit_batch_types_are_accepted(string batchType) =>
        Assert.Equal(batchType, BatchRules.ValidateBatchType(batchType));

    [Theory]
    [InlineData("EXECUTION_RUN")]
    [InlineData("")]
    [InlineData(null)]
    public void Generic_or_unknown_batch_types_fail_closed(string? batchType)
    {
        var exception = Assert.Throws<BatchDomainException>(() => BatchRules.ValidateBatchType(batchType));
        Assert.Equal(BatchErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Specimen_member_requires_allocation_and_forbids_qc_reference()
    {
        var validated = BatchRules.ValidateMember(SpecimenMember());
        var missingAllocation = Assert.Throws<BatchDomainException>(() =>
            BatchRules.ValidateMember(SpecimenMember() with { AllocationId = null }));
        var withQc = Assert.Throws<BatchDomainException>(() =>
            BatchRules.ValidateMember(SpecimenMember() with { QcRef = new BatchVersionedReference("QC-1", 1) }));

        Assert.Equal(BatchMemberTypes.Specimen, validated.MemberType);
        Assert.Equal(BatchErrorCodes.ValidationFailed, missingAllocation.ErrorCode);
        Assert.Equal(BatchErrorCodes.ValidationFailed, withQc.ErrorCode);
    }

    [Fact]
    public void Qc_member_requires_approved_reference_and_forbids_allocation()
    {
        var validated = BatchRules.ValidateMember(QcMember());
        var withAllocation = Assert.Throws<BatchDomainException>(() =>
            BatchRules.ValidateMember(QcMember() with { AllocationId = "A1" }));

        Assert.Equal(BatchMemberTypes.QcSample, validated.MemberType);
        Assert.Equal("QC-CTRL-7", validated.QcRef!.Id);
        Assert.Equal(BatchErrorCodes.ValidationFailed, withAllocation.ErrorCode);
    }

    [Fact]
    public void Evidence_requires_known_source_and_sha256()
    {
        var validated = BatchRules.ValidateEvidence(Evidence());
        var badSource = Assert.Throws<BatchDomainException>(() =>
            BatchRules.ValidateEvidence(Evidence() with { SourceSystem = "USB" }));
        var badHash = Assert.Throws<BatchDomainException>(() =>
            BatchRules.ValidateEvidence(Evidence() with { Sha256 = "not-a-hash" }));

        Assert.Equal(new string('a', 64), validated.Sha256);
        Assert.Equal(BatchErrorCodes.ValidationFailed, badSource.ErrorCode);
        Assert.Equal(BatchErrorCodes.ValidationFailed, badHash.ErrorCode);
    }

    [Fact]
    public void Freeze_requires_explicit_cause_and_rule_set()
    {
        var validated = BatchRules.ValidateFreeze(new FreezeBatchRequest(3, BatchContract.RuleSetVersion, BatchFreezeCauses.QcFailure));
        var badCause = Assert.Throws<BatchDomainException>(() =>
            BatchRules.ValidateFreeze(new FreezeBatchRequest(3, BatchContract.RuleSetVersion, "OPERATOR_MISTAKE")));
        var badRuleSet = Assert.Throws<BatchDomainException>(() =>
            BatchRules.ValidateFreeze(new FreezeBatchRequest(3, "BATCH-EXECUTION@latest", BatchFreezeCauses.QcFailure)));

        Assert.Equal(BatchFreezeCauses.QcFailure, validated.Cause);
        Assert.Equal(BatchErrorCodes.ValidationFailed, badCause.ErrorCode);
        Assert.Equal(BatchErrorCodes.ApplicabilityUnknown, badRuleSet.ErrorCode);
    }

    [Fact]
    public void Frozen_batch_rejects_further_changes_and_version_mismatch_conflicts()
    {
        var frozen = Batch() with
        {
            State = BatchStates.Frozen,
            Freeze = new BatchFreezeResult("f1", "b1", 3, BatchFreezeCauses.QcFailure, 2, null, "actor", DateTimeOffset.MinValue)
        };
        var frozenException = Assert.Throws<BatchDomainException>(() => BatchRules.RequireActive(frozen));
        var versionException = Assert.Throws<BatchDomainException>(() => BatchRules.RequireVersion(1, 2));

        Assert.Equal(BatchErrorCodes.BatchFrozen, frozenException.ErrorCode);
        Assert.Equal(BatchErrorCodes.ExpectedVersionConflict, versionException.ErrorCode);
    }

    [Fact]
    public void Status_pins_rule_set_version_batch_version_and_freeze_state()
    {
        var active = Batch() with { Version = 3 };
        var frozen = active with
        {
            State = BatchStates.Frozen,
            Freeze = new BatchFreezeResult("f1", "b1", 4, BatchFreezeCauses.QcFailure, 2, null, "actor", DateTimeOffset.MinValue),
            Version = 4
        };

        var allowed = BatchRules.EvaluateStatus(Status(3), active);
        var stale = BatchRules.EvaluateStatus(Status(2), active);
        var blockedFrozen = BatchRules.EvaluateStatus(Status(4), frozen);
        var unknownRule = BatchRules.EvaluateStatus(Status(3) with { RuleSetVersion = "BATCH-EXECUTION@latest" }, active);
        var missing = BatchRules.EvaluateStatus(Status(1), null);

        Assert.Equal(BatchStatusDecisions.Allowed, allowed.Decision);
        Assert.Equal(BatchStatusDecisions.Unknown, stale.Decision);
        Assert.Contains(BatchStatusReasons.BatchVersionMismatch, stale.ReasonCodes);
        Assert.Equal(BatchStatusDecisions.Blocked, blockedFrozen.Decision);
        Assert.Contains(BatchStatusReasons.BatchFrozen, blockedFrozen.ReasonCodes);
        Assert.Equal(BatchStatusDecisions.Unknown, unknownRule.Decision);
        Assert.Equal(BatchStatusDecisions.Blocked, missing.Decision);
        Assert.Contains(BatchStatusReasons.BatchRequired, missing.ReasonCodes);
    }

    [Fact]
    public async Task Authorization_requires_exact_legal_entity_and_laboratory_claims()
    {
        var context = new DefaultHttpContext { User = Principal(includeLaboratory: true) };
        var port = new HttpClaimsBatchAuthorizationPort(new HttpContextAccessor { HttpContext = context });

        var allowed = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);
        context.User = Principal(includeLaboratory: false);
        var denied = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    private static AddBatchMemberRequest SpecimenMember() => new(
        1, BatchContract.RuleSetVersion, BatchMemberTypes.Specimen,
        "CUSTOMER-A", "ORDER-A", "TOYS",
        AllocationId: "00000000000000000000000000000031",
        ExpectedSubjectAllocationVersion: 2);

    private static AddBatchMemberRequest QcMember() => new(
        1, BatchContract.RuleSetVersion, BatchMemberTypes.QcSample,
        "CUSTOMER-QC", "ORDER-QC", "TOYS",
        QcRef: new BatchVersionedReference("QC-CTRL-7", 1));

    private static AddBatchEvidenceRequest Evidence() => new(
        2, BatchContract.RuleSetVersion, BatchEvidenceSources.Cds,
        new BatchVersionedReference("CDS-SEQ-9", 3), new string('A', 64));

    private static BatchResult Batch() => new(
        "00000000000000000000000000000040", BatchTypes.Analytical, BatchStates.Active, 1,
        BatchContract.RuleSetVersion, new BatchObjectContext("LEGAL-A", "LAB-A"),
        [], [], null, "actor-a", DateTimeOffset.MinValue);

    private static BatchStatusRequest Status(long expectedVersion) => new(
        "group-a", "00000000000000000000000000000040", expectedVersion, BatchContract.RuleSetVersion);

    private static BatchAuthorizationRequest AuthRequest() => new(
        "group-a", "actor-a", new BatchObjectContext("LEGAL-A", "LAB-A"), BatchCapabilities.Manage);

    private static ClaimsPrincipal Principal(bool includeLaboratory)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "actor-a"),
            new("organization_group", "group-a"),
            new(BatchClaimTypes.Capability, BatchCapabilities.Manage),
            new(BatchClaimTypes.LegalEntity, "LEGAL-A")
        };
        if (includeLaboratory) claims.Add(new Claim(BatchClaimTypes.Laboratory, "LAB-A"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
