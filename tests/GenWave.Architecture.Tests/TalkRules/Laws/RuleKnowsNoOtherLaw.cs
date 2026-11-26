using GenWave.Core.TalkRules;
using GenWave.TalkRules;
using Microsoft.Extensions.DependencyInjection;

namespace GenWave.Architecture.Tests.TalkRules.Laws;

/// <summary>
/// Law R1 (F220.3): no rule type's constructor takes another rule type, the rule book or the switches.
/// Generic arguments and array elements count, so <c>IEnumerable&lt;ITalkRule&gt;</c> and <c>ITalkRule[]</c> are caught too, and so are
/// <c>IServiceProvider</c> and <c>IServiceScopeFactory</c>, which would let a rule look another up.
/// </summary>
public static class RuleKnowsNoOtherLaw
{
    private static readonly Type[] Forbidden =
    [
        typeof(ITalkRule), typeof(ITalkRuleBook), typeof(ITalkRuleSwitches), typeof(ITalkRuleSwitchboard), typeof(TalkRuleBook), typeof(InMemoryTalkRuleSwitches),
        typeof(IServiceProvider), typeof(IServiceScopeFactory),
    ];

    public static IReadOnlyList<string> Violations(IEnumerable<Type> ruleTypes) =>
        ruleTypes
            .SelectMany(type => ConstructorParameters.TypesOf(type)
                .SelectMany(Flatten)
                .Where(IsForbidden)
                .Distinct()
                .Select(taken => $"R1 {type.Name}: constructor takes {taken.Name}; a rule never knows another rule, the rule book, the switches or a way to look them up."))
            .ToList();

    private static bool IsForbidden(Type type) => Forbidden.Any(forbidden => forbidden.IsAssignableFrom(type));

    private static IEnumerable<Type> Flatten(Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            return Flatten(element).Append(type);
        }

        return type.IsGenericType ? type.GetGenericArguments().SelectMany(Flatten).Append(type) : [type];
    }
}
