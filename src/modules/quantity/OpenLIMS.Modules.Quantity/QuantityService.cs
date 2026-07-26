using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Quantity;

namespace OpenLIMS.Modules.Quantity;

public interface IQuantityAccountService
{
    Task<QuantityAccountResult> CreateAsync(
        CreateQuantityAccountRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<QuantityEntryResult> PostEntryAsync(
        string quantityAccountId,
        PostQuantityEntryRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<QuantityAccountResult> GetAccountAsync(
        string quantityAccountId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

internal sealed class QuantityAccountService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IQuantityAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    QuantityStore store,
    QuantityAttemptAuditWriter attemptAuditWriter,
    ILogger<QuantityAccountService> logger) : IQuantityAccountService
{
    public async Task<QuantityAccountResult> CreateAsync(
        CreateQuantityAccountRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var accountId = Guid.Parse(idGenerator.NewId());
        var (organizationGroupId, actorId) = await RequireActorAsync(
            accountId.ToString("N"), correlationId, cancellationToken);
        try
        {
            var objectScope = QuantityRules.NormalizeObjectScope(request?.ObjectScope);
            var (subject, configuration) = QuantityRules.ValidateAccount(request);
            QuantityAccountResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(organizationGroupId, actorId, objectScope, transactionToken);
                result = await store.InsertAccountAsync(
                    accountId,
                    organizationGroupId,
                    objectScope,
                    subject,
                    configuration,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            }, cancellationToken);
            QuantityTelemetry.RecordPosted("ACCOUNT_CREATED");
            return result ?? throw new InvalidOperationException("QTY.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is QuantityDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "CreateQuantityAccount",
                actorId,
                organizationGroupId,
                accountId.ToString("N"),
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<QuantityEntryResult> PostEntryAsync(
        string quantityAccountId,
        PostQuantityEntryRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            quantityAccountId, correlationId, cancellationToken);
        try
        {
            var accountId = ParseAccountId(quantityAccountId);
            if (request is null || request.ExpectedCurrentVersion < 1)
                throw new QuantityDomainException(QuantityErrorCodes.ExpectedVersionConflict);
            QuantityEntryResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireAccountLockAsync(accountId, transactionToken);
                var account = await store.LoadAccountAsync(organizationGroupId, accountId, transactionToken)
                    ?? throw new QuantityDomainException(QuantityErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, account.ObjectScope, transactionToken);
                var balances = await store.LoadBalancesAsync(accountId, transactionToken);
                if (balances.Version != request.ExpectedCurrentVersion)
                    throw new QuantityDomainException(QuantityErrorCodes.ExpectedVersionConflict);

                var referencedEntry = await LoadOptionalSnapshotAsync(
                    accountId, request.ReferencedEntryId, transactionToken);
                var reservation = await LoadOptionalSnapshotAsync(
                    accountId, request.ReservationId, transactionToken);
                var plan = QuantityRules.PlanPosting(
                    request, account.Configuration, balances, referencedEntry, reservation);
                result = await store.InsertEntryAsync(
                    Guid.Parse(idGenerator.NewId()),
                    accountId,
                    balances.Version + 1,
                    organizationGroupId,
                    plan,
                    actorId,
                    clock.UtcNow,
                    correlationId,
                    transactionToken);
            }, cancellationToken);
            QuantityTelemetry.RecordPosted(result!.EntryType);
            return result;
        }
        catch (Exception exception) when (exception is QuantityDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "PostQuantityEntry",
                actorId,
                organizationGroupId,
                quantityAccountId,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    public async Task<QuantityAccountResult> GetAccountAsync(
        string quantityAccountId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(
            quantityAccountId, correlationId, cancellationToken);
        try
        {
            var accountId = ParseAccountId(quantityAccountId);
            QuantityAccountResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var account = await store.LoadAccountAsync(organizationGroupId, accountId, transactionToken)
                    ?? throw new QuantityDomainException(QuantityErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, account.ObjectScope, transactionToken);
                var balances = await store.LoadBalancesAsync(accountId, transactionToken);
                result = ToResult(account, balances);
                await store.WriteReadAuditAsync(
                    result.QuantityAccountId,
                    balances.Version,
                    organizationGroupId,
                    actorId,
                    "READ_QUANTITY_ACCOUNT",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("QTY.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is QuantityDomainException or NpgsqlException)
        {
            throw await FailAsync(
                "GetQuantityAccount",
                actorId,
                organizationGroupId,
                quantityAccountId,
                correlationId,
                exception,
                cancellationToken);
        }
    }

    private async Task<QuantityEntrySnapshot?> LoadOptionalSnapshotAsync(
        Guid accountId,
        string? entryId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return null;
        var trimmed = entryId.Trim();
        if (!Guid.TryParseExact(trimmed, "N", out var id) && !Guid.TryParse(trimmed, out id))
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        return await store.LoadEntrySnapshotAsync(accountId, id, cancellationToken)
            ?? throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
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
            "QuantityCommand",
            actor?.ActorId,
            organizationGroupId,
            target,
            correlationId,
            QuantityErrorCodes.NotAuthorized,
            cancellationToken);
        throw new QuantityDomainException(QuantityErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId,
        string actorId,
        QuantityObjectContext objectScope,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new QuantityAuthorizationRequest(
            organizationGroupId,
            actorId,
            objectScope,
            QuantityCapabilities.Post), cancellationToken);
        if (!decision.Allowed)
            throw new QuantityDomainException(QuantityErrorCodes.NotAuthorized);
    }

    private async Task<QuantityDomainException> FailAsync(
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
            QuantityDomainException domain => domain.ErrorCode,
            PostgresException { SqlState: "23505" } => QuantityErrorCodes.ValidationFailed,
            _ => QuantityErrorCodes.PersistenceUnavailable
        };
        QuantityTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Quantity command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
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
        return new QuantityDomainException(code);
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
                QuantityRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId,
                code,
                clock.UtcNow,
                cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new QuantityDomainException(QuantityErrorCodes.PersistenceUnavailable);
        }
    }

    internal static QuantityAccountResult ToResult(QuantityAccountRow account, QuantityBalances balances) => new(
        account.QuantityAccountId.ToString("N"),
        balances.Version,
        QuantityContract.RuleSetVersion,
        account.ObjectScope,
        account.Subject,
        account.Configuration.Dimension,
        account.Configuration.Unit,
        account.Configuration.PrecisionScale,
        account.Configuration.ConservationTolerance,
        balances.Balance,
        balances.Reserved,
        balances.Available,
        account.CreatedBy,
        account.CreatedAt);

    private static Guid ParseAccountId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new QuantityDomainException(QuantityErrorCodes.ObjectNotAccessible);
}

internal sealed class QuantityAvailabilityPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IQuantityAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    QuantityStore store,
    QuantityAttemptAuditWriter attemptAuditWriter,
    ILogger<QuantityAvailabilityPort> logger) : IQuantityAvailabilityPort
{
    public async ValueTask<QuantityAvailabilityResult> EvaluateAsync(
        QuantityAvailabilityRequest request,
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
                actor?.ActorId,
                organizationGroupId,
                request.QuantityAccountId,
                correlationId,
                cancellationToken);
            throw new QuantityDomainException(QuantityErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.QuantityAccountId, "N", out var accountId) &&
            !Guid.TryParse(request.QuantityAccountId, out accountId))
        {
            return Record(QuantityRules.EvaluateAvailability(request, null));
        }

        try
        {
            QuantityAvailabilityResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var account = await store.LoadAccountAsync(organizationGroupId, accountId, transactionToken);
                if (account is null)
                {
                    result = QuantityRules.EvaluateAvailability(request, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new QuantityAuthorizationRequest(
                    organizationGroupId,
                    actor.ActorId,
                    account.ObjectScope,
                    QuantityCapabilities.Post), transactionToken);
                if (!authorization.Allowed)
                    throw new QuantityDomainException(QuantityErrorCodes.NotAuthorized);

                var balances = await store.LoadBalancesAsync(accountId, transactionToken);
                result = QuantityRules.EvaluateAvailability(
                    request, QuantityAccountService.ToResult(account, balances));
                await store.WriteReadAuditAsync(
                    account.QuantityAccountId.ToString("N"),
                    balances.Version,
                    organizationGroupId,
                    actor.ActorId,
                    "EVALUATE_QUANTITY_AVAILABILITY",
                    correlationId,
                    clock.UtcNow,
                    transactionToken);
            }, cancellationToken);
            return Record(result ?? QuantityRules.EvaluateAvailability(request, null));
        }
        catch (QuantityDomainException exception)
            when (string.Equals(exception.ErrorCode, QuantityErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(
                actor.ActorId,
                organizationGroupId,
                request.QuantityAccountId,
                correlationId,
                cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Quantity availability failed closed because persistence is unavailable");
            return Record(new QuantityAvailabilityResult(
                QuantityAvailabilityDecisions.Unknown,
                [QuantityAvailabilityReasons.QuantityUnavailable],
                request.QuantityAccountId,
                null,
                null,
                QuantityContract.RuleSetVersion));
        }
    }

    private QuantityAvailabilityResult Record(QuantityAvailabilityResult result)
    {
        QuantityTelemetry.RecordGate(result.Decision);
        if (string.Equals(result.Decision, QuantityAvailabilityDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Quantity availability failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
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
                "EvaluateQuantityAvailability",
                actorId,
                organizationGroupId,
                QuantityRules.HashTarget(target),
                correlationId,
                QuantityErrorCodes.NotAuthorized,
                clock.UtcNow,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new QuantityDomainException(QuantityErrorCodes.PersistenceUnavailable);
        }
    }
}
