using OpenLIMS.BuildingBlocks.Platform;
using Xunit;

namespace OpenLIMS.Platform.UnitTests;

public sealed class CorrelationIdPolicyTests
{
    [Theory]
    [InlineData("request-123")]
    [InlineData("a.b_c-9")]
    public void Accepts_supported_correlation_identifiers(string value) => Assert.True(CorrelationIdPolicy.IsValid(value));

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("bad/character")]
    public void Rejects_invalid_correlation_identifiers(string value) => Assert.False(CorrelationIdPolicy.IsValid(value));
}
