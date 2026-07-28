using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Result;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class ToyConclusionService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IToyAuthorizationPort authorizationPort,
    IResultConclusionEvidencePort resultEvidencePort,
    ITransactionCoordinator transactionCoordinator,
    ToyConclusionStore store,
    ToyAttemptAuditWriter attemptAuditWriter,
    ILogger<ToyConclusionService> logger) : IToyConclusionService
{
    public async Task<ToyConclusionResult> CreateItemConformityConclusionAsync(
        CreateItemConformityConclusionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "CreateItemConformityConclusion",
            request?.AdoptedResultRef,
            correlationId,
            cancellationToken);
        try
        {
            var draft = ToyConclusionDomain.ValidateItemConformityRequest(request);
            var evidence = await ResolveEvidenceAsync(
                organizationGroupId,
                draft.AdoptedResultRef,
                draft.AdoptedResultVersion,
                correlationId,
                cancellationToken);
            ToyConclusionDomain.ValidateSeparationOfDuty(actorId, [evidence.RecordedBy]);
            await AuthorizeAsync(
                organizationGroupId,
                actorId,
                evidence.ObjectScope,
                ToyCapabilities.ConclusionApproveItem,
                cancellationToken);

            ToyConclusionResult? result = null;
            var created = false;
            var timestamp = clock.UtcNow;
            await transactionCoordinator.ExecuteAsync(async transactionCancellationToken =>
            {
                await store.AcquireCorrelationLockAsync(
                    organizationGroupId, correlationId, transactionCancellationToken);
                var existing = await store.LoadByCorrelationAsync(
                    organizationGroupId, correlationId, transactionCancellationToken);
                if (existing is not null)
                {
                    EnsureIdempotentMatch(
                        existing,
                        ToyConclusionLevels.ItemConformity,
                        draft.ContentHash,
                        evidence.ObjectScope);
                    result = existing.Result;
                    return;
                }

                var conclusionId = Guid.NewGuid();
                await store.InsertItemConformityConclusionAsync(
                    conclusionId,
                    draft,
                    evidence,
                    organizationGroupId,
                    actorId,
                    timestamp,
                    correlationId,
                    transactionCancellationToken);
                result = (await store.LoadConclusionAsync(
                    organizationGroupId, conclusionId, transactionCancellationToken))?.Result;
                created = true;
            }, cancellationToken);

            if (created)
                ToyTelemetry.RecordConclusion(ToyConclusionLevels.ItemConformity);
            return result ?? throw new InvalidOperationException("TOY.CONCLUSION_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException or InvalidOperationException)
        {
            throw await FailAsync(
                "CreateItemConformityConclusion",
                actorId,
                organizationGroupId,
                request?.AdoptedResultRef,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<ToyConclusionResult> CreateTestedScopeConformityConclusionAsync(
        CreateTestedScopeConformityConclusionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "CreateTestedScopeConformityConclusion",
            request?.ProductRef,
            correlationId,
            cancellationToken);
        try
        {
            var draft = ToyConclusionDomain.ValidateTestedScopeConformityRequest(request);
            var evidenceCache = new Dictionary<(string GroupId, long AdoptionVersion), ToyResolvedResultEvidence>();
            var evidenceByTestUnit = new Dictionary<string, ToyResolvedResultEvidence>(StringComparer.Ordinal);
            foreach (var testUnit in draft.TestUnits)
            {
                var key = (testUnit.AdoptedResultRef, testUnit.AdoptedResultVersion);
                if (!evidenceCache.TryGetValue(key, out var evidence))
                {
                    evidence = await ResolveEvidenceAsync(
                        organizationGroupId,
                        testUnit.AdoptedResultRef,
                        testUnit.AdoptedResultVersion,
                        correlationId,
                        cancellationToken);
                    evidenceCache.Add(key, evidence);
                }
                if (!evidenceByTestUnit.TryAdd(testUnit.TestUnitId, evidence))
                    throw new ToyDomainException(ToyErrorCodes.ConclusionEvidenceIncomplete);
            }

            var objectScope = RequireSingleObjectScope(evidenceCache.Values);
            ToyConclusionDomain.ValidateSeparationOfDuty(
                actorId,
                evidenceCache.Values.Select(evidence => evidence.RecordedBy).ToArray());
            await AuthorizeAsync(
                organizationGroupId,
                actorId,
                objectScope,
                ToyCapabilities.ConclusionApproveScope,
                cancellationToken);

            ToyConclusionResult? result = null;
            var created = false;
            var timestamp = clock.UtcNow;
            await transactionCoordinator.ExecuteAsync(async transactionCancellationToken =>
            {
                await store.AcquireCorrelationLockAsync(
                    organizationGroupId, correlationId, transactionCancellationToken);
                var existing = await store.LoadByCorrelationAsync(
                    organizationGroupId, correlationId, transactionCancellationToken);
                if (existing is not null)
                {
                    EnsureIdempotentMatch(
                        existing,
                        ToyConclusionLevels.TestedScopeConformity,
                        draft.ContentHash,
                        objectScope);
                    result = existing.Result;
                    return;
                }

                var conclusionId = Guid.NewGuid();
                await store.InsertTestedScopeConformityConclusionAsync(
                    conclusionId,
                    draft,
                    evidenceByTestUnit,
                    objectScope,
                    organizationGroupId,
                    actorId,
                    timestamp,
                    correlationId,
                    transactionCancellationToken);
                result = (await store.LoadConclusionAsync(
                    organizationGroupId, conclusionId, transactionCancellationToken))?.Result;
                created = true;
            }, cancellationToken);

            if (created)
                ToyTelemetry.RecordConclusion(ToyConclusionLevels.TestedScopeConformity);
            return result ?? throw new InvalidOperationException("TOY.CONCLUSION_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException or InvalidOperationException)
        {
            throw await FailAsync(
                "CreateTestedScopeConformityConclusion",
                actorId,
                organizationGroupId,
                request?.ProductRef,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<ToyConclusionResult> GetConclusionAsync(
        string conclusionId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "GetToyConclusion", conclusionId, correlationId, cancellationToken);
        try
        {
            var conclusionKey = ParseId(conclusionId);
            StoredToyConclusion? conclusion = null;
            await transactionCoordinator.ExecuteAsync(async transactionCancellationToken =>
            {
                conclusion = await store.LoadConclusionAsync(
                    organizationGroupId, conclusionKey, transactionCancellationToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                var objectScope = conclusion.ObjectScope
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId,
                    actorId,
                    objectScope,
                    CapabilityFor(conclusion.Result.ConclusionLevel),
                    transactionCancellationToken);
                await store.WriteReadAuditAsync(
                    conclusion,
                    organizationGroupId,
                    actorId,
                    "READ_TOY_CONCLUSION",
                    correlationId,
                    clock.UtcNow,
                    transactionCancellationToken);
            }, cancellationToken);
            return conclusion?.Result
                ?? throw new InvalidOperationException("TOY.CONCLUSION_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException or InvalidOperationException)
        {
            throw await FailAsync(
                "GetToyConclusion",
                actorId,
                organizationGroupId,
                conclusionId,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ToyConclusionResult>> GetConclusionsByProductAsync(
        string productRef,
        long productVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            "GetToyConclusionsByProduct", productRef, correlationId, cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(productRef) || productVersion < 1)
                throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
            IReadOnlyList<StoredToyConclusion>? conclusions = null;
            await transactionCoordinator.ExecuteAsync(async transactionCancellationToken =>
            {
                conclusions = await store.LoadConclusionsByProductAsync(
                    productRef,
                    productVersion,
                    organizationGroupId,
                    transactionCancellationToken);
                foreach (var conclusion in conclusions)
                {
                    var objectScope = conclusion.ObjectScope
                        ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                    await AuthorizeAsync(
                        organizationGroupId,
                        actorId,
                        objectScope,
                        CapabilityFor(conclusion.Result.ConclusionLevel),
                        transactionCancellationToken);
                    await store.WriteReadAuditAsync(
                        conclusion,
                        organizationGroupId,
                        actorId,
                        "READ_TOY_CONCLUSION_HISTORY",
                        correlationId,
                        clock.UtcNow,
                        transactionCancellationToken);
                }
            }, cancellationToken);
            return conclusions?.Select(conclusion => conclusion.Result).ToArray()
                ?? throw new InvalidOperationException("TOY.CONCLUSION_RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException or InvalidOperationException)
        {
            throw await FailAsync(
                "GetToyConclusionsByProduct",
                actorId,
                organizationGroupId,
                productRef,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    private async Task<ToyResolvedResultEvidence> ResolveEvidenceAsync(
        string organizationGroupId,
        string resultGroupId,
        long adoptionVersion,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ResultConclusionEvidenceResult result;
        try
        {
            result = await resultEvidencePort.EvaluateAsync(
                new ResultConclusionEvidenceRequest(
                    organizationGroupId,
                    resultGroupId,
                    adoptionVersion,
                    ResultContract.RuleSetVersion)
                {
                    CorrelationId = correlationId
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "Toy conclusion Result evidence failed closed; correlation {CorrelationId}",
                correlationId);
            throw new ToyDomainException(ToyErrorCodes.ConclusionEvidenceUnknown);
        }

        var scope = result.ObjectScope;
        if (!string.Equals(
                result.Decision,
                ResultConclusionEvidenceDecisions.Allowed,
                StringComparison.Ordinal) ||
            !string.Equals(result.ResultGroupId, resultGroupId, StringComparison.Ordinal) ||
            result.AdoptionVersion != adoptionVersion ||
            result.CurrentGroupVersion is null or < 1 ||
            string.IsNullOrWhiteSpace(result.TargetId) ||
            string.IsNullOrWhiteSpace(result.TargetKind) ||
            string.IsNullOrWhiteSpace(result.RecordedBy) ||
            scope is null ||
            string.IsNullOrWhiteSpace(scope.LegalEntityId) ||
            string.IsNullOrWhiteSpace(scope.LaboratoryId) ||
            !string.Equals(result.RuleSetVersion, ResultContract.RuleSetVersion, StringComparison.Ordinal))
        {
            throw new ToyDomainException(ToyErrorCodes.ConclusionEvidenceUnknown);
        }

        return new ToyResolvedResultEvidence(
            resultGroupId,
            adoptionVersion,
            result.CurrentGroupVersion.Value,
            result.TargetId,
            result.TargetKind,
            result.RecordedBy,
            new ToyObjectContext(scope.LegalEntityId, scope.LaboratoryId));
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
            commandType,
            actor?.ActorId,
            organizationGroupId,
            target,
            correlationId,
            ToyErrorCodes.NotAuthorized,
            cancellationToken);
        throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId,
        string actorId,
        ToyObjectContext objectScope,
        string capability,
        CancellationToken cancellationToken)
    {
        try
        {
            var decision = await authorizationPort.AuthorizeAsync(
                new ToyAuthorizationRequest(
                    organizationGroupId,
                    actorId,
                    objectScope,
                    capability),
                cancellationToken);
            if (!decision.Allowed)
                throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
        }
        catch (ToyDomainException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
        }
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
            PostgresException { SqlState: "23505" } => ToyErrorCodes.ExpectedVersionConflict,
            PostgresException { SqlState: "23514" } => ToyErrorCodes.ValidationFailed,
            _ => ToyErrorCodes.PersistenceUnavailable
        };
        ToyTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Toy conclusion command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType,
            code,
            correlationId);
        await WriteAttemptOrFailClosedAsync(
            commandType,
            actorId,
            organizationGroupId,
            target,
            correlationId,
            code,
            cancellationToken);
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
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            throw new ToyDomainException(ToyErrorCodes.PersistenceUnavailable);
        }
    }

    private static ToyObjectContext RequireSingleObjectScope(
        IEnumerable<ToyResolvedResultEvidence> evidence)
    {
        ToyObjectContext? scope = null;
        foreach (var item in evidence)
        {
            scope ??= item.ObjectScope;
            if (scope != item.ObjectScope)
                throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
        }
        return scope ?? throw new ToyDomainException(ToyErrorCodes.ConclusionEvidenceUnknown);
    }

    private static void EnsureIdempotentMatch(
        StoredToyConclusion existing,
        string expectedLevel,
        string expectedContentHash,
        ToyObjectContext expectedObjectScope)
    {
        if (!string.Equals(existing.Result.ConclusionLevel, expectedLevel, StringComparison.Ordinal) ||
            !string.Equals(existing.Result.ContentHash, expectedContentHash, StringComparison.Ordinal) ||
            existing.ObjectScope != expectedObjectScope)
        {
            throw new ToyDomainException(ToyErrorCodes.ExpectedVersionConflict);
        }
    }

    private static string CapabilityFor(string conclusionLevel) => conclusionLevel switch
    {
        ToyConclusionLevels.ItemConformity => ToyCapabilities.ConclusionApproveItem,
        ToyConclusionLevels.TestedScopeConformity => ToyCapabilities.ConclusionApproveScope,
        _ => throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible)
    };

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
}
