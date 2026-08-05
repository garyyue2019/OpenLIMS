namespace OpenLIMS.Contracts.Commercial;

public static class CommercialContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "COMMERCIAL@1.0.0";
    public const string CreateCatalogRecordPath = "/api/v1/catalog-records";
    public const string ReviseCatalogRecordPath = "/api/v1/catalog-records/{id}/versions";
    public const string GetCatalogRecordPath = "/api/v1/catalog-records/{id}/versions/{version:long}";
    public const string CreateInquiryPath = "/api/v1/inquiries";
    public const string GetInquiryPath = "/api/v1/inquiries/{id}";
    public const string ResolveInquiryGapPath = "/api/v1/inquiries/{id}/gaps/{gapId}/resolution";
    public const string RecordCapabilityReviewPath = "/api/v1/inquiries/{id}/capability-reviews";
    public const string CreateQuoteVersionPath = "/api/v1/inquiries/{id}/quote-versions";
    public const string RecordChangeImpactPath = "/api/v1/inquiries/{id}/change-impacts";
}

public static class CatalogRecordKinds
{
    public const string OrganizationUnit = "ORGANIZATION_UNIT";
    public const string Party = "PARTY";
    public const string PartyRole = "PARTY_ROLE";
    public const string Protocol = "PROTOCOL";
    public const string Requirement = "REQUIREMENT";
    public const string Method = "METHOD";
    public const string Accreditation = "ACCREDITATION";
    public const string Capability = "CAPABILITY";
}

public static class CatalogRecordStates
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";
    public const string Retired = "RETIRED";
}

public static class InquiryStates
{
    public const string GapsOpen = "GAPS_OPEN";
    public const string ReadyForReview = "READY_FOR_REVIEW";
    public const string ReviewBlocked = "REVIEW_BLOCKED";
    public const string Reviewed = "REVIEWED";
    public const string Quoted = "QUOTED";
    public const string ChangeReviewRequired = "CHANGE_REVIEW_REQUIRED";
}

public static class InquiryGapCodes
{
    public const string CustomerName = "CUSTOMER_NAME_REQUIRED";
    public const string ProductCategory = "PRODUCT_CATEGORY_REQUIRED";
    public const string Quantity = "QUANTITY_REQUIRED";
    public const string QuantityUnit = "QUANTITY_UNIT_REQUIRED";
    public const string TestPurpose = "TEST_PURPOSE_REQUIRED";
    public const string ExpectedTurnaround = "EXPECTED_TAT_REQUIRED";
    public const string SourceDocument = "SOURCE_DOCUMENT_REQUIRED";
}

public static class CapabilityReviewDecisions
{
    public const string Passed = "PASSED";
    public const string Blocked = "BLOCKED";
}

public static class CommercialChangeKinds
{
    public const string Scope = "SCOPE";
    public const string Quantity = "QUANTITY";
    public const string Method = "METHOD";
    public const string Price = "PRICE";
    public const string Turnaround = "TURNAROUND";
    public const string Other = "OTHER";
}

public static class CommercialCapabilities
{
    public const string Read = "commercial:read";
    public const string Write = "commercial:write";
}

public static class CommercialClaimTypes
{
    public const string Capability = "openlims_capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
    public const string Customer = "customer";
    public const string ServiceOrder = "service_order";
    public const string ProductCategory = "product_category";
}

public static class CommercialErrorCodes
{
    public const string ValidationFailed = "COM.VALIDATION_FAILED";
    public const string ExpectedVersionConflict = "COM.EXPECTED_VERSION_CONFLICT";
    public const string ObjectNotAccessible = "COM.OBJECT_NOT_ACCESSIBLE";
    public const string NotAuthorized = "COM.NOT_AUTHORIZED";
    public const string InquiryGapsOpen = "COM.INQUIRY_GAPS_OPEN";
    public const string CapabilityReviewRequired = "COM.CAPABILITY_REVIEW_REQUIRED";
    public const string CapabilityReviewBlocked = "COM.CAPABILITY_REVIEW_BLOCKED";
    public const string PersistenceUnavailable = "COM.PERSISTENCE_UNAVAILABLE";
}

public sealed record CommercialVersionedReference(string Id, long Version);

