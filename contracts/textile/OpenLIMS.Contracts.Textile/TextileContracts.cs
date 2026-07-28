namespace OpenLIMS.Contracts.Textile;

public static class TextileContract
{
    public const string Version = "1.0.0";
    public const string RuleSetVersion = "TEXTILE-SAMPLE-REQUIREMENT@1.0.0";
}

public static class TextileDirections
{
    public const string Warp = "WARP";
    public const string Weft = "WEFT";
    public const string Lengthwise = "LENGTHWISE";
    public const string Crosswise = "CROSSWISE";
}

public static class TextileCalculationDecisions
{
    public const string Sufficient = "SUFFICIENT";
    public const string Insufficient = "INSUFFICIENT";
    public const string Unknown = "UNKNOWN";
}

public static class TextileCalculationReasons
{
    public const string RuleSetVersionUnknown = "RULE_SET_VERSION_UNKNOWN";
    public const string SampleInsufficient = "SAMPLE_INSUFFICIENT";
}

public static class TextileErrorCodes
{
    public const string ValidationFailed = "TEX.VALIDATION_FAILED";
    public const string DirectionUnknown = "TEX.DIRECTION_UNKNOWN";
    public const string ExclusiveShareRejected = "TEX.EXCLUSIVE_SHARE_REJECTED";
    public const string ApplicabilityUnknown = "TEX.APPLICABILITY_UNKNOWN";
    public const string SampleRequirementNotApprovable = "TEX.SAMPLE_REQUIREMENT_NOT_APPROVABLE";
    public const string ExpectedVersionConflict = "TEX.EXPECTED_VERSION_CONFLICT";
    public const string NotAuthorized = "TEX.NOT_AUTHORIZED";
    public const string ObjectNotAccessible = "TEX.OBJECT_NOT_ACCESSIBLE";
    public const string PersistenceUnavailable = "TEX.PERSISTENCE_UNAVAILABLE";
}

public sealed class TextileContractException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed record TextileVersionedReference(string Id, long Version);

public sealed record TextileDemandLine(
    TextileVersionedReference Style,
    TextileVersionedReference Colorway,
    TextileVersionedReference Component,
    TextileVersionedReference Material,
    string Position,
    string Direction,
    TextileVersionedReference TestItem,
    int ParallelCount,
    int RetestReserveCount,
    int RetentionReserveCount,
    bool Destructive,
    decimal SpecimenLengthMm,
    decimal SpecimenWidthMm,
    TextileVersionedReference? Preconditioning = null,
    string? ExclusiveDestructiveGroupId = null,
    string? ShareGroupId = null);

public sealed record TextileAvailableFabric(
    TextileVersionedReference Style,
    TextileVersionedReference Colorway,
    TextileVersionedReference Component,
    string Position,
    decimal AvailableAreaSquareMm);

public sealed record TextileSampleRequirementCalculation(
    string RuleSetVersion,
    IReadOnlyList<TextileDemandLine> DemandLines,
    IReadOnlyList<TextileAvailableFabric> AvailableFabrics);

public sealed record TextileSpecimenPlan(
    TextileVersionedReference Style,
    TextileVersionedReference Colorway,
    TextileVersionedReference Component,
    string Position,
    string Direction,
    TextileVersionedReference TestItem,
    int RequiredSpecimenCount,
    decimal AreaSquareMm,
    string? ShareGroupId);

public sealed record TextileGapContributor(string Direction, TextileVersionedReference TestItem);

public sealed record TextileSufficiencyGap(
    TextileVersionedReference Style,
    TextileVersionedReference Colorway,
    TextileVersionedReference Component,
    string Position,
    decimal RequiredAreaSquareMm,
    decimal AvailableAreaSquareMm,
    decimal GapAreaSquareMm,
    IReadOnlyList<TextileGapContributor> ContributingItems);

public sealed record TextileSampleRequirementResult(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<TextileSpecimenPlan> SpecimenPlans,
    IReadOnlyList<TextileSufficiencyGap> Gaps,
    string RuleSetVersion);

public sealed record TextileCuttingPlan(
    string CuttingPlanId,
    TextileVersionedReference SourceItem,
    string SamplingPosition,
    string Direction,
    decimal LengthMm,
    decimal WidthMm,
    int PlannedCount,
    decimal MinDistanceFromSelvedgeMm,
    string TemplateVersion,
    string OperatorId,
    IReadOnlyList<string> GeneratedSpecimenIds);

public interface ITextileSampleRequirementCalculator
{
    TextileSampleRequirementResult Calculate(TextileSampleRequirementCalculation calculation);
}

