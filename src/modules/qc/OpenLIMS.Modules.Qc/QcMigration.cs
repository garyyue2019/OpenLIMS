using Npgsql;

namespace OpenLIMS.Modules.Qc;

internal static class QcMigrator
{
    public const string Version = "20260727_001_qc_impact";

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
        select pg_advisory_xact_lock(hashtext('openlims.qc.migration'));

        create schema if not exists qc;

        create table if not exists qc.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists qc.qc_run (
            qc_run_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            batch_id text not null,
            batch_version bigint not null check (batch_version > 0),
            batch_gate_decision text not null check (batch_gate_decision = 'ALLOWED'),
            batch_gate_rule_set_version text not null,
            method_ref text not null,
            method_version bigint not null check (method_version > 0),
            qc_rule_set_ref text not null,
            qc_rule_set_version bigint not null check (qc_rule_set_version > 0),
            rule_set_version text not null,
            opened_by text not null,
            opened_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists qc.qc_result (
            qc_result_id uuid primary key,
            qc_run_id uuid not null references qc.qc_run(qc_run_id),
            run_version bigint not null check (run_version > 1),
            rule_ref text not null,
            rule_version bigint not null check (rule_version > 0),
            control_type text not null check (control_type in
                ('BLANK', 'SPIKE', 'DUPLICATE', 'REFERENCE_MATERIAL', 'CALIBRATION_CHECK')),
            observed_value text not null,
            verdict text not null check (verdict in ('PASS', 'FAIL')),
            verdict_basis text not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (qc_run_id, rule_ref, rule_version)
        );

        create table if not exists qc.qc_verdict (
            qc_run_id uuid primary key references qc.qc_run(qc_run_id),
            verdict_id uuid not null unique,
            run_version bigint not null check (run_version > 1),
            state text not null check (state in ('PASSED', 'FAILED')),
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists qc.qc_impact (
            impact_id uuid primary key,
            qc_run_id uuid not null references qc.qc_run(qc_run_id),
            run_version bigint not null check (run_version > 1),
            target_type text not null check (target_type in ('RESULT_GROUP', 'TASK')),
            target_id text not null,
            target_version bigint not null check (target_version > 0),
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (qc_run_id, target_type, target_id)
        );

        create table if not exists qc.qc_release_gate (
            gate_id uuid primary key,
            qc_run_id uuid not null references qc.qc_run(qc_run_id),
            run_version bigint not null check (run_version > 1),
            kind text not null check (kind in
                ('INVESTIGATION', 'IMPACT_SCOPE', 'VALIDITY_DECISION', 'ADOPTION_RULE', 'TECHNICAL_REVIEW')),
            evidence_ref text not null,
            evidence_version bigint not null check (evidence_version > 0),
            satisfied_by text not null,
            satisfied_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (qc_run_id, kind)
        );

        create table if not exists qc.qc_deviation_approval (
            deviation_id uuid primary key,
            qc_run_id uuid not null references qc.qc_run(qc_run_id),
            run_version bigint not null check (run_version > 1),
            approval_ref text not null,
            approval_version bigint not null check (approval_version > 0),
            reason text not null,
            approved_by text not null,
            approved_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (qc_run_id, approval_ref, approval_version)
        );

        create table if not exists qc.qc_release (
            qc_run_id uuid primary key references qc.qc_run(qc_run_id),
            release_id uuid not null unique,
            run_version bigint not null check (run_version > 1),
            released_by text not null,
            released_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists qc.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function qc.reject_qc_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'QC.QC_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['qc_run', 'qc_result', 'qc_verdict', 'qc_impact',
                                   'qc_release_gate', 'qc_deviation_approval', 'qc_release', 'audit_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on qc.%I for each row execute function qc.reject_qc_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        create index if not exists ix_qc_run_org
          on qc.qc_run (organization_group_id, qc_run_id);

        insert into qc.migration_history (version, applied_at)
        values ('20260727_001_qc_impact', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
