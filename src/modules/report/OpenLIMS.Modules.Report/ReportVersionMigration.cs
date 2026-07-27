using Npgsql;

namespace OpenLIMS.Modules.Report;

/// <summary>
/// DEV-023: adds the immutable version chain on top of the DEV-022 assembly
/// tables. Purely additive — no existing table is altered.
/// </summary>
internal static class ReportVersionMigrator
{
    public const string Version = "20260727_002_report_version_chain";

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
        select pg_advisory_xact_lock(hashtext('openlims.report.version.migration'));

        create table if not exists report.version_snapshot (
            snapshot_id uuid primary key,
            report_id uuid not null references report.report(report_id),
            version_number int not null check (version_number > 0),
            content_hash text not null check (content_hash ~ '^[a-f0-9]{64}$'),
            canonical_content text not null,
            line_count int not null check (line_count > 0),
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (report_id, version_number)
        );

        create table if not exists report.version_signature (
            signature_id uuid primary key,
            report_id uuid not null references report.report(report_id),
            version_number int not null check (version_number > 0),
            content_hash text not null check (content_hash ~ '^[a-f0-9]{64}$'),
            reauthentication_ref text not null,
            reauthentication_version bigint not null check (reauthentication_version > 0),
            signing_intent text not null,
            signatory_id text not null,
            signed_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (report_id, version_number)
        );

        create table if not exists report.controlled_action (
            action_id uuid primary key,
            report_id uuid not null references report.report(report_id),
            version_number int not null check (version_number > 0),
            kind text not null check (kind in
                ('CORRECTION', 'SUPPLEMENT', 'WITHDRAWAL', 'VOID', 'SUPERSESSION')),
            impact_assessment_ref text null,
            impact_assessment_version bigint null check (impact_assessment_version is null or impact_assessment_version > 0),
            superseding_report_number text null,
            reason text not null,
            performed_by text not null,
            performed_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            check (
              (kind in ('CORRECTION', 'SUPPLEMENT') and impact_assessment_ref is not null
                and impact_assessment_version is not null and superseding_report_number is null)
              or (kind = 'SUPERSESSION' and superseding_report_number is not null
                and impact_assessment_ref is null and impact_assessment_version is null)
              or (kind in ('WITHDRAWAL', 'VOID') and impact_assessment_ref is null
                and impact_assessment_version is null and superseding_report_number is null)
            )
        );

        -- One withdrawal per version, and one void per whole chain.
        create unique index if not exists ux_controlled_action_withdrawal
          on report.controlled_action (report_id, version_number) where kind = 'WITHDRAWAL';
        create unique index if not exists ux_controlled_action_void
          on report.controlled_action (report_id) where kind = 'VOID';
        create unique index if not exists ux_controlled_action_new_version
          on report.controlled_action (report_id, version_number) where kind in ('CORRECTION', 'SUPPLEMENT');
        -- One supersession per chain: the verification page carries a single
        -- superseding report number, so a second one could not be represented.
        create unique index if not exists ux_controlled_action_supersession
          on report.controlled_action (report_id) where kind = 'SUPERSESSION';

        create or replace function report.reject_version_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'RPT.REPORT_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['version_snapshot', 'version_signature', 'controlled_action'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on report.%I for each row execute function report.reject_version_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        insert into report.migration_history (version, applied_at)
        values ('20260727_002_report_version_chain', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
