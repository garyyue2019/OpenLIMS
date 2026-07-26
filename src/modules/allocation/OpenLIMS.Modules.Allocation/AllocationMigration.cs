using Npgsql;

namespace OpenLIMS.Modules.Allocation;

internal static class AllocationMigrator
{
    public const string Version = "20260726_001_task_allocation";

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
        select pg_advisory_xact_lock(hashtext('openlims.allocation.migration'));

        create schema if not exists allocation;

        create table if not exists allocation.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists allocation.test_object_allocation (
            allocation_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            subject_type text not null check (subject_type in ('RECEIVED_ITEM', 'TEST_SPECIMEN', 'TEST_PORTION')),
            subject_ref text not null,
            subject_version bigint not null check (subject_version > 0),
            subject_allocation_version bigint not null check (subject_allocation_version > 0),
            identity_assignment_ref text not null,
            identity_assignment_version bigint not null check (identity_assignment_version > 0),
            received_item_id text not null,
            scope_matrix_id text not null,
            scope_line_id text not null,
            plan_step_ref text not null,
            plan_step_version bigint not null check (plan_step_version > 0),
            purpose text not null,
            sequence_order int not null check (sequence_order >= 0),
            destructive boolean not null,
            quantity_account_id text not null,
            requested_amount numeric(18, 6) not null check (requested_amount > 0),
            dimension text not null check (dimension in ('COUNT', 'MASS', 'LENGTH', 'AREA', 'VOLUME')),
            unit text not null,
            storage_condition_ref text not null,
            storage_condition_version bigint not null check (storage_condition_version > 0),
            valid_until timestamptz not null,
            reservation_entry_id text null,
            receiving_decision text not null check (receiving_decision = 'ALLOWED'),
            receiving_item_version bigint not null,
            receiving_rule_set_version text not null,
            scope_decision text not null check (scope_decision = 'ALLOWED'),
            scope_matrix_version bigint not null,
            scope_rule_set_version text not null,
            quantity_decision text not null check (quantity_decision = 'ALLOWED'),
            quantity_account_version bigint not null,
            quantity_available_amount numeric(18, 6) not null,
            quantity_rule_set_version text not null,
            rule_set_version text not null,
            assigned_by text not null,
            assigned_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (organization_group_id, subject_type, subject_ref, subject_allocation_version)
        );

        create table if not exists allocation.allocation_release (
            allocation_id uuid primary key references allocation.test_object_allocation(allocation_id),
            organization_group_id text not null,
            subject_type text not null,
            subject_ref text not null,
            subject_allocation_version bigint not null check (subject_allocation_version > 0),
            reason text not null,
            released_by text not null,
            released_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (organization_group_id, subject_type, subject_ref, subject_allocation_version)
        );

        create table if not exists allocation.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function allocation.reject_allocation_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'ALC.ALLOCATION_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        begin
          if not exists (select 1 from pg_trigger where tgname = 'trg_test_object_allocation_append_only') then
            create trigger trg_test_object_allocation_append_only
            before update or delete on allocation.test_object_allocation
            for each row execute function allocation.reject_allocation_mutation();
          end if;
          if not exists (select 1 from pg_trigger where tgname = 'trg_allocation_release_append_only') then
            create trigger trg_allocation_release_append_only
            before update or delete on allocation.allocation_release
            for each row execute function allocation.reject_allocation_mutation();
          end if;
          if not exists (select 1 from pg_trigger where tgname = 'trg_allocation_audit_attempt_append_only') then
            create trigger trg_allocation_audit_attempt_append_only
            before update or delete on allocation.audit_attempt
            for each row execute function allocation.reject_allocation_mutation();
          end if;
        end
        $$;

        create index if not exists ix_allocation_subject_current
          on allocation.test_object_allocation (organization_group_id, subject_type, subject_ref, subject_allocation_version desc);

        create index if not exists ix_allocation_subject_destructive
          on allocation.test_object_allocation (organization_group_id, subject_type, subject_ref)
          where destructive;

        insert into allocation.migration_history (version, applied_at)
        values ('20260726_001_task_allocation', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
