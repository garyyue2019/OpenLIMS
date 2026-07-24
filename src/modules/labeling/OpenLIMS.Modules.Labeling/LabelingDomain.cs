using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using OpenLIMS.Contracts.Labeling;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Labeling;

public sealed class LabelingDomainException(string errorCode) : InvalidOperationException(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

internal static class LabelingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
}

internal static class LabelingRules
{
    public const int MaximumTargets = 200;
    public const int MaximumReasonLength = 500;

    public static void ValidateCreate(CreateLabelJobsRequest request, string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdempotencyKey(idempotencyKey);
        if (string.IsNullOrWhiteSpace(request.PrinterId) || request.PrinterId.Length > 100 ||
            request.Targets is null || request.Targets.Count is < 1 or > MaximumTargets)
        {
            throw new LabelingDomainException(LabelingErrorCodes.ValidationFailed);
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in request.Targets)
        {
            if (target is null ||
                target.ObjectType is not (ReceivingLabelObjectTypes.Container or ReceivingLabelObjectTypes.ReceivedItem) ||
                !Guid.TryParse(target.ObjectId, out _) ||
                target.ObjectVersion <= 0 ||
                !distinct.Add($"{target.ObjectType}:{target.ObjectId}"))
            {
                throw new LabelingDomainException(LabelingErrorCodes.ValidationFailed);
            }
        }
    }

    public static string ValidateReason(string reason)
    {
        var normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > MaximumReasonLength)
        {
            throw new LabelingDomainException(LabelingErrorCodes.ReprintReasonRequired);
        }

        return normalized;
    }

    public static void ValidateIdempotencyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
        {
            throw new LabelingDomainException(LabelingErrorCodes.ValidationFailed);
        }
    }

    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string RequestHash(object request) =>
        Hash(JsonSerializer.Serialize(request, LabelingJson.Options));
}

internal sealed class LabelPrinterRegistry
{
    private readonly IReadOnlyDictionary<string, LogicalLabelPrinter> _printers;

    public LabelPrinterRegistry(IEnumerable<LogicalLabelPrinter> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        var entries = printers.ToArray();
        if (entries.Select(printer => printer.PrinterId).Distinct(StringComparer.Ordinal).Count() != entries.Length)
        {
            throw new InvalidOperationException("LABEL.PRINTER_CONFIGURATION_DUPLICATE");
        }

        foreach (var printer in entries)
        {
            if (string.IsNullOrWhiteSpace(printer.PrinterId) || printer.PrinterId.Length > 100 ||
                string.IsNullOrWhiteSpace(printer.LaboratoryId) || printer.LaboratoryId.Length > 100 ||
                string.IsNullOrWhiteSpace(printer.DisplayName) || printer.DisplayName.Length > 200 ||
                string.IsNullOrWhiteSpace(printer.ConfigurationVersion) || printer.ConfigurationVersion.Length > 100 ||
                printer.Port != 9100 || !string.Equals(printer.Protocol, "TSPL2", StringComparison.Ordinal) ||
                !Regex.IsMatch(printer.Host ?? string.Empty, "^[A-Za-z0-9.-]{1,253}$"))
            {
                throw new InvalidOperationException("LABEL.PRINTER_CONFIGURATION_INVALID");
            }
        }

        _printers = entries.ToDictionary(printer => printer.PrinterId, StringComparer.Ordinal);
    }

    public LogicalLabelPrinter Require(string printerId) =>
        _printers.TryGetValue(printerId, out var printer) && printer.Enabled
            ? printer
            : throw new LabelingDomainException(LabelingErrorCodes.PrinterNotConfigured);
}

internal interface ILabelingAuthorization
{
    bool IsAuthorized(ReceivingLabelObjectSnapshot snapshot, string capability);
    bool HasCapability(string capability);
}

internal sealed class HttpClaimsLabelingAuthorization(IHttpContextAccessor accessor) : ILabelingAuthorization
{
    public bool IsAuthorized(ReceivingLabelObjectSnapshot snapshot, string capability)
    {
        var user = accessor.HttpContext?.User;
        return user?.Identity?.IsAuthenticated == true &&
               HasExact(user, "organization_group", snapshot.OrganizationGroupId) &&
               HasExact(user, ReceivingClaimTypes.Capability, capability) &&
               HasExact(user, ReceivingClaimTypes.LegalEntity, snapshot.LegalEntityId) &&
               HasExact(user, ReceivingClaimTypes.Laboratory, snapshot.LaboratoryId) &&
               HasExact(user, ReceivingClaimTypes.Customer, snapshot.CustomerId) &&
               HasExact(user, ReceivingClaimTypes.ServiceOrder, snapshot.ServiceOrderId);
    }

    public bool HasCapability(string capability)
    {
        var user = accessor.HttpContext?.User;
        return user?.Identity?.IsAuthenticated == true &&
               HasExact(user, ReceivingClaimTypes.Capability, capability);
    }

    private static bool HasExact(ClaimsPrincipal user, string type, string value) =>
        user.FindAll(type).Any(claim => string.Equals(claim.Value, value, StringComparison.Ordinal));
}
