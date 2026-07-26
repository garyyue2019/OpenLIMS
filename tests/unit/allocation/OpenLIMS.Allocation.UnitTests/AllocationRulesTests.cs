using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Allocation;
using OpenLIMS.Modules.Allocation;
using Xunit;

namespace OpenLIMS.Allocation.UnitTests;

[Trait("Profile", "allocation")]
public sealed class AllocationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_request_normalizes_all_pinned_references()
    {
        var validated = AllocationRules.ValidateRequest(Request(), Now);

        Assert.Equal(AllocationSubjectTypes.ReceivedItem, validated.Subject.SubjectType);
        Assert.Equal("ITEM-1", validated.Subject.Id);
        Assert.Equal("ITEM-1", validated.ReceivedItemId);
        Assert.Equal("MASS", validated.Dimension);
        Assert.Equal("GRAM", validated.Unit);
        Assert.Equal(1, validated.IdentityAssignment.Version);
    }

    [Fact]
    public void Received_item_subject_must_match_receiving_gate_target()
    {
        var exception = Assert.Throws<AllocationDomainException>(() =>
            AllocationRules.ValidateRequest(Request() with { ReceivedItemId = "ITEM-OTHER" }, Now));

        Assert.Equal(AllocationErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Expired_valid_until_fails_closed_before_any_gate()
    {
        var exception = Assert.Throws<AllocationDomainException>(() =>
            AllocationRules.ValidateRequest(Request() with { ValidUntil = Now.AddSeconds(-1) }, Now));

        Assert.Equal(AllocationErrorCodes.AllocationExpired, exception.ErrorCode);
    }

    [Fact]
    public void Unknown_rule_set_or_dimension_is_rejected()
    {
        var unknownRule = Assert.Throws<AllocationDomainException>(() =>
            AllocationRules.ValidateRequest(Request() with { RuleSetVersion = "TASK-ALLOCATION@latest" }, Now));
        var unknownDimension = Assert.Throws<AllocationDomainException>(() =>
            AllocationRules.ValidateRequest(Request() with { Dimension = "WEIGHT" }, Now));

        Assert.Equal(AllocationErrorCodes.ApplicabilityUnknown, unknownRule.ErrorCode);
        Assert.Equal(AllocationErrorCodes.ValidationFailed, unknownDimension.ErrorCode);
    }

    [Theory]
    [InlineData("ALLOWED", null)]
    [InlineData("BLOCKED", AllocationErrorCodes.EligibilityBlocked)]
    [InlineData("UNKNOWN", AllocationErrorCodes.ApplicabilityUnknown)]
    public void Gate_decisions_map_to_fail_closed_outcomes(string decision, string? expectedError)
    {
        if (expectedError is null)
        {
            var gate = AllocationRules.RequireAllowed(
                AllocationGateSources.Scope, decision, 3, "SCOPE-LINE-GATE@1.0.0", []);
            Assert.Equal(3, gate.PinnedVersion);
            return;
        }

        var exception = Assert.Throws<AllocationDomainException>(() =>
            AllocationRules.RequireAllowed(
                AllocationGateSources.Scope, decision, 3, "SCOPE-LINE-GATE@1.0.0", ["REASON"]));
        Assert.Equal(expectedError, exception.ErrorCode);
        Assert.Equal(AllocationGateSources.Scope, exception.GateSource);
    }

    [Fact]
    public void Version_conflict_and_destructive_conflict_block_posting()
    {
        var conflict = Assert.Throws<AllocationDomainException>(() =>
            AllocationRules.RequirePostable(1, new AllocationSubjectState(2, false)));
        var destructive = Assert.Throws<AllocationDomainException>(() =>
            AllocationRules.RequirePostable(2, new AllocationSubjectState(2, true)));

        Assert.Equal(AllocationErrorCodes.ExpectedVersionConflict, conflict.ErrorCode);
        Assert.Equal(AllocationErrorCodes.DestructiveConflict, destructive.ErrorCode);
    }

    [Fact]
    public void Non_destructive_allocations_can_coexist()
    {
        AllocationRules.RequirePostable(3, new AllocationSubjectState(3, false));
    }

    [Fact]
    public void Status_pins_rule_set_and_subject_allocation_version()
    {
        var allocation = Result(state: AllocationStates.Active, subjectVersion: 3);

        var allowed = AllocationRules.EvaluateStatus(Status(3), allocation, 3, Now);
        var stale = AllocationRules.EvaluateStatus(Status(2), allocation, 3, Now);
        var unknownRule = AllocationRules.EvaluateStatus(
            Status(3) with { RuleSetVersion = "TASK-ALLOCATION@latest" }, allocation, 3, Now);
        var missing = AllocationRules.EvaluateStatus(Status(1), null, null, Now);

        Assert.Equal(AllocationStatusDecisions.Allowed, allowed.Decision);
        Assert.Equal(AllocationStatusDecisions.Unknown, stale.Decision);
        Assert.Contains(AllocationStatusReasons.SubjectVersionMismatch, stale.ReasonCodes);
        Assert.Equal(AllocationStatusDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(AllocationStatusReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
        Assert.Equal(AllocationStatusDecisions.Blocked, missing.Decision);
        Assert.Contains(AllocationStatusReasons.AllocationRequired, missing.ReasonCodes);
    }

    [Fact]
    public void Released_or_expired_allocation_status_is_blocked()
    {
        var released = AllocationRules.EvaluateStatus(
            Status(4), Result(AllocationStates.Released, 4), 4, Now);
        var expired = AllocationRules.EvaluateStatus(
            Status(3), Result(AllocationStates.Active, 3) with { ValidUntil = Now.AddSeconds(-1) }, 3, Now);

        Assert.Equal(AllocationStatusDecisions.Blocked, released.Decision);
        Assert.Contains(AllocationStatusReasons.AllocationReleased, released.ReasonCodes);
        Assert.Equal(AllocationStatusDecisions.Blocked, expired.Decision);
        Assert.Contains(AllocationStatusReasons.AllocationExpired, expired.ReasonCodes);
    }

    [Fact]
    public async Task Authorization_requires_all_exact_scope_claims()
    {
        var context = new DefaultHttpContext
        {
            User = Principal(includeProductCategory: true)
        };
        var port = new HttpClaimsAllocationAuthorizationPort(new HttpContextAccessor { HttpContext = context });

        var allowed = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);
        context.User = Principal(includeProductCategory: false);
        var denied = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    internal static CreateTestObjectAllocationRequest Request() => new(
        0,
        AllocationContract.RuleSetVersion,
        ObjectScope(),
        new AllocationSubjectReference(AllocationSubjectTypes.ReceivedItem, "ITEM-1", 1),
        new AllocationVersionedReference("SIA-1", 1),
        "ITEM-1",
        3,
        "00000000000000000000000000000030",
        2,
        new string('a', 64),
        new AllocationVersionedReference("PLAN-STEP-1", 1),
        "Tensile strength execution",
        1,
        false,
        "00000000000000000000000000000031",
        2,
        80.00m,
        "MASS",
        "GRAM",
        new AllocationVersionedReference("STORAGE-COND-1", 1),
        Now.AddDays(7));

    private static AllocationObjectContext ObjectScope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS");

    private static TestObjectAllocationResult Result(string state, long subjectVersion) => new(
        "00000000000000000000000000000032",
        state,
        subjectVersion,
        AllocationContract.RuleSetVersion,
        ObjectScope(),
        new AllocationSubjectReference(AllocationSubjectTypes.ReceivedItem, "ITEM-1", 1),
        new AllocationVersionedReference("SIA-1", 1),
        "00000000000000000000000000000030",
        new string('a', 64),
        new AllocationVersionedReference("PLAN-STEP-1", 1),
        "Tensile strength execution",
        1,
        false,
        "00000000000000000000000000000031",
        80.00m,
        "MASS",
        "GRAM",
        new AllocationVersionedReference("STORAGE-COND-1", 1),
        Now.AddDays(7),
        null,
        new AllocationGateResult(AllocationGateSources.Receiving, "ALLOWED", 3, "REC-ELIGIBILITY@2.0.0", []),
        new AllocationGateResult(AllocationGateSources.Scope, "ALLOWED", 2, "SCOPE-LINE-GATE@1.0.0", []),
        new AllocationGateResult(AllocationGateSources.Quantity, "ALLOWED", 2, "SAMPLE-QUANTITY@1.0.0", []),
        "actor-a",
        Now,
        null,
        null,
        null);

    private static AllocationStatusRequest Status(long expectedVersion) => new(
        "group-a",
        "00000000000000000000000000000032",
        expectedVersion,
        AllocationContract.RuleSetVersion);

    private static AllocationAuthorizationRequest AuthRequest() => new(
        "group-a",
        "actor-a",
        ObjectScope(),
        AllocationCapabilities.Assign);

    private static ClaimsPrincipal Principal(bool includeProductCategory)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "actor-a"),
            new("organization_group", "group-a"),
            new(AllocationClaimTypes.Capability, AllocationCapabilities.Assign),
            new(AllocationClaimTypes.LegalEntity, "LEGAL-A"),
            new(AllocationClaimTypes.Laboratory, "LAB-A"),
            new(AllocationClaimTypes.Customer, "CUSTOMER-A"),
            new(AllocationClaimTypes.ServiceOrder, "ORDER-A")
        };
        if (includeProductCategory) claims.Add(new Claim(AllocationClaimTypes.ProductCategory, "TOYS"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
