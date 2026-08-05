using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

internal sealed record StoredReportDelivery(
    ReportDeliveryResult Delivery,
    ReportObjectContext ObjectScope,
    string OrganizationGroupId);

internal sealed record StoredDownloadGrant(
    string GrantId,
    StoredReportDelivery Delivery,
    string RecipientId,
    DateTimeOffset ExpiresAt,
    string CreatedBy,
    DateTimeOffset CreatedAt);

internal sealed record StoredReportNotification(
    ReportNotificationResult Notification,
    StoredReportDelivery Delivery);

internal sealed class ReportDeliveryStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireKeyLockAsync(string category, string key, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtext(@lock_key))", connection, transaction);
        command.Parameters.AddWithValue("lock_key", $"report.{category}:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredReportDelivery?> LoadDeliveryByIdempotencyAsync(
        string organizationGroupId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = DeliveryCommand(connection, transaction, """
            where d.organization_group_id = @organization_group_id
              and d.idempotency_key = @idempotency_key
            """);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        return await ReadDeliveryAsync(command, cancellationToken);
    }

    public async Task<StoredReportDelivery?> LoadDeliveryAsync(
        string organizationGroupId,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = DeliveryCommand(connection, transaction, """
            where d.organization_group_id = @organization_group_id
              and d.delivery_id = @delivery_id
            """);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("delivery_id", deliveryId);
        return await ReadDeliveryAsync(command, cancellationToken);
    }

    public async Task<ReportDeliveryResult> InsertDeliveryAsync(
        Guid deliveryId,
        string organizationGroupId,
        Guid reportId,
        int versionNumber,
        string contentHash,
        CreateReportDeliveryRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into report.delivery (
                delivery_id, organization_group_id, report_id, version_number, content_hash,
                recipient_id, channel, destination_hash, idempotency_key,
                created_by, created_at, event_id, correlation_id
            ) values (
                @delivery_id, @organization_group_id, @report_id, @version_number, @content_hash,
                @recipient_id, @channel, @destination_hash, @idempotency_key,
                @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("delivery_id", deliveryId);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("report_id", reportId);
        command.Parameters.AddWithValue("version_number", versionNumber);
        command.Parameters.AddWithValue("content_hash", contentHash);
        command.Parameters.AddWithValue("recipient_id", request.RecipientId);
        command.Parameters.AddWithValue("channel", request.Channel);
        command.Parameters.AddWithValue("destination_hash", request.DestinationHash);
        command.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);
        command.Parameters.AddWithValue("created_by", actorId);
        command.Parameters.AddWithValue("created_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            "CREATE_REPORT_DELIVERY", deliveryId.ToString("N"), organizationGroupId, actorId,
            eventId, "Report.DeliveryCreated.v1", correlationId, now, cancellationToken);
        return new ReportDeliveryResult(
            deliveryId.ToString("N"), reportId.ToString("N"), versionNumber, contentHash,
            request.RecipientId, request.Channel, request.DestinationHash, actorId, now);
    }

    public async Task<ReportDownloadGrantResult> InsertGrantAsync(
        Guid grantId,
        StoredReportDelivery delivery,
        string tokenHash,
        string accessToken,
        CreateReportDownloadGrantRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into report.download_grant (
                grant_id, delivery_id, recipient_id, token_hash, expires_at,
                created_by, created_at, event_id, correlation_id
            ) values (
                @grant_id, @delivery_id, @recipient_id, @token_hash, @expires_at,
                @created_by, @created_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("grant_id", grantId);
        command.Parameters.AddWithValue("delivery_id", Guid.Parse(delivery.Delivery.DeliveryId));
        command.Parameters.AddWithValue("recipient_id", request.RecipientId);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        command.Parameters.AddWithValue("expires_at", request.ExpiresAt);
        command.Parameters.AddWithValue("created_by", actorId);
        command.Parameters.AddWithValue("created_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            "CREATE_REPORT_DOWNLOAD_GRANT", grantId.ToString("N"), delivery.OrganizationGroupId, actorId,
            eventId, "Report.DownloadGrantCreated.v1", correlationId, now, cancellationToken);
        return new ReportDownloadGrantResult(
            grantId.ToString("N"), delivery.Delivery.DeliveryId, request.RecipientId,
            request.ExpiresAt, accessToken, actorId, now);
    }

    public async Task<StoredDownloadGrant?> LoadGrantByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select g.grant_id, g.recipient_id, g.expires_at, g.created_by, g.created_at,
                   d.delivery_id, d.organization_group_id, d.report_id, d.version_number,
                   d.content_hash, d.recipient_id, d.channel, d.destination_hash, d.created_by, d.created_at,
                   r.legal_entity_id, r.laboratory_id, r.customer_id, r.service_order_id, r.product_category
            from report.download_grant g
            join report.delivery d on d.delivery_id = g.delivery_id
            join report.report r on r.report_id = d.report_id
            where g.token_hash = @token_hash
              and r.organization_group_id = d.organization_group_id
            """, connection, transaction);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var delivery = MapDelivery(reader, 5);
        return new StoredDownloadGrant(
            reader.GetGuid(0).ToString("N"), delivery, reader.GetString(1),
            reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task<StoredReportNotification?> LoadNotificationByIdempotencyAsync(
        StoredReportDelivery delivery,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select notification_id
            from report.notification
            where delivery_id = @delivery_id and idempotency_key = @idempotency_key
            """, connection, transaction);
        command.Parameters.AddWithValue("delivery_id", Guid.Parse(delivery.Delivery.DeliveryId));
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id
            ? await LoadNotificationAsync(delivery.OrganizationGroupId, id, cancellationToken)
            : null;
    }

    public async Task<ReportNotificationResult> InsertNotificationAsync(
        Guid notificationId,
        StoredReportDelivery delivery,
        QueueReportNotificationRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into report.notification (
                notification_id, delivery_id, channel, destination_hash,
                payload_ref, payload_version, idempotency_key,
                queued_by, queued_at, event_id, correlation_id
            ) values (
                @notification_id, @delivery_id, @channel, @destination_hash,
                @payload_ref, @payload_version, @idempotency_key,
                @queued_by, @queued_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("notification_id", notificationId);
        command.Parameters.AddWithValue("delivery_id", Guid.Parse(delivery.Delivery.DeliveryId));
        command.Parameters.AddWithValue("channel", request.Channel);
        command.Parameters.AddWithValue("destination_hash", request.DestinationHash);
        command.Parameters.AddWithValue("payload_ref", request.Payload.Id);
        command.Parameters.AddWithValue("payload_version", request.Payload.Version);
        command.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);
        command.Parameters.AddWithValue("queued_by", actorId);
        command.Parameters.AddWithValue("queued_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            "QUEUE_REPORT_NOTIFICATION", notificationId.ToString("N"), delivery.OrganizationGroupId, actorId,
            eventId, "Report.NotificationQueued.v1", correlationId, now, cancellationToken);
        return new ReportNotificationResult(
            notificationId.ToString("N"), delivery.Delivery.DeliveryId, request.Channel,
            request.DestinationHash, request.Payload, ReportNotificationOutcomes.Pending,
            [], actorId, now);
    }

    public async Task<StoredReportNotification?> LoadNotificationAsync(
        string organizationGroupId,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select n.notification_id, n.channel, n.destination_hash, n.payload_ref,
                   n.payload_version, n.queued_by, n.queued_at,
                   d.delivery_id, d.organization_group_id, d.report_id, d.version_number,
                   d.content_hash, d.recipient_id, d.channel, d.destination_hash, d.created_by, d.created_at,
                   r.legal_entity_id, r.laboratory_id, r.customer_id, r.service_order_id, r.product_category
            from report.notification n
            join report.delivery d on d.delivery_id = n.delivery_id
            join report.report r on r.report_id = d.report_id
            where n.notification_id = @notification_id
              and d.organization_group_id = @organization_group_id
              and r.organization_group_id = d.organization_group_id
            """, connection, transaction);
        command.Parameters.AddWithValue("notification_id", notificationId);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var notificationBase = new ReportNotificationResult(
            reader.GetGuid(0).ToString("N"), reader.GetGuid(7).ToString("N"),
            reader.GetString(1), reader.GetString(2),
            new ReportVersionedReference(reader.GetString(3), reader.GetInt64(4)),
            ReportNotificationOutcomes.Pending, [], reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6));
        var delivery = MapDelivery(reader, 7);
        await reader.DisposeAsync();
        var attempts = await LoadNotificationAttemptsAsync(notificationId, cancellationToken);
        return new StoredReportNotification(
            notificationBase with
            {
                Status = ReportDeliveryRules.ResolveNotificationStatus(attempts),
                Attempts = attempts
            },
            delivery);
    }

    public async Task<IReadOnlyList<ReportNotificationResult>> LoadNotificationsAsync(
        StoredReportDelivery delivery,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var ids = new List<Guid>();
        await using (var command = new NpgsqlCommand("""
            select notification_id from report.notification
            where delivery_id = @delivery_id order by queued_at, notification_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("delivery_id", Guid.Parse(delivery.Delivery.DeliveryId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                ids.Add(reader.GetGuid(0));
        }
        var results = new List<ReportNotificationResult>();
        foreach (var id in ids)
        {
            var stored = await LoadNotificationAsync(delivery.OrganizationGroupId, id, cancellationToken);
            if (stored is not null)
                results.Add(stored.Notification);
        }
        return results;
    }

    public async Task<ReportNotificationAttemptResult?> LoadNotificationAttemptByIdempotencyAsync(
        Guid notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select attempt_id, attempt_number, outcome, external_reference, detail_code,
                   attempted_by, attempted_at
            from report.notification_attempt
            where notification_id = @notification_id and idempotency_key = @idempotency_key
            """, connection, transaction);
        command.Parameters.AddWithValue("notification_id", notificationId);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? MapNotificationAttempt(reader, notificationId)
            : null;
    }

    public async Task<ReportNotificationAttemptResult> InsertNotificationAttemptAsync(
        Guid attemptId,
        StoredReportNotification notification,
        RecordReportNotificationAttemptRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var notificationId = Guid.Parse(notification.Notification.NotificationId);
        var attemptNumber = notification.Notification.Attempts.Count + 1;
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using var command = new NpgsqlCommand("""
            insert into report.notification_attempt (
                attempt_id, notification_id, attempt_number, idempotency_key,
                outcome, external_reference, detail_code,
                attempted_by, attempted_at, event_id, correlation_id
            ) values (
                @attempt_id, @notification_id, @attempt_number, @idempotency_key,
                @outcome, @external_reference, @detail_code,
                @attempted_by, @attempted_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("attempt_id", attemptId);
        command.Parameters.AddWithValue("notification_id", notificationId);
        command.Parameters.AddWithValue("attempt_number", attemptNumber);
        command.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);
        command.Parameters.AddWithValue("outcome", request.Outcome);
        command.Parameters.AddWithValue("external_reference", (object?)request.ExternalReference ?? DBNull.Value);
        command.Parameters.AddWithValue("detail_code", (object?)request.DetailCode ?? DBNull.Value);
        command.Parameters.AddWithValue("attempted_by", actorId);
        command.Parameters.AddWithValue("attempted_at", now);
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WritePlatformEvidenceAsync(
            "RECORD_REPORT_NOTIFICATION_ATTEMPT", attemptId.ToString("N"),
            notification.Delivery.OrganizationGroupId, actorId, eventId,
            "Report.NotificationAttempted.v1", correlationId, now, cancellationToken);
        return new ReportNotificationAttemptResult(
            attemptId.ToString("N"), notificationId.ToString("N"), attemptNumber,
            request.Outcome, request.ExternalReference, request.DetailCode, actorId, now);
    }

    public Task WriteReadAuditAsync(
        string objectId,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, objectId, action, ReportContract.DeliveryRuleSetVersion,
            "1", "1", correlationId, now), cancellationToken);

    private async Task<IReadOnlyList<ReportNotificationAttemptResult>> LoadNotificationAttemptsAsync(
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var attempts = new List<ReportNotificationAttemptResult>();
        await using var command = new NpgsqlCommand("""
            select attempt_id, attempt_number, outcome, external_reference, detail_code,
                   attempted_by, attempted_at
            from report.notification_attempt
            where notification_id = @notification_id order by attempt_number
            """, connection, transaction);
        command.Parameters.AddWithValue("notification_id", notificationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            attempts.Add(MapNotificationAttempt(reader, notificationId));
        return attempts;
    }

    private static NpgsqlCommand DeliveryCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string whereClause) => new($"""
            select d.delivery_id, d.organization_group_id, d.report_id, d.version_number,
                   d.content_hash, d.recipient_id, d.channel, d.destination_hash, d.created_by, d.created_at,
                   r.legal_entity_id, r.laboratory_id, r.customer_id, r.service_order_id, r.product_category
            from report.delivery d
            join report.report r on r.report_id = d.report_id
            {whereClause}
              and r.organization_group_id = d.organization_group_id
            """, connection, transaction);

    private static async Task<StoredReportDelivery?> ReadDeliveryAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapDelivery(reader, 0) : null;
    }

    private static StoredReportDelivery MapDelivery(NpgsqlDataReader reader, int offset) => new(
        new ReportDeliveryResult(
            reader.GetGuid(offset).ToString("N"), reader.GetGuid(offset + 2).ToString("N"),
            reader.GetInt32(offset + 3), reader.GetString(offset + 4), reader.GetString(offset + 5),
            reader.GetString(offset + 6), reader.GetString(offset + 7), reader.GetString(offset + 8),
            reader.GetFieldValue<DateTimeOffset>(offset + 9)),
        new ReportObjectContext(
            reader.GetString(offset + 10), reader.GetString(offset + 11), reader.GetString(offset + 12),
            reader.GetString(offset + 13), reader.GetString(offset + 14)),
        reader.GetString(offset + 1));

    private static ReportNotificationAttemptResult MapNotificationAttempt(
        NpgsqlDataReader reader,
        Guid notificationId) => new(
            reader.GetGuid(0).ToString("N"), notificationId.ToString("N"), reader.GetInt32(1),
            reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6));

    private async Task WritePlatformEvidenceAsync(
        string action,
        string objectId,
        string organizationGroupId,
        string actorId,
        string eventId,
        string messageType,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, objectId, action, ReportContract.DeliveryRuleSetVersion,
            null, "1", correlationId, now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("RPT.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}
