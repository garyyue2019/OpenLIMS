using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

public sealed class ReportDomainException(string errorCode, string? gateSource = null) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
    public string? GateSource { get; } = gateSource;
}

internal static partial class ReportRules
{
    private const int MaximumLineNumber = 10_000;
    private static readonly Regex StableIdentifier = StableIdentifierPattern();
    private static readonly Regex Sha256Hex = Sha256Pattern();

    public static CreateReportRequest ValidateReport(CreateReportRequest? request)
    {
        if (request is null || request.ObjectScope is null)
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        if (!string.Equals(request.RuleSetVersion, ReportContract.RuleSetVersion, StringComparison.Ordinal))
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);

        return request with
        {
            ObjectScope = new ReportObjectContext(
                Identifier(request.ObjectScope.LegalEntityId),
                Identifier(request.ObjectScope.LaboratoryId),
                Identifier(request.ObjectScope.CustomerId),
                Identifier(request.ObjectScope.ServiceOrderId),
                Identifier(request.ObjectScope.ProductCategory)),
            ReportNumber = Identifier(request.ReportNumber)
        };
    }

    public static AddReportLineRequest ValidateLine(
        AddReportLineRequest? request,
        IReadOnlySet<int> usedLineNumbers,
        IReadOnlySet<string> usedAttributions)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ReportContract.RuleSetVersion, StringComparison.Ordinal) ||
            request.LineNumber is < 1 or > MaximumLineNumber ||
            request.TraceRefs is null || request.AccreditationRef is null || request.AccreditationClaim is null)
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }
        if (!ReportScopePartitions.All.Contains(request.ScopePartition, StringComparer.Ordinal))
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        if (usedLineNumbers.Contains(request.LineNumber))
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        if (request.ExpectedGroupVersion < 1 ||
            request.ExpectedInstrumentFileVersion < 1 || request.ExpectedReceivedItemVersion < 1 ||
            request.ExpectedScopeMatrixVersion < 1 || request.ExpectedAllocationVersion < 1 ||
            request.ExpectedBatchVersion < 1)
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }
        if (!Sha256Hex.IsMatch(request.AccreditationRef.Sha256) || request.AccreditationRef.Version < 1)
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);

        // BUS-RPT-002: the gate must ask every QC run that touches this
        // line's target, so the line has to cite all of them — at least one,
        // each distinct, each version-pinned.
        if (request.QcRuns is null || request.QcRuns.Count == 0)
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        var citedRuns = new HashSet<string>(StringComparer.Ordinal);
        var qcRuns = new List<ReportVersionedReference>();
        foreach (var run in request.QcRuns)
        {
            var normalized = Reference(run);
            if (!citedRuns.Add(normalized.Id))
                throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
            qcRuns.Add(normalized);
        }

        // RULE-005: one scope line may contribute one adopted target once, so a
        // repeat is duplicate attribution rather than a second legitimate line.
        var attribution = AttributionKey(request.ScopeLineId, request.ResultGroupId);
        if (usedAttributions.Contains(attribution))
            throw new ReportDomainException(ReportErrorCodes.DuplicateAttribution);

        var claim = request.AccreditationClaim;
        return request with
        {
            ResultGroupId = Identifier(request.ResultGroupId),
            ScopeLineId = Identifier(request.ScopeLineId),
            ScopeMatrixId = Identifier(request.ScopeMatrixId),
            QcRuns = qcRuns,
            InstrumentFileId = Identifier(request.InstrumentFileId),
            TraceRefs = new ReportTraceReferences(
                Identifier(request.TraceRefs.BatchId),
                Identifier(request.TraceRefs.AllocationId),
                Identifier(request.TraceRefs.ReceivedItemId),
                Reference(request.TraceRefs.RequirementSnapshot)),
            AccreditationRef = new AccreditationScopeReference(
                Identifier(request.AccreditationRef.Id), request.AccreditationRef.Version, request.AccreditationRef.Sha256),
            AccreditationClaim = new AccreditationClaim(
                Identifier(claim.SiteId),
                Reference(claim.Method),
                Identifier(claim.ProductMatrix),
                Identifier(claim.ParameterRange),
                claim.ValidUntil,
                Identifier(claim.SignatoryId)),
            SubcontractingDisclosure = request.SubcontractingDisclosure is null
                ? null
                : Reference(request.SubcontractingDisclosure)
        };
    }

    public static string AttributionKey(string scopeLineId, string resultGroupId) =>
        $"{scopeLineId}|{resultGroupId}";

    /// <summary>
    /// OD-029@1.0.0 / AC-ACC-001: accreditation is computed per line over six
    /// dimensions. Anything unproven — a missing reference, a stale scope
    /// version, an expired validity, an unauthorised signatory — fails the
    /// dimension. An organisation-level flag is not an input here at all, by
    /// construction.
    /// </summary>
    public static ReportLineAccreditationVerdict EvaluateAccreditation(
        ReportLineResult line,
        AccreditationScopeSnapshot? scope,
        SignatoryAuthorityOutcome signatory,
        DateTimeOffset now)
    {
        if (!line.ClaimsAccreditation)
            return new ReportLineAccreditationVerdict(line.LineNumber, ReportAccreditationStatuses.NotAccredited, []);
        if (scope is null)
        {
            return new ReportLineAccreditationVerdict(
                line.LineNumber, ReportAccreditationStatuses.NotAccredited, [.. ReportAccreditationDimensions.All]);
        }

        var failed = new List<string>();
        var claim = line.AccreditationClaim;
        if (!string.Equals(scope.SiteId, claim.SiteId, StringComparison.Ordinal))
            failed.Add(ReportAccreditationDimensions.Site);
        if (!string.Equals(scope.Method.Id, claim.Method.Id, StringComparison.Ordinal) ||
            scope.Method.Version != claim.Method.Version)
        {
            failed.Add(ReportAccreditationDimensions.MethodVersion);
        }
        if (!string.Equals(scope.ProductMatrix, claim.ProductMatrix, StringComparison.Ordinal))
            failed.Add(ReportAccreditationDimensions.ProductMatrix);
        if (!string.Equals(scope.ParameterRange, claim.ParameterRange, StringComparison.Ordinal))
            failed.Add(ReportAccreditationDimensions.ParameterRange);
        if (scope.ValidUntil < now || claim.ValidUntil < now || scope.ValidUntil != claim.ValidUntil)
            failed.Add(ReportAccreditationDimensions.Validity);
        if (!signatory.Authorized || !scope.AuthorizedSignatories.Contains(claim.SignatoryId, StringComparer.Ordinal))
            failed.Add(ReportAccreditationDimensions.Signatory);

        return new ReportLineAccreditationVerdict(
            line.LineNumber,
            failed.Count == 0 ? ReportAccreditationStatuses.Accredited : ReportAccreditationStatuses.NotAccredited,
            failed);
    }

    public static ReportBlocker AccreditationBlocker(ReportLineResult line, ReportLineAccreditationVerdict verdict)
    {
        var reason = verdict.FailedDimensions.Count == ReportAccreditationDimensions.All.Count
            ? ReportBlockerReasons.AccreditationReferenceMissing
            : verdict.FailedDimensions.Contains(ReportAccreditationDimensions.Validity)
                ? ReportBlockerReasons.AccreditationExpired
                : ReportBlockerReasons.AccreditationOutOfScope;
        var nextStep = verdict.FailedDimensions.Contains(ReportAccreditationDimensions.Signatory)
            ? ReportNextSteps.AssignAuthorizedSignatory
            : ReportNextSteps.UpdateAccreditationReference;
        return new ReportBlocker(
            line.AccreditationRef.Id, "AccreditationScope", ReportGateSources.Accreditation,
            ReportContract.RuleSetVersion, reason, [nextStep], line.LineNumber);
    }

    /// <summary>
    /// A source port answered. ALLOWED passes; BLOCKED and UNKNOWN both become
    /// blockers — UNKNOWN is never read as permission.
    /// </summary>
    public static ReportBlocker? SourceBlocker(
        string source,
        string objectRef,
        string objectType,
        string decision,
        string ruleSetVersion,
        string nextStep,
        int lineNumber)
    {
        if (string.Equals(decision, ReportGateDecisions.Allowed, StringComparison.Ordinal))
            return null;
        var reason = string.Equals(decision, ReportGateDecisions.Blocked, StringComparison.Ordinal)
            ? ReportBlockerReasons.SourceBlocked
            : ReportBlockerReasons.SourceUnknown;
        var steps = string.Equals(reason, ReportBlockerReasons.SourceUnknown, StringComparison.Ordinal)
            ? new[] { nextStep, ReportNextSteps.RetryWhenSourceAvailable }
            : [nextStep];
        return new ReportBlocker(
            objectRef, objectType, source, ruleSetVersion, reason, steps, lineNumber);
    }

    /// <summary>
    /// AC-TRACE-001: every required contribution-chain reference must be
    /// present, and the line must name which link is missing rather than
    /// failing anonymously.
    /// </summary>
    public static ReportBlocker? TraceBlocker(ReportLineResult line)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(line.AdoptionTargetId)) missing.Add("adoptionTargetId");
        if (string.IsNullOrWhiteSpace(line.TraceRefs.BatchId)) missing.Add("batchId");
        if (string.IsNullOrWhiteSpace(line.TraceRefs.AllocationId)) missing.Add("allocationId");
        if (string.IsNullOrWhiteSpace(line.TraceRefs.ReceivedItemId)) missing.Add("receivedItemId");
        if (string.IsNullOrWhiteSpace(line.TraceRefs.RequirementSnapshot?.Id)) missing.Add("requirementSnapshot");
        if (string.IsNullOrWhiteSpace(line.ScopeLineId)) missing.Add("scopeLineId");
        if (missing.Count == 0)
            return null;
        return new ReportBlocker(
            string.Join(',', missing), "ReportLine", ReportGateSources.Traceability,
            ReportContract.RuleSetVersion, ReportBlockerReasons.TraceIncomplete,
            [ReportNextSteps.CompleteTraceReferences], line.LineNumber);
    }

    /// <summary>
    /// AC-TRACE-001 requires an EVALUATED line to trace its ConformityDecision.
    /// That object depends on OD-034, which is still open, so the gate blocks
    /// instead of assuming a conclusion (PRD pending-decision rule).
    /// </summary>
    public static ReportBlocker? ConformityBlocker(ReportLineResult line) =>
        string.Equals(line.ScopePartition, ReportScopePartitions.LaboratoryConclusion, StringComparison.Ordinal)
            ? new ReportBlocker(
                line.ScopeLineId, "ScopeLine", ReportGateSources.ConformityDecision,
                ReportContract.RuleSetVersion, ReportBlockerReasons.ConformityDecisionUnavailable,
                [ReportNextSteps.AwaitConformityDecisionCapability], line.LineNumber)
            : null;

    public static string ResolveDecision(IReadOnlyList<ReportBlocker> blockers)
    {
        if (blockers.Count == 0)
            return ReportGateDecisions.Allowed;
        return blockers.Any(blocker =>
            string.Equals(blocker.ReasonCode, ReportBlockerReasons.SourceUnknown, StringComparison.Ordinal))
            ? ReportGateDecisions.Unknown
            : ReportGateDecisions.Blocked;
    }

    public static ReportIssuanceGateResult EvaluateIssuanceGate(
        ReportIssuanceGateRequest request,
        ReportResult? report)
    {
        if (!string.Equals(request.RuleSetVersion, ReportContract.RuleSetVersion, StringComparison.Ordinal))
            return Unknown(request, report, ReportBlockerReasons.RuleSetVersionUnknown);
        if (report is null)
            return Blocked(request, null, ReportBlockerReasons.ReportRequired);
        if (request.ExpectedReportVersion != report.Version)
            return Unknown(request, report, ReportBlockerReasons.VersionMismatch);
        if (report.Lines.Count == 0)
            return Blocked(request, report, ReportBlockerReasons.LinesRequired);

        var latest = report.GateEvaluations.LastOrDefault();
        if (latest is null)
            return Blocked(request, report, ReportBlockerReasons.GateEvaluationRequired);

        // Every evaluation records exactly one accreditation verdict per line
        // that existed when it ran, and lines are append-only. So a shorter
        // verdict list means a line was appended after this decision was
        // pinned and was never put to any source port — the same freshness
        // rule the submit path enforces. Comparing versions cannot express it
        // here, because a legitimate submission also advances the report one
        // version past its evaluation.
        if (latest.AccreditationVerdicts.Count != report.Lines.Count)
            return Blocked(request, report, ReportBlockerReasons.GateEvaluationRequired);

        return new ReportIssuanceGateResult(
            latest.Decision,
            [.. latest.Blockers.Select(blocker => blocker.ReasonCode).Distinct(StringComparer.Ordinal)],
            report.ReportId,
            report.Version,
            latest.Blockers,
            latest.AccreditationVerdicts,
            ReportContract.RuleSetVersion);
    }

    public static string HashTarget(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ReportIssuanceGateResult Blocked(
        ReportIssuanceGateRequest request, ReportResult? report, string reason) => new(
        ReportGateDecisions.Blocked, [reason], report?.ReportId ?? request.ReportId,
        report?.Version, [], [], ReportContract.RuleSetVersion);

    private static ReportIssuanceGateResult Unknown(
        ReportIssuanceGateRequest request, ReportResult? report, string reason) => new(
        ReportGateDecisions.Unknown, [reason], report?.ReportId ?? request.ReportId,
        report?.Version, [], [], ReportContract.RuleSetVersion);

    private static ReportVersionedReference Reference(ReportVersionedReference? value)
    {
        if (value is null || value.Version < 1)
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        return new ReportVersionedReference(Identifier(value.Id), value.Version);
    }

    private static string Identifier(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!StableIdentifier.IsMatch(trimmed))
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        return trimmed;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierPattern();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

/// <summary>
/// The accreditation-scope facts a caller pins for one line, read back from the
/// controlled versioned reference. LIMS never stores the certificate body.
/// </summary>
internal sealed record AccreditationScopeSnapshot(
    string SiteId,
    ReportVersionedReference Method,
    string ProductMatrix,
    string ParameterRange,
    DateTimeOffset ValidUntil,
    IReadOnlyList<string> AuthorizedSignatories);

internal sealed record SignatoryAuthorityOutcome(bool Authorized, IReadOnlyList<string> ReasonCodes);
