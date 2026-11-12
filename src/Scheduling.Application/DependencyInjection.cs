using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Application;

public static class DependencyInjection
{
    /// <summary>Registers every use-case handler (classes named *Handler) and every validator of this assembly.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var handlers = ApplicationAssembly.Instance
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && t.Name.EndsWith("Handler", StringComparison.Ordinal));

        foreach (var handler in handlers)
        {
            services.AddScoped(handler);
        }

        services.AddValidatorsFromAssembly(ApplicationAssembly.Instance, includeInternalTypes: true);
        return services;
    }
}
