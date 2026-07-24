using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Receiving;
using Xunit;

namespace OpenLIMS.Receiving.UnitTests;

[Trait("Profile", "receiving")]
public sealed class ReceivingRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_request_keeps_container_and_complete_toy_boundaries()
    {
        var request = ValidRequest();

        ReceivingRules.Validate(request, "idem-001", Now);
        var plan = ReceivingRules.CreatePlan(
            request,
            new SequentialIdGenerator(),
            "group-a",
            "actor-a",
            Now,
            "LAB-A");

        Assert.Single(plan.Containers);
        Assert.Equal(2, plan.Containers[0].Items.Count);
        Assert.NotEqual(plan.Containers[0].Id, plan.Containers[0].Items[0].Id);
        Assert.NotEqual(plan.Containers[0].Items[0].Id, plan.Containers[0].Items[1].Id);
        Assert.StartsWith("RCP-", plan.Number, StringComparison.Ordinal);
        Assert.All(plan.Containers[0].Items, item => Assert.StartsWith("ITM-", item.Number, StringComparison.Ordinal));
    }

    [Fact]
    public void Quantity_greater_than_one_cannot_hide_multiple_complete_toys_in_one_identity()
    {
        var request = ValidRequest() with
        {
            Containers = [ValidContainer() with { ReceivedItems = [ValidItem() with { Quantity = 2 }] }]
        };

        var exception = Assert.Throws<ReceivingDomainException>(() => ReceivingRules.Validate(request, "idem-002", Now));

        Assert.Equal(ReceivingErrorCodes.IdentityGranularityUnresolved, exception.ErrorCode);
    }

    [Fact]
    public void Empty_container_is_rejected_without_creating_a_sample_like_fallback()
    {
        var request = ValidRequest() with
        {
            Containers = [ValidContainer() with { ReceivedItems = [] }]
        };

        var exception = Assert.Throws<ReceivingDomainException>(() => ReceivingRules.Validate(request, "idem-003", Now));

        Assert.Equal(ReceivingErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Future_arrival_is_rejected()
    {
        var request = ValidRequest() with { ArrivalAt = Now.AddMinutes(6) };

        var exception = Assert.Throws<ReceivingDomainException>(() => ReceivingRules.Validate(request, "idem-004", Now));

        Assert.Equal(ReceivingErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Canonical_request_hash_is_deterministic_and_payload_sensitive()
    {
        var request = ValidRequest();

        var first = ReceivingRules.RequestHash(request);
        var second = ReceivingRules.RequestHash(request);
        var changed = ReceivingRules.RequestHash(request with { CustomerId = "customer-b" });

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void Invalid_id_generator_fails_closed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ReceivingRules.CreatePlan(
            ValidRequest(),
            new InvalidIdGenerator(),
            "group-a",
            "actor-a",
            Now,
            "LAB-A"));

        Assert.Contains("REC.ID_GENERATOR_INVALID", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lab code")]
    [InlineData("LAB_CODE")]
    [InlineData("LAB-CODE-IS-TOO-LONG")]
    public void Untrusted_or_invalid_laboratory_code_fails_closed(string laboratoryCode)
    {
        var exception = Assert.Throws<ReceivingDomainException>(() => ReceivingRules.CreatePlan(
            ValidRequest(),
            new SequentialIdGenerator(),
            "group-a",
            "actor-a",
            Now,
            laboratoryCode));

        Assert.Equal(ReceivingErrorCodes.AuthorizationDenied, exception.ErrorCode);
    }

    internal static RegisterReceiptRequest ValidRequest() => new(
        "legal-a",
        "lab-a",
        "customer-a",
        "order-a",
        Now.AddMinutes(-5),
        [ValidContainer()]);

    internal static RegisterContainerRequest ValidContainer() => new(
        "BOX-01",
        "carton",
        "intact",
        "seal intact",
        [
            ValidItem(),
            ValidItem() with { SerialNumber = "SERIAL-002", Color = "blue" }
        ]);

    internal static RegisterReceivedItemRequest ValidItem() => new(
        "Hard plastic toy set",
        "MODEL-001",
        "BATCH-001",
        "SERIAL-001",
        "red",
        "intact",
        "sealed",
        "intact",
        1,
        "set");

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        private int _next;

        public string NewId()
        {
            _next++;
            return new Guid(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)_next).ToString("N");
        }
    }

    private sealed class InvalidIdGenerator : IIdGenerator
    {
        public string NewId() => "not-a-guid";
    }
}
