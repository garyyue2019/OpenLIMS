using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Batch;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Result;

namespace OpenLIMS.Modules.Result;

public interface IResultGroupService
{
    Task<ResultGroupResult> CreateGroupAsync(CreateResultGroupRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ResultObservationResult> AddObservationAsync(string resultGroupId, AddResultObservationRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ResultDerivationResult> AddDerivationAsync(string resultGroupId, AddResultDerivationRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ResultCalculationResult> ExecuteCalculationAsync(string resultGroupId, ExecuteResultCalculationRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<AdoptionRuleResult> RecordAdoptionRuleAsync(string resultGroupId, RecordAdoptionRuleRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ResultAdoptionResult> AdoptAsync(string resultGroupId, AdoptResultRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ResultAccreditationAssessmentResult> RecordAccreditationAssessmentAsync(string resultGroupId, RecordResultAccreditationAssessmentRequest request, string correlationId, CancellationToken cancellationToken = default);
    Task<ResultGroupResult> GetAsync(string resultGroupId, string correlationId, CancellationToken cancellationToken = default);
}

internal sealed class ResultGroupService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IResultAuthorizationPort authorizationPort,
    IBatchStatusPort batchStatusPort,
    ITransactionCoordinator transactionCoordinator,
    ResultStore store,
    ResultAttemptAuditWriter attemptAuditWriter,
    ILogger<ResultGroupService> logger) : IResultGroupService
{
    public async Task<ResultGroupResult> CreateGroupAsync(
        CreateResultGroupRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var groupId = Guid.Parse(idGenerator.NewId());
        var (organizationGroupId, actorId) = await RequireActorAsync(groupId.ToString("N"), correlationId, cancellationToken);
        try
        {
            var validated = ResultRules.ValidateGroup(request);
            var gate = await EvaluateBatchGateAsync(organizationGroupId, validated, correlationId, cancellationToken);
            ResultGroupResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(organizationGroupId, actorId, validated.ObjectScope, transactionToken);
                result = await store.InsertGroupAsync(
                    groupId, organizationGroupId, validated,
                    gate.Decision, gate.RuleSetVersion,
                    actorId, clock.UtcNow, correlationId, transactionToken);
            }, cancellationToken);
            ResultTelemetry.RecordGroupCreated();
            return result ?? throw new InvalidOperationException("RES.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ResultDomainException or NpgsqlException)
        {
            throw await FailAsync("CreateResultGroup", actorId, organizationGroupId,
                groupId.ToString("N"), correlationId, exception, cancellationToken);
        }
    }

    public Task<ResultObservationResult> AddObservationAsync(
        string resultGroupId, AddResultObservationRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(resultGroupId, correlationId, "AddResultObservation", cancellationToken,
            (group, organizationGroupId, actorId, token) =>
            {
                var validated = ResultRules.ValidateObservation(request, group.AdoptionRules.Count > 0);
                ResultRules.RequireVersion(validated.ExpectedCurrentVersion, group.Version);
                return store.InsertObservationAsync(
                    Guid.ParseExact(group.ResultGroupId, "N"), group.Version + 1, organizationGroupId,
                    validated, actorId, clock.UtcNow, correlationId, token);
            },
            result => ResultTelemetry.RecordObservation(result.Kind));

    public Task<ResultDerivationResult> AddDerivationAsync(
        string resultGroupId, AddResultDerivationRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(resultGroupId, correlationId, "AddResultDerivation", cancellationToken,
            (group, organizationGroupId, actorId, token) =>
            {
                var existing = group.Observations.Select(observation => observation.ObservationId)
                    .Concat(group.Derivations.Select(derivation => derivation.DerivationId))
                    .Concat(group.Calculations.Select(calculation => calculation.CalculationId))
                    .ToHashSet(StringComparer.Ordinal);
                var validated = ResultRules.ValidateDerivation(request, existing);
                ResultRules.RequireVersion(validated.ExpectedCurrentVersion, group.Version);
                return store.InsertDerivationAsync(
                    Guid.ParseExact(group.ResultGroupId, "N"), group.Version + 1, organizationGroupId,
                    validated, actorId, clock.UtcNow, correlationId, token);
            },
            _ => { });

    public Task<ResultCalculationResult> ExecuteCalculationAsync(
        string resultGroupId, ExecuteResultCalculationRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(resultGroupId, correlationId, "ExecuteResultCalculation", cancellationToken,
            (group, organizationGroupId, actorId, token) =>
            {
                var execution = ResultRules.ExecuteCalculation(request, group);
                ResultRules.RequireVersion(request.ExpectedCurrentVersion, group.Version);
                return store.InsertCalculationAsync(
                    Guid.ParseExact(group.ResultGroupId, "N"), group.Version + 1, organizationGroupId,
                    execution, actorId, clock.UtcNow, correlationId, token);
            },
            result => ResultTelemetry.RecordCalculation(result.Qualification, result.LimitDecision));

    public Task<AdoptionRuleResult> RecordAdoptionRuleAsync(
        string resultGroupId, RecordAdoptionRuleRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(resultGroupId, correlationId, "RecordAdoptionRule", cancellationToken,
            (group, organizationGroupId, actorId, token) =>
            {
                var validated = ResultRules.ValidateAdoptionRule(request);
                ResultRules.RequireVersion(validated.ExpectedCurrentVersion, group.Version);
                return store.InsertAdoptionRuleAsync(
                    Guid.ParseExact(group.ResultGroupId, "N"), group.Version + 1,
                    group.AdoptionRules.Count + 1, organizationGroupId,
                    validated, actorId, clock.UtcNow, correlationId, token);
            },
            _ => { });

    public Task<ResultAdoptionResult> AdoptAsync(
        string resultGroupId, AdoptResultRequest request, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(resultGroupId, correlationId, "AdoptResult", cancellationToken,
            (group, organizationGroupId, actorId, token) =>
            {
                var validated = ResultRules.ValidateAdoption(request);
                ResultRules.RequireVersion(validated.ExpectedCurrentVersion, group.Version);
                var rule = group.AdoptionRules.OrderByDescending(candidate => candidate.RuleVersion).FirstOrDefault()
                    ?? throw new ResultDomainException(ResultErrorCodes.AdoptionRuleRequired);
                ResultRules.RequireStrategyCompliance(validated, rule, group);
                return store.InsertAdoptionAsync(
                    Guid.ParseExact(group.ResultGroupId, "N"), group.Version + 1,
                    group.Adoptions.Count + 1, rule.RuleVersion, organizationGroupId,
                    validated, actorId, clock.UtcNow, correlationId, token);
            },
            result => ResultTelemetry.RecordAdoption(result.AdoptionVersion));

    public Task<ResultAccreditationAssessmentResult> RecordAccreditationAssessmentAsync(
        string resultGroupId,
        RecordResultAccreditationAssessmentRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(resultGroupId, correlationId, "RecordResultAccreditationAssessment", cancellationToken,
            (group, organizationGroupId, actorId, token) =>
            {
                var now = clock.UtcNow;
                var evaluation = ResultRules.EvaluateAccreditationAssessment(request, group, actorId, now);
                ResultRules.RequireVersion(evaluation.Request.ExpectedCurrentVersion, group.Version);
                return store.InsertAccreditationAssessmentAsync(
                    Guid.ParseExact(group.ResultGroupId, "N"), group.Version + 1, organizationGroupId,
                    evaluation, actorId, now, correlationId, token);
            },
            result => ResultTelemetry.RecordAccreditation(result.Stage, result.Decision));

    public async Task<ResultGroupResult> GetAsync(
        string resultGroupId, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(resultGroupId, correlationId, cancellationToken);
        try
        {
            var id = ParseGroupId(resultGroupId);
            ResultGroupResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadGroupAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ResultDomainException(ResultErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, result.ObjectScope, transactionToken);
                await store.WriteReadAuditAsync(
                    result.ResultGroupId, result.Version, organizationGroupId, actorId,
                    "READ_RESULT_GROUP", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("RES.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is ResultDomainException or NpgsqlException)
        {
            throw await FailAsync("GetResultGroup", actorId, organizationGroupId,
                resultGroupId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<T> MutateAsync<T>(
        string resultGroupId,
        string correlationId,
        string commandType,
        CancellationToken cancellationToken,
        Func<ResultGroupResult, string, string, CancellationToken, Task<T>> mutate,
        Action<T> record)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(resultGroupId, correlationId, cancellationToken);
        try
        {
            var id = ParseGroupId(resultGroupId);
            T? result = default;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireGroupLockAsync(id, transactionToken);
                var group = await store.LoadGroupAsync(organizationGroupId, id, transactionToken)
                    ?? throw new ResultDomainException(ResultErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, group.ObjectScope, transactionToken);
                result = await mutate(group, organizationGroupId, actorId, transactionToken);
            }, cancellationToken);
            if (result is null)
                throw new InvalidOperationException("RES.RESULT_MISSING");
            record(result);
            return result;
        }
        catch (Exception exception) when (exception is ResultDomainException or NpgsqlException)
        {
            throw await FailAsync(commandType, actorId, organizationGroupId,
                resultGroupId, correlationId, exception, cancellationToken);
        }
    }

    private async Task<BatchStatusResult> EvaluateBatchGateAsync(
        string organizationGroupId,
        CreateResultGroupRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        BatchStatusResult result;
        try
        {
            result = await batchStatusPort.EvaluateAsync(new BatchStatusRequest(
                organizationGroupId, request.BatchId, request.ExpectedBatchVersion,
                BatchContract.RuleSetVersion)
            {
                CorrelationId = correlationId
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ResultTelemetry.RecordGate("UNKNOWN");
            throw new ResultDomainException(ResultErrorCodes.ApplicabilityUnknown, "BATCH");
        }

        ResultTelemetry.RecordGate(result.Decision);
        return result.Decision switch
        {
            BatchStatusDecisions.Allowed => result,
            BatchStatusDecisions.Blocked => throw new ResultDomainException(ResultErrorCodes.EligibilityBlocked, "BATCH"),
            _ => throw new ResultDomainException(ResultErrorCodes.ApplicabilityUnknown, "BATCH")
        };
    }

    private async Task<(string OrganizationGroupId, string ActorId)> RequireActorAsync(
        string? target, string correlationId, CancellationToken cancellationToken)
    {
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        if (actor is not null &&
            string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            return (organizationGroupId, actor.ActorId);
        }

        await WriteAttemptOrFailClosedAsync("ResultCommand", actor?.ActorId, organizationGroupId,
            target, correlationId, ResultErrorCodes.NotAuthorized, cancellationToken);
        throw new ResultDomainException(ResultErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId, string actorId, ResultObjectContext objectScope, CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new ResultAuthorizationRequest(
            organizationGroupId, actorId, objectScope, ResultCapabilities.Record), cancellationToken);
        if (!decision.Allowed)
            throw new ResultDomainException(ResultErrorCodes.NotAuthorized);
    }

    private async Task<ResultDomainException> FailAsync(
        string commandType, string actorId, string organizationGroupId,
        string? target, string correlationId, Exception exception, CancellationToken cancellationToken)
    {
        var (code, gateSource) = exception switch
        {
            ResultDomainException domain => (domain.ErrorCode, domain.GateSource),
            PostgresException { SqlState: "23505" } => (ResultErrorCodes.ValidationFailed, null),
            _ => (ResultErrorCodes.PersistenceUnavailable, (string?)null)
        };
        ResultTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Result command {CommandType} failed closed with {ErrorCode} (gate {GateSource}); correlation {CorrelationId}",
            commandType, code, gateSource ?? "-", correlationId);
        await WriteAttemptOrFailClosedAsync(commandType, actorId, organizationGroupId,
            target, correlationId, code, cancellationToken);
        return new ResultDomainException(code, gateSource);
    }

    private async Task WriteAttemptOrFailClosedAsync(
        string commandType, string? actorId, string organizationGroupId,
        string? target, string correlationId, string code, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(commandType, actorId, organizationGroupId,
                ResultRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new ResultDomainException(ResultErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseGroupId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new ResultDomainException(ResultErrorCodes.ObjectNotAccessible);
}

internal sealed class ResultAdoptionPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IResultAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ResultStore store,
    ResultAttemptAuditWriter attemptAuditWriter,
    ILogger<ResultAdoptionPort> logger) : IResultAdoptionPort
{
    public async ValueTask<ResultAdoptionStatusResult> EvaluateAsync(
        ResultAdoptionStatusRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;
        if (actor is null ||
            !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal) ||
            !string.Equals(request.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor?.ActorId, organizationGroupId, request.ResultGroupId, correlationId, cancellationToken);
            throw new ResultDomainException(ResultErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.ResultGroupId, "N", out var groupId) &&
            !Guid.TryParse(request.ResultGroupId, out groupId))
        {
            return Record(ResultRules.EvaluateStatus(request, null));
        }

        try
        {
            ResultAdoptionStatusResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var group = await store.LoadGroupAsync(organizationGroupId, groupId, transactionToken);
                if (group is null)
                {
                    result = ResultRules.EvaluateStatus(request, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new ResultAuthorizationRequest(
                    organizationGroupId, actor.ActorId, group.ObjectScope, ResultCapabilities.Record), transactionToken);
                if (!authorization.Allowed)
                    throw new ResultDomainException(ResultErrorCodes.NotAuthorized);

                result = ResultRules.EvaluateStatus(request, group);
                await store.WriteReadAuditAsync(
                    group.ResultGroupId, group.Version, organizationGroupId, actor.ActorId,
                    "EVALUATE_RESULT_ADOPTION", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return Record(result ?? ResultRules.EvaluateStatus(request, null));
        }
        catch (ResultDomainException exception)
            when (string.Equals(exception.ErrorCode, ResultErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor.ActorId, organizationGroupId, request.ResultGroupId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Result adoption status failed closed because persistence is unavailable");
            return Record(new ResultAdoptionStatusResult(
                ResultAdoptionDecisions.Unknown,
                [ResultAdoptionReasons.ResultUnavailable],
                request.ResultGroupId, null, null, null, ResultContract.RuleSetVersion));
        }
    }

    private ResultAdoptionStatusResult Record(ResultAdoptionStatusResult result)
    {
        ResultTelemetry.RecordGate(result.Decision);
        if (string.Equals(result.Decision, ResultAdoptionDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Result adoption status failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId, string organizationGroupId, string target, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync("EvaluateResultAdoption", actorId, organizationGroupId,
                ResultRules.HashTarget(target), correlationId, ResultErrorCodes.NotAuthorized,
                clock.UtcNow, cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new ResultDomainException(ResultErrorCodes.PersistenceUnavailable);
        }
    }
}

internal sealed class ResultAccreditationEligibilityPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IResultAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ResultStore store,
    ResultAttemptAuditWriter attemptAuditWriter,
    ILogger<ResultAccreditationEligibilityPort> logger) : IResultAccreditationEligibilityPort
{
    public async ValueTask<ResultAccreditationEligibilityResult> EvaluateAsync(
        ResultAccreditationEligibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;
        if (actor is null ||
            !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal) ||
            !string.Equals(request.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor?.ActorId, organizationGroupId, request.ResultGroupId, correlationId, cancellationToken);
            throw new ResultDomainException(ResultErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.ResultGroupId, "N", out var groupId) &&
            !Guid.TryParse(request.ResultGroupId, out groupId))
        {
            return Record(ResultRules.EvaluateAccreditationEligibility(request, null, actor.ActorId, clock.UtcNow));
        }

        try
        {
            ResultAccreditationEligibilityResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var group = await store.LoadGroupAsync(organizationGroupId, groupId, transactionToken);
                if (group is not null)
                {
                    var authorization = await authorizationPort.AuthorizeAsync(new ResultAuthorizationRequest(
                        organizationGroupId, actor.ActorId, group.ObjectScope, ResultCapabilities.Record), transactionToken);
                    if (!authorization.Allowed)
                        throw new ResultDomainException(ResultErrorCodes.NotAuthorized);
                    await store.WriteReadAuditAsync(
                        group.ResultGroupId, group.Version, organizationGroupId, actor.ActorId,
                        "EVALUATE_RESULT_ACCREDITATION", correlationId, clock.UtcNow, transactionToken);
                }
                result = ResultRules.EvaluateAccreditationEligibility(request, group, actor.ActorId, clock.UtcNow);
            }, cancellationToken);
            return Record(result ?? ResultRules.EvaluateAccreditationEligibility(
                request, null, actor.ActorId, clock.UtcNow));
        }
        catch (ResultDomainException exception)
            when (string.Equals(exception.ErrorCode, ResultErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor.ActorId, organizationGroupId, request.ResultGroupId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Result accreditation eligibility failed closed because persistence is unavailable");
            return Record(new ResultAccreditationEligibilityResult(
                ResultAccreditationDecisions.Unknown,
                [ResultAccreditationEligibilityReasons.EvidenceUnavailable],
                request.ResultGroupId,
                null,
                null,
                null,
                null,
                ResultContract.AccreditationRuleSetVersion));
        }
    }

    private ResultAccreditationEligibilityResult Record(ResultAccreditationEligibilityResult result)
    {
        ResultTelemetry.RecordGate(result.Decision);
        if (string.Equals(result.Decision, ResultAccreditationDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Result accreditation eligibility failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId,
        string organizationGroupId,
        string target,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                "EvaluateResultAccreditation",
                actorId,
                organizationGroupId,
                ResultRules.HashTarget(target),
                correlationId,
                ResultErrorCodes.NotAuthorized,
                clock.UtcNow,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new ResultDomainException(ResultErrorCodes.PersistenceUnavailable);
        }
    }
}

internal sealed class ResultConclusionEvidencePort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IResultAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    ResultStore store,
    ResultAttemptAuditWriter attemptAuditWriter,
    ILogger<ResultConclusionEvidencePort> logger) : IResultConclusionEvidencePort
{
    public async ValueTask<ResultConclusionEvidenceResult> EvaluateAsync(
        ResultConclusionEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationGroupId = organizationContext.Current.OrganizationGroupId;
        var actor = actorContext.Current;
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;
        if (actor is null ||
            !string.Equals(actor.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal) ||
            !string.Equals(request.OrganizationGroupId, organizationGroupId, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(
                actor?.ActorId,
                organizationGroupId,
                request.ResultGroupId,
                correlationId,
                cancellationToken);
            throw new ResultDomainException(ResultErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.ResultGroupId, "N", out var groupId) &&
            !Guid.TryParse(request.ResultGroupId, out groupId))
        {
            return Record(ResultRules.EvaluateConclusionEvidence(request, null));
        }

        try
        {
            ResultConclusionEvidenceResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var group = await store.LoadGroupAsync(organizationGroupId, groupId, transactionToken);
                if (group is not null)
                {
                    var authorization = await authorizationPort.AuthorizeAsync(
                        new ResultAuthorizationRequest(
                            organizationGroupId,
                            actor.ActorId,
                            group.ObjectScope,
                            ResultCapabilities.Record),
                        transactionToken);
                    if (!authorization.Allowed)
                        throw new ResultDomainException(ResultErrorCodes.NotAuthorized);

                    await store.WriteReadAuditAsync(
                        group.ResultGroupId,
                        group.Version,
                        organizationGroupId,
                        actor.ActorId,
                        "EVALUATE_RESULT_CONCLUSION_EVIDENCE",
                        correlationId,
                        clock.UtcNow,
                        transactionToken);
                }

                result = ResultRules.EvaluateConclusionEvidence(request, group);
            }, cancellationToken);
            return Record(result ?? ResultRules.EvaluateConclusionEvidence(request, null));
        }
        catch (ResultDomainException exception)
            when (string.Equals(exception.ErrorCode, ResultErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(
                actor.ActorId,
                organizationGroupId,
                request.ResultGroupId,
                correlationId,
                cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning(
                "Result conclusion evidence failed closed because persistence is unavailable");
            return Record(new ResultConclusionEvidenceResult(
                ResultConclusionEvidenceDecisions.Unknown,
                [ResultConclusionEvidenceReasons.EvidenceUnavailable],
                request.ResultGroupId,
                null,
                request.AdoptionVersion,
                null,
                null,
                null,
                null,
                ResultContract.RuleSetVersion));
        }
    }

    private ResultConclusionEvidenceResult Record(ResultConclusionEvidenceResult result)
    {
        ResultTelemetry.RecordGate(result.Decision);
        if (string.Equals(
            result.Decision,
            ResultConclusionEvidenceDecisions.Unknown,
            StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Result conclusion evidence failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId,
        string organizationGroupId,
        string target,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(
                "EvaluateResultConclusionEvidence",
                actorId,
                organizationGroupId,
                ResultRules.HashTarget(target),
                correlationId,
                ResultErrorCodes.NotAuthorized,
                clock.UtcNow,
                cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new ResultDomainException(ResultErrorCodes.PersistenceUnavailable);
        }
    }
}
