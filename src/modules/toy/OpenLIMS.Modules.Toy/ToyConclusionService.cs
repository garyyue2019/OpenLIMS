using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Toy;

namespace OpenLIMS.Modules.Toy;

internal sealed class ToyConclusionService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IToyAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ToyConclusionStore store,
    ToyAttemptAuditWriter attemptAuditWriter,
    ILogger<ToyConclusionService> logger) : IToyConclusionService
{
    public async Task<ToyConclusionResult> CreateItemConformityConclusionAsync(
        CreateItemConformityConclusionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var organizationGroupId = organizationContext.GetOrganizationGroupId();
        var actorId = actorContext.GetActorId();

        // OD-034: ITEM_CONFORMITY requires toy.conclusion.approve-item capability (technical director)
        var authRequest = new ToyAuthorizationRequest(
            organizationGroupId,
            actorId,
            new ToyObjectContext("", ""), // Conclusion is not scoped to specific legal entity/lab
            ToyCapabilities.ConclusionApproveItem);

        var authDecision = await authorizationPort.AuthorizeAsync(authRequest, cancellationToken);
        if (!authDecision.Allowed)
        {
            await attemptAuditWriter.WriteAsync(
                "CreateItemConformityConclusion",
                null,
                correlationId,
                ToyErrorCodes.NotAuthorized,
                cancellationToken);
            throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
        }

        var draft = ToyConclusionDomain.ValidateItemConformityRequest(request);

        // OD-034: Validate SoD - approver must not be recorder of any adoptedResult
        var resultRecorders = await store.GetResultRecordersAsync(
            [draft.AdoptedResultRef],
            cancellationToken);
        ToyConclusionDomain.ValidateSeparationOfDuty(actorId, resultRecorders);

        var conclusionId = Guid.NewGuid().ToString("N");
        var transactionToken = await transactionCoordinator.BeginAsync(cancellationToken);

        try
        {
            await store.InsertItemConformityConclusionAsync(
                conclusionId,
                draft,
                organizationGroupId,
                actorId,
                clock.UtcNow,
                correlationId,
                transactionToken);

            await transactionCoordinator.CommitAsync(transactionToken, cancellationToken);

            ToyTelemetry.RecordConclusion(ToyConclusionLevels.ItemConformity);

            return new ToyConclusionResult(
                conclusionId,
                ToyConclusionLevels.ItemConformity,
                draft.Statement,
                actorId,
                clock.UtcNow,
                1,
                null,
                null,
                null,
                null);
        }
        catch (Exception exception)
        {
            await transactionCoordinator.RollbackAsync(transactionToken, cancellationToken);
            var errorCode = MapException(exception);
            await attemptAuditWriter.WriteAsync(
                "CreateItemConformityConclusion",
                conclusionId,
                correlationId,
                errorCode,
                cancellationToken);
            throw;
        }
    }

    public async Task<ToyConclusionResult> CreateTestedScopeConformityConclusionAsync(
        CreateTestedScopeConformityConclusionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var organizationGroupId = organizationContext.GetOrganizationGroupId();
        var actorId = actorContext.GetActorId();

        // OD-034: TESTED_SCOPE_CONFORMITY requires toy.conclusion.approve-scope capability (authorized signatory)
        // Plus SEC-SIGN-001 re-authentication signature (not implemented in this phase)
        var authRequest = new ToyAuthorizationRequest(
            organizationGroupId,
            actorId,
            new ToyObjectContext("", ""),
            ToyCapabilities.ConclusionApproveScope);

        var authDecision = await authorizationPort.AuthorizeAsync(authRequest, cancellationToken);
        if (!authDecision.Allowed)
        {
            await attemptAuditWriter.WriteAsync(
                "CreateTestedScopeConformityConclusion",
                null,
                correlationId,
                ToyErrorCodes.NotAuthorized,
                cancellationToken);
            throw new ToyDomainException(ToyErrorCodes.NotAuthorized);
        }

        var draft = ToyConclusionDomain.ValidateTestedScopeConformityRequest(request);

        // OD-034: Validate SoD - approver must not be recorder of any adoptedResult
        var adoptedResultRefs = draft.TestUnits
            .Select(tu => tu.AdoptedResultRef)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var resultRecorders = await store.GetResultRecordersAsync(adoptedResultRefs, cancellationToken);
        ToyConclusionDomain.ValidateSeparationOfDuty(actorId, resultRecorders);

        var conclusionId = Guid.NewGuid().ToString("N");
        var transactionToken = await transactionCoordinator.BeginAsync(cancellationToken);

        try
        {
            // TODO: SEC-SIGN-001 re-authentication signature verification should be here
            // For now, we proceed without signature verification

            await store.InsertTestedScopeConformityConclusionAsync(
                conclusionId,
                draft,
                organizationGroupId,
                actorId,
                clock.UtcNow,
                correlationId,
                transactionToken);

            await transactionCoordinator.CommitAsync(transactionToken, cancellationToken);

            ToyTelemetry.RecordConclusion(ToyConclusionLevels.TestedScopeConformity);

            return new ToyConclusionResult(
                conclusionId,
                ToyConclusionLevels.TestedScopeConformity,
                draft.Statement,
                actorId,
                clock.UtcNow,
                1,
                null, // TODO: signatureRef from SEC-SIGN-001
                draft.CoveredHazardDomains,
                draft.UncoveredScopes,
                draft.ExternalReferences);
        }
        catch (Exception exception)
        {
            await transactionCoordinator.RollbackAsync(transactionToken, cancellationToken);
            var errorCode = MapException(exception);
            await attemptAuditWriter.WriteAsync(
                "CreateTestedScopeConformityConclusion",
                conclusionId,
                correlationId,
                errorCode,
                cancellationToken);
            throw;
        }
    }

    public async Task<ToyConclusionResult> GetConclusionAsync(
        string conclusionId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var organizationGroupId = organizationContext.GetOrganizationGroupId();

        var conclusion = await store.GetConclusionAsync(
            conclusionId,
            organizationGroupId,
            cancellationToken)
            ?? throw new ToyDomainException(ToyErrorCodes.ObjectNotAccessible);

        return conclusion;
    }

    public async Task<IReadOnlyList<ToyConclusionResult>> GetConclusionsByProductAsync(
        string productRef,
        long productVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var organizationGroupId = organizationContext.GetOrganizationGroupId();

        var conclusions = await store.GetConclusionsByProductAsync(
            productRef,
            productVersion,
            organizationGroupId,
            cancellationToken);

        return conclusions;
    }

    private static string MapException(Exception exception) => exception switch
    {
        ToyDomainException domain => domain.ErrorCode,
        PostgresException { SqlState: "23505" } => ToyErrorCodes.ExpectedVersionConflict,
        PostgresException { SqlState: "23514" } => ToyErrorCodes.ValidationFailed,
        _ => ToyErrorCodes.PersistenceUnavailable
    };
}
