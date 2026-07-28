using Npgsql;

namespace OpenLIMS.Modules.Textile;

internal static class TextileMigrator
{
    public const string Version = "20260728_001_textile_runtime";

    public static async Task ApplyAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(MigrationSql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private const string MigrationSql = """
        select pg_advisory_xact_lock(hashtext('openlims.textile.migration'));

        create schema if not exists textile;

        create table if not exists textile.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists textile.sample_requirement (
            requirement_id text not null,
            version bigint not null check (version > 0),
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            rule_set_version text not null,
            input_hash text not null check (length(input_hash) = 64),
            calculation jsonb not null,
            result jsonb not null,
            decision text not null check (decision in ('SUFFICIENT', 'INSUFFICIENT', 'UNKNOWN')),
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            primary key (organization_group_id, requirement_id, version)
        );

        create table if not exists textile.cutting_plan (
            cutting_plan_id text not null,
            version bigint not null check (version > 0),
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            sample_requirement_id text not null,
            sample_requirement_version bigint not null check (sample_requirement_version > 0),
            sample_requirement_input_hash text not null check (length(sample_requirement_input_hash) = 64),
            rule_set_version text not null,
            input_hash text not null check (length(input_hash) = 64),
            plan jsonb not null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            primary key (organization_group_id, cutting_plan_id, version),
            foreign key (organization_group_id, sample_requirement_id, sample_requirement_version)
                references textile.sample_requirement (organization_group_id, requirement_id, version)
        );

        create table if not exists textile.cutting_plan_approval (
            organization_group_id text not null,
            cutting_plan_id text not null,
            cutting_plan_version bigint not null check (cutting_plan_version > 0),
            sample_requirement_id text not null,
            sample_requirement_version bigint not null check (sample_requirement_version > 0),
            sample_requirement_input_hash text not null check (length(sample_requirement_input_hash) = 64),
            rule_set_version text not null,
            approved_by text not null,
            approved_at timestamptz not null,
            approval_comment text null,
            event_id text not null unique,
            correlation_id text not null,
            primary key (organization_group_id, cutting_plan_id, cutting_plan_version),
            foreign key (organization_group_id, cutting_plan_id, cutting_plan_version)
                references textile.cutting_plan (organization_group_id, cutting_plan_id, version)
        );

        create table if not exists textile.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null check (length(target_hash) = 64),
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create index if not exists ix_textile_requirement_decision
            on textile.sample_requirement (organization_group_id, decision, created_at);
        create index if not exists ix_textile_plan_requirement
            on textile.cutting_plan (
                organization_group_id,
                sample_requirement_id,
                sample_requirement_version);

        create or replace function textile.reject_textile_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'TEX.TEXTILE_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array[
            'sample_requirement',
            'cutting_plan',
            'cutting_plan_approval',
            'audit_attempt'
          ] loop
            if not exists (
              select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only'
            ) then
              execute format(
                'create trigger trg_%I_append_only before update or delete on textile.%I for each row execute function textile.reject_textile_mutation()',
                t,
                t);
            end if;
          end loop;
        end
        $$;

        insert into textile.migration_history (version, applied_at)
        values ('20260728_001_textile_runtime', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
