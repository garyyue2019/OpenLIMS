using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class ToyLabelReviewService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IToyAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ToyStore productStore,
    ToyLabelReviewStore labelStore,
    ToyAttemptAuditWriter attemptAuditWriter,
    IObjectStoragePort objectStorage,
    IToyLabelReviewStatusPort statusPort,
    ILogger<ToyLabelReviewService> logger) : IToyLabelReviewService
{
    public async Task<ToyLabelArtifactResult> CreateArtifactAsync(
        string productId,
        CreateToyLabelArtifactRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "CreateToyLabelArtifact", productId, correlationId, cancellationToken);
        try
        {
            var validated = ToyLabelReviewDomain.ValidateArtifact(request);
            await VerifyEvidenceAsync(validated.ImageEvidenceRefs, cancellationToken);
            var productKey = ParseId(productId);
            var artifactId = Guid.NewGuid();
            ToyLabelArtifactResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await labelStore.AcquireProductLockAsync(productKey, transactionToken);
                var product = await productStore.LoadProductAsync(
                    organizationGroupId, productKey, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, product.ObjectScope,
                    ToyCapabilities.LabelManage, transactionToken);
                if (request.ObjectScope != product.ObjectScope)
                    throw new ToyDomainException(ToyErrorCodes.LabelArtifactInvalid);
                await labelStore.InsertArtifactAsync(
                    productKey,
                    artifactId,
                    1,
                    validated.ArtifactType,
                    validated.Language,
                    validated.Market,
                    new ValidatedToyLabelArtifactVersion(
                        validated.ContentHash, validated.ImageEvidenceRefs),
                    organizationGroupId,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
                result = await labelStore.LoadArtifactAsync(
                    organizationGroupId, productKey, artifactId, transactionToken);
            }, cancellationToken);
            ToyTelemetry.RecordLabelArtifact(validated.ArtifactType);
            return result ?? throw new InvalidOperationException("TOY.LABEL_ARTIFACT_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "CreateToyLabelArtifact", actorId, organizationGroupId,
                productId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ToyLabelArtifactResult> AppendArtifactVersionAsync(
        string productId,
        string artifactId,
        AppendToyLabelArtifactVersionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "AppendToyLabelArtifactVersion", artifactId, correlationId, cancellationToken);
        try
        {
            var validated = ToyLabelReviewDomain.ValidateArtifactVersion(request);
            await VerifyEvidenceAsync(validated.ImageEvidenceRefs, cancellationToken);
            var productKey = ParseId(productId);
            var artifactKey = ParseId(artifactId);
            ToyLabelArtifactResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await labelStore.AcquireArtifactLockAsync(artifactKey, transactionToken);
                var artifact = await labelStore.LoadArtifactAsync(
                    organizationGroupId, productKey, artifactKey, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, artifact.ObjectScope,
                    ToyCapabilities.LabelManage, transactionToken);
                if (request.ExpectedCurrentVersion != artifact.CurrentVersion)
                    throw new ToyDomainException(ToyErrorCodes.ExpectedVersionConflict);
                await labelStore.InsertArtifactAsync(
                    productKey,
                    artifactKey,
                    artifact.CurrentVersion + 1,
                    artifact.ArtifactType,
                    artifact.Language,
                    artifact.Market,
                    validated,
                    organizationGroupId,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
                result = await labelStore.LoadArtifactAsync(
                    organizationGroupId, productKey, artifactKey, transactionToken);
            }, cancellationToken);
            ToyTelemetry.RecordLabelArtifact(result?.ArtifactType ?? "UNKNOWN");
            return result ?? throw new InvalidOperationException("TOY.LABEL_ARTIFACT_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "AppendToyLabelArtifactVersion", actorId, organizationGroupId,
                artifactId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ToyLabelReviewResult> CreateReviewAsync(
        string productId,
        string artifactId,
        CreateToyLabelReviewRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "CreateToyLabelReview", artifactId, correlationId, cancellationToken);
        try
        {
            var productKey = ParseId(productId);
            var artifactKey = ParseId(artifactId);
            ToyLabelReviewResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await labelStore.AcquireProductLockAsync(productKey, transactionToken);
                var product = await productStore.LoadProductAsync(
                    organizationGroupId, productKey, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                var artifact = await labelStore.LoadArtifactAsync(
                    organizationGroupId, productKey, artifactKey, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, artifact.ObjectScope,
                    ToyCapabilities.LabelManage, transactionToken);
                var existing = await labelStore.LoadReviewByArtifactAsync(
                    organizationGroupId, productKey, artifactKey, transactionToken);
                var currentVersion = existing?.CurrentVersion ?? 0;
                var previous = existing?.Versions.LastOrDefault();
                ToyLabelReviewDomain.ValidateReview(
                    request, artifact, product, currentVersion, previous);
                await labelStore.InsertReviewAsync(
                    productKey,
                    artifactKey,
                    currentVersion + 1,
                    request,
                    organizationGroupId,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
                result = await labelStore.LoadReviewByArtifactAsync(
                    organizationGroupId, productKey, artifactKey, transactionToken);
            }, cancellationToken);
            ToyTelemetry.RecordLabelReview(ToyLabelReviewStates.Draft);
            return result ?? throw new InvalidOperationException("TOY.LABEL_REVIEW_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "CreateToyLabelReview", actorId, organizationGroupId,
                artifactId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ToyLabelReviewResult> DecideReviewAsync(
        string productId,
        string reviewId,
        DecideToyLabelReviewRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "DecideToyLabelReview", reviewId, correlationId, cancellationToken);
        try
        {
            var productKey = ParseId(productId);
            var reviewKey = ParseId(reviewId);
            ToyLabelReviewResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await labelStore.AcquireProductLockAsync(productKey, transactionToken);
                var review = await labelStore.LoadReviewAsync(
                    organizationGroupId, productKey, reviewKey, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, review.ObjectScope,
                    ToyCapabilities.LabelReview, transactionToken);
                ToyLabelReviewDomain.ValidateDecision(request, review.Versions[^1]);
                await labelStore.InsertDecisionAsync(
                    review,
                    request,
                    organizationGroupId,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
                result = await labelStore.LoadReviewAsync(
                    organizationGroupId, productKey, reviewKey, transactionToken);
            }, cancellationToken);
            ToyTelemetry.RecordLabelReview(request.Decision);
            return result ?? throw new InvalidOperationException("TOY.LABEL_REVIEW_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "DecideToyLabelReview", actorId, organizationGroupId,
                reviewId, correlationId, exception, cancellationToken);
        }
    }

    public Task<ToyLabelReviewStatusResult> GetStatusAsync(
        string productId,
        ToyLabelReviewStatusQuery query,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var request = new ToyLabelReviewStatusRequest(
            organizationGroupId,
            productId,
            query.ProductVersion,
            query.AgeGradeDecisionVersion,
            query.Market,
            query.Language,
            query.ArtifactType,
            query.RuleSetVersion)
        {
            CorrelationId = correlationId
        };
        return statusPort.EvaluateAsync(request, cancellationToken).AsTask();
    }

    private async Task VerifyEvidenceAsync(
        IReadOnlyList<ToyLabelImageEvidenceInput> evidence,
        CancellationToken cancellationToken)
    {
        foreach (var image in evidence)
        {
            try
            {
                await using var content = await objectStorage.OpenReadAsync(
                    new ObjectReference(image.ObjectRef.Bucket, image.ObjectRef.ObjectKey),
                    cancellationToken);
                var actual = Convert.ToHexStringLower(
                    await SHA256.HashDataAsync(content, cancellationToken));
                if (!string.Equals(actual, image.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
            }
            catch (ToyDomainException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
            }
        }
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
        string commandType,
        string? target,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is not null &&
            string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            return (organizationGroupId, actor.ActorId);
        }

        await WriteAttemptOrFailClosedAsync(
            commandType, actor?.ActorId, organizationGroupId,
            target, correlationId, ToyErrorCodes.NotAuthorized, cancellationToken);
        throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId,
        string actorId,
        ToyObjectContext objectScope,
        string capability,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new ToyAuthorizationRequest(
            organizationGroupId, actorId, objectScope, capability), cancellationToken);
        if (!decision.Allowed)
            throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
    }

    private async Task<ToyDomainException> FailAsync(
        string commandType,
        string actorId,
        string organizationGroupId,
        string? target,
        string correlationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var code = exception switch
        {
            ToyDomainException domain => domain.ErrorCode,
            PostgresException { SqlState: "23505" } postgres
                when postgres.ConstraintName?.Contains(
                    "label_review_decision_review_row_id", StringComparison.Ordinal) == true =>
                ToyErrorCodes.ExpectedVersionConflict,
            PostgresException { SqlState: "23505" } postgres
                when postgres.ConstraintName?.Contains(
                    "label_artifact_product_id_artifact_type", StringComparison.Ordinal) == true =>
                ToyErrorCodes.ExpectedVersionConflict,
            PostgresException { SqlState: "23505" } => ToyErrorCodes.ExpectedVersionConflict,
            PostgresException { SqlState: "23514" } when commandType.Contains("Artifact", StringComparison.Ordinal) =>
                ToyErrorCodes.LabelArtifactInvalid,
            PostgresException { SqlState: "23514" } => ToyErrorCodes.LabelReviewInvalid,
            _ => ToyErrorCodes.PersistenceUnavailable
        };
        ToyTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Toy LabelReview command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType, code, correlationId);
        await WriteAttemptOrFailClosedAsync(
            commandType, actorId, organizationGroupId,
            target, correlationId, code, cancellationToken);
        return new ToyDomainException(code);
    }

    private async Task WriteAttemptOrFailClosedAsync(
        string commandType,
        string? actorId,
        string organizationGroupId,
        string? target,
        string correlationId,
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                commandType,
                actorId,
                organizationGroupId,
                ToyDomain.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId,
                code,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new ToyDomainException(ToyErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
}

internal sealed class ToyLabelReviewImpactPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IToyAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ToyStore productStore,
    ToyLabelReviewStore labelStore,
    ToyAttemptAuditWriter attemptAuditWriter,
    ILogger<ToyLabelReviewImpactPort> logger) : IToyLabelReviewImpactPort
{
    public async ValueTask<ToyLabelReviewImpactResult> EvaluateAsync(
        ToyLabelReviewImpactRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;
        if (actor is null ||
            !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal) ||
            !string.Equals(request.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            await WriteAttemptAsync(
                actor?.ActorId, organizationGroupId, request.ProductId,
                correlationId, ToyErrorCodes.NotAuthorized, cancellationToken);
            throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
        }

        try
        {
            var productId = ParseId(request.ProductId);
            var evaluations = new List<ToyLabelImpactEvaluationEntry>();
            var unknown = false;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await labelStore.AcquireProductLockAsync(productId, transactionToken);
                var product = await productStore.LoadProductAsync(
                    organizationGroupId, productId, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                var authorization = await authorizationPort.AuthorizeAsync(new ToyAuthorizationRequest(
                    organizationGroupId, actor.ActorId, product.ObjectScope,
                    ToyCapabilities.LabelManage), transactionToken);
                if (!authorization.Allowed)
                    throw new ToyDomainException(ToyErrorCodes.NotAuthorized);

                var reviews = await labelStore.LoadProductReviewChainsAsync(
                    organizationGroupId, productId, transactionToken);
                foreach (var chain in reviews)
                {
                    var version = chain.Versions[^1];
                    if (!string.Equals(version.State, ToyLabelReviewStates.Approved, StringComparison.Ordinal) ||
                        version.Invalidation is not null)
                    {
                        continue;
                    }

                    if (await labelStore.ImpactExistsAsync(
                            Guid.Parse(chain.ReviewId), version.ReviewVersion, request, transactionToken))
                    {
                        var existing = version.ImpactEvaluations.LastOrDefault(item =>
                            string.Equals(item.ChangeType, request.ChangeType, StringComparison.Ordinal) &&
                            item.ChangeRef == request.ChangeRef);
                        if (existing is not null)
                        {
                            evaluations.Add(existing);
                            unknown |= string.Equals(
                                existing.Result, ToyLabelImpactResults.Unknown, StringComparison.Ordinal);
                        }
                        continue;
                    }

                    var assessment = ToyLabelReviewDomain.EvaluateImpact(version, request, product);
                    await labelStore.InsertImpactAsync(
                        chain,
                        version,
                        request,
                        assessment,
                        organizationGroupId,
                        actor.ActorId,
                        clock.UtcNow,
                        correlationId,
                        transactionToken);
                    evaluations.Add(new ToyLabelImpactEvaluationEntry(
                        request.ChangeType,
                        request.ChangeRef,
                        request.ResultingProductVersion,
                        request.ResultingAgeGradeDecisionVersion,
                        request.ChangeScopeRefs ?? [],
                        assessment.MatchedScopeRefs,
                        request.ImpactRuleRef,
                        assessment.Result,
                        assessment.Reason,
                        clock.UtcNow));
                    unknown |= string.Equals(
                        assessment.Result, ToyLabelImpactResults.Unknown, StringComparison.Ordinal);
                }
            }, cancellationToken);

            foreach (var evaluation in evaluations)
                ToyTelemetry.RecordLabelImpact(evaluation.Result);
            if (unknown)
                throw new ToyDomainException(ToyErrorCodes.LabelImpactUnknown);
            return new ToyLabelReviewImpactResult(
                request.ProductId,
                request.ChangeType,
                request.ChangeRef,
                evaluations,
                ToyLabelReviewContract.RuleSetVersion);
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            var code = exception switch
            {
                ToyDomainException domain => domain.ErrorCode,
                PostgresException { SqlState: "23505" } => ToyErrorCodes.ExpectedVersionConflict,
                _ => ToyErrorCodes.PersistenceUnavailable
            };
            logger.LogWarning(
                "Toy LabelReview impact failed closed with {ErrorCode}; correlation {CorrelationId}",
                code, correlationId);
            await WriteAttemptAsync(
                actor.ActorId, organizationGroupId, request.ProductId,
                correlationId, code, cancellationToken);
            throw new ToyDomainException(code);
        }
    }

    private async Task WriteAttemptAsync(
        string? actorId,
        string organizationGroupId,
        string target,
        string correlationId,
        string code,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                "EvaluateToyLabelReviewImpact",
                actorId,
                organizationGroupId,
                ToyDomain.HashTarget(target),
                correlationId,
                code,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            throw new ToyDomainException(ToyErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
}

internal sealed class ToyLabelReviewStatusPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IToyAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ToyStore productStore,
    ToyLabelReviewStore labelStore,
    ToyAttemptAuditWriter attemptAuditWriter,
    ILogger<ToyLabelReviewStatusPort> logger) : IToyLabelReviewStatusPort
{
    public async ValueTask<ToyLabelReviewStatusResult> EvaluateAsync(
        ToyLabelReviewStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;
        if (actor is null ||
            !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal) ||
            !string.Equals(request.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(
                actor?.ActorId, organizationGroupId, request.ProductId, correlationId, cancellationToken);
            throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.ProductId, "N", out var productId) &&
            !Guid.TryParse(request.ProductId, out productId))
        {
            return Record(ToyLabelReviewDomain.EvaluateStatus(request, null, null));
        }

        try
        {
            ToyLabelReviewStatusResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var product = await productStore.LoadProductAsync(
                    organizationGroupId, productId, transactionToken);
                if (product is null)
                {
                    result = ToyLabelReviewDomain.EvaluateStatus(request, null, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new ToyAuthorizationRequest(
                    organizationGroupId, actor.ActorId, product.ObjectScope,
                    ToyCapabilities.LabelManage), transactionToken);
                if (!authorization.Allowed)
                    throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
                var artifact = await labelStore.LoadArtifactByVariantAsync(
                    organizationGroupId,
                    productId,
                    request.ArtifactType,
                    request.Language,
                    request.Market,
                    transactionToken);
                ToyLabelReviewResult? chain = null;
                if (artifact is not null)
                {
                    chain = await labelStore.LoadReviewByArtifactAsync(
                        organizationGroupId,
                        productId,
                        Guid.Parse(artifact.ArtifactId),
                        transactionToken);
                }
                result = ToyLabelReviewDomain.EvaluateStatus(
                    request, artifact, chain?.Versions.LastOrDefault(), chain?.ReviewId);
                await labelStore.WriteReadAuditAsync(
                    request.ProductId,
                    chain?.CurrentVersion ?? artifact?.CurrentVersion ?? product.Version,
                    organizationGroupId,
                    actor.ActorId,
                    "EVALUATE_TOY_LABEL_REVIEW",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return Record(result ?? ToyLabelReviewDomain.EvaluateStatus(request, null, null));
        }
        catch (ToyDomainException exception)
            when (string.Equals(exception.ErrorCode, ToyErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(
                actor.ActorId, organizationGroupId, request.ProductId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Toy LabelReview status failed closed because persistence is unavailable");
            return Record(new ToyLabelReviewStatusResult(
                ToyLabelReviewStatusDecisions.Unknown,
                [ToyLabelReviewStatusReasons.ToyUnavailable],
                request.ProductId,
                null,
                null,
                null,
                null,
                null,
                null,
                ToyLabelReviewContract.RuleSetVersion));
        }
    }

    private static ToyLabelReviewStatusResult Record(ToyLabelReviewStatusResult result)
    {
        ToyTelemetry.RecordLabelStatus(result.Decision);
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId,
        string organizationGroupId,
        string target,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                "EvaluateToyLabelReview",
                actorId,
                organizationGroupId,
                ToyDomain.HashTarget(target),
                correlationId,
                ToyErrorCodes.NotAuthorized,
                clock.UtcNow,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new ToyDomainException(ToyErrorCodes.PersistenceUnavailable);
        }
    }
}
