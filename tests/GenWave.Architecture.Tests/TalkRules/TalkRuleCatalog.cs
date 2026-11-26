using System.Reflection;
using GenWave.Architecture.Tests.TalkRules.Construction;
using GenWave.Architecture.Tests.TalkRules.Fixtures.Good;
using GenWave.Core.TalkRules;
using GenWave.TalkRules;
using Microsoft.Extensions.DependencyInjection;

namespace GenWave.Architecture.Tests.TalkRules;

/// <summary>
/// The rule sets the laws run over. <see cref="CheckedTypes"/> is every shipped rule plus the good fixture rules:
/// shipped rules arrive one per task (T783 on) and the laws must not be vacuous before then.
/// </summary>
public static class TalkRuleCatalog
{
    public static IReadOnlyList<Type> ShippedTypes { get; } = ConcreteRules(typeof(TalkRuleBook).Assembly);

    public static IReadOnlyList<Type> GoodFixtureTypes { get; } =
        ConcreteRules(typeof(ShoutingJudge).Assembly)
            .Where(type => type.Namespace == typeof(ShoutingJudge).Namespace)
            .ToList();

    public static IReadOnlyList<Type> CheckedTypes { get; } = [.. ShippedTypes, .. GoodFixtureTypes];

    /// <summary>
    /// Builds each type over the production registration (<c>AddTalkRules</c> plus logging), with an empty stand-in for any
    /// other interface collaborator (<see cref="CollaboratorFallbackProvider"/>). The laws judge declared behaviour over
    /// fixture snapshots, never collaborators, and a rule with a new dependency needs no edit here (F220.2).
    /// </summary>
    public static IReadOnlyList<ITalkRule> Build(IEnumerable<Type> types)
    {
        using var provider = new ServiceCollection().AddLogging().AddTalkRules().AddStandInsForUnsuppliedCollaborators().BuildServiceProvider();
        var withStandIns = new CollaboratorFallbackProvider(provider);
        return types
            .Select(type => (ITalkRule)ActivatorUtilities.CreateInstance(withStandIns, type))
            .ToList();
    }

    /// <summary>The production scan's predicate (<see cref="TalkRuleServiceCollectionExtensions.IsRule"/>), so the laws see exactly what production registers.</summary>
    public static IReadOnlyList<Type> ConcreteRules(Assembly assembly) =>
        assembly.GetTypes()
            .Where(TalkRuleServiceCollectionExtensions.IsRule)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
}
