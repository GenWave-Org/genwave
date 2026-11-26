using Microsoft.Extensions.DependencyInjection;

namespace GenWave.Architecture.Tests.TalkRules.Construction;

/// <summary>
/// Registers a <see cref="NullObjectProxy"/> for every interface a registered service's constructor takes that the
/// registration does not supply (the memory gate over the persona stores). Structural, not exception-driven: a circular
/// dependency among registered services is never an unsupplied collaborator, so it still throws.
/// </summary>
public static class StandInRegistration
{
    public static IServiceCollection AddStandInsForUnsuppliedCollaborators(this IServiceCollection services)
    {
        using var probe = services.BuildServiceProvider();
        var isService = probe.GetRequiredService<IServiceProviderIsService>();

        var missing = services
            .Select(descriptor => descriptor.ImplementationType)
            .OfType<Type>()
            .SelectMany(implementation => implementation.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.IsInterface && !type.ContainsGenericParameters && !isService.IsService(type))
            .Distinct()
            .ToList();

        foreach (var type in missing)
        {
            services.AddSingleton(type, _ => NullObjectProxy.For(type));
        }

        return services;
    }
}
