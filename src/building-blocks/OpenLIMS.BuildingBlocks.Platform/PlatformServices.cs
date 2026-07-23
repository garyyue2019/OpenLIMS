using OpenLIMS.Contracts.Platform;

namespace OpenLIMS.BuildingBlocks.Platform;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class GuidIdGenerator : IIdGenerator
{
    public string NewId() => Guid.NewGuid().ToString("N");
}

public sealed class DeploymentOrganizationContext(OrganizationScope scope) : ICurrentOrganizationContext
{
    public OrganizationScope Current { get; } = scope;
}

public sealed class EmptyActorContext : ICurrentActorContext
{
    public ActorContext? Current => null;
}
