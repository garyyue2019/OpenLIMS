using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Receiving;
using Xunit;

namespace OpenLIMS.Receiving.UnitTests;

[Trait("Profile", "receiving")]
public sealed class ReceivingReleaseRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Matched_item_without_exceptions_is_released_for_all_actions()
    {
        var result = ReceivingReleaseRules.Evaluate(Identity(), [], Now);

        Assert.Equal(ReceivingReleaseOutcomes.Released, result.Outcome);
        Assert.Equal(ReceivingReleaseStates.Accepted, result.State);
        Assert.Equal(
            [
                ReceivingEligibilityActions.Disassembly,
                ReceivingEligibilityActions.SamplePreparation,
                ReceivingEligibilityActions.TestAssignment
            ],
            result.AllowedActions);
        Assert.Empty(result.ProhibitedActions);
        Assert.Null(result.ConstraintsValidUntil);
    }

    [Fact]
    public void Conditional_constraints_use_allowed_intersection_prohibited_union_and_earliest_expiry()
    {
        var first = Conditional(
            "00000000000000000000000000000011",
            [ReceivingEligibilityActions.Disassembly, ReceivingEligibilityActions.SamplePreparation],
            [ReceivingEligibilityActions.TestAssignment],
            Now.AddDays(7));
        var second = Conditional(
            "00000000000000000000000000000012",
            [ReceivingEligibilityActions.Disassembly, ReceivingEligibilityActions.TestAssignment],
            [ReceivingEligibilityActions.SamplePreparation],
            Now.AddDays(3));

        var result = ReceivingReleaseRules.Evaluate(Identity(), [first, second], Now);

        Assert.Equal(ReceivingReleaseOutcomes.ReleasedWithConstraints, result.Outcome);
        Assert.Equal(ReceivingReleaseStates.ConditionallyAccepted, result.State);
        Assert.Equal([ReceivingEligibilityActions.Disassembly], result.AllowedActions);
        Assert.Equal(
            [ReceivingEligibilityActions.SamplePreparation, ReceivingEligibilityActions.TestAssignment],
            result.ProhibitedActions);
        Assert.Equal(Now.AddDays(3), result.ConstraintsValidUntil);
    }

    [Theory]
    [InlineData(ReceivingExceptionStatuses.Open)]
    [InlineData(ReceivingExceptionStatuses.AwaitingCustomer)]
    [InlineData(ReceivingExceptionStatuses.Rejected)]
    [InlineData(ReceivingExceptionStatuses.SafetyHold)]
    public void Blocking_exception_states_never_release(string status)
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            ReceivingReleaseRules.Evaluate(Identity(), [Conditional(status: status)], Now));

        Assert.Equal(ReceivingErrorCodes.BlockingException, exception.ErrorCode);
    }

    [Fact]
    public void Expired_constraints_fail_closed()
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            ReceivingReleaseRules.Evaluate(
                Identity(),
                [Conditional(validUntil: Now)],
                Now));

        Assert.Equal(ReceivingErrorCodes.ReleaseApplicabilityUnknown, exception.ErrorCode);
    }

    [Fact]
    public void Empty_final_allowed_intersection_fails_closed()
    {
        var first = Conditional(
            allowed: [ReceivingEligibilityActions.Disassembly],
            prohibited: [ReceivingEligibilityActions.SamplePreparation]);
        var second = Conditional(
            "00000000000000000000000000000012",
            [ReceivingEligibilityActions.SamplePreparation],
            [ReceivingEligibilityActions.TestAssignment]);

        var exception = Assert.Throws<ReceivingDomainException>(() =>
            ReceivingReleaseRules.Evaluate(Identity(), [first, second], Now));

        Assert.Equal(ReceivingErrorCodes.ReleaseApplicabilityUnknown, exception.ErrorCode);
    }

    [Theory]
    [InlineData(IdentityDecisionOutcomes.Mismatched)]
    [InlineData(IdentityDecisionOutcomes.Indeterminate)]
    public void Only_matched_identity_can_release(string outcome)
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            ReceivingReleaseRules.Evaluate(Identity(outcome), [], Now));

        Assert.Equal(ReceivingErrorCodes.IdentityNotMatched, exception.ErrorCode);
    }

    [Fact]
    public void Unversioned_release_rule_is_unknown()
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            ReceivingReleaseRules.ValidateRequest(new SubmitReceivingReleaseDecisionRequest(3, "latest", "Reviewed.")));

        Assert.Equal(ReceivingErrorCodes.ReleaseApplicabilityUnknown, exception.ErrorCode);
    }

    private static ReceivingReleaseIdentitySnapshot Identity(
        string outcome = IdentityDecisionOutcomes.Matched) => new(
        "00000000000000000000000000000001",
        1,
        outcome,
        IdentityAssessmentContract.RuleSetVersion);

    private static ReceivingReleaseExceptionSnapshot Conditional(
        string exceptionId = "00000000000000000000000000000010",
        IReadOnlyList<string>? allowed = null,
        IReadOnlyList<string>? prohibited = null,
        DateTimeOffset? validUntil = null,
        string status = ReceivingExceptionStatuses.ConditionallyAccepted) => new(
        exceptionId,
        status,
        2,
        "00000000000000000000000000000020",
        1,
        ReceivingExceptionDecisionTypes.ConditionalAccept,
        ReceivingExceptionContract.MatrixVersion,
        allowed ?? [ReceivingEligibilityActions.Disassembly],
        prohibited ?? [ReceivingEligibilityActions.SamplePreparation],
        validUntil ?? Now.AddDays(7));
}
