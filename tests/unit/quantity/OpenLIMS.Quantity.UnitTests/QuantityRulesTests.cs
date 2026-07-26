using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Quantity;
using OpenLIMS.Modules.Quantity;
using Xunit;

namespace OpenLIMS.Quantity.UnitTests;

[Trait("Profile", "quantity")]
public sealed class QuantityRulesTests
{
    private static readonly Guid ReserveId = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid ReversalId = Guid.Parse("00000000-0000-0000-0000-000000000102");

    [Fact]
    public void Account_validation_fixes_single_dimension_unit_and_precision()
    {
        var (subject, configuration) = QuantityRules.ValidateAccount(AccountRequest());

        Assert.Equal(QuantitySubjectTypes.ReceivedItem, subject.SubjectType);
        Assert.Equal("ITEM-1", subject.Id);
        Assert.Equal(QuantityDimensions.Mass, configuration.Dimension);
        Assert.Equal("GRAM", configuration.Unit);
        Assert.Equal(2, configuration.PrecisionScale);
        Assert.Equal(0.20m, configuration.ConservationTolerance);
    }

    [Fact]
    public void Non_quantifiable_subject_fails_closed_without_pseudo_precision()
    {
        var exception = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.ValidateAccount(AccountRequest() with { SubjectQuantifiable = false }));

