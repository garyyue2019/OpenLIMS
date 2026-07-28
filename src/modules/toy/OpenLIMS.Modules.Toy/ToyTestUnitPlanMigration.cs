using Npgsql;

namespace OpenLIMS.Modules.Toy;

internal static class ToyTestUnitPlanMigrator
{
    public const string Version = "20260728_002_toy_test_unit_sample_demand";

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
        select pg_advisory_xact_lock(hashtext('openlims.toy.migration'));

        create table if not exists toy.test_unit_plan (
            plan_row_id uuid primary key,
            plan_id uuid not null,
            product_id uuid not null references toy.product(product_id),
            product_version bigint not null check (product_version > 0),
            plan_version bigint not null check (plan_version > 0),
            age_grade_decision_version bigint not null check (age_grade_decision_version > 0),
            accessibility_assessment_version bigint not null check (accessibility_assessment_version > 0),
            scope_matrix_id text not null,
            scope_matrix_version bigint not null check (scope_matrix_version > 0),
            rule_set_version text not null,
            input_hash text not null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (product_id, plan_version),
            unique (product_id, plan_id, plan_version)
        );

        create table if not exists toy.test_unit_plan_scope_line (
            row_id uuid primary key,
            plan_row_id uuid not null references toy.test_unit_plan(plan_row_id),
            reference_id text not null,
            reference_version bigint not null check (reference_version > 0),
            unique (plan_row_id, reference_id, reference_version)
        );

        create table if not exists toy.test_unit_plan_sample_rule (
            row_id uuid primary key,
            plan_row_id uuid not null references toy.test_unit_plan(plan_row_id),
            reference_id text not null,
            reference_version bigint not null check (reference_version > 0),
            unique (plan_row_id, reference_id, reference_version)
        );

        create table if not exists toy.test_unit (
            test_unit_row_id uuid primary key,
            plan_row_id uuid not null references toy.test_unit_plan(plan_row_id),
            test_unit_id uuid not null,
            physical_object_ref text not null,
            physical_object_version bigint not null check (physical_object_version > 0),
            parallel_number int not null check (parallel_number > 0),
            unique (plan_row_id, test_unit_id),
            unique (plan_row_id, parallel_number)
        );

        create table if not exists toy.test_unit_hazard_domain (
            row_id uuid primary key,
            test_unit_row_id uuid not null references toy.test_unit(test_unit_row_id),
            reference_id text not null,
            reference_version bigint not null check (reference_version > 0),
            unique (test_unit_row_id, reference_id, reference_version)
        );

        create table if not exists toy.test_unit_sequence_step (
            step_row_id uuid primary key,
            test_unit_row_id uuid not null references toy.test_unit(test_unit_row_id),
            step_id text not null,
            sequence_order int not null check (sequence_order > 0),
            task_ref text not null,
            task_version bigint not null check (task_version > 0),
            destructive boolean not null,
            exclusive_destructive_group_id text null,
            share_rule_ref text null,
            share_rule_version bigint null,
            unique (test_unit_row_id, step_id),
            unique (test_unit_row_id, sequence_order),
            check ((share_rule_ref is null) = (share_rule_version is null)),
            check (share_rule_version is null or share_rule_version > 0),
            check (not destructive or share_rule_ref is null),
            check (exclusive_destructive_group_id is null or destructive)
        );

        -- A general allocation release cannot remove this history. The key is
        -- the immutable physical object version plus exclusive group, so a new
        -- client-generated TestUnit id cannot disguise reuse of the same item.
        create table if not exists toy.destructive_test_unit_usage (
            usage_id uuid primary key,
            product_id uuid not null references toy.product(product_id),
            plan_row_id uuid not null references toy.test_unit_plan(plan_row_id),
            test_unit_id uuid not null,
            physical_object_ref text not null,
            physical_object_version bigint not null check (physical_object_version > 0),
            exclusive_destructive_group_id text not null,
            recorded_at timestamptz not null,
            unique (
                product_id,
                physical_object_ref,
                physical_object_version,
                exclusive_destructive_group_id)
        );

