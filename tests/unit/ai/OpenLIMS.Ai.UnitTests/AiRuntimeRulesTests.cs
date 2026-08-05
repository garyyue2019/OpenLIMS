using OpenLIMS.Contracts.Ai;
using OpenLIMS.Modules.Ai;
using Xunit;

namespace OpenLIMS.Ai.UnitTests;

[Trait("Profile", "ai")]
public sealed class AiRuntimeRulesTests
{
    [Fact]
    public void Run_validation_normalizes_sets_and_rejects_duplicate_schema_entries()
    {
        var normalized = AiRuntimeRules.ValidateRun(
            Request() with { AllowedFields = ["target-market", "style-number"] },
            AiGovernanceRules.Instance);
        var exception = Assert.Throws<AiDomainException>(() => AiRuntimeRules.ValidateRun(
            Request() with { AllowedFields = ["style-number", "style-number"] },
            AiGovernanceRules.Instance));

        Assert.Equal(["style-number", "target-market"], normalized.AllowedFields);
        Assert.Equal(AiErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Request_hash_binds_complete_object_scope()
    {
        var first = AiRuntimeRules.RequestHash(Request());
        var second = AiRuntimeRules.RequestHash(Request() with
        {
            ObjectScope = Scope() with { CustomerId = "CUSTOMER-B" }
        });

        Assert.NotEqual(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void Disabled_provider_fails_closed_with_manual_fallback()
    {
        var outcome = AiRuntimeRules.EvaluateProviderResponse(
            Request(), new AiProviderResponse(AiProviderStatuses.Disabled), AiGovernanceRules.Instance);

        Assert.Equal(AiRunStatuses.ProviderDisabled, outcome.Status);
        Assert.Equal(AiProviderStatuses.Disabled, outcome.ProviderStatus);
        Assert.True(outcome.ManualFallbackRequired);
        Assert.False(outcome.HumanReviewRequired);
        Assert.Null(outcome.OriginalOutput);
    }

    [Fact]
    public void Exact_completed_output_is_accepted_and_preserved()
    {
        var outcome = AiRuntimeRules.EvaluateProviderResponse(
            Request(),
            new AiProviderResponse(AiProviderStatuses.Completed, Output(), "provider-job-1"),
            AiGovernanceRules.Instance);

        Assert.Equal(AiRunStatuses.Accepted, outcome.Status);
        Assert.Equal(AiValidationDecisions.Accepted, outcome.Validation!.Decision);
        Assert.Equal("STY-1001", Assert.Single(outcome.OriginalOutput!.Candidates).Value);
        Assert.True(outcome.HumanReviewRequired);
        Assert.False(outcome.ManualFallbackRequired);
    }

    [Fact]
    public void Envelope_drift_and_unknown_fields_quarantine_the_whole_output()
    {
        var drift = AiRuntimeRules.EvaluateProviderResponse(
            Request(),
            new AiProviderResponse(
                AiProviderStatuses.Completed,
                Output() with
                {
                    Envelope = Envelope() with { OutputSchema = new AiVersionedReference("OTHER", 1) }
                },
                "provider-job-2"),
            AiGovernanceRules.Instance);
        var unknown = AiRuntimeRules.EvaluateProviderResponse(
            Request(),
            new AiProviderResponse(
                AiProviderStatuses.Completed,
                Output() with { Candidates = [Candidate() with { TargetField = "unknown-field" }] },
                "provider-job-3"),
            AiGovernanceRules.Instance);

        Assert.Equal(AiRunStatuses.Quarantined, drift.Status);
        Assert.Contains(drift.Validation!.Errors, entry => entry.Code == AiValidationErrorCodes.EnvelopeMismatch);
        Assert.Empty(drift.Validation.Candidates);
        Assert.Equal(AiRunStatuses.Quarantined, unknown.Status);
        Assert.Contains(unknown.Validation!.Errors, entry => entry.Code == AiValidationErrorCodes.UnknownField);
        Assert.Empty(unknown.Validation.Candidates);
        Assert.NotNull(unknown.OriginalOutput);
    }

    [Fact]
    public void Invalid_provider_confirmation_becomes_terminal_provider_failure()
    {
        var outcome = AiRuntimeRules.EvaluateProviderResponse(
            Request(),
            new AiProviderResponse(AiProviderStatuses.Completed, Output()),
            AiGovernanceRules.Instance);

        Assert.Equal(AiRunStatuses.ProviderFailed, outcome.Status);
        Assert.Equal(AiValidationErrorCodes.ProviderResponseInvalid, outcome.ProviderFailureCode);
        Assert.True(outcome.ManualFallbackRequired);
    }

    [Fact]
    public void Verified_fact_without_evidence_and_duplicate_candidate_ids_are_quarantined()
    {
        var promotion = AiRuntimeRules.EvaluateProviderResponse(
            Request(),
            new AiProviderResponse(
                AiProviderStatuses.Completed,
                Output() with
                {
                    Candidates = [Candidate() with { FactClass = AiFactClasses.VerifiedFact }]
                },
                "provider-job-4"),
            AiGovernanceRules.Instance);
        var duplicate = AiRuntimeRules.EvaluateProviderResponse(
            Request(),
            new AiProviderResponse(
                AiProviderStatuses.Completed,
                Output() with
                {
                    Candidates = [Candidate(), Candidate() with { Value = "STY-1002" }]
                },
                "provider-job-5"),
            AiGovernanceRules.Instance);

        Assert.Equal(AiRunStatuses.Quarantined, promotion.Status);
        Assert.Contains(promotion.Validation!.Errors, error => error.Code == AiValidationErrorCodes.FactClassPromotion);
        Assert.Equal(AiRunStatuses.Quarantined, duplicate.Status);
        Assert.Contains(duplicate.Validation!.Errors, error => error.Code == AiValidationErrorCodes.DuplicateCandidateId);
    }

    [Fact]
    public void Human_disposition_uses_server_actor_and_original_ai_value()
    {
        var candidate = Candidate();
        var disposition = AiRuntimeRules.BuildDisposition(
            Guid.Parse("00000000-0000-0000-0000-000000000011"),
            new RecordAiDispositionRequest(
                1, AiContract.RuntimeRuleSetVersion, candidate.CandidateId,
                AiDispositionKinds.Modify, "checked against source", "review-1", "STY-1002"),
            candidate,
            "reviewer-a",
            AiGovernanceRules.Instance);

        Assert.Equal(candidate.Value, disposition.AiOriginalValue);
        Assert.Equal("STY-1002", disposition.HumanValue);
        Assert.Equal("reviewer-a", disposition.ResponsibleActor);
    }

    private static CreateAiRunRequest Request() => new(
        AiContract.RuntimeRuleSetVersion,
        Scope(),
        Envelope(),
        new AiVersionedReference("VALIDATION-PROFILE", 1),
        ["style-number"],
        [],
        "request-1");

    private static AiObjectContext Scope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TEXTILE");

    private static AiRunEnvelope Envelope() => new(
        new AiVersionedReference("MODEL-A", 1),
        "gateway-primary",
        new AiVersionedReference("PROMPT-A", 1),
        new AiVersionedReference("SCHEMA-A", 1),
        [new AiVersionedReference("DOC-A", 1)]);

    private static AiStructuredOutput Output() => new(
        AiContract.RuleSetVersion,
        Envelope(),
        [Candidate()],
        [new AiGapSuggestion(
            "gap-1", "target-market", AiGapKinds.MissingInformation, "Which market applies?")]);

    private static AiFieldCandidate Candidate() => new(
        "candidate-1", "style-number", "STY-1001", AiFactClasses.AiInference, 0.94m,
        new AiSourceLocation(new AiVersionedReference("DOC-A", 1), 2, "top-right"));
}