        Assert.Equal(QuantityErrorCodes.NotQuantifiable, exception.ErrorCode);
    }

    [Theory]
    [InlineData("WEIGHT", 2)]
    [InlineData(QuantityDimensions.Count, 2)]
    [InlineData(QuantityDimensions.Mass, 9)]
    public void Unknown_dimension_or_invalid_precision_is_rejected(string dimension, int precisionScale)
    {
        var exception = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.ValidateAccount(AccountRequest() with
            {
                Dimension = dimension,
                PrecisionScale = precisionScale
            }));

        Assert.Equal(QuantityErrorCodes.DimensionMismatch, exception.ErrorCode);
    }

    [Fact]
    public void Unknown_rule_set_or_entry_type_is_unknown_and_blocking()
    {
        var unknownRule = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.ValidateAccount(AccountRequest() with { RuleSetVersion = "SAMPLE-QUANTITY@latest" }));
        var unknownType = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.PlanPosting(
                Posting(QuantityEntryTypes.Receipt, 10m) with { EntryType = "TOPUP" },
                Configuration(),
                new QuantityBalances(1, 0m, 0m),
                null,
                null));

        Assert.Equal(QuantityErrorCodes.ApplicabilityUnknown, unknownRule.ErrorCode);
        Assert.Equal(QuantityErrorCodes.ApplicabilityUnknown, unknownType.ErrorCode);
    }

    [Fact]
    public void Receipt_increases_balance_and_precision_violation_is_rejected()
    {
        var plan = QuantityRules.PlanPosting(
            Posting(QuantityEntryTypes.Receipt, 100.25m),
            Configuration(),
            new QuantityBalances(1, 0m, 0m),
            null,
            null);
        var precision = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.PlanPosting(
                Posting(QuantityEntryTypes.Receipt, 0.005m),
                Configuration(),
                new QuantityBalances(1, 0m, 0m),
                null,
                null));

        Assert.Equal(100.25m, plan.ResultingBalance);
        Assert.Equal(0m, plan.ResultingReserved);
        Assert.Equal(QuantityErrorCodes.ValidationFailed, precision.ErrorCode);
    }

    [Fact]
    public void Reserve_and_consume_track_available_and_block_over_allocation()
    {
        var reserve = QuantityRules.PlanPosting(
            Posting(QuantityEntryTypes.Reserve, 80m),
            Configuration(),
            new QuantityBalances(2, 100m, 0m),
            null,
            null);
        var overAllocation = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.PlanPosting(
                Posting(QuantityEntryTypes.Allocate, 80m),
                Configuration(),
                new QuantityBalances(3, 100m, 80m),
                null,
                null));

        Assert.Equal(100m, reserve.ResultingBalance);
        Assert.Equal(80m, reserve.ResultingReserved);
        Assert.Equal(QuantityErrorCodes.InsufficientBalance, overAllocation.ErrorCode);
    }

    [Fact]
    public void Consuming_an_open_hold_releases_it_and_requires_exact_amount()
    {
        var hold = new QuantityEntrySnapshot(
            ReserveId, QuantityEntryTypes.Reserve, 80m, null, null, false, false, false);
        var consume = QuantityRules.PlanPosting(
            Posting(QuantityEntryTypes.Consume, 80m) with { ReservationId = ReserveId.ToString("N") },
            Configuration(),
            new QuantityBalances(3, 100m, 80m),
            null,
            hold);
        var mismatch = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.PlanPosting(
                Posting(QuantityEntryTypes.Consume, 50m) with { ReservationId = ReserveId.ToString("N") },
                Configuration(),
                new QuantityBalances(3, 100m, 80m),
                null,
                hold));
        var closed = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.PlanPosting(
                Posting(QuantityEntryTypes.Consume, 80m) with { ReservationId = ReserveId.ToString("N") },
                Configuration(),
                new QuantityBalances(4, 100m, 80m),
                null,
                hold with { ReservationClosed = true }));

        Assert.Equal(20m, consume.ResultingBalance);
        Assert.Equal(0m, consume.ResultingReserved);
        Assert.Equal(QuantityErrorCodes.ValidationFailed, mismatch.ErrorCode);
        Assert.Equal(QuantityErrorCodes.ValidationFailed, closed.ErrorCode);
    }

    [Fact]
    public void Negative_balance_is_blocked_for_loss_and_dispose()
    {
        var exception = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.PlanPosting(
                Posting(QuantityEntryTypes.Dispose, 130m),
                Configuration(),
                new QuantityBalances(2, 100m, 0m),
                null,
                null));

        Assert.Equal(QuantityErrorCodes.InsufficientBalance, exception.ErrorCode);
    }

    [Fact]
    public void Reversal_requires_unreversed_referenced_entry_and_inverts_effect()
    {
        var receipt = new QuantityEntrySnapshot(
            ReserveId, QuantityEntryTypes.Receipt, 30m, null, null, false, false, false);
        var reversal = QuantityRules.PlanPosting(
            Posting(QuantityEntryTypes.Reversal, 30m) with { ReferencedEntryId = ReserveId.ToString("N") },
            Configuration(),
            new QuantityBalances(2, 100m, 0m),
            receipt,
            null);
        var alreadyReversed = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.PlanPosting(
                Posting(QuantityEntryTypes.Reversal, 30m) with { ReferencedEntryId = ReserveId.ToString("N") },
                Configuration(),
                new QuantityBalances(3, 70m, 0m),
                receipt with { Reversed = true },
                null));

        Assert.Equal(70m, reversal.ResultingBalance);
        Assert.Equal(QuantityErrorCodes.ValidationFailed, alreadyReversed.ErrorCode);
    }

    [Fact]
    public void Restate_reapplies_original_direction_with_corrected_amount_once()
    {
        var reversal = new QuantityEntrySnapshot(
            ReversalId, QuantityEntryTypes.Reversal, 30m, ReserveId, null, false, false, false,
            QuantityEntryTypes.Receipt);
        var restate = QuantityRules.PlanPosting(
            Posting(QuantityEntryTypes.Restate, 25m) with { ReferencedEntryId = ReversalId.ToString("N") },
            Configuration(),
            new QuantityBalances(3, 70m, 0m),
            reversal,
            null);
        var duplicate = Assert.Throws<QuantityDomainException>(() =>
            QuantityRules.PlanPosting(
                Posting(QuantityEntryTypes.Restate, 25m) with { ReferencedEntryId = ReversalId.ToString("N") },
                Configuration(),
                new QuantityBalances(4, 95m, 0m),
                reversal with { Restated = true },
                null));

        Assert.Equal(95m, restate.ResultingBalance);
        Assert.Equal(QuantityErrorCodes.ValidationFailed, duplicate.ErrorCode);
    }

    [Fact]
    public void Availability_pins_rule_set_version_and_account_version()
    {
        var account = AccountResult(version: 3, balance: 100m, reserved: 30m);

        var allowed = QuantityRules.EvaluateAvailability(Availability(3, 70m), account);
        var insufficient = QuantityRules.EvaluateAvailability(Availability(3, 70.5m), account);
        var stale = QuantityRules.EvaluateAvailability(Availability(2, 10m), account);
        var unknownRule = QuantityRules.EvaluateAvailability(
            Availability(3, 10m) with { RuleSetVersion = "SAMPLE-QUANTITY@latest" }, account);
        var missing = QuantityRules.EvaluateAvailability(Availability(1, 10m), null);

        Assert.Equal(QuantityAvailabilityDecisions.Allowed, allowed.Decision);
        Assert.Equal(QuantityAvailabilityDecisions.Blocked, insufficient.Decision);
        Assert.Contains(QuantityAvailabilityReasons.InsufficientAvailable, insufficient.ReasonCodes);
        Assert.Equal(QuantityAvailabilityDecisions.Unknown, stale.Decision);
        Assert.Contains(QuantityAvailabilityReasons.AccountVersionMismatch, stale.ReasonCodes);
        Assert.Equal(QuantityAvailabilityDecisions.Unknown, unknownRule.Decision);
        Assert.Contains(QuantityAvailabilityReasons.RuleSetVersionUnknown, unknownRule.ReasonCodes);
        Assert.Equal(QuantityAvailabilityDecisions.Blocked, missing.Decision);
        Assert.Contains(QuantityAvailabilityReasons.AccountRequired, missing.ReasonCodes);
    }

    [Fact]
    public async Task Authorization_requires_all_exact_scope_claims()
    {
        var context = new DefaultHttpContext
        {
            User = Principal(includeProductCategory: true)
        };
        var port = new HttpClaimsQuantityAuthorizationPort(new HttpContextAccessor { HttpContext = context });

        var allowed = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);
        context.User = Principal(includeProductCategory: false);
        var denied = await port.AuthorizeAsync(AuthRequest(), TestContext.Current.CancellationToken);

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    private static CreateQuantityAccountRequest AccountRequest() => new(
        QuantityContract.RuleSetVersion,
        ObjectScope(),
        new QuantitySubjectReference(QuantitySubjectTypes.ReceivedItem, "ITEM-1", 1),
        true,
        QuantityDimensions.Mass,
        "GRAM",
        2,
        0.20m);

    private static PostQuantityEntryRequest Posting(string entryType, decimal amount) => new(
        1,
        QuantityContract.RuleSetVersion,
        entryType,
        amount);

    private static QuantityAccountConfiguration Configuration() => new(
        QuantityDimensions.Mass, "GRAM", 2, 0.20m);

    private static QuantityObjectContext ObjectScope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS");

    private static QuantityAccountResult AccountResult(long version, decimal balance, decimal reserved) => new(
        "00000000000000000000000000000001",
        version,
        QuantityContract.RuleSetVersion,
        ObjectScope(),
        new QuantitySubjectReference(QuantitySubjectTypes.ReceivedItem, "ITEM-1", 1),
        QuantityDimensions.Mass,
        "GRAM",
        2,
        0.20m,
        balance,
        reserved,
        balance - reserved,
        "actor-a",
        new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero));

    private static QuantityAvailabilityRequest Availability(long version, decimal requested) => new(
        "group-a",
        "00000000000000000000000000000001",
        version,
        QuantityContract.RuleSetVersion,
        requested);

    private static QuantityAuthorizationRequest AuthRequest() => new(
        "group-a",
        "actor-a",
        ObjectScope(),
        QuantityCapabilities.Post);

    private static ClaimsPrincipal Principal(bool includeProductCategory)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "actor-a"),
            new("organization_group", "group-a"),
            new(QuantityClaimTypes.Capability, QuantityCapabilities.Post),
            new(QuantityClaimTypes.LegalEntity, "LEGAL-A"),
            new(QuantityClaimTypes.Laboratory, "LAB-A"),
            new(QuantityClaimTypes.Customer, "CUSTOMER-A"),
            new(QuantityClaimTypes.ServiceOrder, "ORDER-A")
        };
        if (includeProductCategory) claims.Add(new Claim(QuantityClaimTypes.ProductCategory, "TOYS"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
