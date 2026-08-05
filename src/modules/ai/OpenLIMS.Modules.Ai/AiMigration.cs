using Npgsql;

namespace OpenLIMS.Modules.Ai;

internal static class AiMigrator
{
    public const string Version = "20260805_005_ai_runtime";

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
        select pg_advisory_xact_lock(hashtext('openlims.ai.runtime.migration'));

        create schema if not exists ai;

        create table if not exists ai.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists ai.run_request (
            run_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            envelope_json jsonb not null,
            validation_profile_ref text not null,
            validation_profile_version bigint not null check (validation_profile_version > 0),
            allowed_fields text[] not null,
            allowed_units text[] not null,
            request_hash text not null check (request_hash ~ '^[a-f0-9]{64}$'),
            idempotency_key text not null,
            requested_by text not null,
            requested_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (organization_group_id, idempotency_key)
        );

        create table if not exists ai.run_outcome (
            run_id uuid primary key references ai.run_request(run_id),
            status text not null check (status in
                ('ACCEPTED', 'QUARANTINED', 'PROVIDER_DISABLED', 'PROVIDER_FAILED')),
            provider_status text not null check (provider_status in ('COMPLETED', 'DISABLED', 'FAILED')),
            provider_external_reference text null,
            provider_failure_code text null,
            original_output_json jsonb null,
            validation_json jsonb null,
            human_review_required boolean not null,
            manual_fallback_required boolean not null,
            completed_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            check (
              (provider_status = 'COMPLETED' and provider_external_reference is not null and original_output_json is not null)
              or (provider_status = 'DISABLED' and provider_external_reference is null and original_output_json is null)
              or (provider_status = 'FAILED' and provider_external_reference is null and original_output_json is null)
            )
        );

        create table if not exists ai.disposition (
            disposition_id uuid primary key,
            run_id uuid not null references ai.run_request(run_id),
            run_version bigint not null check (run_version > 1),
            candidate_id text not null,
            kind text not null check (kind in ('ACCEPT', 'MODIFY', 'SPLIT', 'MERGE', 'REJECT')),
            ai_original_value text not null,
            human_value text null,
            reason text not null,
            responsible_actor text not null,
            idempotency_key text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            check ((kind = 'MODIFY' and human_value is not null)
                or (kind <> 'MODIFY' and human_value is null)),
            unique (run_id, run_version),
            unique (run_id, idempotency_key)
        );

        create table if not exists ai.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create index if not exists ix_ai_run_request_scope
          on ai.run_request (organization_group_id, legal_entity_id, laboratory_id, customer_id);
        create index if not exists ix_ai_disposition_run on ai.disposition (run_id, run_version);

        create or replace function ai.reject_runtime_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'AIX.RUNTIME_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['run_request', 'run_outcome', 'disposition', 'audit_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_ai_' || t || '_append_only') then
              execute format(
                'create trigger trg_ai_%I_append_only before update or delete on ai.%I for each row execute function ai.reject_runtime_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        insert into ai.migration_history (version, applied_at)
        values ('20260805_005_ai_runtime', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
