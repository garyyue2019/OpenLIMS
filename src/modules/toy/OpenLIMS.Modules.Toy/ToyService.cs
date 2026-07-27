using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class ToyProductService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IToyAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ToyStore store,
    ToyAttemptAuditWriter attemptAuditWriter,
    ILogger<ToyProductService> logger) : IToyProductService
{
    public Task<ToyProductOverview> RecordDeclarationAsync(
        string productId,
        RecordAgeDeclarationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        MutateAsync("RecordToyAgeDeclaration", productId, request?.ExpectedCurrentVersion,
            request?.ObjectScope, correlationId, cancellationToken,
            async (product, productKey, organizationGroupId, actorId, transactionToken) =>
            {
                var validated = ToyDomain.ValidateDeclaration(request);
                await store.InsertDeclarationAsync(
                    productKey, product.Version, validated, organizationGroupId, actorId,
                    clock.UtcNow, correlationId, transactionToken);
                ToyTelemetry.RecordDeclaration();
            });

    public Task<ToyProductOverview> RecordDecisionAsync(
        string productId,
        RecordAgeGradeDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        MutateAsync("RecordToyAgeGradeDecision", productId, request?.ExpectedCurrentVersion,
            request?.ObjectScope, correlationId, cancellationToken,
            async (product, productKey, organizationGroupId, actorId, transactionToken) =>
            {
                var validated = ToyDomain.ValidateDecision(request);
                await store.InsertDecisionAsync(
                    productKey, product.Decisions.Count + 1, validated, organizationGroupId, actorId,
                    clock.UtcNow, correlationId, transactionToken);
                ToyTelemetry.RecordDecision();
            });

    public Task<ToyProductOverview> FreezeDecisionAsync(
        string productId,
        int versionNumber,
        FreezeAgeGradeDecisionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        MutateAsync("FreezeToyAgeGradeDecision", productId, request?.ExpectedCurrentVersion,
            null, correlationId, cancellationToken,
            async (product, productKey, organizationGroupId, actorId, transactionToken) =>
            {
                if (request is null ||
                    !string.Equals(request.RuleSetVersion, ToyContract.RuleSetVersion, StringComparison.Ordinal))
                {
                    throw new ToyDomainException(ToyErrorCodes.ValidationFailed);
                }

                ToyDomain.RequireFreezable(product.Decisions
                    .FirstOrDefault(decision => decision.VersionNumber == versionNumber));
                await store.InsertFreezeAsync(
                    productKey, versionNumber, organizationGroupId, actorId,
                    clock.UtcNow, correlationId, transactionToken);
                ToyTelemetry.RecordFreeze();
            });

    public Task<ToyProductOverview> RecordAssessmentAsync(
        string productId,
        RecordAccessibilityAssessmentRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        MutateAsync("RecordToyAccessibilityAssessment", productId, request?.ExpectedCurrentVersion,
            request?.ObjectScope, correlationId, cancellationToken,
            async (product, productKey, organizationGroupId, actorId, transactionToken) =>
            {
                var validated = ToyDomain.ValidateAssessment(request);
                var versionNumber = product.Assessments.Count + 1;
                ToyDomain.RequireInitialFirst(validated.Stage, versionNumber);

                var previous = product.Assessments.LastOrDefault();
                var newlyExposed = ToyDomain.NewlyExposedParts(validated.AccessibleParts, previous);
                var assessmentId = await store.InsertAssessmentAsync(
                    productKey, versionNumber, validated, organizationGroupId, actorId,
                    clock.UtcNow, correlationId, transactionToken);
                ToyTelemetry.RecordAssessment(validated.Stage);

                if (newlyExposed.Count == 0)
                    return;
                // OPS-TOY-003: one exposure, three scopes. A part that becomes
                // reachable can pull in mechanical, chemical and labeling
                // requirements independently, so each gets its own open item.
                await store.InsertTriggersAsync(
                    productKey, assessmentId, versionNumber, newlyExposed, organizationGroupId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
                foreach (var scope in ToyReassessmentScopes.All)
                    ToyTelemetry.RecordTriggerRaised(scope);
            });

    public Task<ToyProductOverview> ResolveTriggerAsync(
        string productId,
        string triggerId,
        ResolveReassessmentTriggerRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        MutateAsync("ResolveToyReassessment", productId, request?.ExpectedCurrentVersion,
            null, correlationId, cancellationToken,
            async (product, productKey, organizationGroupId, actorId, transactionToken) =>
            {
                var validated = ToyDomain.ValidateResolution(request);
                var trigger = product.Triggers
                    .FirstOrDefault(entry => string.Equals(entry.TriggerId, Normalize(triggerId), StringComparison.Ordinal));
                ToyDomain.RequirePending(trigger);
                await store.InsertResolutionAsync(
                    productKey, ParseId(triggerId), validated, organizationGroupId, actorId,
                    clock.UtcNow, correlationId, transactionToken);
                ToyTelemetry.RecordTriggerResolved();
            });

    public async Task<ToyProductOverview> GetOverviewAsync(
        string productId, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(productId, correlationId, cancellationToken);
        try
        {
            var productKey = ParseId(productId);
            ToyProductOverview? overview = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                overview = await store.LoadProductAsync(organizationGroupId, productKey, transactionToken)
                    ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, overview.ObjectScope, transactionToken);
                await store.WriteReadAuditAsync(
                    overview.ProductId, overview.Version, organizationGroupId, actorId,
                    "READ_TOY_OVERVIEW", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return overview ?? throw new InvalidOperationException("TOY.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            throw await FailAsync("GetToyOverview", actorId, organizationGroupId,
                productId, correlationId, exception, cancellationToken);
        }
    }

    /// <summary>
    /// Every write goes through here: lock the product, bootstrap it if this is
    /// the command that first names it, authorize against its stored scope,
    /// check the caller saw the version it thinks it saw, then append.
    /// </summary>
    private async Task<ToyProductOverview> MutateAsync(
        string commandType,
        string productId,
        long? expectedCurrentVersion,
        ToyObjectContext? bootstrapScope,
        string correlationId,
        CancellationToken cancellationToken,
        Func<ToyProductOverview, Guid, string, string, CancellationToken, Task> mutate)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(productId, correlationId, cancellationToken);
        try
        {
            var productKey = ParseId(productId);
            ToyProductOverview? overview = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireProductLockAsync(productKey, transactionToken);
                var product = await store.LoadProductAsync(organizationGroupId, productKey, transactionToken);
                // The first command observes an absent aggregate at version 0.
                // Product registration is appended by that same command and is
                // reflected in the returned version, but must not make an
                // expected version of 0 stale before the requested fact is
                // written.
                var currentVersion = product?.Version ?? 0L;
                if (product is null)
                {
                    if (bootstrapScope is null ||
                        string.IsNullOrWhiteSpace(bootstrapScope.LegalEntityId) ||
                        string.IsNullOrWhiteSpace(bootstrapScope.LaboratoryId))
                    {
                        throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                    }

                    await AuthorizeAsync(organizationGroupId, actorId, bootstrapScope, transactionToken);
                    await store.EnsureProductAsync(
                        productKey, organizationGroupId, bootstrapScope, actorId,
                        clock.UtcNow, correlationId, transactionToken);
                    product = await store.LoadProductAsync(organizationGroupId, productKey, transactionToken)
                        ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
                }
                else
                {
                    await AuthorizeAsync(organizationGroupId, actorId, product.ObjectScope, transactionToken);
                    // A later command may not re-point an existing product at a
                    // different legal entity or laboratory.
                    if (bootstrapScope is not null && bootstrapScope != product.ObjectScope)
                        throw new ToyDomainException(ToyErrorCodes.ValidationFailed);
                }

                if (expectedCurrentVersion is null || expectedCurrentVersion != currentVersion)
                    throw new ToyDomainException(ToyErrorCodes.ExpectedVersionConflict);

                await mutate(product, productKey, organizationGroupId, actorId, transactionToken);
                overview = await store.LoadProductAsync(organizationGroupId, productKey, transactionToken);
            }, cancellationToken);
            return overview ?? throw new InvalidOperationException("TOY.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ToyDomainException or NpgsqlException)
        {
            throw await FailAsync(commandType, actorId, organizationGroupId,
                productId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
        string? target, string correlationId, CancellationToken cancellationToken)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is not null &&
            string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            return (organizationGroupId, actor.ActorId);
        }

        await WriteAttemptOrFailClosedAsync("ToyCommand", actor?.ActorId, organizationGroupId,
            target, correlationId, ToyErrorCodes.NotAuthorized, cancellationToken);
        throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId, string actorId, ToyObjectContext objectScope, CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new ToyAuthorizationRequest(
            organizationGroupId, actorId, objectScope, ToyCapabilities.Manage), cancellationToken);
        if (!decision.Allowed)
            throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
    }

    private async Task<ToyDomainException> FailAsync(
        string commandType, string actorId, string organizationGroupId,
        string? target, string correlationId, Exception exception, CancellationToken cancellationToken)
    {
        var code = exception switch
        {
            ToyDomainException domain => domain.ErrorCode,
            // A caller mistake that lands on a unique or check constraint is a
            // validation failure, not an outage.
            PostgresException { SqlState: "23505" or "23514" } => ToyErrorCodes.ValidationFailed,
            _ => ToyErrorCodes.PersistenceUnavailable
        };
        ToyTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Toy command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType, code, correlationId);
        await WriteAttemptOrFailClosedAsync(commandType, actorId, organizationGroupId,
            target, correlationId, code, cancellationToken);
        return new ToyDomainException(code);
    }

    private async Task WriteAttemptOrFailClosedAsync(
        string commandType, string? actorId, string organizationGroupId,
        string? target, string correlationId, string code, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(commandType, actorId, organizationGroupId,
                ToyDomain.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new ToyDomainException(ToyErrorCodes.PersistenceUnavailable);
        }
    }

    private static string Normalize(string value) => ParseId(value).ToString("N");

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);
}

internal sealed class ToyAgeGradeStatusPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IToyAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ToyStore store,
    ToyAttemptAuditWriter attemptAuditWriter,
    ILogger<ToyAgeGradeStatusPort> logger) : IToyAgeGradeStatusPort
{
    public async ValueTask<ToyAgeGradeStatusResult> EvaluateAsync(
        ToyAgeGradeStatusRequest request, CancellationToken cancellationToken = default)
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
            await WriteDeniedAsync(actor?.ActorId, organizationGroupId, request.ProductId, correlationId, cancellationToken);
            throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.ProductId, "N", out var productId) &&
            !Guid.TryParse(request.ProductId, out productId))
        {
            return Record(ToyDomain.EvaluateStatus(request, null));
        }

        try
        {
            ToyAgeGradeStatusResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var product = await store.LoadProductAsync(organizationGroupId, productId, transactionToken);
                if (product is null)
                {
                    result = ToyDomain.EvaluateStatus(request, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new ToyAuthorizationRequest(
                    organizationGroupId, actor.ActorId, product.ObjectScope, ToyCapabilities.Manage), transactionToken);
                if (!authorization.Allowed)
                    throw new ToyDomainException(ToyErrorCodes.NotAuthorized);

                result = ToyDomain.EvaluateStatus(request, product);
                await store.WriteReadAuditAsync(
                    product.ProductId, product.Version, organizationGroupId, actor.ActorId,
                    "EVALUATE_TOY_AGE_GRADE", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return Record(result ?? ToyDomain.EvaluateStatus(request, null));
        }
        catch (ToyDomainException exception)
            when (string.Equals(exception.ErrorCode, ToyErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor.ActorId, organizationGroupId, request.ProductId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Toy age grade status failed closed because persistence is unavailable");
            return Record(new ToyAgeGradeStatusResult(
                ToyAgeGradeDecisions.Unknown, [ToyAgeGradeReasons.ToyUnavailable],
                request.ProductId, null, null, null,
                ToyAccessibilityStatuses.ReassessmentPending, ToyContract.RuleSetVersion));
        }
    }

    private ToyAgeGradeStatusResult Record(ToyAgeGradeStatusResult result)
    {
        ToyTelemetry.RecordStatus(result.Decision);
        if (!string.Equals(result.Decision, ToyAgeGradeDecisions.Allowed, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Toy age grade status answered {Decision} with reasons {ReasonCodes}",
                result.Decision, string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId, string organizationGroupId, string target, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync("EvaluateToyAgeGrade", actorId, organizationGroupId,
                ToyDomain.HashTarget(target), correlationId, ToyErrorCodes.NotAuthorized,
                clock.UtcNow, cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new ToyDomainException(ToyErrorCodes.PersistenceUnavailable);
        }
    }
}
