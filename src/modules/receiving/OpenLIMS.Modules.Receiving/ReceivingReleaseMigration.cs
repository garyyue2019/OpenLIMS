using Npgsql;

namespace OpenLIMS.Modules.Receiving;

internal static class ReceivingReleaseMigrator
{
    public const string Version = "20260726_005_receiving_release";

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

        alter table receiving.received_item
          drop constraint if exists received_item_state_check;
        alter table receiving.received_item
          add constraint received_item_state_check
          check (state in ('REGISTERED', 'QUARANTINED', 'ACCEPTED', 'CONDITIONALLY_ACCEPTED'));

        create table if not exists receiving.receiving_release_decision (
            release_decision_id uuid primary key,
            received_item_id uuid not null references receiving.received_item(id),
            version bigint not null check (version > 0),
            item_version bigint not null check (item_version > 0),
            identity_decision_id uuid not null references receiving.identity_decision(decision_id),
            identity_decision_version bigint not null check (identity_decision_version > 0),
            exception_decision_versions jsonb not null,
            release_rule_version text not null,
            exception_matrix_version text not null,
            outcome text not null check (outcome in ('RELEASED', 'RELEASED_WITH_CONSTRAINTS')),
            allowed_actions jsonb not null,
            prohibited_actions jsonb not null,
            constraints_valid_until timestamptz null,
            rationale text not null,
            approved_at timestamptz not null,
            approved_by text not null,
            unique (received_item_id, version),
            foreign key (received_item_id, identity_decision_version)
              references receiving.identity_decision(received_item_id, version)
        );

        create or replace function receiving.reject_release_decision_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'REC.RELEASE_DECISION_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        create or replace function receiving.reject_received_item_state_history_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'REC.RECEIVED_ITEM_STATE_HISTORY_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        begin
          if not exists (select 1 from pg_trigger where tgname = 'trg_receiving_release_decision_append_only') then
            create trigger trg_receiving_release_decision_append_only
            before update or delete on receiving.receiving_release_decision
            for each row execute function receiving.reject_release_decision_mutation();
          end if;
          if not exists (select 1 from pg_trigger where tgname = 'trg_received_item_state_history_append_only') then
            create trigger trg_received_item_state_history_append_only
            before update or delete on receiving.received_item_state_history
            for each row execute function receiving.reject_received_item_state_history_mutation();
          end if;
        end
        $$;

        create index if not exists ix_receiving_release_decision_item
          on receiving.receiving_release_decision (received_item_id, version desc);

        insert into receiving.migration_history (version, applied_at)
        values ('20260726_005_receiving_release', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
