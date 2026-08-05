using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Commercial;

namespace OpenLIMS.Modules.Commercial;

internal sealed class CommercialDomainException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

internal static partial class CommercialRules
{
    private static readonly Regex StableIdentifier = StableIdentifierPattern();
    private static readonly HashSet<string> KnownCatalogKinds =
    [
        CatalogRecordKinds.OrganizationUnit,
        CatalogRecordKinds.Party,
        CatalogRecordKinds.PartyRole,
        CatalogRecordKinds.Protocol,
        CatalogRecordKinds.Requirement,
        CatalogRecordKinds.Method,
        CatalogRecordKinds.Accreditation,
        CatalogRecordKinds.Capability
    ];

    public static CommercialObjectContext NormalizeScope(CommercialObjectContext? value)
    {
        if (value is null)
            throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
        return new CommercialObjectContext(
            Identifier(value.LegalEntityId),
            Identifier(value.LaboratoryId),
            Identifier(value.CustomerId),
            Identifier(value.ServiceOrderId),
            Identifier(value.ProductCategory));
    }

    public static CatalogRecordResult CreateCatalog(
        string recordId,
        SubmitCatalogRecordRequest? request,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null || request.ExpectedCurrentVersion != 0)
            throw new CommercialDomainException(CommercialErrorCodes.ExpectedVersionConflict);
        return Catalog(recordId, 1, request, actorId, now);
    }

    public static CatalogRecordResult ReviseCatalog(
        CatalogRecordResult current,
        SubmitCatalogRecordRequest? request,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null || request.ExpectedCurrentVersion != current.Version)
            throw new CommercialDomainException(CommercialErrorCodes.ExpectedVersionConflict);
        var revised = Catalog(current.RecordId, current.Version + 1, request, actorId, now);
        if (!string.Equals(revised.Kind, current.Kind, StringComparison.Ordinal) ||
            !string.Equals(revised.Code, current.Code, StringComparison.Ordinal) ||
            revised.ObjectScope != current.ObjectScope)
        {
            throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
        }
        return revised;
    }

    public static InquiryResult CreateInquiry(
        string inquiryId,
        CreateInquiryRequest? request,
        string actorId,
        DateTimeOffset now)
    {
        if (request?.Details is null)
            throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
        var scope = NormalizeScope(request.ObjectScope);
        var details = NormalizeDetails(request.Details);
        var gaps = BuildGaps(details);
        return new InquiryResult(
            inquiryId,
            $"INQ-{inquiryId[..12].ToUpperInvariant()}",
            1,
            CommercialContract.RuleSetVersion,
            gaps.Count == 0 ? InquiryStates.ReadyForReview : InquiryStates.GapsOpen,
            details,
            scope,
            gaps,
            [],
            [],
            [],
            actorId,
            now);
    }

    public static InquiryResult ResolveGap(
        InquiryResult current,
        string gapId,
        ResolveInquiryGapRequest? request,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null || request.ExpectedCurrentVersion != current.Version)
            throw new CommercialDomainException(CommercialErrorCodes.ExpectedVersionConflict);
        var normalizedGapId = Identifier(gapId);
        var gap = current.Gaps.SingleOrDefault(candidate =>
            string.Equals(candidate.GapId, normalizedGapId, StringComparison.Ordinal) &&
            candidate.Resolution is null)
            ?? throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
        var value = Text(request.Value, 500);
        var details = ApplyResolution(current.Details, gap.Code, value);
        var unresolved = BuildGaps(details);
        var resolved = current.Gaps
            .Where(candidate => candidate.Resolution is not null)
            .Append(gap with { Resolution = value, ResolvedBy = actorId, ResolvedAt = now })
            .Concat(unresolved)
            .OrderBy(candidate => candidate.Code, StringComparer.Ordinal)
            .ToArray();
        return current with
        {
            Version = current.Version + 1,
            State = unresolved.Count == 0 ? InquiryStates.ReadyForReview : InquiryStates.GapsOpen,
            Details = details,
            Gaps = resolved,
            RecordedBy = actorId,
            RecordedAt = now
        };
    }

    public static InquiryResult RecordReview(
        InquiryResult current,
        CapabilityReviewInput? input,
        string reviewId,
        string actorId,
        DateTimeOffset now)
    {
        if (input is null || input.ExpectedCurrentVersion != current.Version)
            throw new CommercialDomainException(CommercialErrorCodes.ExpectedVersionConflict);
        if (current.Gaps.Any(gap => gap.Resolution is null))
            throw new CommercialDomainException(CommercialErrorCodes.InquiryGapsOpen);
        var evidence = References(input.Evidence, allowEmpty: false);
        var notes = Text(input.Notes, 2000);
        var reasons = new List<string>();
        if (!input.MethodCapabilityConfirmed) reasons.Add("METHOD_CAPABILITY_UNCONFIRMED");
        if (!input.AccreditationConfirmed) reasons.Add("ACCREDITATION_UNCONFIRMED");
        if (!input.PersonnelAndEquipmentConfirmed) reasons.Add("RESOURCES_UNCONFIRMED");
        if (!input.SampleQuantityConfirmed) reasons.Add("SAMPLE_QUANTITY_UNCONFIRMED");
        if (!input.TurnaroundConfirmed) reasons.Add("TURNAROUND_UNCONFIRMED");
        if (!input.ConfidentialityConfirmed) reasons.Add("CONFIDENTIALITY_UNCONFIRMED");
        var review = new CapabilityReviewResult(
            reviewId,
            reasons.Count == 0 ? CapabilityReviewDecisions.Passed : CapabilityReviewDecisions.Blocked,
            reasons,
            evidence,
            notes,
            actorId,
            now);
        return current with
        {
            Version = current.Version + 1,
            State = reasons.Count == 0 ? InquiryStates.Reviewed : InquiryStates.ReviewBlocked,
            CapabilityReviews = current.CapabilityReviews.Append(review).ToArray(),
            RecordedBy = actorId,
            RecordedAt = now
        };
    }

    public static InquiryResult AddQuote(
        InquiryResult current,
        SubmitQuoteVersionRequest? request,
        string quoteId,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null || request.ExpectedInquiryVersion != current.Version)
            throw new CommercialDomainException(CommercialErrorCodes.ExpectedVersionConflict);
        if (current.Gaps.Any(gap => gap.Resolution is null))
            throw new CommercialDomainException(CommercialErrorCodes.InquiryGapsOpen);
        var review = current.CapabilityReviews.LastOrDefault()
            ?? throw new CommercialDomainException(CommercialErrorCodes.CapabilityReviewRequired);
        if (!string.Equals(review.Decision, CapabilityReviewDecisions.Passed, StringComparison.Ordinal))
            throw new CommercialDomainException(CommercialErrorCodes.CapabilityReviewBlocked);
        var currentQuoteVersion = current.QuoteVersions.LastOrDefault()?.Version ?? 0;
        if (request.ExpectedQuoteVersion != currentQuoteVersion || request.PromisedTurnaroundDays is < 1 or > 3650 ||
            request.Lines is null || request.Lines.Count is < 1 or > 500)
        {
            throw new CommercialDomainException(request.ExpectedQuoteVersion != currentQuoteVersion
                ? CommercialErrorCodes.ExpectedVersionConflict
                : CommercialErrorCodes.ValidationFailed);
        }

        var lines = request.Lines.Select(line =>
        {
            if (line is null || line.Quantity <= 0 || line.UnitPrice < 0)
                throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
            var quantity = decimal.Round(line.Quantity, 4, MidpointRounding.ToEven);
            var unitPrice = decimal.Round(line.UnitPrice, 4, MidpointRounding.ToEven);
            return new QuoteLineResult(
                Identifier(line.LineCode),
                Text(line.Description, 500),
                quantity,
                unitPrice,
                decimal.Round(quantity * unitPrice, 4, MidpointRounding.ToEven));
        }).ToArray();
        if (lines.Select(line => line.LineCode).Distinct(StringComparer.Ordinal).Count() != lines.Length)
            throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);

        var exclusions = (request.Exclusions ?? []).Select(value => Text(value, 500)).Distinct(StringComparer.Ordinal).ToArray();
        var quote = new QuoteVersionResult(
            quoteId,
            currentQuoteVersion + 1,
            Reference(request.ScopeMatrix),
            Reference(request.Currency),
            Reference(request.ContractReference),
            request.PromisedTurnaroundDays,
            exclusions,
            lines,
            decimal.Round(lines.Sum(line => line.Amount), 4, MidpointRounding.ToEven),
            actorId,
            now);
        return current with
        {
            Version = current.Version + 1,
            State = InquiryStates.Quoted,
            QuoteVersions = current.QuoteVersions.Append(quote).ToArray(),
            RecordedBy = actorId,
            RecordedAt = now
        };
    }

    public static InquiryResult AddChangeImpact(
        InquiryResult current,
        RecordChangeImpactRequest? request,
        string impactId,
        string actorId,
        DateTimeOffset now)
    {
        if (request is null || request.ExpectedInquiryVersion != current.Version)
            throw new CommercialDomainException(CommercialErrorCodes.ExpectedVersionConflict);
        var kind = Identifier(request.ChangeKind).ToUpperInvariant();
        if (kind is not (CommercialChangeKinds.Scope or CommercialChangeKinds.Quantity or
            CommercialChangeKinds.Method or CommercialChangeKinds.Price or
            CommercialChangeKinds.Turnaround or CommercialChangeKinds.Other))
        {
            throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
        }
        var impact = kind switch
        {
            CommercialChangeKinds.Scope => Impact(true, true, true, true, true),
            CommercialChangeKinds.Quantity => Impact(true, false, true, true, false),
            CommercialChangeKinds.Method => Impact(true, true, true, true, true),
            CommercialChangeKinds.Price => Impact(true, false, false, false, false),
            CommercialChangeKinds.Turnaround => Impact(false, true, false, true, false),
            _ => Impact(true, true, true, true, true)
        };
        var result = new ChangeImpactResult(
            impactId,
            kind,
            Text(request.Reason, 2000),
            impact.Price,
            impact.Turnaround,
            impact.Sample,
            impact.Work,
            impact.Report,
            actorId,
            now);
        return current with
        {
            Version = current.Version + 1,
            State = InquiryStates.ChangeReviewRequired,
            ChangeImpacts = current.ChangeImpacts.Append(result).ToArray(),
            RecordedBy = actorId,
            RecordedAt = now
        };
    }

    public static string HashTarget(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static CatalogRecordResult Catalog(
        string recordId,
        long version,
        SubmitCatalogRecordRequest request,
        string actorId,
        DateTimeOffset now)
    {
        var kind = Identifier(request.Kind).ToUpperInvariant();
        if (!KnownCatalogKinds.Contains(kind) ||
            request.ValidTo is not null && request.ValidTo < request.ValidFrom ||
            request.State is not (CatalogRecordStates.Active or CatalogRecordStates.Inactive or CatalogRecordStates.Retired))
        {
            throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
        }
        var attributes = (request.Attributes ?? new Dictionary<string, string>())
            .ToDictionary(pair => Identifier(pair.Key), pair => Text(pair.Value, 2000), StringComparer.Ordinal);
        if (attributes.Count > 200)
            throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
        return new CatalogRecordResult(
            recordId,
            version,
            CommercialContract.RuleSetVersion,
            kind,
            Identifier(request.Code),
            Text(request.DisplayName, 500),
            request.ValidFrom,
            request.ValidTo,
            request.State,
            attributes,
            References(request.References, allowEmpty: true),
            NormalizeScope(request.ObjectScope),
            actorId,
            now);
    }

    private static InquiryDetails NormalizeDetails(InquiryDetails input) => new(
        OptionalText(input.CustomerName, 500),
        OptionalIdentifier(input.ProductCategory),
        input.Quantity is > 0 ? decimal.Round(input.Quantity.Value, 4, MidpointRounding.ToEven) : null,
        OptionalIdentifier(input.QuantityUnit),
        OptionalText(input.TestPurpose, 2000),
        input.ExpectedTurnaroundDays is > 0 and <= 3650 ? input.ExpectedTurnaroundDays : null,
        References(input.SourceDocuments, allowEmpty: true));

    private static IReadOnlyList<InquiryGapResult> BuildGaps(InquiryDetails details)
    {
        var gaps = new List<InquiryGapResult>();
        Add(string.IsNullOrWhiteSpace(details.CustomerName), InquiryGapCodes.CustomerName, "customerName", "Customer name is required");
        Add(string.IsNullOrWhiteSpace(details.ProductCategory), InquiryGapCodes.ProductCategory, "productCategory", "Product category is required");
        Add(details.Quantity is null, InquiryGapCodes.Quantity, "quantity", "Positive quantity is required");
        Add(string.IsNullOrWhiteSpace(details.QuantityUnit), InquiryGapCodes.QuantityUnit, "quantityUnit", "Quantity unit is required");
        Add(string.IsNullOrWhiteSpace(details.TestPurpose), InquiryGapCodes.TestPurpose, "testPurpose", "Test purpose is required");
        Add(details.ExpectedTurnaroundDays is null, InquiryGapCodes.ExpectedTurnaround, "expectedTurnaroundDays", "Expected turnaround is required");
        Add(details.SourceDocuments.Count == 0, InquiryGapCodes.SourceDocument, "sourceDocuments", "At least one source document is required");
        return gaps;

        void Add(bool missing, string code, string field, string message)
        {
            if (missing)
                gaps.Add(new InquiryGapResult(code, code, field, message, null, null, null));
        }
    }

    private static InquiryDetails ApplyResolution(InquiryDetails current, string code, string value) => code switch
    {
        InquiryGapCodes.CustomerName => current with { CustomerName = value },
        InquiryGapCodes.ProductCategory => current with { ProductCategory = Identifier(value) },
        InquiryGapCodes.Quantity => decimal.TryParse(value, out var quantity) && quantity > 0
            ? current with { Quantity = decimal.Round(quantity, 4, MidpointRounding.ToEven) }
            : throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed),
        InquiryGapCodes.QuantityUnit => current with { QuantityUnit = Identifier(value) },
        InquiryGapCodes.TestPurpose => current with { TestPurpose = value },
        InquiryGapCodes.ExpectedTurnaround => int.TryParse(value, out var days) && days is > 0 and <= 3650
            ? current with { ExpectedTurnaroundDays = days }
            : throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed),
        InquiryGapCodes.SourceDocument => current with
        {
            SourceDocuments = [ParseReference(value)]
        },
        _ => throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed)
    };

    private static CommercialVersionedReference ParseReference(string value)
    {
        var separator = value.LastIndexOf('@');
        return separator > 0 && long.TryParse(value[(separator + 1)..], out var version) && version > 0
            ? new CommercialVersionedReference(Identifier(value[..separator]), version)
            : throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
    }

    private static IReadOnlyList<CommercialVersionedReference> References(
        IReadOnlyList<CommercialVersionedReference>? values,
        bool allowEmpty)
    {
        if (values is null || !allowEmpty && values.Count == 0 || values.Count > 500)
            throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
        var normalized = values.Select(Reference).Distinct().ToArray();
        if (normalized.Length != values.Count)
            throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
        return normalized;
    }

    private static CommercialVersionedReference Reference(CommercialVersionedReference? value) =>
        value is not null && value.Version > 0
            ? new CommercialVersionedReference(Identifier(value.Id), value.Version)
            : throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);

    private static string Identifier(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return StableIdentifier.IsMatch(normalized)
            ? normalized
            : throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
    }

    private static string? OptionalIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Identifier(value);

    private static string Text(string? value, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 && normalized.Length <= maximumLength
            ? normalized
            : throw new CommercialDomainException(CommercialErrorCodes.ValidationFailed);
    }

    private static string? OptionalText(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Text(value, maximumLength);

    private static (bool Price, bool Turnaround, bool Sample, bool Work, bool Report) Impact(
        bool price,
        bool turnaround,
        bool sample,
        bool work,
        bool report) => (price, turnaround, sample, work, report);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierPattern();
}