public static class TextileRuntimeContract
{
    public const string Version = "1.0.0";
    public const string SampleRequirementPath = "/api/v1/textile/sample-requirements";
    public const string CuttingPlanPath = "/api/v1/textile/cutting-plans";
    public const string CuttingPlanApprovalPath =
        "/api/v1/textile/cutting-plans/{id}/versions/{version:long}/approval";
    public const string CuttingPlanDetailPath =
        "/api/v1/textile/cutting-plans/{id}/versions/{version:long}";
}

public static class TextileCapabilities
{
    public const string SampleRequirementManage = "textile.sample-requirement.manage";
    public const string CuttingPlanApprove = "textile.cutting-plan.approve";
}

public static class TextileClaimTypes
{
    public const string Capability = "capability";
    public const string LegalEntity = "legal_entity";
    public const string Laboratory = "laboratory";
}

public static class TextileCuttingPlanStates
{
    public const string Draft = "DRAFT";
    public const string Approved = "APPROVED";
    public const string Superseded = "SUPERSEDED";
}

public static class TextileStatusDecisions
{
    public const string Allowed = "ALLOWED";
    public const string Blocked = "BLOCKED";
    public const string Unknown = "UNKNOWN";
}

public static class TextileStatusReasons
{
    public const string PlanApproved = "PLAN_APPROVED";
    public const string PlanNotApproved = "PLAN_NOT_APPROVED";
    public const string EvidenceUnknown = "EVIDENCE_UNKNOWN";
}

public sealed class TextileOperationException(string errorCode, Exception? innerException = null) :
    Exception(errorCode, innerException)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed record TextileObjectScope(string LegalEntityId, string LaboratoryId);

public sealed record CreateTextileSampleRequirementRequest(
    string RequirementId,
    long ExpectedCurrentVersion,
    TextileObjectScope ObjectScope,
    TextileSampleRequirementCalculation Calculation);

public sealed record TextileSampleRequirementRecord(
    string RequirementId,
    long Version,
    TextileObjectScope ObjectScope,
    TextileSampleRequirementCalculation Calculation,
    TextileSampleRequirementResult Result,
    string InputHash,
    string CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record CreateTextileCuttingPlanRequest(
    string CuttingPlanId,
    long ExpectedCurrentVersion,
    string SampleRequirementId,
    long SampleRequirementVersion,
    string SampleRequirementInputHash,
    string RuleSetVersion,
    TextileCuttingPlan Plan);

public sealed record ApproveTextileCuttingPlanRequest(
    long ExpectedCurrentVersion,
    string SampleRequirementInputHash,
    string RuleSetVersion,
    string? ApprovalComment = null);

public sealed record TextileCuttingPlanApproval(
    string CuttingPlanId,
    long CuttingPlanVersion,
    string SampleRequirementId,
    long SampleRequirementVersion,
    string SampleRequirementInputHash,
    string RuleSetVersion,
    string ApprovedBy,
    DateTimeOffset ApprovedAt,
    string? ApprovalComment);

public sealed record TextileCuttingPlanResult(
    string CuttingPlanId,
    long Version,
    TextileObjectScope ObjectScope,
    TextileSampleRequirementRecord SampleRequirement,
    TextileCuttingPlan Plan,
    string State,
    string InputHash,
    string RuleSetVersion,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    TextileCuttingPlanApproval? Approval);

public sealed record TextileCuttingPlanStatusRequest(
    string OrganizationGroupId,
    string CuttingPlanId,
    long Version,
    string RuleSetVersion);

public sealed record TextileCuttingPlanStatusDecision(
    string Decision,
    IReadOnlyList<string> ReasonCodes,
    string CuttingPlanId,
    long Version,
    string? SampleRequirementId,
    long? SampleRequirementVersion,
    string RuleSetVersion);

public sealed record TextileAuthorizationRequest(
    string OrganizationGroupId,
    string ActorId,
    TextileObjectScope ObjectScope,
    string Capability);

public sealed record TextileAuthorizationDecision(bool Allowed)
{
    public static TextileAuthorizationDecision Permit { get; } = new(true);
    public static TextileAuthorizationDecision Deny { get; } = new(false);
}

public interface ITextileAuthorizationPort
{
    ValueTask<TextileAuthorizationDecision> AuthorizeAsync(
        TextileAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITextileCuttingPlanStatusPort
{
    ValueTask<TextileCuttingPlanStatusDecision> EvaluateAsync(
        TextileCuttingPlanStatusRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITextileRuntimeService
{
    Task<TextileSampleRequirementRecord> CalculateSampleRequirementAsync(
        CreateTextileSampleRequirementRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<TextileCuttingPlanResult> CreateCuttingPlanAsync(
        CreateTextileCuttingPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<TextileCuttingPlanResult> ApproveCuttingPlanAsync(
        string cuttingPlanId,
        long version,
        ApproveTextileCuttingPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<TextileCuttingPlanResult> GetCuttingPlanAsync(
        string cuttingPlanId,
        long version,
        string correlationId,
        CancellationToken cancellationToken = default);
}
