using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.BuildingBlocks.Platform;

public static partial class CorrelationIdPolicy
{
    public const int MaximumLength = 128;

    public static bool IsValid(string? value) => value is not null && CorrelationPattern().IsMatch(value);

    public static CorrelationId Create() => new(Guid.NewGuid().ToString("N"));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationPattern();
}
