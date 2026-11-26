using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GenWave.TalkRules;

public static class TalkRuleServiceCollectionExtensions
{
    /// <summary>Registers every <see cref="ITalkRule"/> in GenWave.TalkRules, the rule book, the replay, the shared-run <see cref="IRepetitionCheck"/> the repeat judge takes, the <see cref="IMemoryBreakGate"/> the memory shapers share and the <see cref="ISelfNameRoll"/> the name rule rolls and the <see cref="ITalkRuleSwitchboard"/> (all TryAdd), plus the in-memory switch stand-in, also TryAdd: an earlier <see cref="ITalkRuleSwitches"/> registration wins, and a later one must use Replace, as the host's Postgres store does. A new rule is one file, no edit here (F220.2).</summary>
    public static IServiceCollection AddTalkRules(this IServiceCollection services) =>
        services.AddTalkRules(typeof(TalkRuleBook).Assembly);

    /// <summary>As <see cref="AddTalkRules(IServiceCollection)"/>, scanning <paramref name="assemblies"/> for rules.</summary>
    public static IServiceCollection AddTalkRules(this IServiceCollection services, params Assembly[] assemblies)
    {
        foreach (var type in assemblies.SelectMany(assembly => assembly.GetTypes()).Where(IsRule))
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(ITalkRule), type));
        }

        services.TryAddSingleton<IRepetitionCheck, RepetitionCheck>(); // the pure shared-run check the repeat-phrases judge takes
        services.TryAddSingleton<IMemoryBreakGate, MemoryBreakGate>(); // the memory test the callbacks and never-mention rules share
        services.TryAddSingleton<ISelfNameRoll, SelfNameRoll>(); // the one name roll the self-name rule takes: Random.Shared at today's odds
        services.TryAddSingleton<ITalkRuleSwitches, InMemoryTalkRuleSwitches>();
        services.TryAddSingleton<ITalkRuleSwitchboard, TalkRuleSwitchboard>(); // the one door a switch change goes through: refuses an unknown rule or a kind it cannot cover
        services.TryAddSingleton<TalkRuleBook>();
        services.TryAddSingleton<ITalkRuleBook>(provider => provider.GetRequiredService<TalkRuleBook>());
        services.TryAddSingleton<ITalkRuleReplay, TalkRuleReplay>(); // reads the check log through ITalkCheckStore, which the host registers
        return services;
    }

    /// <summary>The scan's predicate: what <see cref="AddTalkRules(IServiceCollection, Assembly[])"/> registers as a rule. Shared so the architecture laws check exactly what production runs.</summary>
    public static bool IsRule(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
        && typeof(ITalkRule).IsAssignableFrom(type);
}
