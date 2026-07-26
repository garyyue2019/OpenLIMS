using System.Text.Json;
using OpenLIMS.Contracts.Ai;
using Xunit;

namespace OpenLIMS.Ai.ContractTests;

[Trait("Profile", "ai")]
public sealed class AiContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedFields = new(StringComparer.Ordinal)
    {
        "style-number", "material-composition", "target-market"
    };
    private static readonly HashSet<string> AllowedUnits = new(StringComparer.Ordinal) { "PERCENT" };

    [Fact]
    public void Complete_envelope_passes_and_missing_reference_fails_closed()
    {
        var accepted = AiGovernanceRules.Instance.Validate(Output(), AllowedFields, AllowedUnits);
        var missingModel = Assert.Throws<AiContractException>(() =>
            AiGovernanceRules.Instance.Validate(
                Output() with { Envelope = Envelope() with { Model = null! } }, AllowedFields, AllowedUnits));

        Assert.Equal(AiValidationDecisions.Accepted, accepted.Decision);
        Assert.Equal(AiErrorCodes.ValidationFailed, missingModel.ErrorCode);
    }

    [Fact]
    public void Unknown_field_illegal_unit_and_missing_source_quarantine_whole_output()
    {
        var result = AiGovernanceRules.Instance.Validate(
            Output() with
            {
                Candidates =
                [
                    Candidate("c1", "unknown-field", "cotton"),
                    Candidate("c2", "material-composition", "95") with { Unit = "KILOGRAM" }
                ]
            },
            AllowedFields, AllowedUnits);

        Assert.Equal(AiValidationDecisions.Quarantined, result.Decision);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.Gaps);
        Assert.Contains(result.Errors, error => error.Code == AiValidationErrorCodes.UnknownField);
        Assert.Contains(result.Errors, error => error.Code == AiValidationErrorCodes.IllegalUnit);
    }

    [Fact]
    public void Verified_fact_requires_authority_source_and_verification_method()
    {
        var promotion = Assert.Throws<AiContractException>(() =>
            AiGovernanceRules.Instance.Validate(
                Output() with
                {
                    Candidates = [Candidate("c1", "material-composition", "cotton") with
                    {
                        FactClass = AiFactClasses.VerifiedFact
                    }]
                },
                AllowedFields, AllowedUnits));
        var explicitCheck = Assert.Throws<AiContractException>(() =>
            AiGovernanceRules.RequireNoPromotion(
                Candidate("c1", "material-composition", "cotton"), AiFactClasses.VerifiedFact));
        AiGovernanceRules.RequireNoPromotion(
            Candidate("c1", "material-composition", "cotton") with
            {
                AuthoritySource = new AiVersionedReference("LAB-REPORT-9", 1),
                VerificationMethod = new AiVersionedReference("METHOD-FTIR", 2)
            },
            AiFactClasses.VerifiedFact);

        Assert.Equal(AiErrorCodes.FactClassPromotionRejected, promotion.ErrorCode);
        Assert.Equal(AiErrorCodes.FactClassPromotionRejected, explicitCheck.ErrorCode);
    }

    [Fact]
    public void Uncertainty_is_expressed_as_branches_or_abstention_not_duplicates()
    {
        var branches = AiGovernanceRules.Instance.Validate(
            Output() with
            {
                Candidates =
                [
                    Candidate("c1", "material-composition", "cotton-95"),
                    Candidate("c2", "material-composition", "cotton-90"),
                    Candidate("c3", "target-market", "unknown") with { Abstained = true }
                ]
            },
            AllowedFields, AllowedUnits);
        var duplicates = AiGovernanceRules.Instance.Validate(
            Output() with
            {
                Candidates =
                [
                    Candidate("c1", "material-composition", "cotton-95"),
                    Candidate("c2", "material-composition", "cotton-95")
                ]
            },
            AllowedFields, AllowedUnits);

        Assert.Equal(AiValidationDecisions.Accepted, branches.Decision);
        Assert.Equal(AiValidationDecisions.Quarantined, duplicates.Decision);
        Assert.Contains(duplicates.Errors, error => error.Code == AiValidationErrorCodes.DuplicateDeterminateField);
    }

    [Fact]
    public void Modify_disposition_keeps_ai_original_value_reason_and_actor()
    {
        var candidate = Candidate("c1", "material-composition", "cotton-95");
        AiGovernanceRules.Instance.ValidateDisposition(Disposition(candidate.Value, "cotton-92"), candidate);

        var missingHumanValue = Assert.Throws<AiContractException>(() =>
            AiGovernanceRules.Instance.ValidateDisposition(
                Disposition(candidate.Value, null), candidate));
        var wrongOriginal = Assert.Throws<AiContractException>(() =>
            AiGovernanceRules.Instance.ValidateDisposition(
                Disposition("tampered-original", "cotton-92"), candidate));
        var acceptWithHumanValue = Assert.Throws<AiContractException>(() =>
            AiGovernanceRules.Instance.ValidateDisposition(
                Disposition(candidate.Value, "cotton-92") with { Kind = AiDispositionKinds.Accept }, candidate));

        Assert.Equal(AiErrorCodes.ValidationFailed, missingHumanValue.ErrorCode);
        Assert.Equal(AiErrorCodes.ValidationFailed, wrongOriginal.ErrorCode);
        Assert.Equal(AiErrorCodes.ValidationFailed, acceptWithHumanValue.ErrorCode);
    }

    [Fact]
    public void Gap_suggestions_are_independent_and_validated()
    {
        var accepted = AiGovernanceRules.Instance.Validate(Output(), AllowedFields, AllowedUnits);
        var badKind = Assert.Throws<AiContractException>(() =>
            AiGovernanceRules.Instance.Validate(
                Output() with { Gaps = [new AiGapSuggestion("g1", "target-market", "GUESS", "which market?")] },
                AllowedFields, AllowedUnits));

        var gap = Assert.Single(accepted.Gaps);
        Assert.Equal(AiGapKinds.MissingInformation, gap.Kind);
        Assert.Equal(AiErrorCodes.ValidationFailed, badKind.ErrorCode);
    }

    [Fact]
    public void Serialization_shape_is_frozen()
    {
        var output = Output();
        var result = AiGovernanceRules.Instance.Validate(output, AllowedFields, AllowedUnits);

        var outputJson = JsonSerializer.Serialize(output, Json);
        var resultJson = JsonSerializer.Serialize(result, Json);

        Assert.Equal(
            """{"ruleSetVersion":"AI-DOC-EXTRACTION@1.0.0","envelope":{"model":{"id":"claude-fable-5","version":1},"gatewayRoute":"gw-primary","promptTemplate":{"id":"TPL-EXTRACT","version":3},"outputSchema":{"id":"SCHEMA-DOC-FIELDS","version":2},"inputRefs":[{"id":"DOC-77","version":1}]},"candidates":[{"candidateId":"c1","targetField":"style-number","value":"STY-1001","factClass":"AI_INFERENCE","confidence":0.92,"sourceLocation":{"document":{"id":"DOC-77","version":1},"page":2,"region":"top-right"},"unit":null,"abstained":false,"authoritySource":null,"verificationMethod":null}],"gaps":[{"gapId":"g1","targetField":"target-market","kind":"MISSING_INFORMATION","question":"Which target market applies?"}]}""",
            outputJson);
        Assert.Equal(
            """{"decision":"ACCEPTED","errors":[],"candidates":[{"candidateId":"c1","targetField":"style-number","value":"STY-1001","factClass":"AI_INFERENCE","confidence":0.92,"sourceLocation":{"document":{"id":"DOC-77","version":1},"page":2,"region":"top-right"},"unit":null,"abstained":false,"authoritySource":null,"verificationMethod":null}],"gaps":[{"gapId":"g1","targetField":"target-market","kind":"MISSING_INFORMATION","question":"Which target market applies?"}],"ruleSetVersion":"AI-DOC-EXTRACTION@1.0.0"}""",
            resultJson);

        var roundTrip = JsonSerializer.Deserialize<AiStructuredOutput>(outputJson, Json);
        Assert.Equal(output.Envelope, roundTrip!.Envelope with { InputRefs = output.Envelope.InputRefs });
        Assert.Equal(output.Candidates.Single(), roundTrip.Candidates.Single());
    }

    [Fact]
    public void Validation_is_deterministic_across_repeated_runs()
    {
        var output = Output() with
        {
            Candidates =
            [
                Candidate("c1", "unknown-field", "x"),
                Candidate("c2", "material-composition", "95") with { Unit = "KILOGRAM" }
            ]
        };

        var first = AiGovernanceRules.Instance.Validate(output, AllowedFields, AllowedUnits);
        var second = AiGovernanceRules.Instance.Validate(output, AllowedFields, AllowedUnits);

        Assert.Equal(JsonSerializer.Serialize(first, Json), JsonSerializer.Serialize(second, Json));
    }

    private static AiRunEnvelope Envelope() => new(
        new AiVersionedReference("claude-fable-5", 1),
        "gw-primary",
        new AiVersionedReference("TPL-EXTRACT", 3),
        new AiVersionedReference("SCHEMA-DOC-FIELDS", 2),
        [new AiVersionedReference("DOC-77", 1)]);

    private static AiFieldCandidate Candidate(string id, string field, string value) => new(
        id, field, value, AiFactClasses.AiInference, 0.92m,
        new AiSourceLocation(new AiVersionedReference("DOC-77", 1), 2, "top-right"));

    private static AiStructuredOutput Output() => new(
        AiContract.RuleSetVersion,
        Envelope(),
        [Candidate("c1", "style-number", "STY-1001")],
        [new AiGapSuggestion("g1", "target-market", AiGapKinds.MissingInformation, "Which target market applies?")]);

    private static AiDisposition Disposition(string aiOriginal, string? humanValue) => new(
        "d1", "c1", AiDispositionKinds.Modify, aiOriginal, "corrected against label", "reviewer-a", humanValue);
}
