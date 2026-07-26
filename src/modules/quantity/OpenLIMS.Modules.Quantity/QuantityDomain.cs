using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Quantity;

namespace OpenLIMS.Modules.Quantity;

internal sealed class QuantityDomainException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

internal sealed record QuantityAccountConfiguration(
    string Dimension,
    string Unit,
    int PrecisionScale,
    decimal ConservationTolerance);

internal sealed record QuantityBalances(long Version, decimal Balance, decimal Reserved)
{
    public decimal Available => Balance - Reserved;
}

internal sealed record QuantityEntrySnapshot(
    Guid EntryId,
    string EntryType,
    decimal Amount,
    Guid? ReferencedEntryId,
    Guid? ReservationId,
    bool Reversed,
    bool ReservationClosed,
    bool Restated,
    string? OriginalEntryType = null);

internal sealed record QuantityPostingPlan(
    string EntryType,
    decimal Amount,
    decimal ResultingBalance,
    decimal ResultingReserved,
    Guid? ReferencedEntryId,
    Guid? ReservationId,
    string? Reason);

internal static class QuantityRules
{
    private const int MaximumPrecisionScale = 6;
    private static readonly decimal MaximumAmount = 1_000_000_000_000m;
    private static readonly Regex StableIdentifier = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static QuantityObjectContext NormalizeObjectScope(QuantityObjectContext? value)
    {
        if (value is null)
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);

