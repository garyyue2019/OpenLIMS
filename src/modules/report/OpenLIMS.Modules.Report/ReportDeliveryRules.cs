using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

internal static class ReportDeliveryRules
{
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Sha256Pattern = new(
        "^[a-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static CreateReportDeliveryRequest ValidateDelivery(CreateReportDeliveryRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ReportContract.DeliveryRuleSetVersion, StringComparison.Ordinal) ||
            !ReportDeliveryChannels.All.Contains(request.Channel, StringComparer.Ordinal))
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }

        return request with
        {
            RecipientId = Identifier(request.RecipientId),
            DestinationHash = Sha256(request.DestinationHash),
            IdempotencyKey = Identifier(request.IdempotencyKey)
        };
    }

    public static CreateReportDownloadGrantRequest ValidateGrant(
        CreateReportDownloadGrantRequest? request,
        DateTimeOffset now)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ReportContract.DeliveryRuleSetVersion, StringComparison.Ordinal) ||
            request.ExpiresAt <= now || request.ExpiresAt > now.AddDays(30))
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }

        return request with { RecipientId = Identifier(request.RecipientId) };
    }

    public static QueueReportNotificationRequest ValidateNotification(QueueReportNotificationRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ReportContract.DeliveryRuleSetVersion, StringComparison.Ordinal) ||
            !ReportDeliveryChannels.All.Contains(request.Channel, StringComparer.Ordinal) ||
            request.Payload is null || request.Payload.Version < 1)
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }

        return request with
        {
            DestinationHash = Sha256(request.DestinationHash),
            Payload = new ReportVersionedReference(Identifier(request.Payload.Id), request.Payload.Version),
            IdempotencyKey = Identifier(request.IdempotencyKey)
        };
    }

    public static RecordReportNotificationAttemptRequest ValidateNotificationAttempt(
        RecordReportNotificationAttemptRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ReportContract.DeliveryRuleSetVersion, StringComparison.Ordinal) ||
            !ReportNotificationOutcomes.Attempts.Contains(request.Outcome, StringComparer.Ordinal))
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }

        var externalReference = Optional(request.ExternalReference, 200);
        var detailCode = Optional(request.DetailCode, 200);
        if (string.Equals(request.Outcome, ReportNotificationOutcomes.Delivered, StringComparison.Ordinal))
        {
            if (externalReference is null)
                throw new ReportDomainException(ReportErrorCodes.NotificationConfirmationInvalid);
        }
        else if (externalReference is not null)
        {
            throw new ReportDomainException(ReportErrorCodes.NotificationConfirmationInvalid);
        }

        return request with
        {
            IdempotencyKey = Identifier(request.IdempotencyKey),
            ExternalReference = externalReference,
            DetailCode = detailCode
        };
    }

    public static string ResolveNotificationStatus(IReadOnlyList<ReportNotificationAttemptResult> attempts) =>
        attempts.OrderBy(entry => entry.AttemptNumber).LastOrDefault()?.Outcome ?? ReportNotificationOutcomes.Pending;

    public static string ResolveVersionState(
        int versionNumber,
        IReadOnlyList<ReportVersionSnapshotResult> snapshots,
        IReadOnlyList<ReportControlledActionResult> actions)
    {
        if (!snapshots.Any(snapshot => snapshot.VersionNumber == versionNumber))
            throw new ReportDomainException(ReportErrorCodes.ObjectNotAccessible);
        if (actions.Any(action => string.Equals(action.Kind, ReportControlledActionKinds.Void, StringComparison.Ordinal)))
            return ReportVersionStates.Voided;
        if (actions.Any(action => action.VersionNumber == versionNumber &&
                                  string.Equals(action.Kind, ReportControlledActionKinds.Withdrawal, StringComparison.Ordinal)))
        {
            return ReportVersionStates.Withdrawn;
        }
        if (actions.Any(action => action.VersionNumber == versionNumber &&
                                  ReportControlledActionKinds.ProduceNewVersion.Contains(action.Kind, StringComparer.Ordinal)))
        {
            return ReportVersionStates.Superseded;
        }
        return ReportVersionStates.Issued;
    }

    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!IdentifierPattern.IsMatch(trimmed))
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static string Sha256(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!Sha256Pattern.IsMatch(normalized))
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        return normalized;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        return trimmed;
    }
}
