using Npgsql;

namespace OpenLIMS.Modules.Batch;

internal static class BatchMigrator
{
    public const string Version = "20260726_001_batch_management";

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
        select pg_advisory_xact_lock(hashtext('openlims.batch.migration'));

        create schema if not exists batch;

        create table if not exists batch.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists batch.batch (
            batch_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            batch_type text not null check (batch_type in ('PREPARATION', 'PRECONDITIONING', 'ANALYTICAL', 'INSTRUMENT_RUN')),
            rule_set_version text not null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists batch.batch_member (
            member_id uuid primary key,
            batch_id uuid not null references batch.batch(batch_id),
            batch_version bigint not null check (batch_version > 1),
            member_type text not null check (member_type in ('SPECIMEN', 'QC_SAMPLE')),
            allocation_id text null,
            subject_allocation_version bigint null check (subject_allocation_version is null or subject_allocation_version > 0),
            allocation_gate_decision text null check (allocation_gate_decision is null or allocation_gate_decision = 'ALLOWED'),
            allocation_gate_rule_set_version text null,
            qc_ref text null,
            qc_version bigint null check (qc_version is null or qc_version > 0),
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            added_by text not null,
            added_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (batch_id, batch_version),
            check (
              (member_type = 'SPECIMEN' and allocation_id is not null and subject_allocation_version is not null
                and allocation_gate_decision is not null and allocation_gate_rule_set_version is not null
                and qc_ref is null and qc_version is null)
              or (member_type = 'QC_SAMPLE' and allocation_id is null and subject_allocation_version is null
                and allocation_gate_decision is null and allocation_gate_rule_set_version is null
                and qc_ref is not null and qc_version is not null)
            )
        );

        create unique index if not exists ux_batch_member_allocation
          on batch.batch_member (batch_id, allocation_id) where allocation_id is not null;

        create table if not exists batch.batch_evidence (
            evidence_id uuid primary key,
            batch_id uuid not null references batch.batch(batch_id),
            batch_version bigint not null check (batch_version > 1),
            source_system text not null check (source_system in ('CDS', 'ELN', 'INSTRUMENT')),
            external_ref text not null,
            external_version bigint not null check (external_version > 0),
            sha256 text not null check (sha256 ~ '^[a-f0-9]{64}$'),
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (batch_id, batch_version)
        );

        create table if not exists batch.batch_freeze (
            batch_id uuid primary key references batch.batch(batch_id),
            freeze_id uuid not null unique,
            batch_version bigint not null check (batch_version > 1),
            cause text not null check (cause in ('QC_FAILURE', 'ENVIRONMENT_OUT_OF_TOLERANCE', 'CALIBRATION_INVALID')),
            affected_member_count int not null check (affected_member_count >= 0),
            approved_follow_up_ref text null,
            approved_follow_up_version bigint null check (approved_follow_up_version is null or approved_follow_up_version > 0),
            frozen_by text not null,
            frozen_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists batch.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function batch.reject_batch_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'BAT.BATCH_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['batch', 'batch_member', 'batch_evidence', 'batch_freeze', 'audit_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on batch.%I for each row execute function batch.reject_batch_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        create index if not exists ix_batch_org
          on batch.batch (organization_group_id, batch_id);

        insert into batch.migration_history (version, applied_at)
        values ('20260726_001_batch_management', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
