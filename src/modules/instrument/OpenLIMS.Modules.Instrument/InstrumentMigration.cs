using Npgsql;

namespace OpenLIMS.Modules.Instrument;

internal static class InstrumentMigrator
{
    public const string Version = "20260727_001_instrument_import";

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
        select pg_advisory_xact_lock(hashtext('openlims.instrument.migration'));

        create schema if not exists instrument;

        create table if not exists instrument.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists instrument.file_registration (
            file_registration_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            external_ref text not null,
            external_version bigint not null check (external_version > 0),
            sha256 text not null check (sha256 ~ '^[a-f0-9]{64}$'),
            source_system text not null check (source_system in ('INSTRUMENT', 'CDS', 'MIDDLEWARE')),
            instrument_ref text not null,
            instrument_version bigint not null check (instrument_version > 0),
            parser_version text not null,
            declared_row_count int not null check (declared_row_count between 1 and 100000),
            rule_set_version text not null,
            registered_by text not null,
            registered_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (organization_group_id, sha256)
        );

        create table if not exists instrument.parsed_row (
            row_id uuid primary key,
            file_registration_id uuid not null references instrument.file_registration(file_registration_id),
            file_version bigint not null check (file_version > 1),
            row_number int not null check (row_number between 1 and 100000),
            sample_number text not null,
            batch_position text not null,
            parameter text not null,
            unit text not null,
            qualifier text null,
            raw_value text not null,
            parsed_value text not null,
            parser_version text not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (file_registration_id, row_number)
        );

        create table if not exists instrument.import_exception (
            exception_id uuid primary key,
            file_registration_id uuid not null references instrument.file_registration(file_registration_id),
            file_version bigint not null check (file_version > 1),
            row_number int not null check (row_number between 1 and 100000),
            reason_code text not null check (reason_code in
                ('UNKNOWN_SAMPLE', 'ILLEGAL_UNIT', 'UNPARSABLE_VALUE', 'DUPLICATE_ROW', 'QUALIFIER_CONFLICT')),
            raw_content text not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (file_registration_id, row_number)
        );

        create table if not exists instrument.exception_resolution (
            resolution_id uuid primary key,
            exception_id uuid not null unique references instrument.import_exception(exception_id),
            file_registration_id uuid not null references instrument.file_registration(file_registration_id),
            file_version bigint not null check (file_version > 1),
            kind text not null check (kind in ('ACCEPT_WITH_MAPPING', 'REJECT_ROW')),
            corrected_sample_number text null,
            corrected_batch_position text null,
            corrected_parameter text null,
            corrected_unit text null,
            corrected_qualifier text null,
            reason text not null,
            resolved_by text not null,
            resolved_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            check (
              (kind = 'ACCEPT_WITH_MAPPING' and corrected_sample_number is not null
                and corrected_batch_position is not null and corrected_parameter is not null
                and corrected_unit is not null)
              or (kind = 'REJECT_ROW' and corrected_sample_number is null
                and corrected_batch_position is null and corrected_parameter is null
                and corrected_unit is null and corrected_qualifier is null)
            )
        );

        create table if not exists instrument.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function instrument.reject_instrument_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'INS.INSTRUMENT_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['file_registration', 'parsed_row', 'import_exception', 'exception_resolution', 'audit_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on instrument.%I for each row execute function instrument.reject_instrument_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        create index if not exists ix_instrument_file_org
          on instrument.file_registration (organization_group_id, file_registration_id);

        insert into instrument.migration_history (version, applied_at)
        values ('20260727_001_instrument_import', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
