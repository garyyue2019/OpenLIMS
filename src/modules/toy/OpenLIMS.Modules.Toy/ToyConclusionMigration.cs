using Npgsql;

namespace OpenLIMS.Modules.Toy;

internal static class ToyConclusionMigrator
{
    public const string Version = "20260728_002_toy_conclusion";

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
        select pg_advisory_xact_lock(hashtext('openlims.toy.conclusion.migration'));

        -- OD-034@1.0.0: Two-level conclusion hierarchy
        -- ITEM_CONFORMITY: single test item conformity, approved by technical director
        -- TESTED_SCOPE_CONFORMITY: multi-TestUnit conformity with mandatory uncovered scope disclosure,
        --                           approved by authorized signatory with re-authentication signature
        -- WHOLE_PRODUCT_COMPLIANCE: permanently prohibited (no table column, no enum, no interface)

        create table if not exists toy.conclusion (
            conclusion_id uuid primary key,
            organization_group_id text not null,
            conclusion_level text not null check (conclusion_level in ('ITEM_CONFORMITY', 'TESTED_SCOPE_CONFORMITY')),

            -- ITEM_CONFORMITY fields
            adopted_result_ref text null,
            adopted_result_version bigint null check (adopted_result_version > 0),
            requirement_ref text null,
            requirement_version bigint null check (requirement_version > 0),

            -- TESTED_SCOPE_CONFORMITY fields
            product_ref text null,
            product_version bigint null check (product_version > 0),
            test_unit_plan_ref text null,
            test_unit_plan_version bigint null check (test_unit_plan_version > 0),
            signature_ref text null,

            -- Common fields
            rule_set_version text not null,
            statement text not null,
            approved_by text not null,
            approved_at timestamptz not null,
            version bigint not null check (version > 0),
            event_id text not null unique,
            correlation_id text not null,

            -- Audit fields (OD-034: conclusions are append-only immutable facts)
            created_at timestamptz not null default now(),
            created_by text not null default 'SYSTEM',
            updated_at timestamptz null,
            updated_by text null,
            deleted_at timestamptz null,
            deleted_by text null,
            is_deleted boolean not null default false,

            -- OD-034: ITEM_CONFORMITY requires adoptedResultRef + requirementRef
            check (
                (conclusion_level = 'ITEM_CONFORMITY' and
                 adopted_result_ref is not null and adopted_result_version is not null and
                 requirement_ref is not null and requirement_version is not null and
                 product_ref is null and test_unit_plan_ref is null and signature_ref is null)
                or
                (conclusion_level = 'TESTED_SCOPE_CONFORMITY' and
                 product_ref is not null and product_version is not null and
                 test_unit_plan_ref is not null and test_unit_plan_version is not null and
                 adopted_result_ref is null and requirement_ref is null)
            )
        );

        create index if not exists idx_conclusion_product on toy.conclusion (product_ref, product_version, organization_group_id)
            where conclusion_level = 'TESTED_SCOPE_CONFORMITY';

        create index if not exists idx_conclusion_org on toy.conclusion (organization_group_id, approved_at desc);

        -- OD-034: TestUnit evidence for TESTED_SCOPE_CONFORMITY
        -- Must reference all version-pinned inputs per BUS-TOY-006
        create table if not exists toy.conclusion_test_unit (
            conclusion_id uuid not null references toy.conclusion(conclusion_id),
            test_unit_id text not null,
            physical_object_ref text not null,
            physical_object_version bigint not null check (physical_object_version > 0),
            hazard_domain_ref text not null,
            hazard_domain_version bigint not null check (hazard_domain_version > 0),
            adopted_result_ref text not null,
            adopted_result_version bigint not null check (adopted_result_version > 0),
            result_provenance_graph_ref text not null,
            result_provenance_graph_version bigint not null check (result_provenance_graph_version > 0),
            coverage_decision_ref text null,
            coverage_decision_version bigint not null check (coverage_decision_version > 0),
            primary key (conclusion_id, test_unit_id)
        );

        -- OD-034: Covered hazard domains list
        create table if not exists toy.conclusion_hazard_domain (
            conclusion_id uuid not null references toy.conclusion(conclusion_id),
            hazard_domain_ref text not null,
            primary key (conclusion_id, hazard_domain_ref)
        );

        -- OD-034: Uncovered scopes disclosure (MANDATORY, cannot be omitted)
        create table if not exists toy.conclusion_uncovered_scope (
            conclusion_id uuid not null references toy.conclusion(conclusion_id),
            scope text not null,
            reason text not null check (reason in ('NOT_TESTED', 'UNKNOWN', 'NOT_APPLICABLE')),
            detail text not null,
            primary key (conclusion_id, scope)
        );

        -- OD-034: External references (informational only, notPartOfThisConclusion=true)
        create table if not exists toy.conclusion_external_reference (
            conclusion_id uuid not null references toy.conclusion(conclusion_id),
            issuer text not null,
            reference text not null,
            stated_scope text not null,
            not_part_of_this_conclusion boolean not null check (not_part_of_this_conclusion = true),
            primary key (conclusion_id, issuer, reference)
        );

        -- OD-034: Conclusions are append-only immutable facts
        -- Prevent UPDATE and DELETE to enforce immutability at database layer
        create or replace function toy.prevent_conclusion_mutation()
        returns trigger as $$
        begin
            if TG_OP = 'UPDATE' then
                raise exception 'TOY.CONCLUSION_IMMUTABLE: Conclusions are append-only immutable facts. Create a new version instead.'
                    using errcode = '23514';
            end if;
            if TG_OP = 'DELETE' then
                raise exception 'TOY.CONCLUSION_IMMUTABLE: Conclusions cannot be deleted. They are permanent audit records.'
                    using errcode = '23514';
            end if;
            return null;
        end;
        $$ language plpgsql;

        drop trigger if exists prevent_conclusion_update on toy.conclusion;
        create trigger prevent_conclusion_update
            before update on toy.conclusion
            for each row execute function toy.prevent_conclusion_mutation();

        drop trigger if exists prevent_conclusion_delete on toy.conclusion;
        create trigger prevent_conclusion_delete
            before delete on toy.conclusion
            for each row execute function toy.prevent_conclusion_mutation();

        -- Record migration
        insert into toy.migration_history (version, applied_at)
        values ('20260728_002_toy_conclusion', now())
        on conflict (version) do nothing;
        """;
}
