using Npgsql;

namespace OpenLIMS.Modules.Toy;

internal static class ToyLabelReviewMigrator
{
    public const string Version = "20260728_003_toy_label_review";

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
        select pg_advisory_xact_lock(hashtext('openlims.toy.migration'));

        create table if not exists toy.label_artifact (
            artifact_row_id uuid primary key,
            artifact_id uuid not null,
            product_id uuid not null references toy.product(product_id),
            version_number bigint not null check (version_number > 0),
            artifact_type text not null check (artifact_type in (
                'PACKAGING', 'LABEL', 'INSTRUCTION', 'MARKETING_AGE_CLAIM')),
            language text not null check (length(trim(language)) > 0),
            market text not null check (length(trim(market)) > 0),
            content_hash text not null check (content_hash ~ '^[0-9a-f]{64}$'),
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (artifact_id, version_number),
            unique (product_id, artifact_type, language, market, version_number)
        );

        create table if not exists toy.label_artifact_image (
            image_row_id uuid primary key,
            artifact_row_id uuid not null references toy.label_artifact(artifact_row_id),
            bucket text not null check (length(trim(bucket)) > 0),
            object_key text not null check (length(trim(object_key)) > 0),
            content_hash text not null check (content_hash ~ '^[0-9a-f]{64}$'),
            unique (artifact_row_id, bucket, object_key)
        );

        create table if not exists toy.label_review (
            review_row_id uuid primary key,
            review_id uuid not null,
            review_version bigint not null check (review_version > 0),
            product_id uuid not null references toy.product(product_id),
            artifact_id uuid not null,
            artifact_version bigint not null check (artifact_version > 0),
            product_version bigint not null check (product_version > 0),
            age_grade_decision_version bigint not null check (age_grade_decision_version > 0),
            market text not null check (length(trim(market)) > 0),
            language text not null check (length(trim(language)) > 0),
            impact_rule_ref text not null check (length(trim(impact_rule_ref)) > 0),
            impact_rule_version bigint not null check (impact_rule_version > 0),
            rule_set_version text not null,
            previous_review_version bigint null,
            trigger_change_type text null check (
                trigger_change_type is null or
                trigger_change_type in ('PRODUCT_VERSION', 'AGE_GRADE_DECISION')),
            trigger_change_ref text null,
            trigger_change_version bigint null,
            created_by text not null,
            created_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (review_id, review_version),
            unique (artifact_id, review_version),
            foreign key (artifact_id, artifact_version)
                references toy.label_artifact(artifact_id, version_number),
            foreign key (review_id, previous_review_version)
                references toy.label_review(review_id, review_version),
            check ((previous_review_version is null) = (trigger_change_type is null)),
            check ((trigger_change_type is null) = (trigger_change_ref is null)),
            check ((trigger_change_ref is null) = (trigger_change_version is null)),
            check (trigger_change_version is null or trigger_change_version > 0)
        );

        create table if not exists toy.label_review_scope (
            scope_row_id uuid primary key,
            review_row_id uuid not null references toy.label_review(review_row_id),
            reference_id text not null check (length(trim(reference_id)) > 0),
            reference_version bigint not null check (reference_version > 0),
            unique (review_row_id, reference_id, reference_version)
        );

        create table if not exists toy.label_review_decision (
            decision_id uuid primary key,
            review_row_id uuid not null unique references toy.label_review(review_row_id),
            decision text not null check (decision in ('APPROVED', 'REJECTED')),
            reviewed_by text not null,
            reviewed_at timestamptz not null,
            decision_reason text not null check (length(trim(decision_reason)) > 0),
            event_id text not null unique,
            correlation_id text not null
        );

        create table if not exists toy.label_review_impact_evaluation (
            evaluation_id uuid primary key,
            review_row_id uuid not null references toy.label_review(review_row_id),
            change_type text not null check (change_type in ('PRODUCT_VERSION', 'AGE_GRADE_DECISION')),
            change_ref text not null,
            change_version bigint not null check (change_version > 0),
            resulting_product_version bigint not null,
            resulting_age_grade_decision_version bigint not null,
            change_scope_refs text[] not null,
            matched_scope_refs text[] not null,
            impact_rule_ref text null,
            impact_rule_version bigint null check (
                impact_rule_version is null or impact_rule_version > 0),
            result text not null check (result in ('IMPACTED', 'NOT_IMPACTED', 'UNKNOWN')),
            reason text not null,
            evaluated_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null,
            unique (review_row_id, change_type, change_ref, change_version)
        );

        create table if not exists toy.label_review_invalidation (
            invalidation_id uuid primary key,
            evaluation_id uuid not null unique
                references toy.label_review_impact_evaluation(evaluation_id),
            review_row_id uuid not null unique references toy.label_review(review_row_id),
            change_type text not null check (change_type in ('PRODUCT_VERSION', 'AGE_GRADE_DECISION')),
            change_ref text not null,
            change_version bigint not null check (change_version > 0),
            matched_scope_refs text[] not null check (cardinality(matched_scope_refs) > 0),
            impact_rule_ref text not null,
            impact_rule_version bigint not null check (impact_rule_version > 0),
            reason text not null,
            invalidated_at timestamptz not null,
            event_id text not null unique,
            correlation_id text not null
        );

        do $$
        declare t text;
        begin
          foreach t in array array[
            'label_artifact', 'label_artifact_image', 'label_review',
            'label_review_scope', 'label_review_decision',
            'label_review_impact_evaluation', 'label_review_invalidation'
          ] loop
            if not exists (select 1 from pg_trigger where tgname = 'trg_' || t || '_append_only') then
              execute format(
                'create trigger trg_%I_append_only before update or delete on toy.%I for each row execute function toy.reject_toy_mutation()',
                t, t);
            end if;
          end loop;
        end
        $$;

        create index if not exists ix_toy_label_artifact_variant
          on toy.label_artifact (product_id, artifact_type, language, market, version_number desc);
        create index if not exists ix_toy_label_review_product
          on toy.label_review (product_id, artifact_id, review_version desc);
        create index if not exists ix_toy_label_impact_review
          on toy.label_review_impact_evaluation (review_row_id, evaluated_at);

        insert into toy.migration_history (version, applied_at)
        values ('20260728_003_toy_label_review', statement_timestamp())
        on conflict (version) do nothing;
        """;
}
