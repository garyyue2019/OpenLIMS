using Npgsql;

namespace OpenLIMS.Modules.Commercial;

internal static class CommercialMigrator
{
    public const string Version = "20260805_001_commercial_front_office";

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
        select pg_advisory_xact_lock(hashtext('openlims.commercial.migration'));

        create schema if not exists commercial;

        create table if not exists commercial.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists commercial.catalog_record_version (
            record_id uuid not null,
            version bigint not null check (version > 0),
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            kind text not null,
            code text not null,
            payload jsonb not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            correlation_id text not null,
            primary key (record_id, version),
            unique (organization_group_id, kind, code, version)
        );

        create index if not exists ix_commercial_catalog_current
            on commercial.catalog_record_version (organization_group_id, kind, code, version desc);

        create table if not exists commercial.inquiry_version (
            inquiry_id uuid not null,
            version bigint not null check (version > 0),
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            inquiry_number text not null,
            state text not null,
            payload jsonb not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            correlation_id text not null,
            primary key (inquiry_id, version),
            unique (organization_group_id, inquiry_number, version)
        );

        create index if not exists ix_commercial_inquiry_current
            on commercial.inquiry_version (organization_group_id, inquiry_id, version desc);

        create table if not exists commercial.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function commercial.reject_fact_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'COM.APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['catalog_record_version', 'inquiry_version', 'audit_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_commercial_' || t || '_append_only') then
              execute format(
                'create trigger trg_commercial_%I_append_only before update or delete on commercial.%I for each row execute function commercial.reject_fact_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        insert into commercial.migration_history (version, applied_at)
        values ('20260805_001_commercial_front_office', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
