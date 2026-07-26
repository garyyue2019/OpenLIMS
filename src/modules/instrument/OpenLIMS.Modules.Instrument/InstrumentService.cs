using Microsoft.Extensions.Logging;
using Npgsql;
using OpenLIMS.Contracts.Instrument;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Instrument;

public interface IInstrumentImportService
{
    Task<InstrumentFileResult> RegisterFileAsync(
        RegisterInstrumentFileRequest request, string correlationId, CancellationToken cancellationToken = default);

    Task<InstrumentFileResult> SubmitRowsAsync(
        string fileRegistrationId, SubmitInstrumentRowsRequest request, string correlationId, CancellationToken cancellationToken = default);

    Task<InstrumentFileResult> ResolveExceptionAsync(
        string fileRegistrationId, string exceptionId, ResolveImportExceptionRequest request, string correlationId, CancellationToken cancellationToken = default);

    Task<InstrumentFileResult> GetAsync(
        string fileRegistrationId, string correlationId, CancellationToken cancellationToken = default);
}

internal sealed class InstrumentImportService(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IIdGenerator idGenerator,
    IInstrumentAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    InstrumentStore store,
    InstrumentAttemptAuditWriter attemptAuditWriter,
    ILogger<InstrumentImportService> logger) : IInstrumentImportService
{
    public async Task<InstrumentFileResult> RegisterFileAsync(
        RegisterInstrumentFileRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var fileId = Guid.Parse(idGenerator.NewId());
        var (organizationGroupId, actorId) = await RequireActorAsync(fileId.ToString("N"), correlationId, cancellationToken);
        try
        {
            var validated = InstrumentRules.ValidateRegistration(request);
            InstrumentFileResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await AuthorizeAsync(organizationGroupId, actorId, validated.ObjectScope, transactionToken);
                if (await store.DuplicateHashExistsAsync(organizationGroupId, validated.Sha256, transactionToken))
                    throw new InstrumentDomainException(InstrumentErrorCodes.DuplicateFile);
                await store.InsertRegistrationAsync(
                    fileId, organizationGroupId, validated, actorId, clock.UtcNow, correlationId, transactionToken);
                result = await store.LoadFileAsync(organizationGroupId, fileId, transactionToken);
            }, cancellationToken);
            InstrumentTelemetry.RecordRegistration(validated.SourceSystem);
            return result ?? throw new InvalidOperationException("INS.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is InstrumentDomainException or NpgsqlException)
        {
            throw await FailAsync("RegisterInstrumentFile", actorId, organizationGroupId,
                fileId.ToString("N"), correlationId, exception, cancellationToken);
        }
    }

    public async Task<InstrumentFileResult> SubmitRowsAsync(
        string fileRegistrationId, SubmitInstrumentRowsRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(fileRegistrationId, correlationId, cancellationToken);
        try
        {
            var fileId = ParseId(fileRegistrationId);
            InstrumentFileResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireFileLockAsync(fileId, transactionToken);
                var file = await store.LoadFileAsync(organizationGroupId, fileId, transactionToken)
                    ?? throw new InstrumentDomainException(InstrumentErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, file.ObjectScope, transactionToken);
                if (request is null || request.ExpectedCurrentVersion != file.Version)
                    throw new InstrumentDomainException(InstrumentErrorCodes.ExpectedVersionConflict);
                if (string.Equals(file.State, InstrumentFileStates.Completed, StringComparison.Ordinal))
                    throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);

                var factRowNumbers = new HashSet<int>(file.Rows.Select(row => row.RowNumber));
                var queuedRowNumbers = new HashSet<int>(file.Exceptions.Select(entry => entry.RowNumber));
                var (validRows, exceptionRows) = InstrumentRules.ClassifyRows(
                    request, factRowNumbers, queuedRowNumbers);
                if (file.Rows.Count + file.Exceptions.Count + validRows.Count + exceptionRows.Count > file.DeclaredRowCount)
                    throw new InstrumentDomainException(InstrumentErrorCodes.ValidationFailed);

                var version = file.Version;
                foreach (var row in validRows)
                {
                    version++;
                    await store.InsertParsedRowAsync(
                        fileId, version, row, file.ParserVersion, actorId, clock.UtcNow, correlationId, transactionToken);
                }
                foreach (var (row, reasonCode) in exceptionRows)
                {
                    version++;
                    await store.InsertExceptionAsync(
                        fileId, version, row, reasonCode, actorId, clock.UtcNow, correlationId, transactionToken);
                }

                await store.WriteRowsSubmittedEvidenceAsync(
                    fileId, organizationGroupId, actorId, validRows.Count, exceptionRows.Count,
                    correlationId, clock.UtcNow, transactionToken);
                result = await store.LoadFileAsync(organizationGroupId, fileId, transactionToken);
                InstrumentTelemetry.RecordRows(validRows.Count, exceptionRows.Count);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("INS.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is InstrumentDomainException or NpgsqlException)
        {
            throw await FailAsync("SubmitInstrumentRows", actorId, organizationGroupId,
                fileRegistrationId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<InstrumentFileResult> ResolveExceptionAsync(
        string fileRegistrationId, string exceptionId, ResolveImportExceptionRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(fileRegistrationId, correlationId, cancellationToken);
        try
        {
            var fileId = ParseId(fileRegistrationId);
            var exceptionKey = ParseId(exceptionId);
            var validated = InstrumentRules.ValidateResolution(request);
            InstrumentFileResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                await store.AcquireFileLockAsync(fileId, transactionToken);
                var file = await store.LoadFileAsync(organizationGroupId, fileId, transactionToken)
                    ?? throw new InstrumentDomainException(InstrumentErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, file.ObjectScope, transactionToken);
                if (validated.ExpectedCurrentVersion != file.Version)
                    throw new InstrumentDomainException(InstrumentErrorCodes.ExpectedVersionConflict);
                var entry = file.Exceptions.FirstOrDefault(candidate =>
                    string.Equals(candidate.ExceptionId, exceptionKey.ToString("N"), StringComparison.Ordinal))
                    ?? throw new InstrumentDomainException(InstrumentErrorCodes.ObjectNotAccessible);
                if (entry.Resolution is not null)
                    throw new InstrumentDomainException(InstrumentErrorCodes.ExceptionAlreadyResolved);

                await store.InsertResolutionAsync(
                    fileId, file.Version + 1, exceptionKey, validated, organizationGroupId,
                    actorId, clock.UtcNow, correlationId, transactionToken);
                result = await store.LoadFileAsync(organizationGroupId, fileId, transactionToken);
                InstrumentTelemetry.RecordResolution(validated.Kind);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("INS.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is InstrumentDomainException or NpgsqlException)
        {
            throw await FailAsync("ResolveImportException", actorId, organizationGroupId,
                fileRegistrationId, correlationId, exception, cancellationToken);
        }
    }

    public async Task<InstrumentFileResult> GetAsync(
        string fileRegistrationId, string correlationId, CancellationToken cancellationToken = default)
    {
        var (organizationGroupId, actorId) = await RequireActorAsync(fileRegistrationId, correlationId, cancellationToken);
        try
        {
            var fileId = ParseId(fileRegistrationId);
            InstrumentFileResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                result = await store.LoadFileAsync(organizationGroupId, fileId, transactionToken)
                    ?? throw new InstrumentDomainException(InstrumentErrorCodes.ObjectNotAccessible);
                await AuthorizeAsync(organizationGroupId, actorId, result.ObjectScope, transactionToken);
                await store.WriteReadAuditAsync(
                    result.FileRegistrationId, result.Version, organizationGroupId, actorId,
                    "READ_INSTRUMENT_FILE", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return result ?? throw new InvalidOperationException("INS.RESULT_MISSING");
        }
        catch (Exception exception) when (exception is InstrumentDomainException or NpgsqlException)
        {
            throw await FailAsync("GetInstrumentFile", actorId, organizationGroupId,
                fileRegistrationId, correlationId, exception, cancellationToken);
        }
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

        await WriteAttemptOrFailClosedAsync("InstrumentCommand", actor?.ActorId, organizationGroupId,
            target, correlationId, InstrumentErrorCodes.NotAuthorized, cancellationToken);
        throw new InstrumentDomainException(InstrumentErrorCodes.NotAuthorized);
    }

    private async Task AuthorizeAsync(
        string organizationGroupId, string actorId, InstrumentObjectContext objectScope, CancellationToken cancellationToken)
    {
        var decision = await authorizationPort.AuthorizeAsync(new InstrumentAuthorizationRequest(
            organizationGroupId, actorId, objectScope, InstrumentCapabilities.Import), cancellationToken);
        if (!decision.Allowed)
            throw new InstrumentDomainException(InstrumentErrorCodes.NotAuthorized);
    }

    private async Task<InstrumentDomainException> FailAsync(
        string commandType, string actorId, string organizationGroupId,
        string? target, string correlationId, Exception exception, CancellationToken cancellationToken)
    {
        var code = exception switch
        {
            InstrumentDomainException domain => domain.ErrorCode,
            PostgresException { SqlState: "23505" } postgres when postgres.ConstraintName?.Contains("sha256") == true =>
                InstrumentErrorCodes.DuplicateFile,
            PostgresException { SqlState: "23505" } => InstrumentErrorCodes.ValidationFailed,
            _ => InstrumentErrorCodes.PersistenceUnavailable
        };
        InstrumentTelemetry.RecordRejected(code);
        logger.LogWarning(
            "Instrument command {CommandType} failed closed with {ErrorCode}; correlation {CorrelationId}",
            commandType, code, correlationId);
        await WriteAttemptOrFailClosedAsync(commandType, actorId, organizationGroupId,
            target, correlationId, code, cancellationToken);
        return new InstrumentDomainException(code);
    }

    private async Task WriteAttemptOrFailClosedAsync(
        string commandType, string? actorId, string organizationGroupId,
        string? target, string correlationId, string code, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync(commandType, actorId, organizationGroupId,
                InstrumentRules.HashTarget(string.IsNullOrWhiteSpace(target) ? "unresolved-target" : target),
                correlationId, code, clock.UtcNow, cancellationToken);
        }
        catch (Exception auditException) when (auditException is NpgsqlException or InvalidOperationException)
        {
            throw new InstrumentDomainException(InstrumentErrorCodes.PersistenceUnavailable);
        }
    }

    private static Guid ParseId(string value) =>
        Guid.TryParseExact(value, "N", out var id) || Guid.TryParse(value, out id)
            ? id
            : throw new InstrumentDomainException(InstrumentErrorCodes.ObjectNotAccessible);
}

internal sealed class InstrumentImportPort(
    ICurrentOrganizationContext organizationContext,
    ICurrentActorContext actorContext,
    IClock clock,
    IInstrumentAuthorizationPort authorizationPort,
    ITransactionCoordinator transactionCoordinator,
    InstrumentStore store,
    InstrumentAttemptAuditWriter attemptAuditWriter,
    ILogger<InstrumentImportPort> logger) : IInstrumentImportPort
{
    public async ValueTask<InstrumentImportStatusResult> EvaluateAsync(
        InstrumentImportStatusRequest request, CancellationToken cancellationToken = default)
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
            await WriteDeniedAsync(actor?.ActorId, organizationGroupId, request.FileRegistrationId, correlationId, cancellationToken);
            throw new InstrumentDomainException(InstrumentErrorCodes.NotAuthorized);
        }

        if (!Guid.TryParseExact(request.FileRegistrationId, "N", out var fileId) &&
            !Guid.TryParse(request.FileRegistrationId, out fileId))
        {
            return Record(InstrumentRules.EvaluateStatus(request, null));
        }

        try
        {
            InstrumentImportStatusResult? result = null;
            await transactionCoordinator.ExecuteAsync(async transactionToken =>
            {
                var file = await store.LoadFileAsync(organizationGroupId, fileId, transactionToken);
                if (file is null)
                {
                    result = InstrumentRules.EvaluateStatus(request, null);
                    return;
                }

                var authorization = await authorizationPort.AuthorizeAsync(new InstrumentAuthorizationRequest(
                    organizationGroupId, actor.ActorId, file.ObjectScope, InstrumentCapabilities.Import), transactionToken);
                if (!authorization.Allowed)
                    throw new InstrumentDomainException(InstrumentErrorCodes.NotAuthorized);

                result = InstrumentRules.EvaluateStatus(request, file);
                await store.WriteReadAuditAsync(
                    file.FileRegistrationId, file.Version, organizationGroupId, actor.ActorId,
                    "EVALUATE_INSTRUMENT_IMPORT", correlationId, clock.UtcNow, transactionToken);
            }, cancellationToken);
            return Record(result ?? InstrumentRules.EvaluateStatus(request, null));
        }
        catch (InstrumentDomainException exception)
            when (string.Equals(exception.ErrorCode, InstrumentErrorCodes.NotAuthorized, StringComparison.Ordinal))
        {
            await WriteDeniedAsync(actor.ActorId, organizationGroupId, request.FileRegistrationId, correlationId, cancellationToken);
            throw;
        }
        catch (NpgsqlException)
        {
            logger.LogWarning("Instrument import status failed closed because persistence is unavailable");
            return Record(new InstrumentImportStatusResult(
                InstrumentStatusDecisions.Unknown,
                [InstrumentStatusReasons.InstrumentUnavailable],
                request.FileRegistrationId, null, null, null, InstrumentContract.RuleSetVersion));
        }
    }

    private InstrumentImportStatusResult Record(InstrumentImportStatusResult result)
    {
        InstrumentTelemetry.RecordGate(result.Decision);
        if (string.Equals(result.Decision, InstrumentStatusDecisions.Unknown, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Instrument import status failed closed with reasons {ReasonCodes}",
                string.Join(',', result.ReasonCodes));
        }
        return result;
    }

    private async Task WriteDeniedAsync(
        string? actorId, string organizationGroupId, string target, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await attemptAuditWriter.WriteAsync("EvaluateInstrumentImport", actorId, organizationGroupId,
                InstrumentRules.HashTarget(target), correlationId, InstrumentErrorCodes.NotAuthorized,
                clock.UtcNow, cancellationToken);
        }
        catch (NpgsqlException)
        {
            throw new InstrumentDomainException(InstrumentErrorCodes.PersistenceUnavailable);
        }
    }
}
