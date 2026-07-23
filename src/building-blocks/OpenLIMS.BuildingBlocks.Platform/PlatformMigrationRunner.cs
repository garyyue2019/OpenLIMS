using Npgsql;

namespace OpenLIMS.BuildingBlocks.Platform;

public static class PlatformMigrationRunner
{
    public const string CurrentMigrationId = "platform-0001";

    public static async Task ApplyAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select pg_advisory_xact_lock(hashtext('openlims-platform-migrations'));

            create schema if not exists platform;

            create table if not exists platform.migration_history (
                migration_id text primary key,
                applied_at timestamptz not null
            );

            create table if not exists platform.outbox (
                id text primary key,
                message_type text not null,
                occurred_at timestamptz not null,
                dispatched_at timestamptz null
            );

            create table if not exists platform.inbox (
                message_id text primary key,
                received_at timestamptz not null
            );

            create table if not exists platform.audit_intent (
                audit_id bigint generated always as identity primary key,
                actor_id text not null,
                organization_group_id text not null,
                object_id text not null,
                action text not null,
                rule_version text not null,
                before_version text null,
                after_version text null,
                correlation_id text not null,
                occurred_at timestamptz not null
            );

            insert into platform.migration_history (migration_id, applied_at)
            values ('platform-0001', now())
            on conflict (migration_id) do nothing;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static async Task<bool> IsCurrentAsync(
        NpgsqlDataSource dataSource,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            select
                to_regclass('platform.migration_history') is not null
                and to_regclass('platform.outbox') is not null
                and to_regclass('platform.inbox') is not null
                and to_regclass('platform.audit_intent') is not null
                and exists (
                    select 1
                    from platform.migration_history
                    where migration_id = 'platform-0001'
                )
            """);
        command.CommandTimeout = commandTimeoutSeconds;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
}
