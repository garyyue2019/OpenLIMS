using Npgsql;

namespace OpenLIMS.Modules.Billing;

internal static class BillingMigrator
{
    public const string Version = "20260726_001_billing_evidence";

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
        select pg_advisory_xact_lock(hashtext('openlims.billing.migration'));

        create schema if not exists billing;

        create table if not exists billing.migration_history (
            version text primary key,
            applied_at timestamptz not null
        );

        create table if not exists billing.billing_evidence (
            billing_evidence_id uuid primary key,
            organization_group_id text not null,
            legal_entity_id text not null,
            laboratory_id text not null,
            customer_id text not null,
            service_order_id text not null,
            product_category text not null,
            result_group_id text not null,
            group_version bigint not null check (group_version > 0),
            adoption_target_id text not null,
            contract_baseline_ref text not null,
            contract_baseline_version bigint not null check (contract_baseline_version > 0),
            charge_dimension text not null,
            billing_rule_version text not null,
            amount numeric(18, 4) not null check (amount >= 0),
            currency_ref text not null,
            currency_version bigint not null check (currency_version > 0),
            zero_amount_reason text null,
            stage text not null check (stage in ('SERVICE_COMPLETED', 'BILLABLE_CANDIDATE')),
            rule_set_version text not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            check ((amount = 0 and zero_amount_reason is not null) or (amount <> 0 and zero_amount_reason is null)),
            unique (organization_group_id, result_group_id, group_version, adoption_target_id,
                    contract_baseline_ref, contract_baseline_version, charge_dimension, billing_rule_version)
        );

        create table if not exists billing.billing_adjustment (
            adjustment_id uuid primary key,
            billing_evidence_id uuid not null references billing.billing_evidence(billing_evidence_id),
            amount numeric(18, 4) not null check (amount <> 0),
            reason text not null,
            recorded_by text not null,
            recorded_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists billing.audit_attempt (
            attempt_id uuid primary key,
            command_type text not null,
            actor_id text null,
            organization_group_id text not null,
            target_hash text not null,
            correlation_id text not null,
            outcome text not null,
            occurred_at timestamptz not null
        );

        create or replace function billing.reject_billing_mutation()
        returns trigger language plpgsql as $$
        begin
          raise exception 'BIL.BILLING_APPEND_ONLY' using errcode = '55000';
        end;
        $$;

        do $$
        declare t text;
        begin
          foreach t in array array['billing_evidence', 'billing_adjustment', 'audit_attempt'] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on billing.%I for each row execute function billing.reject_billing_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        insert into billing.migration_history (version, applied_at)
        values ('20260726_001_billing_evidence', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
