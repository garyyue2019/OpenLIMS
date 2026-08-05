using OpenLIMS.Contracts.Billing;
using OpenLIMS.Modules.Billing;
using Xunit;

namespace OpenLIMS.Billing.UnitTests;

[Trait("Profile", "billing")]
public sealed class BillingIntegrationRulesTests
{
    [Fact]
    public void Export_validation_sorts_ids_and_rejects_duplicates()
    {
        var validated = BillingIntegrationRules.ValidateExport(new CreateBillingExportBatchRequest(
            BillingContract.ExportRuleSetVersion,
            ["00000000000000000000000000000002", "00000000000000000000000000000001"],
            "BILLING-EXPORT-V1", "export-1"));
        var duplicate = Assert.Throws<BillingDomainException>(() =>
            BillingIntegrationRules.ValidateExport(new CreateBillingExportBatchRequest(
                BillingContract.ExportRuleSetVersion,
                ["00000000000000000000000000000001", "00000000000000000000000000000001"],
                "BILLING-EXPORT-V1", "export-2")));

        Assert.Equal("00000000000000000000000000000001", validated.BillingEvidenceIds[0]);
        Assert.Equal(BillingErrorCodes.ValidationFailed, duplicate.ErrorCode);
    }

    [Fact]
    public void Export_items_and_hash_are_deterministic_and_include_adjustments()
    {
        var evidence = new[] { Evidence("02", 120m, -20m), Evidence("01", 80m, 5m) };

        var items = BillingIntegrationRules.BuildItems(evidence);
        var canonical = BillingIntegrationRules.Canonicalize(
            Scope(), "BILLING-EXPORT-V1", items);
        var reversed = BillingIntegrationRules.Canonicalize(
            Scope(), "BILLING-EXPORT-V1", items.Reverse().ToList());

        Assert.Equal("00000000000000000000000000000001", items[0].BillingEvidenceId);
        Assert.Equal(85m, items[0].NetAmount);
        Assert.Equal(canonical, reversed);
        Assert.Equal(BillingIntegrationRules.ComputeHash(canonical), BillingIntegrationRules.ComputeHash(reversed));
    }

    [Fact]
    public void Mixed_scope_or_currency_export_fails_closed()
    {
        var mixed = new[]
        {
            Evidence("01", 10m, 0m),
            Evidence("02", 10m, 0m) with
            {
                ObjectScope = Scope() with { CustomerId = "CUSTOMER-B" }
            }
        };

        var exception = Assert.Throws<BillingDomainException>(() => BillingIntegrationRules.BuildItems(mixed));

        Assert.Equal(BillingErrorCodes.ExportScopeMismatch, exception.ErrorCode);
    }

    [Fact]
    public void Erp_success_requires_complete_external_posting_confirmation()
    {
        var missing = Assert.Throws<BillingDomainException>(() =>
            BillingIntegrationRules.ValidateAttempt(
                BillingExternalSystems.Erp,
                new RecordBillingHandoffAttemptRequest(
                    BillingContract.HandoffRuleSetVersion, "try-1",
                    BillingHandoffOutcomes.Succeeded, "erp-ref")));
        var valid = BillingIntegrationRules.ValidateAttempt(
            BillingExternalSystems.Erp,
            new RecordBillingHandoffAttemptRequest(
                BillingContract.HandoffRuleSetVersion, "try-2",
                BillingHandoffOutcomes.Succeeded, "erp-ref", ErpPosting: new ErpPostingConfirmation(
                    "V-7", "COMPANY-A", 2026, 8, new DateOnly(2026, 8, 5))));

        Assert.Equal(BillingErrorCodes.HandoffConfirmationInvalid, missing.ErrorCode);
        Assert.Equal("V-7", valid.ErpPosting!.VoucherNumber);
    }

    [Fact]
    public void Invoice_success_requires_external_reference_but_forbids_erp_posting()
    {
        var valid = BillingIntegrationRules.ValidateAttempt(
            BillingExternalSystems.Invoice,
            new RecordBillingHandoffAttemptRequest(
                BillingContract.HandoffRuleSetVersion, "try-1",
                BillingHandoffOutcomes.Succeeded, "invoice-7"));
        var invalid = Assert.Throws<BillingDomainException>(() =>
            BillingIntegrationRules.ValidateAttempt(
                BillingExternalSystems.Invoice,
                new RecordBillingHandoffAttemptRequest(
                    BillingContract.HandoffRuleSetVersion, "try-2",
                    BillingHandoffOutcomes.Succeeded, "invoice-8", ErpPosting: new ErpPostingConfirmation(
                        "V-7", "COMPANY-A", 2026, 8, new DateOnly(2026, 8, 5)))));

        Assert.Equal("invoice-7", valid.ExternalReference);
        Assert.Equal(BillingErrorCodes.HandoffConfirmationInvalid, invalid.ErrorCode);
    }

    [Fact]
    public void Handoff_status_keeps_failed_unknown_and_different_visible()
    {
        var attempts = new[]
        {
            Attempt(1, BillingHandoffOutcomes.Failed),
            Attempt(2, BillingHandoffOutcomes.Different)
        };

        Assert.Equal(BillingHandoffOutcomes.Pending, BillingIntegrationRules.ResolveHandoffStatus([]));
        Assert.Equal(BillingHandoffOutcomes.Different, BillingIntegrationRules.ResolveHandoffStatus(attempts));
    }

    private static BillingEvidenceResult Evidence(string suffix, decimal amount, decimal adjustment) => new(
        $"000000000000000000000000000000{suffix}", BillingStages.BillableCandidate,
        BillingContract.RuleSetVersion, Scope(), $"group-{suffix}", 3, $"target-{suffix}",
        new BillingVersionedReference("CONTRACT-1", 1), "ITEM", "PRICE-1", amount,
        new BillingVersionedReference("CNY", 1), null,
        adjustment == 0 ? [] : [new BillingAdjustmentResult(
            Guid.NewGuid().ToString("N"), $"000000000000000000000000000000{suffix}",
            adjustment, "adjust", "actor", DateTimeOffset.MinValue)],
        "actor", DateTimeOffset.MinValue);

    private static BillingObjectContext Scope() => new(
        "LEGAL-A", "LAB-A", "CUSTOMER-A", "ORDER-A", "TOYS");

    private static BillingHandoffAttemptResult Attempt(int number, string outcome) => new(
        Guid.NewGuid().ToString("N"), "00000000000000000000000000000031", number,
        outcome, null, null, null, "actor", DateTimeOffset.MinValue);
}
