using Npgsql;

namespace OpenLIMS.Modules.Quantity;

internal static class QuantityMigrator
{
    public const string Version = "20260726_001_quantity_ledger";

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
        select pg_advisory_xact_lock(hashtext('openlims.quantity.migration'));

        create schema if not exists quantity;

        create table if not exists quantity.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists quantity.quantity_account (
            quantity_account_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            subject_type text not null check (subject_type in ('RECEIVED_ITEM', 'DERIVED_SAMPLE', 'TEST_SPECIMEN')),
            subject_ref text not null,
            subject_version bigint not null check (subject_version > 0),
            dimension text not null check (dimension in ('COUNT', 'MASS', 'LENGTH', 'AREA', 'VOLUME')),
            unit text not null,
            precision_scale int not null check (precision_scale between 0 and 6),
            conservation_tolerance numeric(18, 6) not null check (conservation_tolerance >= 0),
            rule_set_version text not null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            check (dimension <> 'COUNT' or precision_scale = 0),
            unique (organization_group_id, subject_type, subject_ref, subject_version, dimension, unit)
        );

        create table if not exists quantity.quantity_entry (
            entry_id uuid primary key,
            quantity_account_id uuid not null references quantity.quantity_account(quantity_account_id),
            account_version bigint not null check (account_version > 1),
            entry_type text not null check (entry_type in (
                'RECEIPT', 'OUTPUT', 'RESERVE', 'RESERVE_RELEASE', 'ALLOCATE', 'CONSUME',
                'RETURN', 'LOSS', 'DISPOSE', 'REVERSAL', 'RESTATE')),
            amount numeric(18, 6) not null check (amount > 0),
            resulting_balance numeric(18, 6) not null check (resulting_balance >= 0),
            resulting_reserved numeric(18, 6) not null check (resulting_reserved >= 0),
            referenced_entry_id uuid null references quantity.quantity_entry(entry_id),
            reservation_id uuid null references quantity.quantity_entry(entry_id),
            reason text null,
            posted_by text not null,
            posted_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (quantity_account_id, account_version),
            check (resulting_balance >= resulting_reserved)
        );

        create table if not exists quantity.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function quantity.reject_posted_quantity_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'QTY.POSTED_QUANTITY_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        begin
          if not exists (select 1 from pg_trigger where tgname = 'trg_quantity_account_append_only') then
            create trigger trg_quantity_account_append_only
            before update or delete on quantity.quantity_account
            for each row execute function quantity.reject_posted_quantity_mutation();
          end if;
          if not exists (select 1 from pg_trigger where tgname = 'trg_quantity_entry_append_only') then
            create trigger trg_quantity_entry_append_only
            before update or delete on quantity.quantity_entry
            for each row execute function quantity.reject_posted_quantity_mutation();
          end if;
          if not exists (select 1 from pg_trigger where tgname = 'trg_quantity_audit_attempt_append_only') then
            create trigger trg_quantity_audit_attempt_append_only
            before update or delete on quantity.audit_attempt
            for each row execute function quantity.reject_posted_quantity_mutation();
          end if;
        end
        $$;

        create index if not exists ix_quantity_entry_current
          on quantity.quantity_entry (quantity_account_id, account_version desc);

        create index if not exists ix_quantity_entry_reservation
          on quantity.quantity_entry (reservation_id) where reservation_id is not null;

        create index if not exists ix_quantity_entry_referenced
          on quantity.quantity_entry (referenced_entry_id) where referenced_entry_id is not null;

        insert into quantity.migration_history (version, applied_at)
        values ('20260726_001_quantity_ledger', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
