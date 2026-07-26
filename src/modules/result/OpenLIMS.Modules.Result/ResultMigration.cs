using Npgsql;

namespace OpenLIMS.Modules.Result;

internal static class ResultMigrator
{
    public const string Version = "20260726_001_result_adoption";

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
        select pg_advisory_xact_lock(hashtext('openlims.result.migration'));

        create schema if not exists result;

        create table if not exists result.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists result.result_group (
            result_group_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            batch_id text not null,
            batch_version bigint not null check (batch_version > 0),
            batch_gate_decision text not null check (batch_gate_decision = 'ALLOWED'),
            batch_gate_rule_set_version text not null,
            member_id text not null,
            test_item_ref text not null,
            test_item_version bigint not null check (test_item_version > 0),
            scope_line_id text not null,
            rule_set_version text not null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists result.result_observation (
            observation_id uuid primary key,
            result_group_id uuid not null references result.result_group(result_group_id),
            group_version bigint not null check (group_version > 1),
            kind text not null check (kind in ('INITIAL', 'DUPLICATE', 'RETEST', 'SUPPLEMENT', 'RE_PREPARATION', 'RE_SAMPLING')),
            value text not null,
            unit text not null,
            evidence_source text not null check (evidence_source in ('CDS', 'ELN', 'INSTRUMENT', 'MANUAL')),
            evidence_ref text not null,
            evidence_version bigint not null check (evidence_version > 0),
            evidence_sha256 text not null check (evidence_sha256 ~ '^[a-f0-9]{64}$'),
            parser_version text not null,
            trigger_reason text null,
            approval_ref text null,
            approval_version bigint null check (approval_version is null or approval_version > 0),
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (result_group_id, group_version),
            check (
              (kind = 'INITIAL' and trigger_reason is null and approval_ref is null and approval_version is null)
              or (kind <> 'INITIAL' and trigger_reason is not null and approval_ref is not null and approval_version is not null)
            )
        );

        create table if not exists result.result_derivation (
            derivation_id uuid primary key,
            result_group_id uuid not null references result.result_group(result_group_id),
            group_version bigint not null check (group_version > 1),
            aggregation_rule_ref text not null,
            aggregation_rule_version bigint not null check (aggregation_rule_version > 0),
            value text not null,
            unit text not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (result_group_id, group_version)
        );

        create table if not exists result.derivation_input (
            derivation_id uuid not null references result.result_derivation(derivation_id),
            target_id uuid not null,
            included boolean not null,
            rationale text null,
            primary key (derivation_id, target_id),
            check (included or rationale is not null)
        );

        create table if not exists result.adoption_rule (
            result_group_id uuid not null references result.result_group(result_group_id),
            rule_version bigint not null check (rule_version > 0),
            group_version bigint not null check (group_version > 1),
            strategy text not null check (strategy in ('RETEST_REPLACES_ORIGINAL', 'TECHNICAL_REVIEW_SELECTS')),
            rule_ref text not null,
            rule_ref_version bigint not null check (rule_ref_version > 0),
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            primary key (result_group_id, rule_version),
            unique (result_group_id, group_version)
        );

        create table if not exists result.result_adoption (
            result_group_id uuid not null references result.result_group(result_group_id),
            adoption_version bigint not null check (adoption_version > 0),
            group_version bigint not null check (group_version > 1),
            target_id uuid not null,
            rule_version bigint not null,
            review_approval_ref text null,
            review_approval_version bigint null check (review_approval_version is null or review_approval_version > 0),
            adopted_by text not null,
            adopted_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            primary key (result_group_id, adoption_version),
            unique (result_group_id, group_version),
            foreign key (result_group_id, rule_version) references result.adoption_rule(result_group_id, rule_version)
        );

        create table if not exists result.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function result.reject_result_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'RES.RESULT_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['result_group', 'result_observation', 'result_derivation', 'derivation_input', 'adoption_rule', 'result_adoption', 'audit_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on result.%I for each row execute function result.reject_result_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        create index if not exists ix_result_group_org
          on result.result_group (organization_group_id, result_group_id);

        insert into result.migration_history (version, applied_at)
        values ('20260726_001_result_adoption', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
