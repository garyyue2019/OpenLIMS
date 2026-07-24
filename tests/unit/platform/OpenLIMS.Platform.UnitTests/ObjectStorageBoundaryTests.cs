using Amazon.S3;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using Xunit;

namespace OpenLIMS.Platform.UnitTests;

public sealed class ObjectStorageBoundaryTests : IDisposable
{
    private readonly AmazonS3Client _client = new(
        "synthetic-access",
        "synthetic-secret",
        new AmazonS3Config { ServiceURL = "http://127.0.0.1:1", ForcePathStyle = true });

    [Fact]
    public async Task A_reference_cannot_switch_the_deployment_bucket()
    {
        var port = new S3ObjectStoragePort(_client, "bound-group-bucket");
        var reference = new ObjectReference("other-group-bucket", "evidence/object.txt");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await port.OpenReadAsync(reference, TestContext.Current.CancellationToken));

        Assert.Equal("PLT.OBJECT_STORAGE_BUCKET_MISMATCH", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_object_keys_fail_before_any_storage_request(string objectKey)
    {
        var port = new S3ObjectStoragePort(_client, "bound-group-bucket");
        var reference = new ObjectReference("bound-group-bucket", objectKey);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await port.OpenReadAsync(reference, TestContext.Current.CancellationToken));

        Assert.Contains("PLT.OBJECT_STORAGE_KEY_INVALID", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _client.Dispose();
}