        return new QuantityObjectContext(
            Identifier(value.LegalEntityId),
            Identifier(value.LaboratoryId),
            Identifier(value.CustomerId),
            Identifier(value.ServiceOrderId),
            Identifier(value.ProductCategory));
    }

    public static (QuantitySubjectReference Subject, QuantityAccountConfiguration Configuration) ValidateAccount(
        CreateQuantityAccountRequest? request)
    {
        if (request is null)
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        if (!string.Equals(request.RuleSetVersion, QuantityContract.RuleSetVersion, StringComparison.Ordinal))
            throw new QuantityDomainException(QuantityErrorCodes.ApplicabilityUnknown);
        _ = NormalizeObjectScope(request.ObjectScope);
        if (!request.SubjectQuantifiable)
            throw new QuantityDomainException(QuantityErrorCodes.NotQuantifiable);

        var subject = Subject(request.Subject);
        var dimension = request.Dimension?.Trim() ?? string.Empty;
        if (!KnownDimension(dimension))
            throw new QuantityDomainException(QuantityErrorCodes.DimensionMismatch);
        if (request.PrecisionScale is < 0 or > MaximumPrecisionScale ||
            (string.Equals(dimension, QuantityDimensions.Count, StringComparison.Ordinal) && request.PrecisionScale != 0))
        {
            throw new QuantityDomainException(QuantityErrorCodes.DimensionMismatch);
        }

        if (request.ConservationTolerance < 0 ||
            request.ConservationTolerance >= MaximumAmount ||
            Scale(request.ConservationTolerance) > request.PrecisionScale)
        {
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        }

        return (subject, new QuantityAccountConfiguration(
            dimension,
            Identifier(request.Unit),
            request.PrecisionScale,
            request.ConservationTolerance));
    }

    public static QuantityPostingPlan PlanPosting(
        PostQuantityEntryRequest? request,
        QuantityAccountConfiguration configuration,
        QuantityBalances current,
        QuantityEntrySnapshot? referencedEntry,
        QuantityEntrySnapshot? reservation)
    {
        if (request is null)
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        if (!string.Equals(request.RuleSetVersion, QuantityContract.RuleSetVersion, StringComparison.Ordinal))
            throw new QuantityDomainException(QuantityErrorCodes.ApplicabilityUnknown);

        var entryType = request.EntryType?.Trim() ?? string.Empty;
        var amount = ValidateAmount(request.Amount, configuration.PrecisionScale);
        var reason = OptionalText(request.Reason);
        var referencedEntryId = OptionalGuid(request.ReferencedEntryId);
        var reservationId = OptionalGuid(request.ReservationId);

        return entryType switch
        {
            QuantityEntryTypes.Receipt or QuantityEntryTypes.Output or QuantityEntryTypes.Return =>
                Increase(entryType, amount, current, referencedEntryId, reservationId, reason),
            QuantityEntryTypes.Reserve =>
                Reserve(amount, current, referencedEntryId, reservationId, reason),
            QuantityEntryTypes.ReserveRelease =>
                ReleaseReserve(amount, current, referencedEntryId, reservationId, reservation, reason),
            QuantityEntryTypes.Allocate or QuantityEntryTypes.Consume =>
                Decrease(entryType, amount, current, referencedEntryId, reservationId, reservation, reason),
            QuantityEntryTypes.Loss or QuantityEntryTypes.Dispose =>
                Decrease(entryType, amount, current, referencedEntryId, reservationId: null, reservation: null, reason,
                    forbidReservation: reservationId is not null),
            QuantityEntryTypes.Reversal =>
                Reverse(amount, current, referencedEntryId, reservationId, referencedEntry, reason),
            QuantityEntryTypes.Restate =>
                Restate(amount, current, referencedEntryId, reservationId, referencedEntry, reason),
            _ => throw new QuantityDomainException(QuantityErrorCodes.ApplicabilityUnknown)
        };
    }

    public static QuantityAvailabilityResult EvaluateAvailability(
        QuantityAvailabilityRequest request,
        QuantityAccountResult? current)
    {
        if (!string.Equals(request.RuleSetVersion, QuantityContract.RuleSetVersion, StringComparison.Ordinal))
            return Unknown(current, QuantityAvailabilityReasons.RuleSetVersionUnknown);
        if (current is null)
            return Blocked(null, QuantityAvailabilityReasons.AccountRequired);
        if (request.ExpectedAccountVersion != current.Version)
            return Unknown(current, QuantityAvailabilityReasons.AccountVersionMismatch);
        if (request.RequestedAmount <= 0 ||
            request.RequestedAmount >= MaximumAmount ||
            Scale(request.RequestedAmount) > current.PrecisionScale)
        {
            return Blocked(current, QuantityAvailabilityReasons.RequestInvalid);
        }

        if (current.Available < request.RequestedAmount)
            return Blocked(current, QuantityAvailabilityReasons.InsufficientAvailable);

        return new QuantityAvailabilityResult(
            QuantityAvailabilityDecisions.Allowed,
            [],
            current.QuantityAccountId,
            current.Version,
            current.Available,
            QuantityContract.RuleSetVersion);
    }

    public static string HashTarget(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }

    private static QuantityPostingPlan Increase(
        string entryType,
        decimal amount,
        QuantityBalances current,
        Guid? referencedEntryId,
        Guid? reservationId,
        string? reason)
    {
        if (referencedEntryId is not null || reservationId is not null)
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        var balance = current.Balance + amount;
        if (balance >= MaximumAmount)
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        return new QuantityPostingPlan(entryType, amount, balance, current.Reserved, null, null, reason);
    }

    private static QuantityPostingPlan Reserve(
        decimal amount,
        QuantityBalances current,
        Guid? referencedEntryId,
        Guid? reservationId,
        string? reason)
    {
        if (referencedEntryId is not null || reservationId is not null)
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        if (current.Available < amount)
            throw new QuantityDomainException(QuantityErrorCodes.InsufficientBalance);
        return new QuantityPostingPlan(
            QuantityEntryTypes.Reserve,
            amount,
            current.Balance,
            current.Reserved + amount,
            null,
            null,
            reason);
    }

    private static QuantityPostingPlan ReleaseReserve(
        decimal amount,
        QuantityBalances current,
        Guid? referencedEntryId,
        Guid? reservationId,
        QuantityEntrySnapshot? reservation,
        string? reason)
    {
        if (referencedEntryId is not null)
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        var hold = RequireOpenReservation(reservationId, reservation, amount);
        return new QuantityPostingPlan(
            QuantityEntryTypes.ReserveRelease,
            amount,
            current.Balance,
            current.Reserved - hold.Amount,
            null,
            hold.EntryId,
            reason);
    }

    private static QuantityPostingPlan Decrease(
        string entryType,
        decimal amount,
        QuantityBalances current,
        Guid? referencedEntryId,
        Guid? reservationId,
        QuantityEntrySnapshot? reservation,
        string? reason,
        bool forbidReservation = false)
    {
        if (referencedEntryId is not null || forbidReservation)
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);

        if (reservationId is null)
        {
            if (current.Available < amount)
                throw new QuantityDomainException(QuantityErrorCodes.InsufficientBalance);
            return new QuantityPostingPlan(entryType, amount, current.Balance - amount, current.Reserved, null, null, reason);
        }

        var hold = RequireOpenReservation(reservationId, reservation, amount);
        return new QuantityPostingPlan(
            entryType,
            amount,
            current.Balance - amount,
            current.Reserved - hold.Amount,
            null,
            hold.EntryId,
            reason);
    }

    private static QuantityPostingPlan Reverse(
        decimal amount,
        QuantityBalances current,
        Guid? referencedEntryId,
        Guid? reservationId,
        QuantityEntrySnapshot? referencedEntry,
        string? reason)
    {
        if (reservationId is not null || referencedEntryId is null || referencedEntry is null ||
            referencedEntry.EntryId != referencedEntryId)
        {
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        }

        if (referencedEntry.EntryType is QuantityEntryTypes.Reversal or QuantityEntryTypes.Restate ||
            referencedEntry.Reversed ||
            referencedEntry.ReservationId is not null ||
            amount != referencedEntry.Amount)
        {
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        }

        decimal balance;
        var reserved = current.Reserved;
        switch (referencedEntry.EntryType)
        {
            case QuantityEntryTypes.Receipt:
            case QuantityEntryTypes.Output:
            case QuantityEntryTypes.Return:
                balance = current.Balance - amount;
                if (balance - current.Reserved < 0)
                    throw new QuantityDomainException(QuantityErrorCodes.InsufficientBalance);
                break;
            case QuantityEntryTypes.Reserve:
                if (referencedEntry.ReservationClosed)
                    throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
                balance = current.Balance;
                reserved = current.Reserved - amount;
                if (reserved < 0)
                    throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
                break;
            case QuantityEntryTypes.Allocate:
            case QuantityEntryTypes.Consume:
            case QuantityEntryTypes.Loss:
            case QuantityEntryTypes.Dispose:
                balance = current.Balance + amount;
                if (balance >= MaximumAmount)
                    throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
                break;
            default:
                throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        }

        return new QuantityPostingPlan(
            QuantityEntryTypes.Reversal,
            amount,
            balance,
            reserved,
            referencedEntry.EntryId,
            null,
            reason);
    }

    private static QuantityPostingPlan Restate(
        decimal amount,
        QuantityBalances current,
        Guid? referencedEntryId,
        Guid? reservationId,
        QuantityEntrySnapshot? referencedReversal,
        string? reason)
    {
        if (reservationId is not null || referencedEntryId is null || referencedReversal is null ||
            referencedReversal.EntryId != referencedEntryId ||
            !string.Equals(referencedReversal.EntryType, QuantityEntryTypes.Reversal, StringComparison.Ordinal) ||
            referencedReversal.Restated)
        {
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        }

        decimal balance;
        switch (referencedReversal.OriginalEntryType)
        {
            case QuantityEntryTypes.Receipt:
            case QuantityEntryTypes.Output:
            case QuantityEntryTypes.Return:
                balance = current.Balance + amount;
                if (balance >= MaximumAmount)
                    throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
                break;
            case QuantityEntryTypes.Allocate:
            case QuantityEntryTypes.Consume:
            case QuantityEntryTypes.Loss:
            case QuantityEntryTypes.Dispose:
                if (current.Available < amount)
                    throw new QuantityDomainException(QuantityErrorCodes.InsufficientBalance);
                balance = current.Balance - amount;
                break;
            default:
                throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        }

        return new QuantityPostingPlan(
            QuantityEntryTypes.Restate,
            amount,
            balance,
            current.Reserved,
            referencedReversal.EntryId,
            null,
            reason);
    }

    private static QuantityEntrySnapshot RequireOpenReservation(
        Guid? reservationId,
        QuantityEntrySnapshot? reservation,
        decimal amount)
    {
        if (reservationId is null || reservation is null ||
            reservation.EntryId != reservationId ||
            !string.Equals(reservation.EntryType, QuantityEntryTypes.Reserve, StringComparison.Ordinal) ||
            reservation.Reversed ||
            reservation.ReservationClosed ||
            amount != reservation.Amount)
        {
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        }

        return reservation;
    }

    private static decimal ValidateAmount(decimal amount, int precisionScale)
    {
        if (amount <= 0 || amount >= MaximumAmount || Scale(amount) > precisionScale)
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        return amount;
    }

    private static QuantitySubjectReference Subject(QuantitySubjectReference? value)
    {
        if (value is null || value.Version < 1 || !KnownSubjectType(value.SubjectType))
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        return new QuantitySubjectReference(value.SubjectType.Trim(), Identifier(value.Id), value.Version);
    }

    private static bool KnownSubjectType(string? value) => value?.Trim() is
        QuantitySubjectTypes.ReceivedItem or QuantitySubjectTypes.DerivedSample or QuantitySubjectTypes.TestSpecimen;

    private static bool KnownDimension(string value) => value is
        QuantityDimensions.Count or QuantityDimensions.Mass or QuantityDimensions.Length or
        QuantityDimensions.Area or QuantityDimensions.Volume;

    private static int Scale(decimal value)
    {
        value = Math.Abs(value);
        var scale = 0;
        while (scale <= MaximumPrecisionScale + 1 && value != decimal.Truncate(value))
        {
            value *= 10m;
            scale++;
        }

        return scale;
    }

    private static Guid? OptionalGuid(string? value)
    {
        if (value is null)
            return null;
        var trimmed = value.Trim();
        return Guid.TryParseExact(trimmed, "N", out var id) || Guid.TryParse(trimmed, out id)
            ? id
            : throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
    }

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!StableIdentifier.IsMatch(trimmed))
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static string? OptionalText(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.Length is < 1 or > 500)
            throw new QuantityDomainException(QuantityErrorCodes.ValidationFailed);
        return trimmed;
    }

    private static QuantityAvailabilityResult Blocked(QuantityAccountResult? current, string reason) => new(
        QuantityAvailabilityDecisions.Blocked,
        [reason],
        current?.QuantityAccountId,
        current?.Version,
        current?.Available,
        QuantityContract.RuleSetVersion);

    private static QuantityAvailabilityResult Unknown(QuantityAccountResult? current, string reason) => new(
        QuantityAvailabilityDecisions.Unknown,
        [reason],
        current?.QuantityAccountId,
        current?.Version,
        null,
        QuantityContract.RuleSetVersion);
}
