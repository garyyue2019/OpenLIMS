using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Scope;
using OpenLIMS.Modules.Scope;
using Xunit;

namespace OpenLIMS.Scope.UnitTests;

[Trait("Profile", "scope")]
public sealed class ScopeRulesTests
{
    [Theory]
    [InlineData(ScopeEvaluationModes.MeasuredOnly)]
    [InlineData(ScopeEvaluationModes.Evaluated)]
    [InlineData(ScopeEvaluationModes.NotEvaluated)]
    [InlineData(ScopeEvaluationModes.Waived)]
    public void Four_evaluation_modes_normalize_with_explicit_conditional_references(string mode)
    {
        var lines = ScopeRules.ValidateAndNormalize(Request(0, [Line(mode)]));

        var line = Assert.Single(lines);
        Assert.Equal(mode, line.EvaluationMode);
        Assert.Equal(64, line.ScopeLineId.Length);
        Assert.Equal(mode == ScopeEvaluationModes.Evaluated, line.LimitRule is not null);
        Assert.Equal(mode == ScopeEvaluationModes.Evaluated, line.DecisionRule is not null);
        Assert.Equal(mode == ScopeEvaluationModes.NotEvaluated, line.NonEvaluationReason is not null);
        Assert.Equal(mode == ScopeEvaluationModes.Waived, line.WaiverApproval is not null);
    }

    [Fact]
    public void Evaluated_line_without_limit_and_decision_fails_closed()
    {
        var line = Line(ScopeEvaluationModes.Evaluated) with { LimitRule = null };

        var exception = Assert.Throws<ScopeDomainException>(() =>
            ScopeRules.ValidateAndNormalize(Request(0, [line])));

        Assert.Equal(ScopeErrorCodes.EvaluationIncomplete, exception.ErrorCode);
    }

    [Fact]
    public void Non_evaluated_mode_rejects_conformity_fields()
    {
        var line = Line(ScopeEvaluationModes.MeasuredOnly) with { LimitRule = Ref("LIMIT-1") };

        var exception = Assert.Throws<ScopeDomainException>(() =>
            ScopeRules.ValidateAndNormalize(Request(0, [line])));

        Assert.Equal(ScopeErrorCodes.EvaluationConflict, exception.ErrorCode);
    }

    [Fact]
    public void Unknown_mode_or_rule_version_is_unknown_and_blocking()
    {
        var unknownMode = Assert.Throws<ScopeDomainException>(() =>
            ScopeRules.ValidateAndNormalize(Request(0, [Line("AUTO_PASS")])));
        var unknownRule = Assert.Throws<ScopeDomainException>(() =>
            ScopeRules.ValidateAndNormalize(Request(0, [Line(ScopeEvaluationModes.MeasuredOnly)]) with
            {
                RuleSetVersion = "SCOPE-LINE-GATE@latest"
            }));

        Assert.Equal(ScopeErrorCodes.ApplicabilityUnknown, unknownMode.ErrorCode);
        Assert.Equal(ScopeErrorCodes.ApplicabilityUnknown, unknownRule.ErrorCode);
    }

    [Fact]
    public void Duplicate_line_identity_is_rejected_even_when_method_changes()
    {
        var first = Line(ScopeEvaluationModes.MeasuredOnly);
        var second = first with { Method = Ref("METHOD-2") };

        var exception = Assert.Throws<ScopeDomainException>(() =>
            ScopeRules.ValidateAndNormalize(Request(0, [first, second])));

        Assert.Equal(ScopeErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Current_complete_version_is_allowed_and_old_version_is_unknown()
    {
        var line = Assert.Single(ScopeRules.ValidateAndNormalize(
            Request(0, [Line(ScopeEvaluationModes.Evaluated)])));
        var current = Result(2, [line]);
        var allowed = ScopeRules.Evaluate(Eligibility(2), current);
        var old = ScopeRules.Evaluate(Eligibility(1), current);
        var missing = ScopeRules.Evaluate(Eligibility(1), null);

        Assert.Equal(ScopeEligibilityDecisions.Allowed, allowed.Decision);
        Assert.Equal(ScopeEligibilityDecisions.Unknown, old.Decision);
        Assert.Contains(ScopeEligibilityReasons.MatrixVersionMismatch, old.ReasonCodes);
        Assert.Equal(ScopeEligibilityDecisions.Blocked, missing.Decision);
    }

    [Fact]
    public async Task Authorization_requires_all_exact_scope_claims()
    {
        var context = new DefaultHttpContext
        {
            User = Principal(includeProductCategory: true)
        };
        var port = new HttpClaimsScopeAuthorizationPort(new HttpContextAccessor { HttpContext = context });

        var allowed = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);
        context.User = Principal(includeProductCategory: false);
        var denied = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    private static SubmitScopeMatrixVersionRequest Request(long expected, IReadOnlyList<ScopeLineInput> lines) => new(
        expected,
        ScopeContract.RuleSetVersion,
        ObjectScope(),
        lines);

    private static ScopeLineInput Line(string mode) => new(
        ScopeSubjectTypes.FeatureNode,
        Ref("FEATURE-1"),
        Ref("MARKET-1"),
        Ref("REQ-1"),
        Ref("ITEM-1"),
        Ref("METHOD-1"),
        "OPTION-A",
        Ref("SAMPLE-REQ-1"),
        mode,
        Ref("WC-1"),
        "REPORT-1",
        mode == ScopeEvaluationModes.Evaluated ? Ref("LIMIT-1") : null,
        mode == ScopeEvaluationModes.Evaluated ? Ref("DECISION-1") : null,
        mode == ScopeEvaluationModes.NotEvaluated ? "Customer requested measurement without evaluation." : null,
        mode == ScopeEvaluationModes.Waived ? Ref("WAIVER-1") : null);

    private static ScopeVersionedReference Ref(string id) => new(id, 1);

    private static ScopeObjectContext ObjectScope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS");

    private static ScopeMatrixVersionResult Result(long version, IReadOnlyList<ScopeLineResult> lines) => new(
        "00000000000000000000000000000001",
        version,
        ScopeMatrixStates.Approved,
        ScopeContract.RuleSetVersion,
        ObjectScope(),
        lines,
        "actor-a",
        new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero));

    private static ScopeProductionEligibilityRequest Eligibility(long version) => new(
        "group-a",
        "00000000000000000000000000000001",
        version,
        ScopeContract.RuleSetVersion);

    private static ScopeAuthorizationRequest AuthRequest() => new(
        "group-a",
        "actor-a",
        ObjectScope(),
        ScopeCapabilities.Approve);

    private static ClaimsPrincipal Principal(bool includeProductCategory)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "actor-a"),
            new("organization_group", "group-a"),
            new(ScopeClaimTypes.Capability, ScopeCapabilities.Approve),
            new(ScopeClaimTypes.LegalEntity, "LEGAL-A"),
            new(ScopeClaimTypes.Laboratory, "LAB-A"),
            new(ScopeClaimTypes.Customer, "CUSTOMER-A"),
            new(ScopeClaimTypes.ServiceOrder, "ORDER-A")
        };
        if (includeProductCategory) claims.Add(new Claim(ScopeClaimTypes.ProductCategory, "TOYS"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
