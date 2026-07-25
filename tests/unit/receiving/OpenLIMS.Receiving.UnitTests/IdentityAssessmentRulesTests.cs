using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Receiving;
using Xunit;

namespace OpenLIMS.Receiving.UnitTests;

[Trait("Profile", "receiving")]
public sealed class IdentityAssessmentRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Complete_observation_evidence_is_accepted()
    {
        IdentityAssessmentRules.ValidateObservation(Observation());
    }

    [Fact]
    public void Missing_attachment_evidence_is_rejected()
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            IdentityAssessmentRules.ValidateObservation(Observation() with
            {
                AttachmentRefs = [],
                AttachmentHashes = []
            }));

        Assert.Equal(ReceivingErrorCodes.IdentityEvidenceIncomplete, exception.ErrorCode);
    }

    [Fact]
    public void Matched_cannot_hide_a_model_conflict()
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            IdentityAssessmentRules.ValidateDecision(
                Decision(IdentityDecisionOutcomes.Matched, "CONSISTENT"),
                Declaration(),
                ObservationResult() with { ObservedModel = "MODEL-OTHER" }));

        Assert.Equal(ReceivingErrorCodes.IdentityConflict, exception.ErrorCode);
    }

    [Fact]
    public void Mismatched_requires_an_observed_key_field_conflict()
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            IdentityAssessmentRules.ValidateDecision(
                Decision(IdentityDecisionOutcomes.Mismatched, "MODEL_CONFLICT"),
                Declaration(),
                ObservationResult()));

        Assert.Equal(ReceivingErrorCodes.IdentityConflict, exception.ErrorCode);
    }

    [Fact]
    public void Indeterminate_requires_explicit_ambiguity_reason()
    {
        var exception = Assert.Throws<ReceivingDomainException>(() =>
            IdentityAssessmentRules.ValidateDecision(
                Decision(IdentityDecisionOutcomes.Indeterminate, "NOT_SURE"),
                Declaration(),
                ObservationResult()));

        Assert.Equal(ReceivingErrorCodes.IdentityAmbiguous, exception.ErrorCode);
    }

    [Theory]
    [InlineData(ReceivingEligibilityActions.Disassembly)]
    [InlineData(ReceivingEligibilityActions.SamplePreparation)]
    [InlineData(ReceivingEligibilityActions.TestAssignment)]
    public void Exactly_three_execution_actions_share_the_known_gate(string action)
    {
        Assert.True(IdentityAssessmentRules.IsKnownEligibilityAction(action));
    }

    [Fact]
    public void Unversioned_or_unknown_action_is_not_treated_as_allowed()
    {
        Assert.False(IdentityAssessmentRules.IsKnownEligibilityAction("LATEST"));
    }

    private static CreateIdentityObservationRequest Observation() => new(
        1,
        ["OUTER-LABEL-01"],
        "MODEL-001",
        "BATCH-001",
        "Intact red toy set",
        ["object://identity/photo-01"],
        [new string('a', 64)]);

    private static IdentityDeclarationSnapshotResult Declaration() => new(
        "00000000000000000000000000000001",
        1,
        1,
        "Hard plastic toy set",
        "MODEL-001",
        "BATCH-001",
        "SERIAL-001",
        "red",
        Now);

    private static IdentityObservationResult ObservationResult() => new(
        "00000000000000000000000000000002",
        1,
        1,
        ["OUTER-LABEL-01"],
        "MODEL-001",
        "BATCH-001",
        "Intact red toy set",
        ["object://identity/photo-01"],
        [new string('a', 64)],
        Now,
        "actor-a");

    private static SubmitIdentityDecisionRequest Decision(string outcome, string reason) => new(
        2,
        1,
        1,
        outcome,
        reason,
        "Evidence reviewed by the assigned identity assessor.",
        IdentityAssessmentContract.RuleSetVersion);
}
