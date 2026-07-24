using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.BuildingBlocks.Platform;

public sealed class PlatformDependencyOptions
{
    public const string SectionName = "Platform";

    public required string PostgresConnectionString { get; init; }
    public required string OidcAuthority { get; init; }
    public required string OidcAudience { get; init; }
    public required string ObjectStorageEndpoint { get; init; }
    public required string ObjectStorageBucket { get; init; }
    public required string ObjectStorageAccessKey { get; init; }
    public required string ObjectStorageSecretKey { get; init; }
    public required int PostgresCommandTimeoutSeconds { get; init; }
    public required int OidcMetadataTimeoutSeconds { get; init; }
    public required int ObjectStorageProbeTimeoutSeconds { get; init; }
    public required int DependencyProbeTimeoutSeconds { get; init; }
}

public interface IPlatformDependencyProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public sealed class PlatformDependencyProbe(
    NpgsqlDataSource dataSource,
    HttpClient oidcClient,
    IAmazonS3 objectStorage,
    PlatformDependencyOptions options) : IPlatformDependencyProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalTimeout.CancelAfter(TimeSpan.FromSeconds(options.DependencyProbeTimeoutSeconds));
        try
        {
            if (!await ProbePostgresAsync(totalTimeout.Token))
            {
                return false;
            }

            if (!await RunWithTimeoutAsync(
                    async token =>
                    {
                        var metadataUri = new Uri(
                            $"{options.OidcAuthority.TrimEnd('/')}/.well-known/openid-configuration",
                            UriKind.Absolute);
                        using var response = await oidcClient.GetAsync(metadataUri, token);
                        return response.IsSuccessStatusCode;
                    },
                    options.OidcMetadataTimeoutSeconds,
                    totalTimeout.Token))
            {
                return false;
            }

            return await RunWithTimeoutAsync(
                async token =>
                {
                    await objectStorage.ListObjectsV2Async(
                        new ListObjectsV2Request { BucketName = options.ObjectStorageBucket, MaxKeys = 1 },
                        token);
                    return true;
                },
                options.ObjectStorageProbeTimeoutSeconds,
                totalTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<bool> ProbePostgresAsync(CancellationToken cancellationToken) =>
        await RunWithTimeoutAsync(
            async token =>
            {
                await using var command = dataSource.CreateCommand("select 1");
                command.CommandTimeout = options.PostgresCommandTimeoutSeconds;
                await command.ExecuteScalarAsync(token);
                return await PlatformMigrationRunner.IsCurrentAsync(
                    dataSource,
                    options.PostgresCommandTimeoutSeconds,
                    token);
            },
            options.PostgresCommandTimeoutSeconds,
            cancellationToken);

    private static async Task<bool> RunWithTimeoutAsync(
        Func<CancellationToken, Task<bool>> operation,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return await operation(timeout.Token);
    }
}

public sealed class S3ObjectStoragePort(
    IAmazonS3 client,
    string configuredBucket) : IObjectStoragePort
{
    public async Task PutAsync(ObjectReference reference, Stream content, CancellationToken cancellationToken = default)
    {
        ValidateReference(reference);
        ArgumentNullException.ThrowIfNull(content);
        await client.PutObjectAsync(
            new PutObjectRequest { BucketName = configuredBucket, Key = reference.ObjectKey, InputStream = content },
            cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(ObjectReference reference, CancellationToken cancellationToken = default)
    {
        ValidateReference(reference);
        var response = await client.GetObjectAsync(configuredBucket, reference.ObjectKey, cancellationToken);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(ObjectReference reference, CancellationToken cancellationToken = default)
    {
        ValidateReference(reference);
        await client.DeleteObjectAsync(configuredBucket, reference.ObjectKey, cancellationToken);
    }

    private void ValidateReference(ObjectReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!string.Equals(reference.Bucket, configuredBucket, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PLT.OBJECT_STORAGE_BUCKET_MISMATCH");
        }

        if (string.IsNullOrWhiteSpace(reference.ObjectKey) || reference.ObjectKey.Length > 1024)
        {
            throw new ArgumentException("PLT.OBJECT_STORAGE_KEY_INVALID", nameof(reference));
        }
    }
}

public static class PlatformDependencyRegistration
{
    public static void AddPlatformDependencies(this IServiceCollection services, PlatformDependencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var dataSource = NpgsqlDataSource.Create(options.PostgresConnectionString);
        services.AddSingleton(options);
        services.AddSingleton(dataSource);
        services.AddSingleton(new HttpClient());
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(options.ObjectStorageAccessKey, options.ObjectStorageSecretKey),
            new AmazonS3Config
            {
                ServiceURL = options.ObjectStorageEndpoint,
                ForcePathStyle = true
            }));
        services.AddSingleton<IObjectStoragePort>(serviceProvider => new S3ObjectStoragePort(
            serviceProvider.GetRequiredService<IAmazonS3>(),
            options.ObjectStorageBucket));
        services.AddSingleton<IPlatformDependencyProbe, PlatformDependencyProbe>();

        services.AddSingleton<PostgresTransactionContext>();
        services.AddSingleton<IPostgresTransactionAccessor>(serviceProvider => serviceProvider.GetRequiredService<PostgresTransactionContext>());
        services.AddSingleton<PostgresPlatformPersistence>();
        services.AddSingleton<ITransactionCoordinator>(serviceProvider => serviceProvider.GetRequiredService<PostgresPlatformPersistence>());
        services.AddSingleton<IOutboxWriter>(serviceProvider => serviceProvider.GetRequiredService<PostgresPlatformPersistence>());
        services.AddSingleton<IInboxDeduplicator>(serviceProvider => serviceProvider.GetRequiredService<PostgresPlatformPersistence>());
        services.AddSingleton<IAuditIntentWriter>(serviceProvider => serviceProvider.GetRequiredService<PostgresPlatformPersistence>());
    }
}
