using System.Text;

namespace OpenLIMS.Contracts.Receiving;

public static class LabelTemplateVersions
{
    public const string Container = "REC-CT-50X30@1.0.0";
    public const string ReceivedItem = "REC-RI-50X30@1.0.0";

    public static string ForObjectType(string objectType) => objectType switch
    {
        ReceivingLabelObjectTypes.Container => Container,
        ReceivingLabelObjectTypes.ReceivedItem => ReceivedItem,
        _ => throw new ArgumentOutOfRangeException(nameof(objectType))
    };
}

public sealed record LabelBarcodeValue(string FormatVersion, string ObjectType, Guid OpaqueReference);

public static class LabelBarcodeCodec
{
    public const string CurrentFormatVersion = "OL1";

    public static string Create(string objectType, Guid opaqueReference)
    {
        ValidateObjectType(objectType);
        var body = $"{CurrentFormatVersion}:{objectType}:{opaqueReference:N}";
        return $"{body}:{Crc32(body):X8}";
    }

    public static bool TryParse(string value, out LabelBarcodeValue? barcode, out string errorCode)
    {
        barcode = null;
        errorCode = "LABEL.BARCODE_INVALID";
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
        {
            return false;
        }

        var parts = value.Trim().Split(':', StringSplitOptions.None);
        if (parts.Length != 4)
        {
            return false;
        }

        if (!string.Equals(parts[0], CurrentFormatVersion, StringComparison.Ordinal))
        {
            errorCode = "LABEL.BARCODE_VERSION_UNSUPPORTED";
            return false;
        }

        if (!IsObjectType(parts[1]) ||
            parts[2].Length != 32 ||
            !Guid.TryParseExact(parts[2], "N", out var reference) ||
            parts[3].Length != 8 ||
            !uint.TryParse(parts[3], System.Globalization.NumberStyles.HexNumber, null, out var suppliedChecksum))
        {
            return false;
        }

        var body = $"{parts[0]}:{parts[1]}:{parts[2]}";
        if (Crc32(body) != suppliedChecksum)
        {
            return false;
        }

        barcode = new LabelBarcodeValue(parts[0], parts[1], reference);
        errorCode = string.Empty;
        return true;
    }

    private static void ValidateObjectType(string objectType)
    {
        if (!IsObjectType(objectType))
        {
            throw new ArgumentOutOfRangeException(nameof(objectType));
        }
    }

    private static bool IsObjectType(string objectType) =>
        string.Equals(objectType, ReceivingLabelObjectTypes.Container, StringComparison.Ordinal) ||
        string.Equals(objectType, ReceivingLabelObjectTypes.ReceivedItem, StringComparison.Ordinal);

    private static uint Crc32(string value)
    {
        var crc = uint.MaxValue;
        foreach (var valueByte in Encoding.ASCII.GetBytes(value))
        {
            crc ^= valueByte;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
