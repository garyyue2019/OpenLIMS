using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal static partial class IdentityAssessmentRules
{
    private const int TextMaximumLength = 1000;
    private const int IdentifierMaximumLength = 200;
    private const int MaximumEvidenceItems = 50;

    public static void ValidateObservation(CreateIdentityObservationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedItemVersion < 1 ||
            request.ObservedLabels is null || request.ObservedLabels.Count is < 1 or > MaximumEvidenceItems ||
            request.AttachmentRefs is null || request.AttachmentRefs.Count is < 1 or > MaximumEvidenceItems ||
            request.AttachmentHashes is null || request.AttachmentHashes.Count != request.AttachmentRefs.Count ||
            !ValidText(request.ObservedModel, IdentifierMaximumLength) ||
            !ValidText(request.ObservedBatch, IdentifierMaximumLength) ||
            !ValidText(request.Appearance, TextMaximumLength) ||
            request.ObservedLabels.Any(value => !ValidText(value, IdentifierMaximumLength)) ||
            request.AttachmentRefs.Any(value => !ValidText(value, IdentifierMaximumLength)) ||
            request.AttachmentHashes.Any(value => !Sha256Regex().IsMatch(value)))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.IdentityEvidenceIncomplete);
        }
    }

    public static void ValidateDecision(
        SubmitIdentityDecisionRequest request,
        IdentityDeclarationSnapshotResult declaration,
        IdentityObservationResult observation)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedItemVersion < 1 ||
            request.ObservationVersion < 1 ||
            request.DeclarationSnapshotVersion < 1 ||
            !ValidText(request.ReasonCode, IdentifierMaximumLength) ||
            !ValidText(request.Rationale, TextMaximumLength) ||
            !string.Equals(request.RuleSetVersion, IdentityAssessmentContract.RuleSetVersion, StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.IdentityEvidenceIncomplete);
        }

        var outcomeKnown = string.Equals(request.Outcome, IdentityDecisionOutcomes.Matched, StringComparison.Ordinal) ||
            string.Equals(request.Outcome, IdentityDecisionOutcomes.Mismatched, StringComparison.Ordinal) ||
            string.Equals(request.Outcome, IdentityDecisionOutcomes.Indeterminate, StringComparison.Ordinal);
        if (!outcomeKnown)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.IdentityEvidenceIncomplete);
        }

        var hasConflict = !Equivalent(declaration.Model, observation.ObservedModel) ||
            !Equivalent(declaration.Batch, observation.ObservedBatch);
        if (string.Equals(request.Outcome, IdentityDecisionOutcomes.Matched, StringComparison.Ordinal) && hasConflict)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.IdentityConflict);
        }

        if (string.Equals(request.Outcome, IdentityDecisionOutcomes.Mismatched, StringComparison.Ordinal) && !hasConflict)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.IdentityConflict);
        }

        if (string.Equals(request.Outcome, IdentityDecisionOutcomes.Indeterminate, StringComparison.Ordinal) &&
            !string.Equals(request.ReasonCode, ReceivingErrorCodes.IdentityAmbiguous, StringComparison.Ordinal))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.IdentityAmbiguous);
        }
    }

    public static bool IsKnownEligibilityAction(string value) =>
        string.Equals(value, ReceivingEligibilityActions.Disassembly, StringComparison.Ordinal) ||
        string.Equals(value, ReceivingEligibilityActions.SamplePreparation, StringComparison.Ordinal) ||
        string.Equals(value, ReceivingEligibilityActions.TestAssignment, StringComparison.Ordinal);

    private static bool ValidText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static bool Equivalent(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

internal sealed record IdentityItemScope(
    Guid ReceivedItemId,
    string ReceivedItemNumber,
    long ItemVersion,
    string CurrentState,
    string OrganizationGroupId,
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory,
    string DeclaredDescription,
    string Model,
    string Batch,
    string? SerialNumber,
    string Color);
