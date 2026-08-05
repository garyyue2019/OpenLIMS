using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

public interface IReportDeliveryService
{
    Task<ReportDeliveryResult> CreateDeliveryAsync(string reportId, int versionNumber, CreateReportDeliveryRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportDeliveryDetailResult> GetDeliveryAsync(string deliveryId, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportDownloadGrantResult> CreateDownloadGrantAsync(string deliveryId, CreateReportDownloadGrantRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportDownloadResult> DownloadAsync(string accessToken, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportNotificationResult> QueueNotificationAsync(string deliveryId, QueueReportNotificationRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ReportNotificationAttemptResult> RecordNotificationAttemptAsync(string notificationId, RecordReportNotificationAttemptRequest request, string correlationId, CancellationToken cancellationToken = default);
}

internal sealed class ReportDeliveryService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IReportAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ReportStore reportStore,
    ReportVersionStore versionStore,
    ReportDeliveryStore deliveryStore,
    ReportAttemptAuditWriter attemptAuditWriter,
    ILogger<ReportDeliveryService> logger) : IReportDeliveryService
{
    public async Task<ReportDeliveryResult> CreateDeliveryAsync(
        string reportId,
        int versionNumber,
        CreateReportDeliveryRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(reportId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(reportId);
            if (versionNumber < 1)
                throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
            var validated = ReportDeliveryRules.ValidateDelivery(request);
            ReportDeliveryResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await reportStore.AcquireReportLockAsync(id, transactionToken);
                await deliveryStore.AcquireKeyLockAsync("delivery", $"{organizationGroupId}:{validated.IdempotencyKey}", transactionToken);
                var report = await reportStore.LoadReportAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, report.ObjectScope, ReportCapabilities.Manage, transactionToken);

                var existing = await deliveryStore.LoadDeliveryByIdempotencyAsync(
                    organizationGroupId, validated.IdempotencyKey, transactionToken);
                if (existing is not null)
                {
                    if (!Matches(existing.Delivery, id, versionNumber, validated))
                        throw new ReportDomainException(ReportErrorCodes.IdempotencyConflict);
                    await deliveryStore.WriteReadAuditAsync(
                        existing.Delivery.DeliveryId, organizationGroupId, actorId,
                        "RETRY_REPORT_DELIVERY", correlationId, clock.UtcNow, transactionToken);
                    result = existing.Delivery;
                    return;
                }

                var snapshots = await versionStore.LoadSnapshotsAsync(id, transactionToken);
                var actions = await versionStore.LoadActionsAsync(id, transactionToken);
                if (!string.Equals(
                        ReportDeliveryRules.ResolveVersionState(versionNumber, snapshots, actions),
                        ReportVersionStates.Issued,
                        StringComparison.Ordinal))
                {
                    throw new ReportDomainException(ReportErrorCodes.DeliveryVersionUnavailable);
                }
                var snapshot = snapshots.Single(entry => entry.VersionNumber == versionNumber);
                result = await deliveryStore.InsertDeliveryAsync(
                    Guid.Parse(idGenerator.NewId()), organizationGroupId, id, versionNumber,
                    snapshot.ContentHash, validated, actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("CreateReportDelivery", actorId, organizationGroupId,
                reportId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportDeliveryDetailResult> GetDeliveryAsync(
        string deliveryId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(deliveryId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(deliveryId);
            ReportDeliveryDetailResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var delivery = await deliveryStore.LoadDeliveryAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, delivery.ObjectScope, ReportCapabilities.Manage, transactionToken);
                var notifications = await deliveryStore.LoadNotificationsAsync(delivery, transactionToken);
                await deliveryStore.WriteReadAuditAsync(
                    delivery.Delivery.DeliveryId, organizationGroupId, actorId,
                    "READ_REPORT_DELIVERY", correlationId, clock.UtcNow, transactionToken);
                result = new ReportDeliveryDetailResult(
                    delivery.Delivery, notifications, ReportContract.DeliveryRuleSetVersion);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("GetReportDelivery", actorId, organizationGroupId,
                deliveryId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportDownloadGrantResult> CreateDownloadGrantAsync(
        string deliveryId,
        CreateReportDownloadGrantRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(deliveryId, correlationId, cancellationToken);
        var accessToken = idGenerator.NewId() + idGenerator.NewId();
        try
        {
            var id = ParseId(deliveryId);
            var validated = ReportDeliveryRules.ValidateGrant(request, clock.UtcNow);
            ReportDownloadGrantResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var delivery = await deliveryStore.LoadDeliveryAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, delivery.ObjectScope, ReportCapabilities.Manage, transactionToken);
                if (!string.Equals(delivery.Delivery.RecipientId, validated.RecipientId, StringComparison.Ordinal))
                    throw new ReportDomainException(ReportErrorCodes.NotAuthorized);
                result = await deliveryStore.InsertGrantAsync(
                    Guid.Parse(idGenerator.NewId()), delivery, ReportDeliveryRules.HashToken(accessToken),
                    accessToken, validated, actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("CreateReportDownloadGrant", actorId, organizationGroupId,
                deliveryId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportDownloadResult> DownloadAsync(
        string accessToken,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = ReportDeliveryRules.HashToken(accessToken ?? string.Empty);
        var (organizationGroupId, actorId) = await RequireActorAsync(tokenHash, correlationId, cancellationToken);
        try
        {
            ReportDownloadResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var grant = await deliveryStore.LoadGrantByTokenHashAsync(tokenHash, transactionToken)
                    ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                if (!string.Equals(grant.Delivery.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
                    throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                if (!string.Equals(grant.RecipientId, actorId, StringComparison.Ordinal) ||
                    !string.Equals(grant.Delivery.Delivery.RecipientId, actorId, StringComparison.Ordinal))
                {
                    throw new ReportDomainException(ReportErrorCodes.NotAuthorized);
                }
                await AuthorizeAsync(organizationGroupId, actorId, grant.Delivery.ObjectScope, ReportCapabilities.Receive, transactionToken);
                if (grant.ExpiresAt <= clock.UtcNow)
                    throw new ReportDomainException(ReportErrorCodes.DownloadGrantExpired);

                var reportId = Guid.Parse(grant.Delivery.Delivery.ReportId);
                var snapshots = await versionStore.LoadSnapshotsAsync(reportId, transactionToken);
                var actions = await versionStore.LoadActionsAsync(reportId, transactionToken);
                if (!string.Equals(
                        ReportDeliveryRules.ResolveVersionState(
                            grant.Delivery.Delivery.VersionNumber, snapshots, actions),
                        ReportVersionStates.Issued,
                        StringComparison.Ordinal))
                {
                    throw new ReportDomainException(ReportErrorCodes.DeliveryVersionUnavailable);
                }
                var snapshot = snapshots.Single(entry => entry.VersionNumber == grant.Delivery.Delivery.VersionNumber);
                if (!string.Equals(snapshot.ContentHash, grant.Delivery.Delivery.ContentHash, StringComparison.Ordinal))
                    throw new ReportDomainException(ReportErrorCodes.ContentHashMismatch);

                await deliveryStore.WriteReadAuditAsync(
                    grant.GrantId, organizationGroupId, actorId,
                    "DOWNLOAD_REPORT_VERSION", correlationId, clock.UtcNow, transactionToken);
                result = new ReportDownloadResult(
                    grant.Delivery.Delivery.DeliveryId, grant.Delivery.Delivery.ReportId,
                    grant.Delivery.Delivery.VersionNumber, snapshot.ContentHash, snapshot.CanonicalContent,
                    actorId, grant.ExpiresAt, ReportContract.DeliveryRuleSetVersion);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("DownloadReportVersion", actorId, organizationGroupId,
                tokenHash, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportNotificationResult> QueueNotificationAsync(
        string deliveryId,
        QueueReportNotificationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(deliveryId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(deliveryId);
            var validated = ReportDeliveryRules.ValidateNotification(request);
            ReportNotificationResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await deliveryStore.AcquireKeyLockAsync("notification", $"{id:N}:{validated.IdempotencyKey}", transactionToken);
                var delivery = await deliveryStore.LoadDeliveryAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, delivery.ObjectScope, ReportCapabilities.Manage, transactionToken);
                var existing = await deliveryStore.LoadNotificationByIdempotencyAsync(
                    delivery, validated.IdempotencyKey, transactionToken);
                if (existing is not null)
                {
                    if (!Matches(existing.Notification, validated))
                        throw new ReportDomainException(ReportErrorCodes.IdempotencyConflict);
                    await deliveryStore.WriteReadAuditAsync(
                        existing.Notification.NotificationId, organizationGroupId, actorId,
                        "RETRY_REPORT_NOTIFICATION", correlationId, clock.UtcNow, transactionToken);
                    result = existing.Notification;
                    return;
                }
                result = await deliveryStore.InsertNotificationAsync(
                    Guid.Parse(idGenerator.NewId()), delivery, validated, actorId,
                    clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("QueueReportNotification", actorId, organizationGroupId,
                deliveryId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<ReportNotificationAttemptResult> RecordNotificationAttemptAsync(
        string notificationId,
        RecordReportNotificationAttemptRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(notificationId, correlationId, cancellationToken);
        try
        {
            var id = ParseId(notificationId);
            var validated = ReportDeliveryRules.ValidateNotificationAttempt(request);
            ReportNotificationAttemptResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await deliveryStore.AcquireKeyLockAsync("notification-attempt", id.ToString("N"), transactionToken);
                var notification = await deliveryStore.LoadNotificationAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(
                    organizationGroupId, actorId, notification.Delivery.ObjectScope,
                    ReportCapabilities.Manage, transactionToken);
                var existing = await deliveryStore.LoadNotificationAttemptByIdempotencyAsync(
                    id, validated.IdempotencyKey, transactionToken);
                if (existing is not null)
                {
                    if (!Matches(existing, validated))
                        throw new ReportDomainException(ReportErrorCodes.IdempotencyConflict);
                    await deliveryStore.WriteReadAuditAsync(
                        existing.AttemptId, organizationGroupId, actorId,
                        "RETRY_REPORT_NOTIFICATION_ATTEMPT", correlationId, clock.UtcNow, transactionToken);
                    result = existing;
                    return;
                }
                if (notification.Notification.Attempts.Any(entry =>
                        string.Equals(entry.Outcome, ReportNotificationOutcomes.Delivered, StringComparison.Ordinal)))
                {
                    throw new ReportDomainException(ReportErrorCodes.NotificationConfirmationInvalid);
                }
                result = await deliveryStore.InsertNotificationAttemptAsync(
                    Guid.Parse(idGenerator.NewId()), notification, validated, actorId,
                    clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("RPT.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ReportDomainException or NpgsqlException)
        {
            throw await FailAsync("RecordReportNotificationAttempt", actorId, organizationGroupId,
                notificationId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
        string? target,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is not null && string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
            return (organizationGroupId, actor.ActorId);
        await WriteAttemptOrFailClosedAsync(
            "ReportDeliveryCommand", actor?.ActorId, organizationGroupId, target,
            correlationId, ReportErrorCodes.NotAuthorized, cancellationToken);
        throw new ReportDomainException(ReportErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId,
        string actorId,
        ReportObjectContext objectScope,
        string capability,
        CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new ReportAuthorizationRequest(
            organizationGroupId, actorId, objectScope, capability), cancellationToken);
        if (!decision.Allowed)
            throw new ReportDomainException(ReportErrorCodes.NotAuthorized);
    }

    private async Task<ReportDomainException> FailAsync(
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
            ReportDomainException domain => domain.ErrorCode,
            PostgresException { SqlState: "23505" } => ReportErrorCodes.IdempotencyConflict,
            _ => ReportErrorCodes.PersistenceUnavailable
        };
        ReportTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Report delivery command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType, code, correlationId);
        await WriteAttemptOrFailClosedAsync(
            commandType, actorId, organizationGroupId, target, correlationId, code, cancellationToken);
        return new ReportDomainException(code);
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
                commandType, actorId, organizationGroupId,
                ReportRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new ReportDomainException(ReportErrorCodes.PersistenceUnavailable);
        }
    }

    private static bool Matches(
        ReportDeliveryResult delivery,
        Guid reportId,
        int versionNumber,
        CreateReportDeliveryRequest request) =>
        string.Equals(delivery.ReportId, reportId.ToString("N"), StringComparison.Ordinal) &&
        delivery.VersionNumber == versionNumber &&
        string.Equals(delivery.RecipientId, request.RecipientId, StringComparison.Ordinal) &&
        string.Equals(delivery.Channel, request.Channel, StringComparison.Ordinal) &&
        string.Equals(delivery.DestinationHash, request.DestinationHash, StringComparison.Ordinal);

    private static bool Matches(ReportNotificationResult notification, QueueReportNotificationRequest request) =>
        string.Equals(notification.Channel, request.Channel, StringComparison.Ordinal) &&
        string.Equals(notification.DestinationHash, request.DestinationHash, StringComparison.Ordinal) &&
        notification.Payload == request.Payload;

    private static bool Matches(
        ReportNotificationAttemptResult attempt,
        RecordReportNotificationAttemptRequest request) =>
        string.Equals(attempt.Outcome, request.Outcome, StringComparison.Ordinal) &&
        string.Equals(attempt.ExternalReference, request.ExternalReference, StringComparison.Ordinal) &&
        string.Equals(attempt.DetailCode, request.DetailCode, StringComparison.Ordinal);

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
}
