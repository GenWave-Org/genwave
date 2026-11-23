using System.Reflection;
using GenWave.Core.TalkRules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GenWave.TalkRules;

public static class TalkRuleServiceCollectionExtensions
{
    /// <summary>Registers every <see cref="ITalkRule"/> in GenWave.TalkRules, the rule book, and the in-memory switch stand-in (registered with TryAdd: an earlier <see cref="ITalkRuleSwitches"/> registration wins; a later one must use Replace). A new rule is one file, no edit here (F220.2).</summary>
    public static IServiceCollection AddTalkRules(this IServiceCollection services) =>
        services.AddTalkRules(typeof(TalkRuleBook).Assembly);

    /// <summary>As <see cref="AddTalkRules(IServiceCollection)"/>, scanning <paramref name="assemblies"/> for rules.</summary>
    public static IServiceCollection AddTalkRules(this IServiceCollection services, params Assembly[] assemblies)
    {
        foreach (var type in assemblies.SelectMany(assembly => assembly.GetTypes()).Where(IsRule))
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(ITalkRule), type));
        }

        services.TryAddSingleton<ITalkRuleSwitches, InMemoryTalkRuleSwitches>();
        services.TryAddSingleton<TalkRuleBook>();
        services.TryAddSingleton<ITalkRuleBook>(provider => provider.GetRequiredService<TalkRuleBook>());
        return services;
    }

    private static bool IsRule(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
        && typeof(ITalkRule).IsAssignableFrom(type);
}
