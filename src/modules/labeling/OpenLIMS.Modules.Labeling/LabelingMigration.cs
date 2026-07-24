using Npgsql;

namespace OpenLIMS.Modules.Labeling;

internal static class LabelingMigrator
{
    public const string Version = "20260724_001_label_printing";

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
        select pg_advisory_xact_lock(hashtext('openlims.labeling.migration'));

        create schema if not exists labeling;
        create table if not exists labeling.migration_history (
          version text primary key,
          applied_at timestamptz not null
        );

        create table if not exists labeling.idempotency (
          organization_group_id text not null,
          key_hash char(64) not null,
          request_hash char(64) not null,
          actor_id text not null,
          response_json jsonb null,
          created_at timestamptz not null,
          primary key (organization_group_id, key_hash)
        );

        create table if not exists labeling.print_job (
          id uuid primary key,
          organization_group_id text not null,
          actor_id text not null,
          legal_entity_id text not null,
          laboratory_id text not null,
          customer_id text not null,
          service_order_id text not null,
          object_type text not null check (object_type in ('CT', 'RI')),
          object_id uuid not null,
          object_version bigint not null check (object_version > 0),
          business_number text not null,
          barcode_payload text not null,
          template_version text not null,
          printer_id text not null,
          printer_configuration_version text not null,
          printer_host text not null,
          printer_port integer not null check (printer_port = 9100),
          protocol text not null check (protocol = 'TSPL2'),
          rendered_payload bytea not null,
          copies integer not null check (copies = 1),
          is_reprint boolean not null,
          reason text null,
          source_print_job_id uuid null references labeling.print_job(id),
          idempotency_key_hash char(64) not null,
          status text not null check (status in ('REQUESTED','DISPATCHING','DISPATCHED','VERIFIED','FAILED','UNKNOWN')),
          attempt_count integer not null default 0 check (attempt_count >= 0),
          dispatch_lease_expires_at timestamptz null,
          next_attempt_at timestamptz null,
          last_error_code text null,
          correlation_id text not null,
          created_at timestamptz not null,
          updated_at timestamptz not null,
          dispatched_at timestamptz null,
          verified_at timestamptz null
        );

        alter table labeling.print_job
          add column if not exists dispatch_lease_expires_at timestamptz null;

        create unique index if not exists ux_print_job_initial_object
          on labeling.print_job (organization_group_id, object_type, object_id)
          where is_reprint = false;
        create index if not exists ix_print_job_pending
          on labeling.print_job (next_attempt_at, created_at)
          where status = 'REQUESTED';
        create index if not exists ix_print_job_object
          on labeling.print_job (organization_group_id, object_type, object_id, created_at desc);

        create table if not exists labeling.print_event (
          id uuid primary key,
          print_job_id uuid not null references labeling.print_job(id),
          event_type text not null,
          actor_id text not null,
          reason text null,
          occurred_at timestamptz not null,
          details_json jsonb not null
        );

        create table if not exists labeling.audit_pending (
          id uuid primary key,
          event_type text not null,
          actor_id text not null,
          organization_group_id text not null,
          legal_entity_id text not null,
          laboratory_id text not null,
          customer_id text not null,
          service_order_id text not null,
          object_type text not null,
          object_id uuid not null,
          print_job_id uuid null,
          correlation_id text not null,
          rule_version text not null,
          occurred_at timestamptz not null,
          payload_json jsonb not null,
          dispatched_at timestamptz null
        );

        create table if not exists labeling.scan_attempt (
          id uuid primary key,
          actor_id text null,
          organization_group_id text not null,
          payload_hash char(64) not null,
          decision_code text not null,
          correlation_id text not null,
          occurred_at timestamptz not null
        );

        create table if not exists labeling.outbox (
          id uuid primary key,
          event_type text not null,
          aggregate_type text not null,
          aggregate_id uuid not null,
          occurred_at timestamptz not null,
          payload_json jsonb not null,
          attempt_count integer not null default 0 check (attempt_count >= 0),
          next_attempt_at timestamptz null,
          dispatched_at timestamptz null
        );

        insert into labeling.migration_history (version, applied_at)
        values ('20260724_001_label_printing', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
