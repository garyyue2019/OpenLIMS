using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenLIMS.Contracts.Platform;
using OpenLIMS.Contracts.Receiving;

namespace OpenLIMS.Modules.Receiving;

public static class ReceivingRules
{
    private const int IdentifierMaximumLength = 100;
    private const int DescriptionMaximumLength = 500;
    private const int ObservationMaximumLength = 1000;

    public static void Validate(RegisterReceiptRequest request, string idempotencyKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireIdentifier(request.LegalEntityId);
        RequireIdentifier(request.LaboratoryId);
        RequireIdentifier(request.CustomerId);
        RequireIdentifier(request.ServiceOrderId);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ValidationFailed);
        }

        if (request.ArrivalAt == default || request.ArrivalAt > now.AddMinutes(5))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ValidationFailed);
        }

        if (request.Containers is null || request.Containers.Count is < 1 or > 100)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ValidationFailed);
        }

        foreach (var container in request.Containers)
        {
            if (container is null ||
                string.IsNullOrWhiteSpace(container.PackageType) ||
                string.IsNullOrWhiteSpace(container.Condition) ||
                container.PackageType.Length > IdentifierMaximumLength ||
                container.Condition.Length > IdentifierMaximumLength ||
                container.ExternalLabel?.Length > IdentifierMaximumLength ||
                container.SealObservation?.Length > ObservationMaximumLength ||
                container.ReceivedItems is null ||
                container.ReceivedItems.Count is < 1 or > 500)
            {
                throw new ReceivingDomainException(ReceivingErrorCodes.ValidationFailed);
            }

            foreach (var item in container.ReceivedItems)
            {
                ValidateItem(item);
            }
        }
    }

    public static string Hash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static string RequestHash(RegisterReceiptRequest request) =>
        Hash(JsonSerializer.Serialize(request, ReceivingJson.Options));

    internal static ReceiptPlan CreatePlan(
        RegisterReceiptRequest request,
        IIdGenerator idGenerator,
        string organizationGroupId,
        string actorId,
        DateTimeOffset now,
        string laboratoryCode)
    {
        var normalizedLaboratoryCode = NormalizeLaboratoryCode(laboratoryCode);
        var receiptId = NextGuid(idGenerator);
        var containers = request.Containers.Select((container, containerIndex) =>
        {
            var containerId = NextGuid(idGenerator);
            var items = container.ReceivedItems.Select((item, itemIndex) =>
            {
                var itemId = NextGuid(idGenerator);
                return new ReceivedItemPlan(
                    itemId,
                    BusinessNumber("ITM", itemId),
                    NextGuid(idGenerator),
                    containerIndex,
                    itemIndex,
                    item);
            }).ToArray();
            return new ContainerPlan(
                containerId,
                BusinessNumber("CNT", containerId),
                NextGuid(idGenerator),
                containerIndex,
                container,
                items);
        }).ToArray();

        return new ReceiptPlan(
            receiptId,
            BusinessNumber("RCP", receiptId),
            organizationGroupId,
            actorId,
            now,
            normalizedLaboratoryCode,
            request,
            containers);
    }

    private static void ValidateItem(RegisterReceivedItemRequest item)
    {
        if (item is null ||
            string.IsNullOrWhiteSpace(item.DeclaredDescription) ||
            item.DeclaredDescription.Length > DescriptionMaximumLength ||
            string.IsNullOrWhiteSpace(item.Model) ||
            item.Model.Length > IdentifierMaximumLength ||
            string.IsNullOrWhiteSpace(item.Batch) ||
            item.Batch.Length > IdentifierMaximumLength ||
            item.SerialNumber?.Length > IdentifierMaximumLength ||
            string.IsNullOrWhiteSpace(item.Color) ||
            item.Color.Length > IdentifierMaximumLength ||
            string.IsNullOrWhiteSpace(item.PackageCondition) ||
            string.IsNullOrWhiteSpace(item.SealCondition) ||
            string.IsNullOrWhiteSpace(item.ItemCondition) ||
            string.IsNullOrWhiteSpace(item.Unit))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ValidationFailed);
        }

        // The approved pilot model requires every complete retail toy or set to
        // have its own ReceivedItem identity. Quantity greater than one would
        // silently create a homogeneous group and is therefore rejected.
        if (item.Quantity != 1m)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.IdentityGranularityUnresolved);
        }
    }

    private static void RequireIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > IdentifierMaximumLength)
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.ValidationFailed);
        }
    }

    private static Guid NextGuid(IIdGenerator idGenerator)
    {
        var value = idGenerator.NewId();
        if (!Guid.TryParse(value, out var result))
        {
            throw new InvalidOperationException("REC.ID_GENERATOR_INVALID");
        }

        return result;
    }

    private static string BusinessNumber(string prefix, Guid id) =>
        $"{prefix}-{id:N}".ToUpperInvariant();

    internal static string NormalizeLaboratoryCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length is < 2 or > 16 || !Regex.IsMatch(normalized, "^[A-Z0-9][A-Z0-9-]*$"))
        {
            throw new ReceivingDomainException(ReceivingErrorCodes.AuthorizationDenied);
        }

        return normalized;
    }
}

public sealed class ReceivingDomainException(string errorCode) : InvalidOperationException(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

internal static class ReceivingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
}

internal sealed record ReceiptPlan(
    Guid Id,
    string Number,
    string OrganizationGroupId,
    string ActorId,
    DateTimeOffset OccurredAt,
    string LaboratoryCode,
    RegisterReceiptRequest Request,
    IReadOnlyList<ContainerPlan> Containers);

internal sealed record ContainerPlan(
    Guid Id,
    string Number,
    Guid LabelOpaqueReference,
    int Index,
    RegisterContainerRequest Request,
    IReadOnlyList<ReceivedItemPlan> Items);

internal sealed record ReceivedItemPlan(
    Guid Id,
    string Number,
    Guid LabelOpaqueReference,
    int ContainerIndex,
    int ItemIndex,
    RegisterReceivedItemRequest Request);
