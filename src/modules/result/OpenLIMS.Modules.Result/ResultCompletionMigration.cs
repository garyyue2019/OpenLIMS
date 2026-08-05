using Npgsql;

namespace OpenLIMS.Modules.Result;

internal static class ResultCompletionMigrator
{
    public const string Version = "20260805_002_result_completion";

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
        select pg_advisory_xact_lock(hashtext('openlims.result.completion.migration'));

        create table if not exists result.result_calculation (
            calculation_id uuid primary key,
            result_group_id uuid not null references result.result_group(result_group_id),
            group_version bigint not null check (group_version > 1),
            inputs_snapshot jsonb not null check (jsonb_typeof(inputs_snapshot) = 'array'),
            rule_snapshot jsonb not null check (jsonb_typeof(rule_snapshot) = 'object'),
            exact_value numeric not null,
            rounded_value numeric not null,
            reported_value text not null,
            output_unit text not null,
            qualification text not null check (qualification in ('BELOW_LOD', 'BELOW_LOQ', 'QUANTIFIED')),
            limit_decision text not null check (limit_decision in ('NOT_EVALUATED', 'PASS', 'FAIL', 'UNKNOWN')),
            executed_by text not null,
            executed_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (result_group_id, group_version)
        );

        create table if not exists result.accreditation_assessment (
            assessment_id uuid primary key,
            result_group_id uuid not null references result.result_group(result_group_id),
            group_version bigint not null check (group_version > 1),
            stage text not null check (stage in ('EXECUTION', 'RESULT')),
            target_id uuid null,
            accreditation_ref text not null,
            accreditation_version bigint not null check (accreditation_version > 0),
            method_ref text not null,
            method_version bigint not null check (method_version > 0),
            site_id text not null,
            product_or_matrix text not null,
            parameter_id text not null,
            range_unit text not null,
            range_lower numeric not null,
            range_upper numeric not null,
            valid_from date not null,
            valid_to date not null,
            authorized_actor_ids jsonb not null check (jsonb_typeof(authorized_actor_ids) = 'array'),
            decision text not null check (decision in ('ELIGIBLE', 'BLOCKED')),
            reason_codes jsonb not null check (jsonb_typeof(reason_codes) = 'array'),
            assessed_by text not null,
            assessed_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (result_group_id, group_version),
            check (range_lower <= range_upper),
            check (valid_from <= valid_to),
            check ((stage = 'EXECUTION' and target_id is null) or (stage = 'RESULT' and target_id is not null))
        );

        do $$
        declare t text;
        begin
          foreach t in array array['result_calculation', 'accreditation_assessment'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on result.%I for each row execute function result.reject_result_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        create index if not exists ix_result_calculation_group
          on result.result_calculation (result_group_id, group_version);
        create index if not exists ix_result_accreditation_group_stage
          on result.accreditation_assessment (result_group_id, stage, group_version desc);

        insert into result.migration_history (version, applied_at)
        values ('20260805_002_result_completion', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
