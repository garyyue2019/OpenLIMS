using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;

var settings = SmokeSettings.FromEnvironment();
var services = new ServiceCollection();
services.AddPlatformDependencies(new PlatformDependencyOptions
{
    PostgresConnectionString = settings.PostgresConnectionString,
    OidcAuthority = settings.OidcAuthority,
    OidcAudience = settings.OidcAudience,
    ObjectStorageEndpoint = settings.ObjectStorageEndpoint,
    ObjectStorageBucket = settings.ObjectStorageBucket,
    ObjectStorageAccessKey = settings.ObjectStorageAccessKey,
    ObjectStorageSecretKey = settings.ObjectStorageSecretKey,
    PostgresCommandTimeoutSeconds = 10,
    OidcMetadataTimeoutSeconds = 5,
    ObjectStorageProbeTimeoutSeconds = 5,
    DependencyProbeTimeoutSeconds = 15
});

await using var provider = services.BuildServiceProvider();
var cancellationToken = CancellationToken.None;
var probe = provider.GetRequiredService<IPlatformDependencyProbe>();
if (!await probe.IsReadyAsync(cancellationToken))
{
    throw new InvalidOperationException("Platform dependencies are not ready.");
}

var suffix = Guid.NewGuid().ToString("N");
var committedOutboxId = $"smoke-commit-{suffix}";
var rolledBackOutboxId = $"smoke-rollback-{suffix}";
var inboxId = $"smoke-inbox-{suffix}";
var coordinator = provider.GetRequiredService<ITransactionCoordinator>();
var outbox = provider.GetRequiredService<IOutboxWriter>();
var audit = provider.GetRequiredService<IAuditIntentWriter>();
var inbox = provider.GetRequiredService<IInboxDeduplicator>();
var transactionAccessor = provider.GetRequiredService<IPostgresTransactionAccessor>();
var now = DateTimeOffset.UtcNow;
var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
await CreateSmokeFactTableAsync(dataSource, cancellationToken);

await coordinator.ExecuteAsync(async token =>
{
    await InsertFactAsync(transactionAccessor, committedOutboxId, token);
    await outbox.WriteAsync(new OutboxEnvelope(committedOutboxId, "platform.smoke", now), token);
    await audit.WriteAsync(CreateAudit(committedOutboxId, settings.OrganizationGroupId, now), token);
}, cancellationToken);

try
{
    await coordinator.ExecuteAsync(async token =>
    {
        await InsertFactAsync(transactionAccessor, rolledBackOutboxId, token);
        await outbox.WriteAsync(new OutboxEnvelope(rolledBackOutboxId, "platform.smoke", now), token);
        await audit.WriteAsync(CreateAudit(rolledBackOutboxId, settings.OrganizationGroupId, now), token);
        throw new InvalidOperationException("synthetic rollback");
    }, cancellationToken);
    throw new InvalidOperationException("Rollback probe did not throw.");
}
catch (InvalidOperationException exception) when (exception.Message == "synthetic rollback")
{
}

var claims = await Task.WhenAll(
    Enumerable.Range(0, 4).Select(_ => inbox.TryRecordAsync(new InboxReceipt(inboxId, now), cancellationToken)));
if (claims.Count(claimed => claimed) != 1)
{
    throw new InvalidOperationException("Concurrent inbox deduplication did not produce exactly one owner.");
}

if (await CountAsync(dataSource, "platform_smoke.fact", committedOutboxId, cancellationToken) != 1 ||
    await CountAsync(dataSource, "platform.outbox", committedOutboxId, cancellationToken) != 1 ||
    await CountAsync(dataSource, "platform.audit_intent", committedOutboxId, cancellationToken) != 1 ||
    await CountAsync(dataSource, "platform_smoke.fact", rolledBackOutboxId, cancellationToken) != 0 ||
    await CountAsync(dataSource, "platform.outbox", rolledBackOutboxId, cancellationToken) != 0 ||
    await CountAsync(dataSource, "platform.audit_intent", rolledBackOutboxId, cancellationToken) != 0)
{
    throw new InvalidOperationException("PostgreSQL transaction evidence is inconsistent.");
}

