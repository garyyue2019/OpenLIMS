using Npgsql;
using OpenLIMS.BuildingBlocks.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

internal sealed record ReceivingLabelIdentity(
    string ObjectType,
    Guid ObjectId,
    long ObjectVersion,
    string BusinessNumber,
    Guid OpaqueReference,
    string TemplateVersion);

internal sealed class ReceivingLabelIdentityWriter(IPostgresTransactionAccessor transactionAccessor)
{
    public async Task<ReceivingLabelIdentity> AllocateAsync(
        ReceiptPlan plan,
        string objectType,
        Guid objectId,
        long objectVersion,
        string objectState,
        Guid opaqueReference,
        string idempotencyKeyHash,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!transactionAccessor.HasActiveTransaction)
        {
            throw new InvalidOperationException("REC.TRANSACTION_REQUIRED");
        }

        var connection = transactionAccessor.Connection;
        var transaction = transactionAccessor.Transaction;
        var sequenceDate = DateOnly.FromDateTime(plan.OccurredAt.UtcDateTime);
        await using var sequence = new NpgsqlCommand("""
            insert into receiving.label_sequence (
              organization_group_id, object_type, sequence_date, current_value
            ) values (
              @organization_group_id, @object_type, @sequence_date, 1
            )
            on conflict (organization_group_id, object_type, sequence_date)
            do update set current_value = receiving.label_sequence.current_value + 1
            returning current_value
            """, connection, transaction);
        sequence.Parameters.AddWithValue("organization_group_id", plan.OrganizationGroupId);
        sequence.Parameters.AddWithValue("object_type", objectType);
        sequence.Parameters.AddWithValue("sequence_date", sequenceDate);
        var sequenceValue = Convert.ToInt64(await sequence.ExecuteScalarAsync(cancellationToken));
        var businessNumber = $"{plan.LaboratoryCode}-{objectType}-{sequenceDate:yyyyMMdd}-{sequenceValue:000000}";

        await using var insert = new NpgsqlCommand("""
            insert into receiving.label_identity (
              object_type, object_id, object_version, organization_group_id, legal_entity_id,
              laboratory_id, laboratory_code, customer_id, service_order_id, business_number,
              opaque_reference, format_version, sequence_date, sequence_value, object_state,
              created_at, created_by
            ) values (
              @object_type, @object_id, @object_version, @organization_group_id, @legal_entity_id,
              @laboratory_id, @laboratory_code, @customer_id, @service_order_id, @business_number,
              @opaque_reference, 'OL1', @sequence_date, @sequence_value, @object_state,
              @created_at, @created_by
            )
            """, connection, transaction);
        insert.Parameters.AddWithValue("object_type", objectType);
        insert.Parameters.AddWithValue("object_id", objectId);
        insert.Parameters.AddWithValue("object_version", objectVersion);
        insert.Parameters.AddWithValue("organization_group_id", plan.OrganizationGroupId);
        insert.Parameters.AddWithValue("legal_entity_id", plan.Request.LegalEntityId);
        insert.Parameters.AddWithValue("laboratory_id", plan.Request.LaboratoryId);
        insert.Parameters.AddWithValue("laboratory_code", plan.LaboratoryCode);
        insert.Parameters.AddWithValue("customer_id", plan.Request.CustomerId);
        insert.Parameters.AddWithValue("service_order_id", plan.Request.ServiceOrderId);
        insert.Parameters.AddWithValue("business_number", businessNumber);
        insert.Parameters.AddWithValue("opaque_reference", opaqueReference);
        insert.Parameters.AddWithValue("sequence_date", sequenceDate);
        insert.Parameters.AddWithValue("sequence_value", sequenceValue);
        insert.Parameters.AddWithValue("object_state", objectState);
        insert.Parameters.AddWithValue("created_at", plan.OccurredAt);
        insert.Parameters.AddWithValue("created_by", plan.ActorId);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        await ReceivingRegistrationStore.InsertAuditAndOutboxPairAsync(
            connection,
            transaction,
            plan,
            objectType == ReceivingLabelObjectTypes.Container ? "Container" : "ReceivedItem",
            objectId,
            "LABEL_IDENTITY_ALLOCATED",
            idempotencyKeyHash,
            correlationId,
            System.Text.Json.JsonSerializer.Serialize(
                new { objectType, businessNumber, formatVersion = LabelBarcodeCodec.CurrentFormatVersion, objectVersion },
                ReceivingJson.Options),
            cancellationToken);

        return new ReceivingLabelIdentity(
            objectType,
            objectId,
            objectVersion,
            businessNumber,
            opaqueReference,
            LabelTemplateVersions.ForObjectType(objectType));
    }
}

internal sealed class ReceivingLabelObjectPort(ReceivingDataSource dataSource) : IReceivingLabelObjectPort
{
    public ValueTask<ReceivingLabelObjectSnapshot?> GetAsync(
        string organizationGroupId,
        string objectType,
        string objectId,
        CancellationToken cancellationToken = default) =>
        Guid.TryParse(objectId, out var id)
            ? FindAsync(organizationGroupId, objectType, "object_id", id, cancellationToken)
            : ValueTask.FromResult<ReceivingLabelObjectSnapshot?>(null);

    public ValueTask<ReceivingLabelObjectSnapshot?> ResolveAsync(
        string organizationGroupId,
        string objectType,
        string opaqueReference,
        CancellationToken cancellationToken = default) =>
        Guid.TryParse(opaqueReference, out var reference)
            ? FindAsync(organizationGroupId, objectType, "opaque_reference", reference, cancellationToken)
            : ValueTask.FromResult<ReceivingLabelObjectSnapshot?>(null);

    private async ValueTask<ReceivingLabelObjectSnapshot?> FindAsync(
        string organizationGroupId,
        string objectType,
        string column,
        Guid value,
        CancellationToken cancellationToken)
    {
        if (objectType is not (ReceivingLabelObjectTypes.Container or ReceivingLabelObjectTypes.ReceivedItem))
        {
            return null;
        }

        var sql = $"""
            select object_type, object_id, object_version, organization_group_id,
                   legal_entity_id, laboratory_id, laboratory_code, customer_id,
                   service_order_id, business_number, opaque_reference, format_version,
                   object_state
            from receiving.label_identity
            where organization_group_id = @organization_group_id
              and object_type = @object_type and {column} = @value
            """;
        await using var command = dataSource.DataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("organization_group_id", organizationGroupId);
        command.Parameters.AddWithValue("object_type", objectType);
        command.Parameters.AddWithValue("value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReceivingLabelObjectSnapshot(
            reader.GetString(0),
            reader.GetGuid(1).ToString("N"),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetGuid(10).ToString("N"),
            reader.GetString(11),
            reader.GetString(12));
    }
}
