using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Receiving;
using Xunit;

namespace OpenLIMS.Receiving.UnitTests;

[Trait("Profile", "receiving")]
public sealed class ReceivingExceptionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Standard_and_contamination_classification_are_deterministic()
    {
        Assert.Equal(ReceivingExceptionSeverities.Standard,
            ReceivingExceptionRules.ValidateCreate(Create(ReceivingExceptionTypes.QuantityShortage), Now));
        Assert.Equal(ReceivingExceptionSeverities.SafetyCritical,
            ReceivingExceptionRules.ValidateCreate(Create(ReceivingExceptionTypes.Contamination), Now));
    }

    [Fact]
    public void Unknown_classification_fails_closed()
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            ReceivingExceptionRules.ValidateCreate(Create("LATEST"), Now));
        Assert.Equal(ReceivingErrorCodes.ExceptionTypeUnknown, exception.ErrorCode);
    }

    [Fact]
    public void Conditional_accept_requires_nonempty_disjoint_constraints_and_expiry()
    {
        var capability = ReceivingExceptionRules.ValidateDecision(
            Conditional(), ReceivingExceptionSeverities.Standard, "creator", "quality", Now);
        Assert.Equal(ReceivingCapabilities.ExceptionQualityApprove, capability);

        var exception = Assert.Throws<ReceivingDomainException>(() =>
            ReceivingExceptionRules.ValidateDecision(
                Conditional() with { ProhibitedActions = [] },
                ReceivingExceptionSeverities.Standard, "creator", "quality", Now));
        Assert.Equal(ReceivingErrorCodes.ConditionalAcceptConstraintsRequired, exception.ErrorCode);
    }

    [Fact]
    public void Safety_critical_exception_rejects_quality_conditional_acceptance()
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            ReceivingExceptionRules.ValidateDecision(
                Conditional(), ReceivingExceptionSeverities.SafetyCritical, "creator", "quality", Now));
        Assert.Equal(ReceivingErrorCodes.DecisionNotAuthorized, exception.ErrorCode);

        var capability = ReceivingExceptionRules.ValidateDecision(
            Decision(ReceivingExceptionDecisionTypes.SafetyHold),
            ReceivingExceptionSeverities.SafetyCritical, "creator", "ehs", Now);
        Assert.Equal(ReceivingCapabilities.ExceptionEhsApprove, capability);
    }

    [Fact]
    public void Initiator_cannot_approve_own_exception()
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            ReceivingExceptionRules.ValidateDecision(
                Decision(ReceivingExceptionDecisionTypes.Reject),
                ReceivingExceptionSeverities.Standard, "same", "same", Now));
        Assert.Equal(ReceivingErrorCodes.DecisionNotAuthorized, exception.ErrorCode);
    }

    [Fact]
    public void Identity_exception_requires_matching_identity_assessment_state()
    {
        ReceivingExceptionRules.ValidateIdentityState(
            ReceivingExceptionTypes.IdentityMismatch, IdentityAssessmentStates.Mismatched);
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            ReceivingExceptionRules.ValidateIdentityState(
                ReceivingExceptionTypes.IdentityMismatch, IdentityAssessmentStates.Matched));
        Assert.Equal(ReceivingErrorCodes.ApplicabilityUnknown, exception.ErrorCode);
    }

    private static CreateReceivingExceptionRequest Create(string type) => new(
        "00000000000000000000000000000001", 1, type, Now,
        "Observed exception evidence.", ["object://exception/evidence"], [new string('a', 64)]);

    private static SubmitReceivingExceptionDecisionRequest Conditional() => new(
        1, ReceivingExceptionDecisionTypes.ConditionalAccept,
        [ReceivingEligibilityActions.Disassembly],
        [ReceivingEligibilityActions.SamplePreparation],
        Now.AddDays(7), ["object://exception/decision"], [new string('b', 64)],
        "The documented impact permits only the listed action.",
        "Quality reviewer approved the explicit constraints.",
        ReceivingExceptionContract.MatrixVersion);

    private static SubmitReceivingExceptionDecisionRequest Decision(string type) => new(
        1, type, [], [], null,
        ["object://exception/decision"], [new string('b', 64)], string.Empty,
        "Authorized reviewer recorded the decision.", ReceivingExceptionContract.MatrixVersion);
}
