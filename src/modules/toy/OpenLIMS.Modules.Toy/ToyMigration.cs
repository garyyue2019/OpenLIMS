using Npgsql;

namespace OpenLIMS.Modules.Toy;

internal static class ToyMigrator
{
    public const string Version = "20260727_001_toy_age_grade";

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

        create schema if not exists toy;

        create table if not exists toy.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists toy.product (
            product_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            rule_set_version text not null,
            registered_by text not null,
            registered_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        -- OPS-TOY-001: the customer's claim lives here and nowhere else. It is
        -- never merged into a determination, so revising it cannot rewrite one.
        create table if not exists toy.age_declaration (
            declaration_id uuid primary key,
            product_id uuid not null references toy.product(product_id),
            product_version bigint not null check (product_version > 0),
            declared_minimum_age_months int not null
                check (declared_minimum_age_months between 0 and 216),
            intended_use text not null,
            declaration_source text not null,
            declared_by text not null,
            declared_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists toy.age_grade_decision (
            decision_id uuid primary key,
            product_id uuid not null references toy.product(product_id),
            version_number int not null check (version_number > 0),
            minimum_age_months int not null check (minimum_age_months between 0 and 216),
            rationale text not null,
            standard_ref text not null,
            standard_version bigint not null check (standard_version > 0),
            approved_by text not null,
            decided_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (product_id, version_number)
        );

        -- Freezing is its own fact rather than a status column, so "which
        -- determination is in force" is derived from an append-only log and
        -- two of them can never be in force at once.
        create table if not exists toy.age_grade_freeze (
            freeze_id uuid primary key,
            product_id uuid not null,
            version_number int not null check (version_number > 0),
            frozen_by text not null,
            frozen_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (product_id, version_number),
            foreign key (product_id, version_number)
                references toy.age_grade_decision (product_id, version_number)
        );

        create table if not exists toy.accessibility_assessment (
            assessment_id uuid primary key,
            product_id uuid not null references toy.product(product_id),
            version_number int not null check (version_number > 0),
            stage text not null check (stage in ('INITIAL', 'AFTER_NORMAL_USE', 'AFTER_ABUSE')),
            abuse_event_ref text null,
            assessed_by text not null,
            assessed_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (product_id, version_number),
            -- An abuse assessment must name its event; the other two must not
            -- claim one (OPS-TOY-003).
            check ((stage = 'AFTER_ABUSE') = (abuse_event_ref is not null)),
            -- The as-received state is the baseline everything later is
            -- compared against, so it is version 1 and nothing else is.
            check ((version_number = 1) = (stage = 'INITIAL'))
        );

        create table if not exists toy.accessible_part (
            part_row_id uuid primary key,
            assessment_id uuid not null references toy.accessibility_assessment(assessment_id),
            part_name text not null,
            unique (assessment_id, part_name)
        );

        create table if not exists toy.reassessment_trigger (
            trigger_id uuid primary key,
            product_id uuid not null references toy.product(product_id),
            assessment_id uuid not null references toy.accessibility_assessment(assessment_id),
            assessment_version int not null check (assessment_version > 0),
            scope text not null check (scope in ('MECHANICAL', 'CHEMICAL', 'LABELING')),
            newly_exposed_parts text[] not null check (cardinality(newly_exposed_parts) > 0),
            raised_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (assessment_id, scope)
        );

        create table if not exists toy.reassessment_resolution (
            resolution_id uuid primary key,
            trigger_id uuid not null unique references toy.reassessment_trigger(trigger_id),
            product_id uuid not null references toy.product(product_id),
            resolution_ref text not null,
            resolution_version bigint not null check (resolution_version > 0),
            resolved_by text not null,
            resolved_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists toy.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function toy.reject_toy_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'TOY.TOY_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['product', 'age_declaration', 'age_grade_decision',
                                   'age_grade_freeze', 'accessibility_assessment', 'accessible_part',
                                   'reassessment_trigger', 'reassessment_resolution', 'audit_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on toy.%I for each row execute function toy.reject_toy_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        create index if not exists ix_toy_product_org
          on toy.product (organization_group_id, product_id);

        insert into toy.migration_history (version, applied_at)
        values ('20260727_001_toy_age_grade', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