        create table if not exists toy.sample_requirement (
            requirement_id uuid primary key,
            plan_row_id uuid not null unique references toy.test_unit_plan(plan_row_id),
            requirement_version bigint not null check (requirement_version > 0),
            input_hash text not null,
            rule_set_version text not null,
            created_at timestamptz not null
        );

        create table if not exists toy.sample_demand_component (
            component_row_id uuid primary key,
            requirement_id uuid not null references toy.sample_requirement(requirement_id),
            component_id text not null,
            kind text not null check (kind in (
                'BASE', 'PARALLEL', 'EXCLUSIVE_DESTRUCTIVE',
                'CHEMICAL_MINIMUM', 'RETEST_RESERVE', 'RETENTION')),
            hazard_domain_ref text null,
            hazard_domain_version bigint null,
            test_unit_id uuid null,
            amount numeric not null check (amount >= 0),
            dimension text not null,
            unit text not null,
            source_rule_ref text not null,
            source_rule_version bigint not null check (source_rule_version > 0),
            unique (requirement_id, component_id),
            check ((hazard_domain_ref is null) = (hazard_domain_version is null)),
            check (hazard_domain_version is null or hazard_domain_version > 0),
            check (kind not in ('BASE', 'CHEMICAL_MINIMUM') or amount > 0)
        );

        create table if not exists toy.technical_approval (
            approval_id uuid primary key,
            requirement_id uuid not null unique references toy.sample_requirement(requirement_id),
            approved_by text not null,
            approved_at timestamptz not null,
            approval_comment text not null,
            input_hash text not null,
            rule_set_version text not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists toy.downstream_request (
            request_id uuid primary key,
            requirement_id uuid not null references toy.sample_requirement(requirement_id),
            requested_by text not null,
            requested_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists toy.quantity_decision (
            decision_id uuid primary key,
            request_id uuid not null references toy.downstream_request(request_id),
            quantity_account_id text not null,
            expected_account_version bigint not null check (expected_account_version > 0),
            current_account_version bigint not null check (current_account_version > 0),
            requested_amount numeric not null check (requested_amount >= 0),
            available_amount numeric not null check (available_amount >= 0),
            dimension text not null,
            unit text not null,
            reservation_ref text not null,
            decision text not null check (decision = 'ALLOWED'),
            reason_codes text[] not null,
            rule_set_version text not null,
            unique (request_id, quantity_account_id),
            unique (request_id, reservation_ref)
        );

        create table if not exists toy.allocation_decision (
            decision_id uuid primary key,
            request_id uuid not null references toy.downstream_request(request_id),
            allocation_id text not null,
            expected_subject_allocation_version bigint not null check (expected_subject_allocation_version > 0),
            current_subject_allocation_version bigint not null check (current_subject_allocation_version > 0),
            allocation_state text not null,
            test_unit_id uuid not null,
            sequence_step_id text not null,
            decision text not null check (decision = 'ALLOWED'),
            reason_codes text[] not null,
            rule_set_version text not null,
            unique (request_id, allocation_id),
            unique (request_id, test_unit_id, sequence_step_id)
        );

        do $$
        declare t text;
        begin
          foreach t in array array[
            'test_unit_plan', 'test_unit_plan_scope_line', 'test_unit_plan_sample_rule',
            'test_unit', 'test_unit_hazard_domain', 'test_unit_sequence_step',
            'destructive_test_unit_usage', 'sample_requirement', 'sample_demand_component',
            'technical_approval', 'downstream_request', 'quantity_decision', 'allocation_decision'
          ] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on toy.%I for each row execute function toy.reject_toy_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        create index if not exists ix_toy_test_unit_plan_product
          on toy.test_unit_plan (product_id, plan_version);
        create index if not exists ix_toy_destructive_usage_product
          on toy.destructive_test_unit_usage (product_id, exclusive_destructive_group_id);

        insert into toy.migration_history (version, applied_at)
        values ('20260728_002_toy_test_unit_sample_demand', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
