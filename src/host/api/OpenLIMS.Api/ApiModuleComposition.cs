using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenLIMS.BuildingBlocks.Platform;

namespace OpenLIMS.Api;

public static class ApiModuleComposition
{
    public static IServiceCollection AddOpenLimsModule(
        this IServiceCollection services,
        OpenLimsModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(catalog);
        EnsureNotRegistered(services);

        services.AddSingleton<ApiModuleRegistrationMarker>();
        foreach (var module in catalog.Modules.OfType<IOpenLimsApiModule>())
        {
            module.AddApiServices(services);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapOpenLimsModuleEndpoints(
        this IEndpointRouteBuilder endpoints,
        OpenLimsModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(catalog);

        foreach (var module in catalog.Modules.OfType<IOpenLimsApiModule>())
        {
            module.MapApiEndpoints(endpoints);
            EnsureRoutesAreUnique(endpoints);
        }

        return endpoints;
    }

    private static void EnsureNotRegistered(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ApiModuleRegistrationMarker)))
        {
            throw new ModuleCompositionException(ModuleCompositionErrorCodes.RegistrationDuplicate);
        }
    }

    private static void EnsureRoutesAreUnique(IEndpointRouteBuilder endpoints)
    {
        var registeredMethods = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in endpoints.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>())
        {
            var route = endpoint.RoutePattern.RawText ?? endpoint.RoutePattern.ToString() ?? string.Empty;
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"];
            if (!registeredMethods.TryGetValue(route, out var existingMethods))
            {
                existingMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                registeredMethods.Add(route, existingMethods);
            }

            foreach (var method in methods)
            {
                if (existingMethods.Contains("*") || method == "*" && existingMethods.Count > 0 || !existingMethods.Add(method))
                {
                    throw new ModuleCompositionException(ModuleCompositionErrorCodes.RouteDuplicate);
                }
            }
        }
    }

    private sealed class ApiModuleRegistrationMarker;
}
