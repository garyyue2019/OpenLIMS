using Microsoft.Extensions.DependencyInjection;
using OpenLIMS.BuildingBlocks.Platform;

namespace OpenLIMS.Worker;

public static class WorkerModuleComposition
{
    public static IServiceCollection AddOpenLimsWorkerModule(
        this IServiceCollection services,
        OpenLimsModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(catalog);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(WorkerModuleRegistrationMarker)))
        {
            throw new ModuleCompositionException(ModuleCompositionErrorCodes.RegistrationDuplicate);
        }

        services.AddSingleton<WorkerModuleRegistrationMarker>();
        foreach (var module in catalog.Modules.OfType<IOpenLimsWorkerModule>())
        {
            module.AddWorkerServices(services);
        }

        return services;
    }

    private sealed class WorkerModuleRegistrationMarker;
}
