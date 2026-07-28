using System.Text.Json;
using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class ToyLabelReviewStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireProductLockAsync(Guid productId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
        command.Parameters.AddWithValue("key", $"openlims.toy.label-review.{productId:N}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AcquireArtifactLockAsync(Guid artifactId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
        command.Parameters.AddWithValue("key", $"openlims.toy.label-artifact.{artifactId:N}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertArtifactAsync(
        Guid productId,
        Guid artifactId,
        long versionNumber,
        string artifactType,
        string language,
        string market,
        ValidatedToyLabelArtifactVersion version,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var rowId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into toy.label_artifact (
                artifact_row_id, artifact_id, product_id, version_number,
                artifact_type, language, market, content_hash,
                created_by, created_at, event_id, correlation_id
            ) values (
                @artifact_row_id, @artifact_id, @product_id, @version_number,
                @artifact_type, @language, @market, @content_hash,
                @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("artifact_row_id", rowId);
            command.Parameters.AddWithValue("artifact_id", artifactId);
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("version_number", versionNumber);
            command.Parameters.AddWithValue("artifact_type", artifactType);
            command.Parameters.AddWithValue("language", language);
            command.Parameters.AddWithValue("market", market);
            command.Parameters.AddWithValue("content_hash", version.ContentHash);
            command.Parameters.AddWithValue("created_by", actorId);
            command.Parameters.AddWithValue("created_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var image in version.ImageEvidenceRefs)
        {
            await using var command = new NpgsqlCommand("""
                insert into toy.label_artifact_image (
                    image_row_id, artifact_row_id, bucket, object_key, content_hash
                ) values (
                    @image_row_id, @artifact_row_id, @bucket, @object_key, @content_hash
                )
                """, connection, transaction);
            command.Parameters.AddWithValue("image_row_id", Guid.NewGuid());
            command.Parameters.AddWithValue("artifact_row_id", rowId);
            command.Parameters.AddWithValue("bucket", image.ObjectRef.Bucket);
            command.Parameters.AddWithValue("object_key", image.ObjectRef.ObjectKey);
            command.Parameters.AddWithValue("content_hash", image.Hash);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEvidenceAsync(
            productId.ToString("N"),
            organizationGroupId,
            actorId,
            "CREATE_LABEL_ARTIFACT_VERSION",
            eventId,
            "Toy.LabelArtifactVersionCreated.v1",
            (versionNumber - 1).ToString(),
            versionNumber.ToString(),
            correlationId,
            now,
            cancellationToken);
    }

    public async Task<ToyLabelArtifactResult?> LoadArtifactAsync(
        string organizationGroupId,
        Guid productId,
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var rows = new List<ArtifactRow>();
        await using (var command = new NpgsqlCommand("""
            select a.artifact_row_id, a.version_number, a.artifact_type,
                   a.language, a.market, a.content_hash, a.created_by, a.created_at,
                   p.legal_entity_id, p.laboratory_id
            from toy.label_artifact a
            join toy.product p on p.product_id = a.product_id
            where a.product_id = @product_id
              and a.artifact_id = @artifact_id
              and p.organization_group_id = @organization_group_id
            order by a.version_number
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("artifact_id", artifactId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ArtifactRow(
                    reader.GetGuid(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    new ToyObjectContext(reader.GetString(8), reader.GetString(9))));
            }
        }

        if (rows.Count == 0)
            return null;
        var versions = new List<ToyLabelArtifactVersionEntry>(rows.Count);
        foreach (var row in rows)
        {
            versions.Add(new ToyLabelArtifactVersionEntry(
                row.VersionNumber,
                row.ContentHash,
                await LoadImagesAsync(row.RowId, cancellationToken),
                row.CreatedBy,
                row.CreatedAt));
        }

        var first = rows[0];
        return new ToyLabelArtifactResult(
            artifactId.ToString("N"),
            productId.ToString("N"),
            first.ArtifactType,
            first.Language,
            first.Market,
            first.ObjectScope,
            versions);
    }

    public async Task<ToyLabelArtifactResult?> LoadArtifactByVariantAsync(
        string organizationGroupId,
        Guid productId,
        string artifactType,
        string language,
        string market,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select a.artifact_id
            from toy.label_artifact a
            join toy.product p on p.product_id = a.product_id
            where a.product_id = @product_id
              and a.artifact_type = @artifact_type
              and a.language = @language
              and a.market = @market
              and p.organization_group_id = @organization_group_id
            order by a.version_number desc
            limit 1
            """, connection, transaction);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("artifact_type", artifactType);
        command.Parameters.AddWithValue("language", language);
        command.Parameters.AddWithValue("market", market);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        var artifact = await command.ExecuteScalarAsync(cancellationToken);
        return artifact is Guid id
            ? await LoadArtifactAsync(organizationGroupId, productId, id, cancellationToken)
            : null;
    }

    public async Task InsertReviewAsync(
        Guid productId,
        Guid artifactId,
        long reviewVersion,
        CreateToyLabelReviewRequest request,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var reviewId = await ExistingReviewIdAsync(artifactId, cancellationToken) ?? Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into toy.label_review (
                review_row_id, review_id, review_version, product_id,
                artifact_id, artifact_version, product_version,
                age_grade_decision_version, market, language,
                impact_rule_ref, impact_rule_version, rule_set_version,
                previous_review_version, trigger_change_type,
                trigger_change_ref, trigger_change_version,
                created_by, created_at, event_id, correlation_id
            ) values (
                @review_row_id, @review_id, @review_version, @product_id,
                @artifact_id, @artifact_version, @product_version,
                @age_grade_decision_version, @market, @language,
                @impact_rule_ref, @impact_rule_version, @rule_set_version,
                @previous_review_version, @trigger_change_type,
                @trigger_change_ref, @trigger_change_version,
                @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("review_row_id", rowId);
            command.Parameters.AddWithValue("review_id", reviewId);
            command.Parameters.AddWithValue("review_version", reviewVersion);
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("artifact_id", artifactId);
            command.Parameters.AddWithValue("artifact_version", request.ArtifactVersion);
            command.Parameters.AddWithValue("product_version", request.ProductVersion);
            command.Parameters.AddWithValue("age_grade_decision_version", request.AgeGradeDecisionVersion);
            command.Parameters.AddWithValue("market", request.Market);
            command.Parameters.AddWithValue("language", request.Language);
            command.Parameters.AddWithValue("impact_rule_ref", request.ImpactRuleRef.Id);
            command.Parameters.AddWithValue("impact_rule_version", request.ImpactRuleRef.Version);
            command.Parameters.AddWithValue("rule_set_version", request.RuleSetVersion);
            command.Parameters.AddWithValue(
                "previous_review_version", (object?)request.PreviousReviewVersion ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "trigger_change_type", (object?)request.TriggerChange?.ChangeType ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "trigger_change_ref", (object?)request.TriggerChange?.ChangeRef.Id ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "trigger_change_version", (object?)request.TriggerChange?.ChangeRef.Version ?? DBNull.Value);
            command.Parameters.AddWithValue("created_by", actorId);
            command.Parameters.AddWithValue("created_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var scope in request.ReviewScopeRefs)
        {
            await using var command = new NpgsqlCommand("""
                insert into toy.label_review_scope (
                    scope_row_id, review_row_id, reference_id, reference_version
                ) values (@scope_row_id, @review_row_id, @reference_id, @reference_version)
                """, connection, transaction);
            command.Parameters.AddWithValue("scope_row_id", Guid.NewGuid());
            command.Parameters.AddWithValue("review_row_id", rowId);
            command.Parameters.AddWithValue("reference_id", scope.Id);
            command.Parameters.AddWithValue("reference_version", scope.Version);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteEvidenceAsync(
            productId.ToString("N"), organizationGroupId, actorId,
            "CREATE_LABEL_REVIEW", eventId, "Toy.LabelReviewCreated.v1",
            (reviewVersion - 1).ToString(), reviewVersion.ToString(),
            correlationId, now, cancellationToken);
    }

    public async Task InsertDecisionAsync(
        ToyLabelReviewResult review,
        DecideToyLabelReviewRequest request,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var rowId = await ReviewRowIdAsync(Guid.Parse(review.ReviewId), review.CurrentVersion, cancellationToken);
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into toy.label_review_decision (
                decision_id, review_row_id, decision, reviewed_by,
                reviewed_at, decision_reason, event_id, correlation_id
            ) values (
                @decision_id, @review_row_id, @decision, @reviewed_by,
                @reviewed_at, @decision_reason, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("decision_id", Guid.NewGuid());
        command.Parameters.AddWithValue("review_row_id", rowId);
        command.Parameters.AddWithValue("decision", request.Decision);
        command.Parameters.AddWithValue("reviewed_by", actorId);
        command.Parameters.AddWithValue("reviewed_at", now);
        command.Parameters.AddWithValue("decision_reason", request.DecisionReason.Trim());
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WriteEvidenceAsync(
            review.ProductId, organizationGroupId, actorId,
            "DECIDE_LABEL_REVIEW", eventId, "Toy.LabelReviewDecided.v1",
            ToyLabelReviewStates.Draft, request.Decision,
            correlationId, now, cancellationToken);
    }

    public async Task<IReadOnlyList<ToyLabelReviewResult>> LoadProductReviewChainsAsync(
        string organizationGroupId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var ids = new List<Guid>();
        await using (var command = new NpgsqlCommand("""
            select distinct r.review_id
            from toy.label_review r
            join toy.product p on p.product_id = r.product_id
            where r.product_id = @product_id
              and p.organization_group_id = @organization_group_id
            order by r.review_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                ids.Add(reader.GetGuid(0));
        }

        var reviews = new List<ToyLabelReviewResult>(ids.Count);
        foreach (var id in ids)
        {
            var review = await LoadReviewAsync(organizationGroupId, productId, id, cancellationToken);
            if (review is not null)
                reviews.Add(review);
        }
        return reviews;
    }

    public async Task<ToyLabelReviewResult?> LoadReviewByArtifactAsync(
        string organizationGroupId,
        Guid productId,
        Guid artifactId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select r.review_id
            from toy.label_review r
            join toy.product p on p.product_id = r.product_id
            where r.product_id = @product_id
              and r.artifact_id = @artifact_id
              and p.organization_group_id = @organization_group_id
            order by r.review_version desc
            limit 1
            """, connection, transaction);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("artifact_id", artifactId);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid reviewId
            ? await LoadReviewAsync(organizationGroupId, productId, reviewId, cancellationToken)
            : null;
    }

    public async Task<ToyLabelReviewResult?> LoadReviewAsync(
        string organizationGroupId,
        Guid productId,
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var rows = new List<ReviewRow>();
        await using (var command = new NpgsqlCommand("""
            select r.review_row_id, r.review_version, r.artifact_id,
                   r.artifact_version, r.product_version, r.age_grade_decision_version,
                   r.market, r.language, r.impact_rule_ref, r.impact_rule_version,
                   r.rule_set_version, r.previous_review_version,
                   r.trigger_change_type, r.trigger_change_ref, r.trigger_change_version,
                   r.created_by, r.created_at, a.artifact_type,
                   p.legal_entity_id, p.laboratory_id
            from toy.label_review r
            join toy.product p on p.product_id = r.product_id
            join toy.label_artifact a
              on a.artifact_id = r.artifact_id and a.version_number = 1
            where r.product_id = @product_id
              and r.review_id = @review_id
              and p.organization_group_id = @organization_group_id
            order by r.review_version
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("product_id", productId);
            command.Parameters.AddWithValue("review_id", reviewId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ReviewRow(
                    reader.GetGuid(0),
                    reader.GetInt64(1),
                    reader.GetGuid(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    new ToyVersionedReference(reader.GetString(8), reader.GetInt64(9)),
                    reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetInt64(11),
                    reader.IsDBNull(12)
                        ? null
                        : new ToyLabelReviewChangeReference(
                            reader.GetString(12),
                            new ToyVersionedReference(reader.GetString(13), reader.GetInt64(14))),
                    reader.GetString(15),
                    reader.GetFieldValue<DateTimeOffset>(16),
                    reader.GetString(17),
                    new ToyObjectContext(reader.GetString(18), reader.GetString(19))));
            }
        }

        if (rows.Count == 0)
            return null;
        var versions = new List<ToyLabelReviewVersionEntry>(rows.Count);
        foreach (var row in rows)
        {
            var decision = await LoadDecisionAsync(row.RowId, cancellationToken);
            var evaluations = await LoadEvaluationsAsync(row.RowId, cancellationToken);
            var invalidation = await LoadInvalidationAsync(row.RowId, cancellationToken);
            var state = invalidation is not null
                ? ToyLabelReviewStates.Invalidated
                : decision?.Decision ?? ToyLabelReviewStates.Draft;
            versions.Add(new ToyLabelReviewVersionEntry(
                row.ReviewVersion,
                row.ArtifactVersion,
                row.ProductVersion,
                row.AgeGradeDecisionVersion,
                row.Market,
                row.Language,
                await LoadReviewScopesAsync(row.RowId, cancellationToken),
                row.ImpactRuleRef,
                row.RuleSetVersion,
                row.PreviousReviewVersion,
                row.TriggerChange,
                state,
                decision,
                evaluations,
                invalidation,
                row.CreatedBy,
                row.CreatedAt));
        }

        var first = rows[0];
        return new ToyLabelReviewResult(
            reviewId.ToString("N"),
            productId.ToString("N"),
            first.ArtifactId.ToString("N"),
            first.ArtifactType,
            first.ObjectScope,
            versions);
    }

    public async Task InsertImpactAsync(
        ToyLabelReviewResult review,
        ToyLabelReviewVersionEntry version,
        ToyLabelReviewImpactRequest request,
        ToyLabelImpactAssessment assessment,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var reviewRowId = await ReviewRowIdAsync(
            Guid.Parse(review.ReviewId), version.ReviewVersion, cancellationToken);
        var evaluationId = Guid.NewGuid();
        var evaluationEventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into toy.label_review_impact_evaluation (
                evaluation_id, review_row_id, change_type, change_ref, change_version,
                resulting_product_version, resulting_age_grade_decision_version,
                change_scope_refs, matched_scope_refs,
                impact_rule_ref, impact_rule_version, result, reason,
                evaluated_at, event_id, correlation_id
            ) values (
                @evaluation_id, @review_row_id, @change_type, @change_ref, @change_version,
                @resulting_product_version, @resulting_age_grade_decision_version,
                @change_scope_refs, @matched_scope_refs,
                @impact_rule_ref, @impact_rule_version, @result, @reason,
                @evaluated_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            command.Parameters.AddWithValue("review_row_id", reviewRowId);
            command.Parameters.AddWithValue("change_type", request.ChangeType);
            command.Parameters.AddWithValue("change_ref", request.ChangeRef.Id);
            command.Parameters.AddWithValue("change_version", request.ChangeRef.Version);
            command.Parameters.AddWithValue("resulting_product_version", request.ResultingProductVersion);
            command.Parameters.AddWithValue(
                "resulting_age_grade_decision_version", request.ResultingAgeGradeDecisionVersion);
            command.Parameters.AddWithValue(
                "change_scope_refs", (request.ChangeScopeRefs ?? []).Select(SerializeReference).ToArray());
            command.Parameters.AddWithValue(
                "matched_scope_refs", assessment.MatchedScopeRefs.Select(SerializeReference).ToArray());
            command.Parameters.AddWithValue(
                "impact_rule_ref", (object?)request.ImpactRuleRef?.Id ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "impact_rule_version", (object?)request.ImpactRuleRef?.Version ?? DBNull.Value);
            command.Parameters.AddWithValue("result", assessment.Result);
            command.Parameters.AddWithValue("reason", assessment.Reason);
            command.Parameters.AddWithValue("evaluated_at", now);
            command.Parameters.AddWithValue("event_id", evaluationEventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var evidenceEventId = evaluationEventId;
        var action = "EVALUATE_LABEL_REVIEW_IMPACT";
        var messageType = "Toy.LabelReviewImpactEvaluated.v1";
        var after = assessment.Result;
        if (string.Equals(assessment.Result, ToyLabelImpactResults.Impacted, StringComparison.Ordinal))
        {
            var impactRule = request.ImpactRuleRef
                ?? throw new InvalidOperationException("TOY.LABEL_IMPACT_RULE_MISSING");
            evidenceEventId = Guid.NewGuid().ToString("N");
            await using var command = new NpgsqlCommand("""
                insert into toy.label_review_invalidation (
                    invalidation_id, evaluation_id, review_row_id,
                    change_type, change_ref, change_version, matched_scope_refs,
                    impact_rule_ref, impact_rule_version, reason,
                    invalidated_at, event_id, correlation_id
                ) values (
                    @invalidation_id, @evaluation_id, @review_row_id,
                    @change_type, @change_ref, @change_version, @matched_scope_refs,
                    @impact_rule_ref, @impact_rule_version, @reason,
                    @invalidated_at, @event_id, @correlation_id
                )
                """, connection, transaction);
            command.Parameters.AddWithValue("invalidation_id", Guid.NewGuid());
            command.Parameters.AddWithValue("evaluation_id", evaluationId);
            command.Parameters.AddWithValue("review_row_id", reviewRowId);
            command.Parameters.AddWithValue("change_type", request.ChangeType);
            command.Parameters.AddWithValue("change_ref", request.ChangeRef.Id);
            command.Parameters.AddWithValue("change_version", request.ChangeRef.Version);
            command.Parameters.AddWithValue(
                "matched_scope_refs", assessment.MatchedScopeRefs.Select(SerializeReference).ToArray());
            command.Parameters.AddWithValue("impact_rule_ref", impactRule.Id);
            command.Parameters.AddWithValue("impact_rule_version", impactRule.Version);
            command.Parameters.AddWithValue("reason", assessment.Reason);
            command.Parameters.AddWithValue("invalidated_at", now);
            command.Parameters.AddWithValue("event_id", evidenceEventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            action = "INVALIDATE_LABEL_REVIEW";
            messageType = "Toy.LabelReviewInvalidated.v1";
            after = ToyLabelReviewStates.Invalidated;
        }

        await WriteEvidenceAsync(
            review.ProductId, organizationGroupId, actorId,
            action, evidenceEventId, messageType,
            ToyLabelReviewStates.Approved, after,
            correlationId, now, cancellationToken);
    }

    public async Task<bool> ImpactExistsAsync(
        Guid reviewId,
        long reviewVersion,
        ToyLabelReviewImpactRequest request,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select exists (
                select 1
                from toy.label_review_impact_evaluation e
                join toy.label_review r on r.review_row_id = e.review_row_id
                where r.review_id = @review_id
                  and r.review_version = @review_version
                  and e.change_type = @change_type
                  and e.change_ref = @change_ref
                  and e.change_version = @change_version
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("review_id", reviewId);
        command.Parameters.AddWithValue("review_version", reviewVersion);
        command.Parameters.AddWithValue("change_type", request.ChangeType);
        command.Parameters.AddWithValue("change_ref", request.ChangeRef.Id);
        command.Parameters.AddWithValue("change_version", request.ChangeRef.Version);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public Task WriteReadAuditAsync(
        string productId,
        long version,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            productId,
            action,
            ToyLabelReviewContract.RuleSetVersion,
            version.ToString(),
            version.ToString(),
            correlationId,
            now), cancellationToken);

    private async Task<IReadOnlyList<ToyLabelImageEvidenceEntry>> LoadImagesAsync(
        Guid artifactRowId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var result = new List<ToyLabelImageEvidenceEntry>();
        await using var command = new NpgsqlCommand("""
            select bucket, object_key, content_hash
            from toy.label_artifact_image
            where artifact_row_id = @artifact_row_id
            order by bucket, object_key
            """, connection, transaction);
        command.Parameters.AddWithValue("artifact_row_id", artifactRowId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ToyLabelImageEvidenceEntry(
                new ToyImageObjectReference(reader.GetString(0), reader.GetString(1)),
                reader.GetString(2)));
        }
        return result;
    }

    private async Task<IReadOnlyList<ToyVersionedReference>> LoadReviewScopesAsync(
        Guid reviewRowId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var result = new List<ToyVersionedReference>();
        await using var command = new NpgsqlCommand("""
            select reference_id, reference_version
            from toy.label_review_scope
            where review_row_id = @review_row_id
            order by reference_id, reference_version
            """, connection, transaction);
        command.Parameters.AddWithValue("review_row_id", reviewRowId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ToyVersionedReference(reader.GetString(0), reader.GetInt64(1)));
        return result;
    }

    private async Task<ToyLabelReviewDecisionEntry?> LoadDecisionAsync(
        Guid reviewRowId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select decision, reviewed_by, reviewed_at, decision_reason
            from toy.label_review_decision
            where review_row_id = @review_row_id
            """, connection, transaction);
        command.Parameters.AddWithValue("review_row_id", reviewRowId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ToyLabelReviewDecisionEntry(
                reader.GetString(0), reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3))
            : null;
    }

    private async Task<IReadOnlyList<ToyLabelImpactEvaluationEntry>> LoadEvaluationsAsync(
        Guid reviewRowId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var result = new List<ToyLabelImpactEvaluationEntry>();
        await using var command = new NpgsqlCommand("""
            select change_type, change_ref, change_version,
                   resulting_product_version, resulting_age_grade_decision_version,
                   change_scope_refs, matched_scope_refs,
                   impact_rule_ref, impact_rule_version, result, reason, evaluated_at
            from toy.label_review_impact_evaluation
            where review_row_id = @review_row_id
            order by evaluated_at, evaluation_id
            """, connection, transaction);
        command.Parameters.AddWithValue("review_row_id", reviewRowId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ToyLabelImpactEvaluationEntry(
                reader.GetString(0),
                new ToyVersionedReference(reader.GetString(1), reader.GetInt64(2)),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetFieldValue<string[]>(5).Select(DeserializeReference).ToArray(),
                reader.GetFieldValue<string[]>(6).Select(DeserializeReference).ToArray(),
                reader.IsDBNull(7)
                    ? null
                    : new ToyVersionedReference(reader.GetString(7), reader.GetInt64(8)),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11)));
        }
        return result;
    }

    private async Task<ToyLabelReviewInvalidationEntry?> LoadInvalidationAsync(
        Guid reviewRowId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select change_type, change_ref, change_version, matched_scope_refs,
                   impact_rule_ref, impact_rule_version, reason, invalidated_at
            from toy.label_review_invalidation
            where review_row_id = @review_row_id
            """, connection, transaction);
        command.Parameters.AddWithValue("review_row_id", reviewRowId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ToyLabelReviewInvalidationEntry(
                reader.GetString(0),
                new ToyVersionedReference(reader.GetString(1), reader.GetInt64(2)),
                reader.GetFieldValue<string[]>(3).Select(DeserializeReference).ToArray(),
                new ToyVersionedReference(reader.GetString(4), reader.GetInt64(5)),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7))
            : null;
    }

    private async Task<Guid?> ExistingReviewIdAsync(Guid artifactId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select review_id from toy.label_review
            where artifact_id = @artifact_id
            order by review_version desc limit 1
            """, connection, transaction);
        command.Parameters.AddWithValue("artifact_id", artifactId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : null;
    }

    private async Task<Guid> ReviewRowIdAsync(
        Guid reviewId,
        long reviewVersion,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select review_row_id from toy.label_review
            where review_id = @review_id and review_version = @review_version
            """, connection, transaction);
        command.Parameters.AddWithValue("review_id", reviewId);
        command.Parameters.AddWithValue("review_version", reviewVersion);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id
            ? id
            : throw new ToyDomainException(ToyErrorCodes.LabelReviewInvalid);
    }

    private async Task WriteEvidenceAsync(
        string productId,
        string organizationGroupId,
        string actorId,
        string action,
        string eventId,
        string messageType,
        string? beforeVersion,
        string afterVersion,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId,
            organizationGroupId,
            productId,
            action,
            ToyLabelReviewContract.RuleSetVersion,
            beforeVersion,
            afterVersion,
            correlationId,
            now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("TOY.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }

    private static string SerializeReference(ToyVersionedReference reference) =>
        JsonSerializer.Serialize(reference);

    private static ToyVersionedReference DeserializeReference(string value) =>
        JsonSerializer.Deserialize<ToyVersionedReference>(value)
        ?? throw new InvalidOperationException("TOY.LABEL_REFERENCE_INVALID");

    private sealed record ArtifactRow(
        Guid RowId,
        long VersionNumber,
        string ArtifactType,
        string Language,
        string Market,
        string ContentHash,
        string CreatedBy,
        DateTimeOffset CreatedAt,
        ToyObjectContext ObjectScope);

    private sealed record ReviewRow(
        Guid RowId,
        long ReviewVersion,
        Guid ArtifactId,
        long ArtifactVersion,
        long ProductVersion,
        long AgeGradeDecisionVersion,
        string Market,
        string Language,
        ToyVersionedReference ImpactRuleRef,
        string RuleSetVersion,
        long? PreviousReviewVersion,
        ToyLabelReviewChangeReference? TriggerChange,
        string CreatedBy,
        DateTimeOffset CreatedAt,
        string ArtifactType,
        ToyObjectContext ObjectScope);
}
