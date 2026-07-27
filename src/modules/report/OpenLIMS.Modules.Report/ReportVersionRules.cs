using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenLIMS.Contracts.Report;

namespace OpenLIMS.Modules.Report;

/// <summary>
/// DEV-023 version-chain rules: canonical snapshot hashing, the three signing
/// requirements of SEC-SIGN-001, and the five controlled actions of OD-022.
/// </summary>
internal static class ReportVersionRules
{
    /// <summary>
    /// The bytes a signature is bound to (RPT-SIGN-001). Every field that can
    /// change what the report asserts is in here, ordered by line number so the
    /// same report always canonicalises identically — and any edit to a line
    /// changes the hash, which is exactly what makes SEC-SIGN-002 automatic.
    /// </summary>
    public static string Canonicalize(ReportResult report, int versionNumber)
    {
        var builder = new StringBuilder();
        builder.Append("report=").Append(report.ReportNumber).Append('\n');
        builder.Append("version=").Append(versionNumber.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("ruleSet=").Append(ReportContract.RuleSetVersion).Append('\n');
        builder.Append("scope=")
            .Append(report.ObjectScope.LegalEntityId).Append('|')
            .Append(report.ObjectScope.LaboratoryId).Append('|')
            .Append(report.ObjectScope.CustomerId).Append('|')
            .Append(report.ObjectScope.ServiceOrderId).Append('|')
            .Append(report.ObjectScope.ProductCategory).Append('\n');

        var latest = report.GateEvaluations.LastOrDefault();
        foreach (var line in report.Lines.OrderBy(line => line.LineNumber))
        {
            var verdict = latest?.AccreditationVerdicts
                .FirstOrDefault(entry => entry.LineNumber == line.LineNumber);
            builder.Append("line=").Append(line.LineNumber.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(line.AdoptionTargetId).Append('|')
                .Append(line.GroupVersion.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(line.ScopeLineId).Append('|')
                .Append(line.ScopePartition).Append('|')
                .Append(verdict?.Status ?? ReportAccreditationStatuses.Unknown).Append('|')
                .Append(line.TraceRefs.BatchId).Append('|')
                .Append(line.TraceRefs.AllocationId).Append('|')
                .Append(line.TraceRefs.ReceivedItemId).Append('|')
                .Append(line.TraceRefs.RequirementSnapshot.Id).Append('@')
                .Append(line.TraceRefs.RequirementSnapshot.Version.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(line.AccreditationRef.Id).Append('@')
                .Append(line.AccreditationRef.Version.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return builder.ToString();
    }

    public static string ComputeHash(string canonicalContent) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalContent)));

    /// <summary>
    /// SEC-SIGN-001: re-authentication evidence, an explicit signing intent and
    /// the caller's expected content hash must all be present before anything
    /// is signed.
    /// </summary>
    public static IssueReportRequest ValidateIssuance(IssueReportRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ReportContract.RuleSetVersion, StringComparison.Ordinal))
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }
        if (request.ReauthenticationRef is null || request.ReauthenticationRef.Version < 1 ||
            string.IsNullOrWhiteSpace(request.ReauthenticationRef.Id) ||
            string.IsNullOrWhiteSpace(request.SigningIntent) ||
            string.IsNullOrWhiteSpace(request.ExpectedContentHash) ||
            string.IsNullOrWhiteSpace(request.SignatoryId))
        {
            throw new ReportDomainException(ReportErrorCodes.SignatureRequirementsUnmet);
        }

        return request;
    }

    /// <summary>
    /// SEC-SIGN-002 falls out of the hash: if the signed content moved, the
    /// caller's expected hash no longer matches what the server recomputes.
    /// </summary>
    public static void RequireMatchingHash(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new ReportDomainException(ReportErrorCodes.ContentHashMismatch);
    }

    /// <summary>
    /// Issuance is only meaningful on top of a gate decision that ALLOWED and
    /// that actually covered every line the version is about to freeze.
    /// </summary>
    public static void RequireSatisfiedGate(ReportResult report)
    {
        var latest = report.GateEvaluations.LastOrDefault();
        if (latest is null ||
            !string.Equals(latest.Decision, ReportGateDecisions.Allowed, StringComparison.Ordinal) ||
            latest.AccreditationVerdicts.Count != report.Lines.Count)
        {
            throw new ReportDomainException(ReportErrorCodes.IssuanceGateNotSatisfied);
        }
    }

    public static PerformControlledActionRequest ValidateControlledAction(
        PerformControlledActionRequest? request)
    {
        if (request is null ||
            !string.Equals(request.RuleSetVersion, ReportContract.RuleSetVersion, StringComparison.Ordinal) ||
            request.VersionNumber < 1 ||
            !ReportControlledActionKinds.All.Contains(request.Kind, StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }

        var producesVersion = ReportControlledActionKinds.ProduceNewVersion
            .Contains(request.Kind, StringComparer.Ordinal);
        if (producesVersion)
        {
            // AC-RPT-002: a correction is preceded by a result-attribution
            // impact assessment, so its reference is not optional.
            if (request.ImpactAssessmentRef is null ||
                request.ImpactAssessmentRef.Version < 1 ||
                string.IsNullOrWhiteSpace(request.ImpactAssessmentRef.Id))
            {
                throw new ReportDomainException(ReportErrorCodes.ImpactAssessmentRequired);
            }
            if (request.SupersedingReportNumber is not null)
                throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }
        else if (request.ImpactAssessmentRef is not null)
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }

        var isSupersession = string.Equals(
            request.Kind, ReportControlledActionKinds.Supersession, StringComparison.Ordinal);
        // "Absent" has to mean here exactly what it means to persistence and to
        // the controlled_action CHECK, i.e. NULL. Treating whitespace as absent
        // let a blank value through the rule and into a 23514, which surfaces
        // as PERSISTENCE_UNAVAILABLE instead of a plain validation failure.
        var supersedingInvalid = isSupersession
            ? string.IsNullOrWhiteSpace(request.SupersedingReportNumber)
            : request.SupersedingReportNumber is not null;
        if (supersedingInvalid)
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);

