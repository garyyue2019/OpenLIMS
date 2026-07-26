using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Allocation;

namespace OpenLIMS.Modules.Allocation;

internal sealed class AllocationDomainException(string errorCode, string? gateSource = null) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
    public string? GateSource { get; } = gateSource;
}

internal sealed record AllocationSubjectState(long CurrentVersion, bool HasActiveDestructiveAllocation);

internal static class AllocationRules
{
    private const int MaximumSequenceOrder = 100_000;
    private static readonly decimal MaximumAmount = 1_000_000_000_000m;
    private static readonly string[] KnownDimensions = ["COUNT", "MASS", "LENGTH", "AREA", "VOLUME"];
    private static readonly Regex StableIdentifier = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static AllocationObjectContext NormalizeObjectScope(AllocationObjectContext? value)
    {
        if (value is null)
            throw new AllocationDomainException(AllocationErrorCodes.ValidationFailed);

        return new AllocationObjectContext(
            Identifier(value.LegalEntityId),
            Identifier(value.LaboratoryId),
            Identifier(value.CustomerId),
            Identifier(value.ServiceOrderId),
            Identifier(value.ProductCategory));
    }

    public static CreateTestObjectAllocationRequest ValidateRequest(
        CreateTestObjectAllocationRequest? request,
        DateTimeOffset now)
    {
        if (request is null || request.ExpectedCurrentVersion < 0)
            throw new AllocationDomainException(AllocationErrorCodes.ValidationFailed);
        if (!string.Equals(request.RuleSetVersion, AllocationContract.RuleSetVersion, StringComparison.Ordinal))
            throw new AllocationDomainException(AllocationErrorCodes.ApplicabilityUnknown);

        var objectScope = NormalizeObjectScope(request.ObjectScope);
        var subject = Subject(request.Subject);
        var receivedItemId = Identifier(request.ReceivedItemId);
        if (request.ExpectedReceivedItemVersion < 1 ||
            request.ExpectedScopeMatrixVersion < 1 ||
            request.ExpectedQuantityAccountVersion < 1)
        {
            throw new AllocationDomainException(AllocationErrorCodes.ValidationFailed);
        }

        if (string.Equals(subject.SubjectType, AllocationSubjectTypes.ReceivedItem, StringComparison.Ordinal) &&
            !string.Equals(subject.Id, receivedItemId, StringComparison.Ordinal))
        {
            throw new AllocationDomainException(AllocationErrorCodes.ValidationFailed);
        }

        if (request.RequestedAmount <= 0 ||
            request.RequestedAmount >= MaximumAmount ||
            Scale(request.RequestedAmount) > 6 ||
            !KnownDimensions.Contains(request.Dimension?.Trim() ?? string.Empty, StringComparer.Ordinal) ||
            request.SequenceOrder is < 0 or > MaximumSequenceOrder)
        {
            throw new AllocationDomainException(AllocationErrorCodes.ValidationFailed);
        }

        if (request.ValidUntil <= now)
            throw new AllocationDomainException(AllocationErrorCodes.AllocationExpired);

        return request with
        {
            ObjectScope = objectScope,
            Subject = subject,
            IdentityAssignment = Reference(request.IdentityAssignment),
            ReceivedItemId = receivedItemId,
            ScopeMatrixId = Identifier(request.ScopeMatrixId),
            ScopeLineId = Identifier(request.ScopeLineId),
            PlanStep = Reference(request.PlanStep),
            Purpose = Text(request.Purpose),
            Dimension = request.Dimension!.Trim(),
            Unit = Identifier(request.Unit),
            StorageCondition = Reference(request.StorageCondition),
            ReservationEntryId = OptionalIdentifier(request.ReservationEntryId)
        };
    }

    public static AllocationGateResult RequireAllowed(
        string source,
        string decision,
        long? pinnedVersion,
        string ruleSetVersion,
        IReadOnlyList<string> reasonCodes)
    {
        var result = new AllocationGateResult(source, decision, pinnedVersion, ruleSetVersion, reasonCodes);
        return decision switch
        {
            "ALLOWED" => result,
            "BLOCKED" => throw new AllocationDomainException(AllocationErrorCodes.EligibilityBlocked, source),
            _ => throw new AllocationDomainException(AllocationErrorCodes.ApplicabilityUnknown, source)
        };
    }

