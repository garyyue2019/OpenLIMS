using Npgsql;

namespace OpenLIMS.Modules.Report;

internal static class ReportMigrator
{
    public const string Version = "20260727_001_report_issuance_gate";

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
        select pg_advisory_xact_lock(hashtext('openlims.report.migration'));

        create schema if not exists report;

        create table if not exists report.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists report.report (
            report_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            report_number text not null,
            rule_set_version text not null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (organization_group_id, report_number)
        );

        create table if not exists report.report_line (
            line_id uuid primary key,
            report_id uuid not null references report.report(report_id),
            report_version bigint not null check (report_version > 1),
            line_number int not null check (line_number between 1 and 10000),
            result_group_id text not null,
            group_version bigint not null check (group_version > 0),
            adoption_target_id text not null,
            adoption_rule_set_version text not null,
            scope_line_id text not null,
            scope_partition text not null check (scope_partition in
                ('ACTUAL_TESTED', 'APPROVED_COVERAGE', 'NOT_EVALUATED', 'CUSTOMER_DECLARED', 'LABORATORY_CONCLUSION')),
            batch_id text not null,
            allocation_id text not null,
            received_item_id text not null,
            requirement_snapshot_ref text not null,
            requirement_snapshot_version bigint not null check (requirement_snapshot_version > 0),
            accreditation_ref text not null,
            accreditation_version bigint not null check (accreditation_version > 0),
            accreditation_sha256 text not null check (accreditation_sha256 ~ '^[a-f0-9]{64}$'),
            site_id text not null,
            method_ref text not null,
            method_version bigint not null check (method_version > 0),
            product_matrix text not null,
            parameter_range text not null,
            accreditation_valid_until timestamptz not null,
            signatory_id text not null,
            claims_accreditation boolean not null,
            subcontracting_ref text null,
            subcontracting_version bigint null check (subcontracting_version is null or subcontracting_version > 0),
            instrument_file_id text not null,
            instrument_file_version bigint not null check (instrument_file_version > 0),
            scope_matrix_id text not null,
            scope_matrix_version bigint not null check (scope_matrix_version > 0),
            received_item_version bigint not null check (received_item_version > 0),
            allocation_version bigint not null check (allocation_version > 0),
            batch_version bigint not null check (batch_version > 0),
            added_by text not null,
            added_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (report_id, line_number),
            unique (report_id, scope_line_id, adoption_target_id)
        );

        create table if not exists report.report_line_qc_run (
            line_id uuid not null references report.report_line(line_id),
            qc_run_id text not null,
            qc_run_version bigint not null check (qc_run_version > 0),
            primary key (line_id, qc_run_id)
        );

        create table if not exists report.gate_evaluation (
            evaluation_id uuid primary key,
            report_id uuid not null references report.report(report_id),
            report_version bigint not null check (report_version > 1),
            decision text not null check (decision in ('ALLOWED', 'BLOCKED', 'UNKNOWN')),
            blocker_count int not null check (blocker_count >= 0),
            signatory_id text not null,
            evaluated_by text not null,
            evaluated_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists report.gate_blocker (
            blocker_id uuid primary key,
            evaluation_id uuid not null references report.gate_evaluation(evaluation_id),
            object_ref text not null,
            object_type text not null,
            source text not null,
            rule_set_version text not null,
            reason_code text not null,
            allowed_next_steps text not null,
            line_number int null
        );

        create table if not exists report.accreditation_verdict (
            verdict_id uuid primary key,
            evaluation_id uuid not null references report.gate_evaluation(evaluation_id),
            line_number int not null,
            status text not null check (status in ('ACCREDITED', 'NOT_ACCREDITED', 'UNKNOWN')),
            failed_dimensions text not null,
            unique (evaluation_id, line_number)
        );

        create table if not exists report.approval_submission (
            report_id uuid primary key references report.report(report_id),
            submission_id uuid not null unique,
            report_version bigint not null check (report_version > 1),
            evaluation_id uuid not null references report.gate_evaluation(evaluation_id),
            submitted_by text not null,
            submitted_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists report.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function report.reject_report_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'RPT.REPORT_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['report', 'report_line', 'report_line_qc_run',
                                   'gate_evaluation', 'gate_blocker',
                                   'accreditation_verdict', 'approval_submission', 'audit_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on report.%I for each row execute function report.reject_report_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        create index if not exists ix_report_org
          on report.report (organization_group_id, report_id);

        insert into report.migration_history (version, applied_at)
        values ('20260727_001_report_issuance_gate', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