        return request;
    }

    /// <summary>
    /// A controlled action only applies to a version that was actually issued,
    /// on a chain that has not been voided, and never twice.
    /// </summary>
    public static void RequireActionable(
        ReportVersionChainState chain,
        PerformControlledActionRequest request)
    {
        if (string.Equals(chain.ChainState, ReportChainStates.Voided, StringComparison.Ordinal))
            throw new ReportDomainException(ReportErrorCodes.VersionChainClosed);
        if (!chain.IssuedVersions.Contains(request.VersionNumber))
            throw new ReportDomainException(ReportErrorCodes.VersionNotIssued);

        var alreadyWithdrawn = chain.WithdrawnVersions.Contains(request.VersionNumber);
        var alreadySuperseded = chain.SupersededVersions.Contains(request.VersionNumber);
        var isSupersession = string.Equals(
            request.Kind, ReportControlledActionKinds.Supersession, StringComparison.Ordinal);
        if (string.Equals(request.Kind, ReportControlledActionKinds.Withdrawal, StringComparison.Ordinal) &&
            alreadyWithdrawn)
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }
        if (ReportControlledActionKinds.ProduceNewVersion.Contains(request.Kind, StringComparer.Ordinal) &&
            alreadySuperseded)
        {
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
        }
        // BUS-RPT-005: no controlled action repeats, supersession included. The
        // verification contract carries a single superseding report number, so
        // the chain accepts exactly one — and a duplicate would be a permanent,
        // uncorrectable row in an append-only evidence log.
        if (isSupersession && chain.SupersedingReportNumber is not null)
            throw new ReportDomainException(ReportErrorCodes.ValidationFailed);
    }

    public static string ResolveVersionState(
        int versionNumber,
        ReportVersionChainState chain) =>
        string.Equals(chain.ChainState, ReportChainStates.Voided, StringComparison.Ordinal)
            ? ReportVersionStates.Voided
            : chain.WithdrawnVersions.Contains(versionNumber)
                ? ReportVersionStates.Withdrawn
                : chain.SupersededVersions.Contains(versionNumber)
                    ? ReportVersionStates.Superseded
                    : ReportVersionStates.Issued;

    /// <summary>
    /// The current version is the highest issued one that is neither superseded
    /// nor withdrawn; a voided chain has none.
    /// </summary>
    public static int? ResolveCurrentVersion(ReportVersionChainState chain) =>
        string.Equals(chain.ChainState, ReportChainStates.Voided, StringComparison.Ordinal)
            ? null
            : chain.IssuedVersions
                .Where(version => !chain.SupersededVersions.Contains(version) &&
                                  !chain.WithdrawnVersions.Contains(version))
                .Select(version => (int?)version)
                .DefaultIfEmpty(null)
                .Max();

    public static ReportVersionChainResult EvaluateChain(
        ReportVersionChainRequest request,
        ReportVersionChainState? chain,
        string? contentHash)
    {
        if (!string.Equals(request.RuleSetVersion, ReportContract.RuleSetVersion, StringComparison.Ordinal))
            return Unknown(request, ReportVersionChainReasons.RuleSetVersionUnknown);
        if (chain is null || chain.IssuedVersions.Count == 0)
        {
            return new ReportVersionChainResult(
                ReportVersionChainDecisions.Blocked, [ReportVersionChainReasons.NoIssuedVersion],
                request.ReportId, null, chain?.ChainState ?? ReportChainStates.Active, null,
                ReportContract.RuleSetVersion);
        }

        var current = ResolveCurrentVersion(chain);
        if (string.Equals(chain.ChainState, ReportChainStates.Voided, StringComparison.Ordinal))
        {
            return new ReportVersionChainResult(
                ReportVersionChainDecisions.Blocked, [ReportVersionChainReasons.ChainVoided],
                request.ReportId, null, ReportChainStates.Voided, null, ReportContract.RuleSetVersion);
        }
        if (request.ExpectedVersionNumber != current)
        {
            return new ReportVersionChainResult(
                ReportVersionChainDecisions.Unknown, [ReportVersionChainReasons.VersionMismatch],
                request.ReportId, current, chain.ChainState, null, ReportContract.RuleSetVersion);
        }

        return new ReportVersionChainResult(
            ReportVersionChainDecisions.Allowed, [], request.ReportId, current,
            chain.ChainState, contentHash, ReportContract.RuleSetVersion);
    }

    private static ReportVersionChainResult Unknown(ReportVersionChainRequest request, string reason) => new(
        ReportVersionChainDecisions.Unknown, [reason], request.ReportId, null,
        ReportChainStates.Active, null, ReportContract.RuleSetVersion);
}

internal sealed record ReportVersionChainState(
    string ChainState,
    IReadOnlySet<int> IssuedVersions,
    IReadOnlySet<int> WithdrawnVersions,
    IReadOnlySet<int> SupersededVersions,
    string? SupersedingReportNumber);
