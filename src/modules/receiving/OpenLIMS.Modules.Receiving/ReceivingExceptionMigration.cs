using Npgsql;

namespace OpenLIMS.Modules.Receiving;

internal static class ReceivingExceptionMigrator
{
    public const string Version = "20260725_004_receiving_exception";

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
        select pg_advisory_xact_lock(hashtext('openlims.receiving.migration'));

        create table if not exists receiving.receiving_exception (
            exception_id uuid primary key,
            received_item_id uuid not null references receiving.received_item(id),
            item_version bigint not null check (item_version > 0),
            type text not null check (type in (
              'QUANTITY_SHORTAGE', 'TEMPERATURE_EXCURSION', 'DAMAGED', 'CONTAMINATION',
              'LABEL_CONFLICT', 'IDENTITY_MISMATCH', 'IDENTITY_INDETERMINATE')),
            severity text not null check (severity in ('STANDARD', 'SAFETY_CRITICAL')),
            description text not null,
            observed_at timestamptz not null,
            evidence_refs jsonb not null,
            evidence_hashes jsonb not null,
            created_by text not null,
            created_at timestamptz not null
        );

        create table if not exists receiving.receiving_exception_decision (
            decision_id uuid primary key,
            exception_id uuid not null references receiving.receiving_exception(exception_id),
            version bigint not null check (version > 0),
            expected_exception_version bigint not null check (expected_exception_version > 0),
            decision_type text not null check (decision_type in (
              'AWAIT_CUSTOMER', 'CONDITIONAL_ACCEPT', 'REJECT', 'SAFETY_HOLD')),
            matrix_version text not null,
            allowed_actions jsonb not null,
            prohibited_actions jsonb not null,
            valid_until timestamptz null,
            evidence_refs jsonb not null,
            evidence_hashes jsonb not null,
            technical_impact text not null,
            rationale text not null,
            decided_at timestamptz not null,
            decided_by text not null,
            unique (exception_id, version)
        );

        create table if not exists receiving.receiving_exception_state (
            exception_id uuid primary key references receiving.receiving_exception(exception_id),
            status text not null check (status in (
              'OPEN', 'AWAITING_CUSTOMER', 'CONDITIONALLY_ACCEPTED', 'REJECTED', 'SAFETY_HOLD')),
            version bigint not null check (version > 0),
            current_decision_version bigint null check (current_decision_version is null or current_decision_version > 0),
            updated_at timestamptz not null,
            updated_by text not null,
            foreign key (exception_id, current_decision_version)
              references receiving.receiving_exception_decision(exception_id, version)
        );

        create or replace function receiving.reject_exception_fact_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'REC.EXCEPTION_FACT_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        begin
          if not exists (select 1 from pg_trigger where tgname = 'trg_receiving_exception_append_only') then
            create trigger trg_receiving_exception_append_only
            before update or delete on receiving.receiving_exception
            for each row execute function receiving.reject_exception_fact_mutation();
          end if;
          if not exists (select 1 from pg_trigger where tgname = 'trg_receiving_exception_decision_append_only') then
            create trigger trg_receiving_exception_decision_append_only
            before update or delete on receiving.receiving_exception_decision
            for each row execute function receiving.reject_exception_fact_mutation();
          end if;
        end
        $$;

        create index if not exists ix_receiving_exception_item
          on receiving.receiving_exception (received_item_id, created_at desc);
        create index if not exists ix_receiving_exception_state_status
          on receiving.receiving_exception_state (status);

        insert into receiving.migration_history (version, applied_at)
        values ('20260725_004_receiving_exception', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
