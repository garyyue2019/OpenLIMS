using Npgsql;

namespace OpenLIMS.Modules.Operations;

internal static class OperationsMigrator
{
    public const string Version = "20260805_001_sample_operations";

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
        select pg_advisory_xact_lock(hashtext('openlims.operations.migration'));

        create schema if not exists operations;

        create table if not exists operations.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists operations.lineage_edge (
            edge_id uuid primary key,
            organization_group_id text not null,
            source_object_id text not null,
            target_object_id text not null,
            relation_kind text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            payload jsonb not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            correlation_id text not null,
            unique (organization_group_id, source_object_id, target_object_id, relation_kind)
        );

        create table if not exists operations.custody_event (
            event_id uuid primary key,
            organization_group_id text not null,
            object_id text not null,
            sequence bigint not null check (sequence > 0),
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            payload jsonb not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            correlation_id text not null,
            unique (organization_group_id, object_id, sequence)
        );

        create table if not exists operations.work_plan_version (
            work_plan_id uuid not null,
            version bigint not null check (version > 0),
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            state text not null,
            payload jsonb not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            correlation_id text not null,
            primary key (work_plan_id, version)
        );

        create index if not exists ix_operations_work_plan_current
            on operations.work_plan_version (organization_group_id, work_plan_id, version desc);

        create table if not exists operations.resource_reservation (
            reservation_id uuid primary key,
            organization_group_id text not null,
            work_plan_id uuid not null,
            work_plan_version bigint not null check (work_plan_version > 0),
            task_id text not null,
            resource_kind text not null,
            resource_id text not null,
            starts_at timestamptz not null,
            ends_at timestamptz not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            correlation_id text not null,
            check (ends_at > starts_at)
        );

        create index if not exists ix_operations_resource_window
            on operations.resource_reservation (organization_group_id, resource_kind, resource_id, starts_at, ends_at);

        create table if not exists operations.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function operations.reject_fact_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'OPS.APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['lineage_edge', 'custody_event', 'work_plan_version', 'resource_reservation', 'audit_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_operations_' || t || '_append_only') then
              execute format(
                'create trigger trg_operations_%I_append_only before update or delete on operations.%I for each row execute function operations.reject_fact_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        insert into operations.migration_history (version, applied_at)
        values ('20260805_001_sample_operations', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
