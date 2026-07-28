using Npgsql;

namespace OpenLIMS.Modules.Toy;

internal static class ToyConclusionRemediationMigrator
{
    public const string Version = "20260728_004_toy_conclusion_remediation";

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
        select pg_advisory_xact_lock(hashtext('openlims.toy.conclusion.remediation.migration'));

        -- DEV-031 / ATC-TOY-005@1.0.0: append-only remediation. Existing rows
        -- remain untouched and may retain nulls in the new evidence columns.
        alter table toy.conclusion
            add column if not exists legal_entity_id text null,
            add column if not exists laboratory_id text null,
            add column if not exists content_hash text null,
            add column if not exists reauthentication_ref text null,
            add column if not exists reauthentication_version bigint null,
            add column if not exists signing_intent text null,
            add column if not exists resolved_target_ref text null,
            add column if not exists resolved_target_kind text null,
            add column if not exists result_recorded_by text null,
            add column if not exists result_group_version bigint null;

        alter table toy.conclusion_test_unit
            add column if not exists requirement_refs text[] null,
            add column if not exists resolved_target_ref text null,
            add column if not exists resolved_target_kind text null,
            add column if not exists result_recorded_by text null,
            add column if not exists result_group_version bigint null;

        do $$
        begin
            if not exists (
                select 1 from pg_constraint
                where conname = 'ck_toy_conclusion_remediation_complete'
                  and conrelid = 'toy.conclusion'::regclass
            ) then
                alter table toy.conclusion
                    add constraint ck_toy_conclusion_remediation_complete check (
                        legal_entity_id is not null and btrim(legal_entity_id) <> '' and
                        laboratory_id is not null and btrim(laboratory_id) <> '' and
                        content_hash is not null and content_hash ~ '^[0-9a-f]{64}$' and
                        ((
                            conclusion_level = 'ITEM_CONFORMITY' and
                            resolved_target_ref is not null and btrim(resolved_target_ref) <> '' and
                            resolved_target_kind is not null and btrim(resolved_target_kind) <> '' and
                            result_recorded_by is not null and btrim(result_recorded_by) <> '' and
                            result_group_version is not null and result_group_version > 0 and
                            reauthentication_ref is null and reauthentication_version is null and
                            signing_intent is null and signature_ref is null
                        ) or (
                            conclusion_level = 'TESTED_SCOPE_CONFORMITY' and
                            reauthentication_ref is not null and btrim(reauthentication_ref) <> '' and
                            reauthentication_version is not null and reauthentication_version > 0 and
                            signing_intent is not null and btrim(signing_intent) <> '' and
                            signature_ref = reauthentication_ref || '@' || reauthentication_version::text and
                            resolved_target_ref is null and resolved_target_kind is null and
                            result_recorded_by is null and result_group_version is null
                        ))
                    ) not valid;
            end if;

            if not exists (
                select 1 from pg_constraint
                where conname = 'ck_toy_conclusion_test_unit_remediation_complete'
                  and conrelid = 'toy.conclusion_test_unit'::regclass
            ) then
                alter table toy.conclusion_test_unit
                    add constraint ck_toy_conclusion_test_unit_remediation_complete check (
                        coverage_decision_ref is not null and btrim(coverage_decision_ref) <> '' and
                        coverage_decision_version > 0 and
                        requirement_refs is not null and
                        resolved_target_ref is not null and btrim(resolved_target_ref) <> '' and
                        resolved_target_kind is not null and btrim(resolved_target_kind) <> '' and
                        result_recorded_by is not null and btrim(result_recorded_by) <> '' and
                        result_group_version is not null and result_group_version > 0
                    ) not valid;
            end if;
        end;
        $$;

        create unique index if not exists ux_toy_conclusion_correlation_remediated
            on toy.conclusion (organization_group_id, correlation_id)
            where content_hash is not null;

        create or replace function toy.prevent_conclusion_fact_mutation()
        returns trigger as $$
        begin
            raise exception 'TOY.CONCLUSION_IMMUTABLE: conclusion facts are append-only'
                using errcode = '55000';
        end;
        $$ language plpgsql;

        drop trigger if exists prevent_conclusion_update on toy.conclusion;
        drop trigger if exists prevent_conclusion_delete on toy.conclusion;
        create trigger prevent_conclusion_update
            before update on toy.conclusion
            for each row execute function toy.prevent_conclusion_fact_mutation();
        create trigger prevent_conclusion_delete
            before delete on toy.conclusion
            for each row execute function toy.prevent_conclusion_fact_mutation();

        drop trigger if exists prevent_conclusion_test_unit_mutation on toy.conclusion_test_unit;
        create trigger prevent_conclusion_test_unit_mutation
            before update or delete on toy.conclusion_test_unit
            for each row execute function toy.prevent_conclusion_fact_mutation();

        drop trigger if exists prevent_conclusion_hazard_domain_mutation on toy.conclusion_hazard_domain;
        create trigger prevent_conclusion_hazard_domain_mutation
            before update or delete on toy.conclusion_hazard_domain
            for each row execute function toy.prevent_conclusion_fact_mutation();

        drop trigger if exists prevent_conclusion_uncovered_scope_mutation on toy.conclusion_uncovered_scope;
        create trigger prevent_conclusion_uncovered_scope_mutation
            before update or delete on toy.conclusion_uncovered_scope
            for each row execute function toy.prevent_conclusion_fact_mutation();

        drop trigger if exists prevent_conclusion_external_reference_mutation on toy.conclusion_external_reference;
        create trigger prevent_conclusion_external_reference_mutation
            before update or delete on toy.conclusion_external_reference
            for each row execute function toy.prevent_conclusion_fact_mutation();

        insert into toy.migration_history (version, applied_at)
        values ('20260728_004_toy_conclusion_remediation', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
