using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Receiving;
using OpenLIMS.Modules.Labeling;
using Xunit;

namespace OpenLIMS.Labeling.UnitTests;

[Trait("Profile", "labeling")]
public sealed class LabelingRulesTests
{
    [Fact]
    public void Barcode_round_trip_preserves_only_version_type_and_opaque_reference()
    {
        var reference = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        var payload = LabelBarcodeCodec.Create(ReceivingLabelObjectTypes.ReceivedItem, reference);
        var parsed = LabelBarcodeCodec.TryParse(payload, out var barcode, out var errorCode);

        Assert.True(parsed);
        Assert.NotNull(barcode);
        Assert.Equal("OL1", barcode.FormatVersion);
        Assert.Equal("RI", barcode.ObjectType);
        Assert.Equal(reference, barcode.OpaqueReference);
        Assert.DoesNotContain("customer", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, errorCode);
    }

    [Fact]
    public void Damaged_or_unknown_barcode_fails_closed()
    {
        var valid = LabelBarcodeCodec.Create(ReceivingLabelObjectTypes.Container, Guid.NewGuid());
        var damaged = valid[..^1] + (valid[^1] == '0' ? '1' : '0');

        Assert.False(LabelBarcodeCodec.TryParse(damaged, out _, out var damagedError));
        Assert.Equal(LabelingErrorCodes.BarcodeInvalid, damagedError);
        Assert.False(LabelBarcodeCodec.TryParse(valid.Replace("OL1", "OL2", StringComparison.Ordinal), out _, out var versionError));
        Assert.Equal(LabelingErrorCodes.BarcodeVersionUnsupported, versionError);
    }

    [Fact]
    public void Tspl_template_is_versioned_50x30_and_distinguishes_object_type()
    {
        var snapshot = Snapshot(ReceivingLabelObjectTypes.ReceivedItem);

        var rendered = Encoding.UTF8.GetString(TsplLabelRenderer.Render(snapshot));

        Assert.Contains("SIZE 50 mm,30 mm", rendered, StringComparison.Ordinal);
        Assert.Contains("QRCODE", rendered, StringComparison.Ordinal);
        Assert.Contains("实物 ITEM", rendered, StringComparison.Ordinal);
        Assert.Contains(snapshot.BusinessNumber, rendered, StringComparison.Ordinal);
        Assert.Contains(LabelTemplateVersions.ReceivedItem, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(snapshot.CustomerId, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(snapshot.ServiceOrderId, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_targets_are_rejected_before_side_effects()
    {
        var target = new LabelPrintTarget("CT", Guid.NewGuid().ToString("N"), 1);
        var request = new CreateLabelJobsRequest("printer-a", [target, target]);

        var exception = Assert.Throws<LabelingDomainException>(() =>
            LabelingRules.ValidateCreate(request, "idem-a"));

        Assert.Equal(LabelingErrorCodes.ValidationFailed, exception.ErrorCode);
    }

    [Fact]
    public void Printer_configuration_is_exact_and_fail_closed()
    {
        var invalid = Printer() with { Port = 9101 };

        Assert.Throws<InvalidOperationException>(() => new LabelPrinterRegistry([invalid]));
        var registry = new LabelPrinterRegistry([Printer()]);
        Assert.Equal("printer-a", registry.Require("printer-a").PrinterId);
        var missing = Assert.Throws<LabelingDomainException>(() => registry.Require("missing"));
        Assert.Equal(LabelingErrorCodes.PrinterNotConfigured, missing.ErrorCode);
    }

    [Fact]
    public void Server_configuration_binds_a_versioned_logical_printer_without_client_fields()
    {
        var values = new Dictionary<string, string?>
        {
            ["Labeling:Printers:0:PrinterId"] = "printer-a",
            ["Labeling:Printers:0:LaboratoryId"] = "lab-a",
            ["Labeling:Printers:0:DisplayName"] = "Receiving printer",
            ["Labeling:Printers:0:Host"] = "printer-a.internal",
            ["Labeling:Printers:0:Port"] = "9100",
            ["Labeling:Printers:0:Protocol"] = "TSPL2",
            ["Labeling:Printers:0:ConfigurationVersion"] = "1.0.0",
            ["Labeling:Printers:0:Enabled"] = "true"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var printers = configuration.GetSection("Labeling:Printers").Get<LogicalLabelPrinter[]>();

        Assert.NotNull(printers);
        Assert.Equal(Printer(), printers[0]);
    }

    [Fact]
    public void Multidimensional_label_authorization_does_not_expand_from_group_membership()
    {
        var allowed = Authorization(
            Claim("organization_group", "group-a"),
            Claim(ReceivingClaimTypes.Capability, ReceivingCapabilities.LabelPrint),
            Claim(ReceivingClaimTypes.LegalEntity, "legal-a"),
            Claim(ReceivingClaimTypes.Laboratory, "lab-a"),
            Claim(ReceivingClaimTypes.Customer, "customer-secret"),
            Claim(ReceivingClaimTypes.ServiceOrder, "order-secret"));
        Assert.True(allowed.IsAuthorized(Snapshot("CT"), ReceivingCapabilities.LabelPrint));

        var denied = Authorization(
            Claim("organization_group", "group-a"),
            Claim(ReceivingClaimTypes.Capability, ReceivingCapabilities.LabelPrint),
            Claim(ReceivingClaimTypes.LegalEntity, "legal-a"),
            Claim(ReceivingClaimTypes.Laboratory, "lab-b"),
            Claim(ReceivingClaimTypes.Customer, "customer-secret"),
            Claim(ReceivingClaimTypes.ServiceOrder, "order-secret"));

        Assert.False(denied.IsAuthorized(Snapshot("CT"), ReceivingCapabilities.LabelPrint));
    }

    private static LogicalLabelPrinter Printer() => new(
        "printer-a",
        "lab-a",
        "Receiving printer",
        "printer-a.internal",
        9100,
        "TSPL2",
        "1.0.0",
        true);

    private static ReceivingLabelObjectSnapshot Snapshot(string objectType) => new(
        objectType,
        Guid.NewGuid().ToString("N"),
        1,
        "group-a",
        "legal-a",
        "lab-a",
        "LAB-A",
        "customer-secret",
        "order-secret",
        $"LAB-A-{objectType}-20260724-000001",
        Guid.NewGuid().ToString("N"),
        "OL1",
        objectType == "RI" ? "QUARANTINED" : "REGISTERED");

    private static HttpClaimsLabelingAuthorization Authorization(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        return new HttpClaimsLabelingAuthorization(new HttpContextAccessor { HttpContext = context });
    }

    private static Claim Claim(string type, string value) => new(type, value);
}
