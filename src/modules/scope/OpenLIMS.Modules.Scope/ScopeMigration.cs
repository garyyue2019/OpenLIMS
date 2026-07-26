using Npgsql;

namespace OpenLIMS.Modules.Scope;

internal static class ScopeMigrator
{
    public const string Version = "20260726_001_scope_line_gate";

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
        select pg_advisory_xact_lock(hashtext('openlims.scope.migration'));

        create schema if not exists scope;

        create table if not exists scope.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists scope.scope_matrix_version (
            scope_matrix_id uuid not null,
            version bigint not null check (version > 0),
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            state text not null check (state = 'APPROVED'),
            rule_set_version text not null,
            approved_by text not null,
            approved_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            primary key (scope_matrix_id, version)
        );

        create table if not exists scope.scope_line_version (
            scope_matrix_id uuid not null,
            matrix_version bigint not null check (matrix_version > 0),
            scope_line_id text not null,
            subject_type text not null check (subject_type in ('SUBMISSION_ITEM', 'PRODUCT_VARIANT', 'FEATURE_NODE')),
            subject_ref text not null,
            subject_version bigint not null check (subject_version > 0),
            target_market_ref text not null,
            target_market_version bigint not null check (target_market_version > 0),
            requirement_clause_ref text not null,
            requirement_clause_version bigint not null check (requirement_clause_version > 0),
            test_item_ref text not null,
            test_item_version bigint not null check (test_item_version > 0),
            method_ref text not null,
            method_version bigint not null check (method_version > 0),
            method_option text not null,
            sample_requirement_ref text not null,
            sample_requirement_version bigint not null check (sample_requirement_version > 0),
            evaluation_mode text not null check (evaluation_mode in ('MEASURED_ONLY', 'EVALUATED', 'NOT_EVALUATED', 'WAIVED')),
            work_center_ref text not null,
            work_center_version bigint not null check (work_center_version > 0),
            report_position text not null,
            limit_rule_ref text null,
            limit_rule_version bigint null check (limit_rule_version is null or limit_rule_version > 0),
            decision_rule_ref text null,
            decision_rule_version bigint null check (decision_rule_version is null or decision_rule_version > 0),
            non_evaluation_reason text null,
            waiver_approval_ref text null,
            waiver_approval_version bigint null check (waiver_approval_version is null or waiver_approval_version > 0),
            primary key (scope_matrix_id, matrix_version, scope_line_id),
            foreign key (scope_matrix_id, matrix_version)
              references scope.scope_matrix_version(scope_matrix_id, version),
            check (
              (evaluation_mode = 'EVALUATED' and limit_rule_ref is not null and limit_rule_version is not null
                and decision_rule_ref is not null and decision_rule_version is not null
                and non_evaluation_reason is null and waiver_approval_ref is null and waiver_approval_version is null)
              or (evaluation_mode = 'MEASURED_ONLY' and limit_rule_ref is null and limit_rule_version is null
                and decision_rule_ref is null and decision_rule_version is null
                and non_evaluation_reason is null and waiver_approval_ref is null and waiver_approval_version is null)
              or (evaluation_mode = 'NOT_EVALUATED' and limit_rule_ref is null and limit_rule_version is null
                and decision_rule_ref is null and decision_rule_version is null
                and non_evaluation_reason is not null and waiver_approval_ref is null and waiver_approval_version is null)
              or (evaluation_mode = 'WAIVED' and limit_rule_ref is null and limit_rule_version is null
                and decision_rule_ref is null and decision_rule_version is null
                and waiver_approval_ref is not null and waiver_approval_version is not null)
            )
        );

        create table if not exists scope.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function scope.reject_approved_scope_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'SCP.APPROVED_SCOPE_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        begin
          if not exists (select 1 from pg_trigger where tgname = 'trg_scope_matrix_version_append_only') then
            create trigger trg_scope_matrix_version_append_only
            before update or delete on scope.scope_matrix_version
            for each row execute function scope.reject_approved_scope_mutation();
          end if;
          if not exists (select 1 from pg_trigger where tgname = 'trg_scope_line_version_append_only') then
            create trigger trg_scope_line_version_append_only
            before update or delete on scope.scope_line_version
            for each row execute function scope.reject_approved_scope_mutation();
          end if;
          if not exists (select 1 from pg_trigger where tgname = 'trg_scope_audit_attempt_append_only') then
            create trigger trg_scope_audit_attempt_append_only
            before update or delete on scope.audit_attempt
            for each row execute function scope.reject_approved_scope_mutation();
          end if;
        end
        $$;

        create index if not exists ix_scope_matrix_current
          on scope.scope_matrix_version (organization_group_id, scope_matrix_id, version desc);

        insert into scope.migration_history (version, applied_at)
        values ('20260726_001_scope_line_gate', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
