using Npgsql;

namespace OpenLIMS.Modules.Receiving;

internal static class ReceivingMigrator
{
    public const string Version = "20260724_001_receipt_registration";

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

        create schema if not exists receiving;

        create table if not exists receiving.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists receiving.receipt (
            id uuid primary key,
            organization_group_id text not null,
            receipt_number text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            arrival_at timestamptz not null,
            aggregate_version bigint not null check (aggregate_version > 0),
            created_at timestamptz not null,
            created_by text not null,
            updated_at timestamptz not null,
            updated_by text not null,
            unique (organization_group_id, receipt_number)
        );

        create table if not exists receiving.container (
            id uuid primary key,
            receipt_id uuid not null references receiving.receipt(id),
            container_number text not null,
            ordinal integer not null check (ordinal > 0),
            external_label text null,
            package_type text not null,
            condition text not null,
            seal_observation text null,
            created_at timestamptz not null,
            created_by text not null,
            updated_at timestamptz not null,
            updated_by text not null,
            unique (receipt_id, ordinal),
            unique (container_number)
        );

        create table if not exists receiving.received_item (
            id uuid primary key,
            container_id uuid not null references receiving.container(id),
            received_item_number text not null unique,
            ordinal integer not null check (ordinal > 0),
            declared_description text not null,
            model text not null,
            batch text not null,
            serial_number text null,
            color text not null,
            package_condition text not null,
            seal_condition text not null,
            item_condition text not null,
            quantity numeric(18,6) not null check (quantity = 1),
            unit text not null,
            state text not null check (state in ('REGISTERED', 'QUARANTINED')),
            version bigint not null check (version > 0),
            created_at timestamptz not null,
            created_by text not null,
            updated_at timestamptz not null,
            updated_by text not null,
            unique (container_id, ordinal)
        );

        create table if not exists receiving.received_item_state_history (
            id uuid primary key,
            received_item_id uuid not null references receiving.received_item(id),
            sequence integer not null check (sequence > 0),
            from_state text null,
            to_state text not null,
            occurred_at timestamptz not null,
            actor_id text not null,
            unique (received_item_id, sequence)
        );

        create table if not exists receiving.idempotency (
            organization_group_id text not null,
            key_hash char(64) not null,
            request_hash char(64) not null,
            actor_id text not null,
            receipt_id uuid null references receiving.receipt(id),
            response_json jsonb null,
            created_at timestamptz not null,
            primary key (organization_group_id, key_hash),
            check ((receipt_id is null and response_json is null) or (receipt_id is not null and response_json is not null))
        );

        create table if not exists receiving.audit_pending (
            id uuid primary key,
            event_type text not null,
            actor_id text not null,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            object_type text not null,
            object_id uuid not null,
            correlation_id text not null,
            idempotency_key_hash char(64) not null,
            occurred_at timestamptz not null,
            payload_json jsonb not null,
            dispatched_at timestamptz null
        );

        create table if not exists receiving.audit_attempt (
            attempt_id uuid primary key,
            actor_id text null,
            organization_group_id text not null,
            command_type text not null,
            target_hash char(64) not null,
            decision_code text not null,
            correlation_id text not null,
            occurred_at timestamptz not null,
            original_attempt_id uuid null
        );

        create table if not exists receiving.outbox (
            id uuid primary key,
            event_type text not null,
            aggregate_type text not null,
            aggregate_id uuid not null,
            occurred_at timestamptz not null,
            payload_json jsonb not null,
            attempt_count integer not null default 0 check (attempt_count >= 0),
            next_attempt_at timestamptz null,
            dispatched_at timestamptz null
        );

        create index if not exists ix_receipt_service_order on receiving.receipt (organization_group_id, service_order_id);
        create index if not exists ix_received_item_state on receiving.received_item (state);
        create index if not exists ix_outbox_pending on receiving.outbox (occurred_at) where dispatched_at is null;
        create index if not exists ix_audit_pending_dispatch on receiving.audit_pending (occurred_at) where dispatched_at is null;

        insert into receiving.migration_history (version, applied_at)
        values ('20260724_001_receipt_registration', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
