using OpenLIMS.Contracts.Report;
using OpenLIMS.Modules.Report;
using Xunit;

namespace OpenLIMS.Report.UnitTests;

[Trait("Profile", "report")]
public sealed class ReportDeliveryRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Delivery_validation_pins_rule_channel_recipient_and_destination_hash()
    {
        var validated = ReportDeliveryRules.ValidateDelivery(new CreateReportDeliveryRequest(
            ReportContract.DeliveryRuleSetVersion, "recipient-a", ReportDeliveryChannels.Portal,
            new string('A', 64), "delivery-1"));
        var invalid = Assert.Throws<ReportDomainException>(() =>
            ReportDeliveryRules.ValidateDelivery(new CreateReportDeliveryRequest(
                "RPT-DELIVERY@latest", "recipient-a", "SMS", "not-a-hash", "delivery-1")));

        Assert.Equal(new string('a', 64), validated.DestinationHash);
        Assert.Equal(ReportErrorCodes.ValidationFailed, invalid.ErrorCode);
    }

    [Fact]
    public void Download_grant_is_recipient_bound_and_short_lived()
    {
        var valid = ReportDeliveryRules.ValidateGrant(new CreateReportDownloadGrantRequest(
            ReportContract.DeliveryRuleSetVersion, "recipient-a", Now.AddDays(7)), Now);
        var expired = Assert.Throws<ReportDomainException>(() =>
            ReportDeliveryRules.ValidateGrant(new CreateReportDownloadGrantRequest(
                ReportContract.DeliveryRuleSetVersion, "recipient-a", Now), Now));
        var tooLong = Assert.Throws<ReportDomainException>(() =>
            ReportDeliveryRules.ValidateGrant(new CreateReportDownloadGrantRequest(
                ReportContract.DeliveryRuleSetVersion, "recipient-a", Now.AddDays(31)), Now));

        Assert.Equal("recipient-a", valid.RecipientId);
        Assert.Equal(ReportErrorCodes.ValidationFailed, expired.ErrorCode);
        Assert.Equal(ReportErrorCodes.ValidationFailed, tooLong.ErrorCode);
    }

    [Fact]
    public void Notification_success_requires_external_reference_and_failure_cannot_claim_one()
    {
        var delivered = ReportDeliveryRules.ValidateNotificationAttempt(
            new RecordReportNotificationAttemptRequest(
                ReportContract.DeliveryRuleSetVersion, "attempt-1",
                ReportNotificationOutcomes.Delivered, "provider-message-7"));
        var missing = Assert.Throws<ReportDomainException>(() =>
            ReportDeliveryRules.ValidateNotificationAttempt(
                new RecordReportNotificationAttemptRequest(
                    ReportContract.DeliveryRuleSetVersion, "attempt-2",
                    ReportNotificationOutcomes.Delivered)));
        var falseFailure = Assert.Throws<ReportDomainException>(() =>
            ReportDeliveryRules.ValidateNotificationAttempt(
                new RecordReportNotificationAttemptRequest(
                    ReportContract.DeliveryRuleSetVersion, "attempt-3",
                    ReportNotificationOutcomes.Failed, "provider-message-8")));

        Assert.Equal("provider-message-7", delivered.ExternalReference);
        Assert.Equal(ReportErrorCodes.NotificationConfirmationInvalid, missing.ErrorCode);
        Assert.Equal(ReportErrorCodes.NotificationConfirmationInvalid, falseFailure.ErrorCode);
    }

    [Fact]
    public void Controlled_actions_make_old_delivery_unavailable_instead_of_retargeting_it()
    {
        var snapshots = new[] { Snapshot(1, "one"), Snapshot(2, "two") };
        var actions = new[]
        {
            new ReportControlledActionResult(
                "a1", ReportId, 1, ReportControlledActionKinds.Correction,
                new ReportVersionedReference("impact-1", 1), null, "fix", "actor", Now)
        };

        Assert.Equal(
            ReportVersionStates.Superseded,
            ReportDeliveryRules.ResolveVersionState(1, snapshots, actions));
        Assert.Equal(
            ReportVersionStates.Issued,
            ReportDeliveryRules.ResolveVersionState(2, snapshots, actions));
    }

    [Fact]
    public void Notification_status_is_derived_from_the_latest_immutable_attempt()
    {
        var attempts = new[]
        {
            Attempt(1, ReportNotificationOutcomes.Failed),
            Attempt(2, ReportNotificationOutcomes.Unknown)
        };

        Assert.Equal(ReportNotificationOutcomes.Pending, ReportDeliveryRules.ResolveNotificationStatus([]));
        Assert.Equal(ReportNotificationOutcomes.Unknown, ReportDeliveryRules.ResolveNotificationStatus(attempts));
    }

    private const string ReportId = "00000000000000000000000000000011";

    private static ReportVersionSnapshotResult Snapshot(int version, string content) => new(
        Guid.NewGuid().ToString("N"), ReportId, version, new string((char)('a' + version), 64),
        content, 1, "actor", Now);

    private static ReportNotificationAttemptResult Attempt(int number, string outcome) => new(
        Guid.NewGuid().ToString("N"), "00000000000000000000000000000021", number,
        outcome, null, null, "actor", Now);
}
