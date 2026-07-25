using Npgsql;

namespace OpenLIMS.Modules.Receiving;

internal static class ReceivingIdentityAssessmentMigrator
{
    public const string Version = "20260725_003_identity_assessment";

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

        create table if not exists receiving.identity_declaration_snapshot (
            received_item_id uuid not null references receiving.received_item(id),
            snapshot_version bigint not null check (snapshot_version > 0),
            item_version bigint not null check (item_version > 0),
            declared_description text not null,
            model text not null,
            batch text not null,
            serial_number text null,
            color text not null,
            captured_at timestamptz not null,
            captured_by text not null,
            primary key (received_item_id, snapshot_version)
        );

        create table if not exists receiving.identity_observation (
            observation_id uuid primary key,
            received_item_id uuid not null references receiving.received_item(id),
            version bigint not null check (version > 0),
            expected_item_version bigint not null check (expected_item_version > 0),
            observed_labels jsonb not null,
            observed_model text not null,
            observed_batch text not null,
            appearance text not null,
            attachment_refs jsonb not null,
            attachment_hashes jsonb not null,
            observed_at timestamptz not null,
            observed_by text not null,
            unique (received_item_id, version)
        );

        create table if not exists receiving.identity_decision (
            decision_id uuid primary key,
            received_item_id uuid not null references receiving.received_item(id),
            version bigint not null check (version > 0),
            observation_version bigint not null check (observation_version > 0),
            declaration_snapshot_version bigint not null check (declaration_snapshot_version > 0),
            outcome text not null check (outcome in ('MATCHED', 'MISMATCHED', 'INDETERMINATE')),
            reason_code text not null,
            rationale text not null,
            rule_set_version text not null,
            decided_at timestamptz not null,
            decided_by text not null,
            unique (received_item_id, version),
            foreign key (received_item_id, observation_version)
              references receiving.identity_observation(received_item_id, version),
            foreign key (received_item_id, declaration_snapshot_version)
              references receiving.identity_declaration_snapshot(received_item_id, snapshot_version)
        );

        create table if not exists receiving.identity_assessment (
            received_item_id uuid primary key references receiving.received_item(id),
            assessment_state text not null check (assessment_state in ('IN_PROGRESS', 'MATCHED', 'MISMATCHED', 'INDETERMINATE')),
            assessment_version bigint not null check (assessment_version > 0),
            declaration_snapshot_version bigint not null check (declaration_snapshot_version > 0),
            current_observation_version bigint not null check (current_observation_version > 0),
            current_decision_version bigint null check (current_decision_version is null or current_decision_version > 0),
            updated_at timestamptz not null,
            updated_by text not null,
            foreign key (received_item_id, declaration_snapshot_version)
              references receiving.identity_declaration_snapshot(received_item_id, snapshot_version),
            foreign key (received_item_id, current_observation_version)
              references receiving.identity_observation(received_item_id, version),
            foreign key (received_item_id, current_decision_version)
              references receiving.identity_decision(received_item_id, version)
        );

        create or replace function receiving.reject_identity_fact_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'REC.IDENTITY_FACT_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        begin
          if not exists (select 1 from pg_trigger where tgname = 'trg_identity_declaration_append_only') then
            create trigger trg_identity_declaration_append_only
            before update or delete on receiving.identity_declaration_snapshot
            for each row execute function receiving.reject_identity_fact_mutation();
          end if;
          if not exists (select 1 from pg_trigger where tgname = 'trg_identity_observation_append_only') then
            create trigger trg_identity_observation_append_only
            before update or delete on receiving.identity_observation
            for each row execute function receiving.reject_identity_fact_mutation();
          end if;
          if not exists (select 1 from pg_trigger where tgname = 'trg_identity_decision_append_only') then
            create trigger trg_identity_decision_append_only
            before update or delete on receiving.identity_decision
            for each row execute function receiving.reject_identity_fact_mutation();
          end if;
        end
        $$;

        create index if not exists ix_identity_observation_item
          on receiving.identity_observation (received_item_id, version desc);
        create index if not exists ix_identity_decision_item
          on receiving.identity_decision (received_item_id, version desc);
        create index if not exists ix_identity_assessment_state
          on receiving.identity_assessment (assessment_state);

        insert into receiving.migration_history (version, applied_at)
        values ('20260725_003_identity_assessment', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
