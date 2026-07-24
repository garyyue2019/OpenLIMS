using Npgsql;

namespace OpenLIMS.Modules.Receiving;

internal static class ReceivingLabelIdentityMigrator
{
    public const string Version = "20260724_002_label_identity";

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

        do $$
        begin
          if exists (
            select 1
            from receiving.receipt
            where length(upper(laboratory_id)) not between 2 and 16
               or upper(laboratory_id) !~ '^[A-Z0-9][A-Z0-9-]*$'
          ) then
            raise exception 'REC.LABORATORY_CODE_BACKFILL_INVALID';
          end if;
        end
        $$;

        create table if not exists receiving.label_sequence (
            organization_group_id text not null,
            object_type text not null check (object_type in ('CT', 'RI')),
            sequence_date date not null,
            current_value bigint not null check (current_value > 0),
            primary key (organization_group_id, object_type, sequence_date)
        );

        create table if not exists receiving.label_identity (
            object_type text not null check (object_type in ('CT', 'RI')),
            object_id uuid not null,
            object_version bigint not null check (object_version > 0),
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            laboratory_code text not null check (laboratory_code ~ '^[A-Z0-9][A-Z0-9-]{1,15}$'),
            customer_id text not null,
            service_order_id text not null,
            business_number text not null,
            opaque_reference uuid not null,
            format_version text not null check (format_version = 'OL1'),
            sequence_date date not null,
            sequence_value bigint not null check (sequence_value > 0),
            object_state text not null,
            created_at timestamptz not null,
            created_by text not null,
            primary key (object_type, object_id),
            unique (organization_group_id, business_number),
            unique (organization_group_id, opaque_reference),
            unique (organization_group_id, object_type, sequence_date, sequence_value)
        );

        with source_objects as (
          select
            'CT'::text as object_type,
            c.id as object_id,
            1::bigint as object_version,
            r.organization_group_id,
            r.legal_entity_id,
            r.laboratory_id,
            upper(r.laboratory_id) as laboratory_code,
            r.customer_id,
            r.service_order_id,
            'REGISTERED'::text as object_state,
            c.created_at,
            c.created_by
          from receiving.container c
          join receiving.receipt r on r.id = c.receipt_id
          union all
          select
            'RI'::text,
            i.id,
            i.version,
            r.organization_group_id,
            r.legal_entity_id,
            r.laboratory_id,
            upper(r.laboratory_id),
            r.customer_id,
            r.service_order_id,
            i.state,
            i.created_at,
            i.created_by
          from receiving.received_item i
          join receiving.container c on c.id = i.container_id
          join receiving.receipt r on r.id = c.receipt_id
        ), numbered as (
          select *,
            created_at::date as sequence_date,
            row_number() over (
              partition by organization_group_id, object_type, created_at::date
              order by created_at, object_id
            )::bigint as sequence_value
          from source_objects
        )
        insert into receiving.label_identity (
          object_type, object_id, object_version, organization_group_id, legal_entity_id,
          laboratory_id, laboratory_code, customer_id, service_order_id, business_number,
          opaque_reference, format_version, sequence_date, sequence_value, object_state,
          created_at, created_by
        )
        select
          object_type, object_id, object_version, organization_group_id, legal_entity_id,
          laboratory_id, laboratory_code, customer_id, service_order_id,
          laboratory_code || '-' || object_type || '-' || to_char(sequence_date, 'YYYYMMDD') || '-' || lpad(sequence_value::text, 6, '0'),
          gen_random_uuid(), 'OL1', sequence_date, sequence_value, object_state,
          created_at, created_by
        from numbered
        on conflict (object_type, object_id) do nothing;

        insert into receiving.label_sequence (
          organization_group_id, object_type, sequence_date, current_value
        )
        select organization_group_id, object_type, sequence_date, max(sequence_value)
        from receiving.label_identity
        group by organization_group_id, object_type, sequence_date
        on conflict (organization_group_id, object_type, sequence_date)
        do update set current_value = greatest(receiving.label_sequence.current_value, excluded.current_value);

        create index if not exists ix_label_identity_object
          on receiving.label_identity (organization_group_id, object_type, object_id);
        create index if not exists ix_label_identity_opaque
          on receiving.label_identity (organization_group_id, object_type, opaque_reference);

        insert into receiving.migration_history (version, applied_at)
        values ('20260724_002_label_identity', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