    public static void RequirePostable(
        long expectedCurrentVersion,
        AllocationSubjectState subjectState)
    {
        if (subjectState.CurrentVersion != expectedCurrentVersion)
            throw new AllocationDomainException(AllocationErrorCodes.ExpectedVersionConflict);
        if (subjectState.HasActiveDestructiveAllocation)
            throw new AllocationDomainException(AllocationErrorCodes.DestructiveConflict);
    }

    public static AllocationStatusResult EvaluateStatus(
        AllocationStatusRequest request,
        TestObjectAllocationResult? allocation,
        long? currentSubjectVersion,
        DateTimeOffset now)
    {
        if (!string.Equals(request.RuleSetVersion, AllocationContract.RuleSetVersion, StringComparison.Ordinal))
            return Unknown(allocation, currentSubjectVersion, AllocationStatusReasons.RuleSetVersionUnknown);
        if (allocation is null || currentSubjectVersion is null)
            return Blocked(null, null, AllocationStatusReasons.AllocationRequired);
        if (request.ExpectedSubjectAllocationVersion != currentSubjectVersion)
            return Unknown(allocation, currentSubjectVersion, AllocationStatusReasons.SubjectVersionMismatch);
        if (string.Equals(allocation.State, AllocationStates.Released, StringComparison.Ordinal))
            return Blocked(allocation, currentSubjectVersion, AllocationStatusReasons.AllocationReleased);
        if (allocation.ValidUntil <= now)
            return Blocked(allocation, currentSubjectVersion, AllocationStatusReasons.AllocationExpired);

        return new AllocationStatusResult(
            AllocationStatusDecisions.Allowed,
            [],
            allocation.AllocationId,
            allocation.State,
            currentSubjectVersion,
            AllocationContract.RuleSetVersion);
    }

    public static string HashTarget(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }

    public static string Text(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > 500)
            throw new AllocationDomainException(AllocationErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static AllocationSubjectReference Subject(AllocationSubjectReference? value)
    {
        if (value is null || value.Version < 1 || !KnownSubjectType(value.SubjectType))
            throw new AllocationDomainException(AllocationErrorCodes.ValidationFailed);
        return new AllocationSubjectReference(value.SubjectType.Trim(), Identifier(value.Id), value.Version);
    }

    private static bool KnownSubjectType(string? value) => value?.Trim() is
        AllocationSubjectTypes.ReceivedItem or AllocationSubjectTypes.TestSpecimen or AllocationSubjectTypes.TestPortion;

    private static AllocationVersionedReference Reference(AllocationVersionedReference? value)
    {
        if (value is null || value.Version < 1)
            throw new AllocationDomainException(AllocationErrorCodes.ValidationFailed);
        return new AllocationVersionedReference(Identifier(value.Id), value.Version);
    }

    private static int Scale(decimal value)
    {
        value = Math.Abs(value);
        var scale = 0;
        while (scale <= 7 && value != decimal.Truncate(value))
        {
            value *= 10m;
            scale++;
        }

        return scale;
    }

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!StableIdentifier.IsMatch(trimmed))
            throw new AllocationDomainException(AllocationErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static string? OptionalIdentifier(string? value) =>
        value is null ? null : Identifier(value);

    private static AllocationStatusResult Blocked(
        TestObjectAllocationResult? allocation,
        long? currentSubjectVersion,
        string reason) => new(
        AllocationStatusDecisions.Blocked,
        [reason],
        allocation?.AllocationId,
        allocation?.State,
        currentSubjectVersion,
        AllocationContract.RuleSetVersion);

    private static AllocationStatusResult Unknown(
        TestObjectAllocationResult? allocation,
        long? currentSubjectVersion,
        string reason) => new(
        AllocationStatusDecisions.Unknown,
        [reason],
        allocation?.AllocationId,
        allocation?.State,
        currentSubjectVersion,
        AllocationContract.RuleSetVersion);
}