public sealed record CommercialObjectContext(
    string LegalEntityId,
    string LaboratoryId,
    string CustomerId,
    string ServiceOrderId,
    string ProductCategory);

public sealed record SubmitCatalogRecordRequest(
    long ExpectedCurrentVersion,
    string Kind,
    string Code,
    string DisplayName,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    string State,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<CommercialVersionedReference> References,
    CommercialObjectContext ObjectScope);

public sealed record CatalogRecordResult(
    string RecordId,
    long Version,
    string RuleSetVersion,
    string Kind,
    string Code,
    string DisplayName,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    string State,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<CommercialVersionedReference> References,
    CommercialObjectContext ObjectScope,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record InquiryDetails(
    string? CustomerName,
    string? ProductCategory,
    decimal? Quantity,
    string? QuantityUnit,
    string? TestPurpose,
    int? ExpectedTurnaroundDays,
    IReadOnlyList<CommercialVersionedReference> SourceDocuments);

public sealed record CreateInquiryRequest(
    InquiryDetails Details,
    CommercialObjectContext ObjectScope);

public sealed record InquiryGapResult(
    string GapId,
    string Code,
    string Field,
    string Message,
    string? Resolution,
    string? ResolvedBy,
    DateTimeOffset? ResolvedAt);

public sealed record ResolveInquiryGapRequest(
    long ExpectedCurrentVersion,
    string Value);

public sealed record CapabilityReviewInput(
    long ExpectedCurrentVersion,
    bool MethodCapabilityConfirmed,
    bool AccreditationConfirmed,
    bool PersonnelAndEquipmentConfirmed,
    bool SampleQuantityConfirmed,
    bool TurnaroundConfirmed,
    bool ConfidentialityConfirmed,
    IReadOnlyList<CommercialVersionedReference> Evidence,
    string Notes);

public sealed record CapabilityReviewResult(
    string ReviewId,
    string Decision,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<CommercialVersionedReference> Evidence,
    string Notes,
    string ReviewedBy,
    DateTimeOffset ReviewedAt);

public sealed record QuoteLineInput(
    string LineCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice);

public sealed record QuoteLineResult(
    string LineCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount);

public sealed record SubmitQuoteVersionRequest(
    long ExpectedInquiryVersion,
    long ExpectedQuoteVersion,
    CommercialVersionedReference ScopeMatrix,
    CommercialVersionedReference Currency,
    CommercialVersionedReference ContractReference,
    int PromisedTurnaroundDays,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<QuoteLineInput> Lines);

public sealed record QuoteVersionResult(
    string QuoteId,
    long Version,
    CommercialVersionedReference ScopeMatrix,
    CommercialVersionedReference Currency,
    CommercialVersionedReference ContractReference,
    int PromisedTurnaroundDays,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<QuoteLineResult> Lines,
    decimal TotalAmount,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record RecordChangeImpactRequest(
    long ExpectedInquiryVersion,
    string ChangeKind,
    string Reason);

public sealed record ChangeImpactResult(
    string ImpactId,
    string ChangeKind,
    string Reason,
    bool PriceAffected,
    bool TurnaroundAffected,
    bool SampleRequirementAffected,
    bool WorkInProgressAffected,
    bool ReportAffected,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record InquiryResult(
    string InquiryId,
    string InquiryNumber,
    long Version,
    string RuleSetVersion,
    string State,
    InquiryDetails Details,
    CommercialObjectContext ObjectScope,
    IReadOnlyList<InquiryGapResult> Gaps,
    IReadOnlyList<CapabilityReviewResult> CapabilityReviews,
    IReadOnlyList<QuoteVersionResult> QuoteVersions,
    IReadOnlyList<ChangeImpactResult> ChangeImpacts,
    string RecordedBy,
    DateTimeOffset RecordedAt);

public sealed record CommercialAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    CommercialObjectContext ObjectScope,
    string Capability);

public sealed record CommercialAuthorizationDecision(bool Allowed)
{
    public static CommercialAuthorizationDecision Permit { get; } = new(true);
    public static CommercialAuthorizationDecision Deny { get; } = new(false);
}

public interface ICommercialAuthorizationPort
{
    ValueTask<CommercialAuthorizationDecision> AuthorizeAsync(
        CommercialAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
