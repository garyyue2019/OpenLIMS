using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Instrument;
using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.Modules.Instrument;

internal sealed class InstrumentDataSource : IAsyncDisposable
{
    public InstrumentDataSource(InstrumentPersistenceOptions options) =>
        Value = NpgsqlDataSource.Create(options.ConnectionString);

    public NpgsqlDataSource Value { get; }
    public ValueTask DisposeAsync() => Value.DisposeAsync();
}

internal sealed class InstrumentStore(
    IPostgresTransactionAccessor transactionAccessor,
    IAuditIntentWriter auditWriter,
    IOutboxWriter outboxWriter)
{
    public async Task AcquireFileLockAsync(Guid fileRegistrationId, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand(
            "select pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
        command.Parameters.AddWithValue("key", $"openlims.instrument.file.{fileRegistrationId:N}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DuplicateHashExistsAsync(
        string organizationGroupId, string sha256, CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            select exists (
                select 1 from instrument.file_registration
                where organization_group_id = @organization_group_id and sha256 = @sha256
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("sha256", sha256);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task InsertRegistrationAsync(
        Guid fileRegistrationId,
        string organizationGroupId,
        RegisterInstrumentFileRequest request,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into instrument.file_registration (
                file_registration_id, organization_group_id, legal_entity_id, laboratory_id,
                external_ref, external_version, sha256, source_system,
                instrument_ref, instrument_version, parser_version, declared_row_count,
                rule_set_version, registered_by, registered_at, event_id, correlation_id
            ) values (
                @file_registration_id, @organization_group_id, @legal_entity_id, @laboratory_id,
                @external_ref, @external_version, @sha256, @source_system,
                @instrument_ref, @instrument_version, @parser_version, @declared_row_count,
                @rule_set_version, @registered_by, @registered_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("file_registration_id", fileRegistrationId);
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("legal_entity_id", request.ObjectScope.LegalEntityId);
            command.Parameters.AddWithValue("laboratory_id", request.ObjectScope.LaboratoryId);
            command.Parameters.AddWithValue("external_ref", request.ExternalRef.Id);
            command.Parameters.AddWithValue("external_version", request.ExternalRef.Version);
            command.Parameters.AddWithValue("sha256", request.Sha256);
            command.Parameters.AddWithValue("source_system", request.SourceSystem);
            command.Parameters.AddWithValue("instrument_ref", request.InstrumentRef.Id);
            command.Parameters.AddWithValue("instrument_version", request.InstrumentRef.Version);
            command.Parameters.AddWithValue("parser_version", request.ParserVersion);
            command.Parameters.AddWithValue("declared_row_count", request.DeclaredRowCount);
            command.Parameters.AddWithValue("rule_set_version", InstrumentContract.RuleSetVersion);
            command.Parameters.AddWithValue("registered_by", actorId);
            command.Parameters.AddWithValue("registered_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "REGISTER_INSTRUMENT_FILE", fileRegistrationId.ToString("N"), organizationGroupId, actorId,
            eventId, "Instrument.FileRegistered.v1", correlationId, now, cancellationToken);
    }

    public async Task InsertParsedRowAsync(
        Guid fileRegistrationId,
        long fileVersion,
        InstrumentRowInput row,
        string parserVersion,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into instrument.parsed_row (
                row_id, file_registration_id, file_version, row_number,
                sample_number, batch_position, parameter, unit, qualifier,
                raw_value, parsed_value, parser_version,
                recorded_by, recorded_at, event_id, correlation_id
            ) values (
                @row_id, @file_registration_id, @file_version, @row_number,
                @sample_number, @batch_position, @parameter, @unit, @qualifier,
                @raw_value, @parsed_value, @parser_version,
                @recorded_by, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("row_id", Guid.NewGuid());
        command.Parameters.AddWithValue("file_registration_id", fileRegistrationId);
        command.Parameters.AddWithValue("file_version", fileVersion);
        command.Parameters.AddWithValue("row_number", row.RowNumber);
        command.Parameters.AddWithValue("sample_number", row.SampleNumber);
        command.Parameters.AddWithValue("batch_position", row.BatchPosition);
        command.Parameters.AddWithValue("parameter", row.Parameter);
        command.Parameters.AddWithValue("unit", row.Unit);
        command.Parameters.AddWithValue("qualifier", (object?)row.Qualifier ?? DBNull.Value);
        command.Parameters.AddWithValue("raw_value", row.RawValue);
        command.Parameters.AddWithValue("parsed_value", row.ParsedValue);
        command.Parameters.AddWithValue("parser_version", parserVersion);
        command.Parameters.AddWithValue("recorded_by", actorId);
        command.Parameters.AddWithValue("recorded_at", now);
        command.Parameters.AddWithValue("event_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertExceptionAsync(
        Guid fileRegistrationId,
        long fileVersion,
        InstrumentRowInput row,
        string reasonCode,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        await using var command = new NpgsqlCommand("""
            insert into instrument.import_exception (
                exception_id, file_registration_id, file_version, row_number,
                reason_code, raw_content, recorded_by, recorded_at, event_id, correlation_id
            ) values (
                @exception_id, @file_registration_id, @file_version, @row_number,
                @reason_code, @raw_content, @recorded_by, @recorded_at, @event_id, @correlation_id
            )
            """, connection, transaction);
        command.Parameters.AddWithValue("exception_id", Guid.NewGuid());
        command.Parameters.AddWithValue("file_registration_id", fileRegistrationId);
        command.Parameters.AddWithValue("file_version", fileVersion);
        command.Parameters.AddWithValue("row_number", row.RowNumber);
        command.Parameters.AddWithValue("reason_code", reasonCode);
        command.Parameters.AddWithValue("raw_content",
            $"{row.SampleNumber}|{row.BatchPosition}|{row.Parameter}|{row.Unit}|{row.Qualifier}|{row.RawValue}|{row.ParsedValue}");
        command.Parameters.AddWithValue("recorded_by", actorId);
        command.Parameters.AddWithValue("recorded_at", now);
        command.Parameters.AddWithValue("event_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task WriteRowsSubmittedEvidenceAsync(
        Guid fileRegistrationId,
        string organizationGroupId,
        string actorId,
        int validCount,
        int exceptionCount,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await WritePlatformEvidenceAsync(
            "SUBMIT_INSTRUMENT_ROWS", fileRegistrationId.ToString("N"), organizationGroupId, actorId,
            Guid.NewGuid().ToString("N"),
            exceptionCount > 0 ? "Instrument.RowsSubmittedWithExceptions.v1" : "Instrument.RowsSubmitted.v1",
            correlationId, now, cancellationToken);
        _ = validCount;
    }

    public async Task InsertResolutionAsync(
        Guid fileRegistrationId,
        long fileVersion,
        Guid exceptionId,
        ResolveImportExceptionRequest request,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        var eventId = Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand("""
            insert into instrument.exception_resolution (
                resolution_id, exception_id, file_registration_id, file_version, kind,
                corrected_sample_number, corrected_batch_position, corrected_parameter,
                corrected_unit, corrected_qualifier,
                reason, resolved_by, resolved_at, event_id, correlation_id
            ) values (
                @resolution_id, @exception_id, @file_registration_id, @file_version, @kind,
                @corrected_sample_number, @corrected_batch_position, @corrected_parameter,
                @corrected_unit, @corrected_qualifier,
                @reason, @resolved_by, @resolved_at, @event_id, @correlation_id
            )
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("resolution_id", Guid.NewGuid());
            command.Parameters.AddWithValue("exception_id", exceptionId);
            command.Parameters.AddWithValue("file_registration_id", fileRegistrationId);
            command.Parameters.AddWithValue("file_version", fileVersion);
            command.Parameters.AddWithValue("kind", request.Kind);
            command.Parameters.AddWithValue("corrected_sample_number", (object?)request.CorrectedMapping?.SampleNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("corrected_batch_position", (object?)request.CorrectedMapping?.BatchPosition ?? DBNull.Value);
            command.Parameters.AddWithValue("corrected_parameter", (object?)request.CorrectedMapping?.Parameter ?? DBNull.Value);
            command.Parameters.AddWithValue("corrected_unit", (object?)request.CorrectedMapping?.Unit ?? DBNull.Value);
            command.Parameters.AddWithValue("corrected_qualifier", (object?)request.CorrectedMapping?.Qualifier ?? DBNull.Value);
            command.Parameters.AddWithValue("reason", request.Reason);
            command.Parameters.AddWithValue("resolved_by", actorId);
            command.Parameters.AddWithValue("resolved_at", now);
            command.Parameters.AddWithValue("event_id", eventId);
            command.Parameters.AddWithValue("correlation_id", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WritePlatformEvidenceAsync(
            "RESOLVE_IMPORT_EXCEPTION", fileRegistrationId.ToString("N"), organizationGroupId, actorId,
            eventId, "Instrument.ExceptionResolved.v1", correlationId, now, cancellationToken);
    }

    public async Task<InstrumentFileResult?> LoadFileAsync(
        string organizationGroupId,
        Guid fileRegistrationId,
        CancellationToken cancellationToken)
    {
        var (connection, transaction) = RequireTransaction();
        InstrumentObjectContext? objectScope = null;
        InstrumentVersionedReference? externalRef = null;
        InstrumentVersionedReference? instrumentRef = null;
        string? sha256 = null, sourceSystem = null, parserVersion = null, registeredBy = null;
        var declaredRowCount = 0;
        DateTimeOffset registeredAt = default;
        await using (var command = new NpgsqlCommand("""
            select legal_entity_id, laboratory_id, external_ref, external_version, sha256,
                   source_system, instrument_ref, instrument_version, parser_version,
                   declared_row_count, registered_by, registered_at
            from instrument.file_registration
            where organization_group_id = @organization_group_id
              and file_registration_id = @file_registration_id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
            command.Parameters.AddWithValue("file_registration_id", fileRegistrationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            objectScope = new InstrumentObjectContext(reader.GetString(0), reader.GetString(1));
            externalRef = new InstrumentVersionedReference(reader.GetString(2), reader.GetInt64(3));
            sha256 = reader.GetString(4);
            sourceSystem = reader.GetString(5);
            instrumentRef = new InstrumentVersionedReference(reader.GetString(6), reader.GetInt64(7));
            parserVersion = reader.GetString(8);
            declaredRowCount = reader.GetInt32(9);
            registeredBy = reader.GetString(10);
            registeredAt = reader.GetFieldValue<DateTimeOffset>(11);
        }

        var rows = new List<InstrumentParsedRowResult>();
        await using (var command = new NpgsqlCommand("""
            select row_id, row_number, sample_number, batch_position, parameter, unit, qualifier,
                   raw_value, parsed_value, parser_version, recorded_by, recorded_at
            from instrument.parsed_row
            where file_registration_id = @id
            order by row_number
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", fileRegistrationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new InstrumentParsedRowResult(
                    reader.GetGuid(0).ToString("N"),
                    fileRegistrationId.ToString("N"),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetFieldValue<DateTimeOffset>(11)));
            }
        }

        var exceptions = new List<InstrumentImportExceptionResult>();
        await using (var command = new NpgsqlCommand("""
            select e.exception_id, e.row_number, e.reason_code, e.raw_content,
                   r.resolution_id, r.kind, r.corrected_sample_number, r.corrected_batch_position,
                   r.corrected_parameter, r.corrected_unit, r.corrected_qualifier,
                   r.reason, r.resolved_by, r.resolved_at
            from instrument.import_exception e
            left join instrument.exception_resolution r on r.exception_id = e.exception_id
            where e.file_registration_id = @id
            order by e.row_number
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", fileRegistrationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var exceptionId = reader.GetGuid(0).ToString("N");
                InstrumentExceptionResolutionResult? resolution = null;
                if (!reader.IsDBNull(4))
                {
                    InstrumentRowMapping? mapping = null;
                    if (!reader.IsDBNull(6))
                    {
                        mapping = new InstrumentRowMapping(
                            reader.GetString(6), reader.GetString(7), reader.GetString(8),
                            reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10));
                    }

                    resolution = new InstrumentExceptionResolutionResult(
                        reader.GetGuid(4).ToString("N"),
                        exceptionId,
                        reader.GetString(5),
                        mapping,
                        reader.GetString(11),
                        reader.GetString(12),
                        reader.GetFieldValue<DateTimeOffset>(13));
                }

                exceptions.Add(new InstrumentImportExceptionResult(
                    exceptionId,
                    fileRegistrationId.ToString("N"),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    resolution is null ? InstrumentExceptionStates.Pending : InstrumentExceptionStates.Resolved,
                    resolution));
            }
        }

        var resolvedCount = exceptions.Count(entry => entry.Resolution is not null);
        var pendingCount = exceptions.Count - resolvedCount;
        var version = 1L + rows.Count + exceptions.Count + resolvedCount;
        var state = InstrumentRules.ResolveFileState(declaredRowCount, rows.Count, pendingCount, resolvedCount);
        return new InstrumentFileResult(
            fileRegistrationId.ToString("N"),
            version,
            state,
            InstrumentContract.RuleSetVersion,
            objectScope!,
            externalRef!,
            sha256!,
            sourceSystem!,
            instrumentRef!,
            parserVersion!,
            declaredRowCount,
            rows,
            exceptions,
            registeredBy!,
            registeredAt);
    }

    public Task WriteReadAuditAsync(
        string fileRegistrationId,
        long fileVersion,
        string organizationGroupId,
        string actorId,
        string action,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, fileRegistrationId, action, InstrumentContract.RuleSetVersion,
            fileVersion.ToString(), fileVersion.ToString(), correlationId, now), cancellationToken);

    private async Task WritePlatformEvidenceAsync(
        string action,
        string objectId,
        string organizationGroupId,
        string actorId,
        string eventId,
        string messageType,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await auditWriter.WriteAsync(new AuditIntent(
            actorId, organizationGroupId, objectId, action, InstrumentContract.RuleSetVersion,
            null, "1", correlationId, now), cancellationToken);
        await outboxWriter.WriteAsync(new OutboxEnvelope(eventId, messageType, now), cancellationToken);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) RequireTransaction()
    {
        if (!transactionAccessor.HasActiveTransaction)
            throw new InvalidOperationException("INS.TRANSACTION_REQUIRED");
        return (transactionAccessor.Connection, transactionAccessor.Transaction);
    }
}

internal sealed class InstrumentAttemptAuditWriter(InstrumentDataSource dataSource)
{
    public async Task WriteAsync(
        string commandType,
        string? actorId,
        string organizationGroupId,
        string targetHash,
        string correlationId,
        string outcome,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.Value.CreateCommand("""
            insert into instrument.audit_attempt (
                attempt_id, command_type, actor_id, organization_group_id,
                target_hash, correlation_id, outcome, occurred_at
            ) values (
                @attempt_id, @command_type, @actor_id, @organization_group_id,
                @target_hash, @correlation_id, @outcome, @occurred_at
            )
            """);
        command.Parameters.AddWithValue("attempt_id", Guid.NewGuid());
        command.Parameters.AddWithValue("command_type", commandType);
        command.Parameters.AddWithValue("actor_id", (object?)actorId ?? DBNull.Value);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("target_hash", targetHash);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
