using Npgsql;

namespace OpenLIMS.Modules.Billing;

internal static class BillingIntegrationMigrator
{
    public const string Version = "20260805_004_billing_export_handoff";

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
        select pg_advisory_xact_lock(hashtext('openlims.billing.integration.migration'));

        create table if not exists billing.export_batch (
            batch_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            export_schema_version text not null,
            currency_ref text not null,
            currency_version bigint not null check (currency_version > 0),
            total_amount numeric(18, 4) not null,
            content_hash text not null check (content_hash ~ '^[a-f0-9]{64}$'),
            canonical_content text not null,
            request_hash text not null check (request_hash ~ '^[a-f0-9]{64}$'),
            idempotency_key text not null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (organization_group_id, idempotency_key)
        );

        create table if not exists billing.export_item (
            batch_id uuid not null references billing.export_batch(batch_id),
            billing_evidence_id uuid not null references billing.billing_evidence(billing_evidence_id),
            result_group_id text not null,
            group_version bigint not null check (group_version > 0),
            base_amount numeric(18, 4) not null,
            adjustment_amount numeric(18, 4) not null,
            net_amount numeric(18, 4) not null,
            currency_ref text not null,
            currency_version bigint not null check (currency_version > 0),
            primary key (batch_id, billing_evidence_id)
        );

        create table if not exists billing.handoff (
            handoff_id uuid primary key,
            organization_group_id text not null,
            batch_id uuid not null references billing.export_batch(batch_id),
            external_system text not null check (external_system in ('ERP', 'INVOICE')),
            mode text not null check (mode in ('AUTOMATED', 'MANUAL')),
            endpoint_ref text not null,
            endpoint_version bigint not null check (endpoint_version > 0),
            idempotency_key text not null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (organization_group_id, idempotency_key)
        );

        create table if not exists billing.handoff_attempt (
            attempt_id uuid primary key,
            handoff_id uuid not null references billing.handoff(handoff_id),
            attempt_number int not null check (attempt_number > 0),
            idempotency_key text not null,
            outcome text not null check (outcome in ('SUCCEEDED', 'FAILED', 'UNKNOWN', 'DIFFERENT')),
            external_reference text null,
            detail_code text null,
            voucher_number text null,
            company_code text null,
            fiscal_year int null,
            fiscal_period int null,
            posting_date date null,
            attempted_by text not null,
            attempted_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            check ((outcome = 'SUCCEEDED' and external_reference is not null)
                or outcome <> 'SUCCEEDED'),
            check ((voucher_number is null and company_code is null and fiscal_year is null
                    and fiscal_period is null and posting_date is null)
                or (voucher_number is not null and company_code is not null and fiscal_year is not null
                    and fiscal_period is not null and posting_date is not null)),
            unique (handoff_id, attempt_number),
            unique (handoff_id, idempotency_key)
        );

        create index if not exists ix_billing_handoff_batch on billing.handoff (batch_id);
        create index if not exists ix_billing_handoff_attempt_handoff on billing.handoff_attempt (handoff_id, attempt_number);

        do $$
        declare t text;
        begin
          foreach t in array array['export_batch', 'export_item', 'handoff', 'handoff_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on billing.%I for each row execute function billing.reject_billing_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        insert into billing.migration_history (version, applied_at)
        values ('20260805_004_billing_export_handoff', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
