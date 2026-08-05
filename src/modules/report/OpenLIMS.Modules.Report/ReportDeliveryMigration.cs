using Npgsql;

namespace OpenLIMS.Modules.Report;

internal static class ReportDeliveryMigrator
{
    public const string Version = "20260805_003_report_delivery";

    public static async Task ApplyAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(MigrationSql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private const string MigrationSql = """
        select pg_advisory_xact_lock(hashtext('openlims.report.delivery.migration'));

        create table if not exists report.delivery (
            delivery_id uuid primary key,
            organization_group_id text not null,
            report_id uuid not null,
            version_number int not null check (version_number > 0),
            content_hash text not null check (content_hash ~ '^[a-f0-9]{64}$'),
            recipient_id text not null,
            channel text not null check (channel in ('PORTAL', 'EMAIL', 'API', 'MANUAL')),
            destination_hash text not null check (destination_hash ~ '^[a-f0-9]{64}$'),
            idempotency_key text not null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            foreign key (report_id, version_number)
                references report.version_snapshot(report_id, version_number),
            unique (organization_group_id, idempotency_key)
        );

        create table if not exists report.download_grant (
            grant_id uuid primary key,
            delivery_id uuid not null references report.delivery(delivery_id),
            recipient_id text not null,
            token_hash text not null unique check (token_hash ~ '^[a-f0-9]{64}$'),
            expires_at timestamptz not null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists report.notification (
            notification_id uuid primary key,
            delivery_id uuid not null references report.delivery(delivery_id),
            channel text not null check (channel in ('PORTAL', 'EMAIL', 'API', 'MANUAL')),
            destination_hash text not null check (destination_hash ~ '^[a-f0-9]{64}$'),
            payload_ref text not null,
            payload_version bigint not null check (payload_version > 0),
            idempotency_key text not null,
            queued_by text not null,
            queued_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (delivery_id, idempotency_key)
        );

        create table if not exists report.notification_attempt (
            attempt_id uuid primary key,
            notification_id uuid not null references report.notification(notification_id),
            attempt_number int not null check (attempt_number > 0),
            idempotency_key text not null,
            outcome text not null check (outcome in ('DELIVERED', 'FAILED', 'UNKNOWN')),
            external_reference text null,
            detail_code text null,
            attempted_by text not null,
            attempted_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            check ((outcome = 'DELIVERED' and external_reference is not null)
                or (outcome <> 'DELIVERED' and external_reference is null)),
            unique (notification_id, attempt_number),
            unique (notification_id, idempotency_key)
        );

        create index if not exists ix_report_delivery_report_version
          on report.delivery (report_id, version_number);
        create index if not exists ix_report_notification_delivery
          on report.notification (delivery_id);

        do $$
        declare t text;
        begin
          foreach t in array array['delivery', 'download_grant', 'notification', 'notification_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on report.%I for each row execute function report.reject_version_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        insert into report.migration_history (version, applied_at)
        values ('20260805_003_report_delivery', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
