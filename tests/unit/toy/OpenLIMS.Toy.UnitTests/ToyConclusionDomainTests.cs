using OpenLIMS.Contracts.Toy;
using OpenLIMS.Modules.Toy;
using Xunit;

namespace OpenLIMS.Toy.UnitTests;

[Trait("Profile", "toy")]
public sealed class ToyConclusionDomainTests
{
    [Fact]
    public void Item_conformity_is_deterministic_and_rejects_custom_wording()
    {
        var request = ItemRequest();

        var first = ToyConclusionDomain.ValidateItemConformityRequest(request);
        var second = ToyConclusionDomain.ValidateItemConformityRequest(request);

        Assert.Equal(first.Statement, second.Statement);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Contains("result-group-1@2", first.Statement, StringComparison.Ordinal);
        Assert.Equal(64, first.ContentHash.Length);
        Assert.Equal(
            ToyErrorCodes.ConclusionPolicyUnknown,
            Assert.Throws<ToyDomainException>(() =>
                ToyConclusionDomain.ValidateItemConformityRequest(
                    request with { CustomStatement = "全部符合" })).ErrorCode);
    }

    [Fact]
    public void Tested_scope_requires_every_coverage_decision_and_mandatory_uncovered_scope()
    {
        var valid = SignedTestedScopeRequest();
        var missingCoverage = valid with
        {
            TestUnits = [valid.TestUnits[0] with { CoverageDecisionRef = null, CoverageDecisionVersion = 0 }]
        };

        Assert.Equal(
            ToyErrorCodes.ConclusionEvidenceIncomplete,
            Assert.Throws<ToyDomainException>(() =>
                ToyConclusionDomain.ValidateTestedScopeConformityRequest(missingCoverage)).ErrorCode);
        Assert.Equal(
            ToyErrorCodes.ConclusionEvidenceIncomplete,
            Assert.Throws<ToyDomainException>(() =>
                ToyConclusionDomain.ValidateTestedScopeConformityRequest(
                    valid with { UncoveredScopes = [] })).ErrorCode);
    }

    [Fact]
    public void Tested_scope_binds_reauthentication_intent_and_canonical_content_hash()
    {
        var request = SignedTestedScopeRequest();

        var draft = ToyConclusionDomain.ValidateTestedScopeConformityRequest(request);

        Assert.Equal(request.SignedContentHash, draft.ContentHash);
        Assert.Equal("REAUTH-1", draft.ReauthenticationRef.Id);
        Assert.Equal(3, draft.ReauthenticationRef.Version);
        Assert.Equal("I approve the tested scope conclusion", draft.SigningIntent);
        Assert.Contains("未覆盖项（强制披露）", draft.Statement, StringComparison.Ordinal);

        foreach (var invalid in new[]
        {
            request with { ReauthenticationRef = null },
            request with { SigningIntent = " " },
            request with { SignedContentHash = new string('0', 64) }
        })
        {
            Assert.Equal(
                ToyErrorCodes.ConclusionSignatureInvalid,
                Assert.Throws<ToyDomainException>(() =>
                    ToyConclusionDomain.ValidateTestedScopeConformityRequest(invalid)).ErrorCode);
        }
    }

    [Fact]
    public void Unknown_rule_set_and_fictitious_whole_item_fail_closed()
    {
        var request = SignedTestedScopeRequest();

        Assert.Equal(
            ToyErrorCodes.ConclusionPolicyUnknown,
            Assert.Throws<ToyDomainException>(() =>
                ToyConclusionDomain.ValidateTestedScopeConformityRequest(
                    request with { RuleSetVersion = "TOY-CONCLUSION-COVERAGE@latest" })).ErrorCode);
        Assert.Equal(
            ToyErrorCodes.FictitiousWholeItemConclusion,
            Assert.Throws<ToyDomainException>(() =>
                ToyConclusionDomain.ValidateTestedScopeConformityRequest(
                    Resign(request with { IsFictitiousWholeItemConclusion = true }))).ErrorCode);
    }

    [Fact]
    public void External_references_are_informational_only()
    {
        var request = SignedTestedScopeRequest();
        var participating = request with
        {
            ExternalReferences =
            [
                new ExternalReferenceInput("CERTIFIER", "CERT-1", "whole product", false)
            ]
        };

        Assert.Equal(
            ToyErrorCodes.ConclusionPolicyUnknown,
            Assert.Throws<ToyDomainException>(() =>
                ToyConclusionDomain.ValidateTestedScopeConformityRequest(
                    Resign(participating))).ErrorCode);
    }

    [Fact]
    public void Separation_of_duty_requires_known_recorders_and_rejects_the_approver()
    {
        ToyConclusionDomain.ValidateSeparationOfDuty("approver", ["recorder-a", "recorder-b"]);

        Assert.Equal(
            ToyErrorCodes.ConclusionEvidenceUnknown,
            Assert.Throws<ToyDomainException>(() =>
                ToyConclusionDomain.ValidateSeparationOfDuty("approver", [])).ErrorCode);
        Assert.Equal(
            ToyErrorCodes.ConclusionSodViolation,
            Assert.Throws<ToyDomainException>(() =>
                ToyConclusionDomain.ValidateSeparationOfDuty("approver", ["recorder-a", "approver"])).ErrorCode);
    }

    [Fact]
    public void Canonical_hash_is_independent_of_input_collection_order()
    {
        var request = SignedTestedScopeRequest();
        var reordered = request with
        {
            TestUnits = request.TestUnits.Reverse().ToArray(),
            UncoveredScopes = request.UncoveredScopes.Reverse().ToArray(),
            ExternalReferences = request.ExternalReferences?.Reverse().ToArray()
        };

        Assert.Equal(
            ToyConclusionDomain.CalculateTestedScopeContentHash(request),
            ToyConclusionDomain.CalculateTestedScopeContentHash(reordered));
    }

    private static CreateItemConformityConclusionRequest ItemRequest() => new(
        ToyConclusionContract.RuleSetVersion,
        "result-group-1",
        2,
        "REQ-1",
        4,
        null);

    private static CreateTestedScopeConformityConclusionRequest SignedTestedScopeRequest()
    {
        var request = new CreateTestedScopeConformityConclusionRequest(
            ToyConclusionContract.RuleSetVersion,
            "product-1",
            5,
            "plan-1",
            3,
            [TestUnit("unit-b", "result-group-b", 2), TestUnit("unit-a", "result-group-a", 1)],
            [
                new UncoveredScopeInput("CHEMICAL", ToyUncoveredReasons.NotTested, "not ordered"),
                new UncoveredScopeInput("FLAMMABILITY", ToyUncoveredReasons.NotApplicable, "material excluded")
            ],
            [new ExternalReferenceInput("CERTIFIER", "CERT-1", "market access", true)],
            null,
            false,
            new ToyVersionedReference("REAUTH-1", 3),
            "I approve the tested scope conclusion",
            new string('0', 64));
        return Resign(request);
    }

    private static CreateTestedScopeConformityConclusionRequest Resign(
        CreateTestedScopeConformityConclusionRequest request) =>
        request with
        {
            SignedContentHash = ToyConclusionDomain.CalculateTestedScopeContentHash(request)
        };

    private static TestUnitEvidenceInput TestUnit(
        string testUnitId,
        string resultGroupId,
        long adoptionVersion) => new(
        testUnitId,
        $"physical-{testUnitId}",
        2,
        $"hazard-{testUnitId}",
        4,
        resultGroupId,
        adoptionVersion,
        $"graph-{testUnitId}",
        3,
        $"coverage-{testUnitId}",
        2,
        ["REQ-1@4"]);
}
