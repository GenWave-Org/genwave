using System.Collections.Concurrent;
using GenWave.Core.TalkRules;

namespace GenWave.TalkRules;

/// <summary>
/// Switches held in process memory: a rule with no entry runs at its <c>OnByDefault</c>. The stand-in
/// <see cref="TalkRuleServiceCollectionExtensions.AddTalkRules(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>
/// registers until the Postgres-backed store replaces it (PLAN T799); changes here do not survive a restart.
/// </summary>
public sealed class InMemoryTalkRuleSwitches : ITalkRuleSwitches
{
    private readonly ConcurrentDictionary<(string RuleId, TalkKinds Kind), bool> stored = new();

    public ValueTask<IReadOnlyDictionary<string, bool>> GetAllAsync(TalkKinds kind, CancellationToken ct)
    {
        IReadOnlyDictionary<string, bool> forKind = stored
            .Where(entry => entry.Key.Kind == kind)
            .ToDictionary(entry => entry.Key.RuleId, entry => entry.Value, StringComparer.Ordinal);
        return ValueTask.FromResult(forKind);
    }

    public ValueTask SetAsync(string ruleId, TalkKinds kind, bool enabled, CancellationToken ct)
    {
        stored[(ruleId, kind)] = enabled;
        return ValueTask.CompletedTask;
    }
}