var storage = provider.GetRequiredService<IObjectStoragePort>();
var objectReference = new ObjectReference(settings.ObjectStorageBucket, $"smoke/{suffix}.txt");
await using (var input = new MemoryStream(Encoding.UTF8.GetBytes("openlims-platform-smoke")))
{
    await storage.PutAsync(objectReference, input, cancellationToken);
}

await using (var output = await storage.OpenReadAsync(objectReference, cancellationToken))
using (var reader = new StreamReader(output, Encoding.UTF8))
{
    if (await reader.ReadToEndAsync(cancellationToken) != "openlims-platform-smoke")
    {
        throw new InvalidOperationException("Object storage round trip changed the payload.");
    }
}
await storage.DeleteAsync(objectReference, cancellationToken);

await CleanupAsync(dataSource, [committedOutboxId, inboxId], cancellationToken);
Console.WriteLine("PLATFORM_SMOKE_PASS");

static AuditIntent CreateAudit(string objectId, string organizationGroupId, DateTimeOffset occurredAt) =>
    new(
        "smoke-actor",
        organizationGroupId,
        objectId,
        "platform.smoke",
        "platform-0001",
        null,
        "1",
        $"smoke-{objectId}",
        occurredAt);

static async Task CreateSmokeFactTableAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
{
    await using var command = dataSource.CreateCommand("""
        create schema if not exists platform_smoke;
        create table if not exists platform_smoke.fact (
            id text primary key,
            value text not null
        );
        """);
    await command.ExecuteNonQueryAsync(cancellationToken);
}

static async Task InsertFactAsync(
    IPostgresTransactionAccessor transactionAccessor,
    string id,
    CancellationToken cancellationToken)
{
    if (!transactionAccessor.HasActiveTransaction)
    {
        throw new InvalidOperationException("Smoke fact write is not inside the coordinated transaction.");
    }

    await using var command = new NpgsqlCommand(
        "insert into platform_smoke.fact (id, value) values (@id, 'synthetic')",
        transactionAccessor.Connection,
        transactionAccessor.Transaction);
    command.Parameters.AddWithValue("id", id);
    await command.ExecuteNonQueryAsync(cancellationToken);
}

static async Task<long> CountAsync(
    NpgsqlDataSource dataSource,
    string table,
    string objectId,
    CancellationToken cancellationToken)
{
    var predicate = table.EndsWith("audit_intent", StringComparison.Ordinal) ? "object_id" : "id";
    await using var command = dataSource.CreateCommand($"select count(*) from {table} where {predicate} = @id");
    command.Parameters.AddWithValue("id", objectId);
    return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
}

static async Task CleanupAsync(
    NpgsqlDataSource dataSource,
    IReadOnlyCollection<string> ids,
    CancellationToken cancellationToken)
{
    await using var command = dataSource.CreateCommand("""
        delete from platform.audit_intent where object_id = any(@ids);
        delete from platform.outbox where id = any(@ids);
        delete from platform.inbox where message_id = any(@ids);
        delete from platform_smoke.fact where id = any(@ids);
        """);
    command.Parameters.AddWithValue("ids", ids.ToArray());
    await command.ExecuteNonQueryAsync(cancellationToken);
}

internal sealed record SmokeSettings(
    string OrganizationGroupId,
    string PostgresConnectionString,
    string OidcAuthority,
    string OidcAudience,
    string ObjectStorageEndpoint,
    string ObjectStorageBucket,
    string ObjectStorageAccessKey,
    string ObjectStorageSecretKey)
{
    public static SmokeSettings FromEnvironment() => new(
        Required("Platform__OrganizationGroupId"),
        Required("Platform__PostgresConnectionString"),
        Required("Platform__OidcAuthority"),
        Required("Platform__OidcAudience"),
        Required("Platform__ObjectStorageEndpoint"),
        Required("Platform__ObjectStorageBucket"),
        Required("Platform__ObjectStorageAccessKey"),
        Required("Platform__ObjectStorageSecretKey"));

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Missing required smoke setting: {name}");
}
