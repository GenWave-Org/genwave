using System.Reflection;

namespace GenWave.Architecture.Tests.TalkRules.Laws;

/// <summary>The one walk over a rule type's constructors, shared by the laws that read them (R1, R2).</summary>
public static class ConstructorParameters
{
    private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static IEnumerable<Type> TypesOf(Type ruleType) =>
        ruleType.GetConstructors(AllInstance)
            .SelectMany(ctor => ctor.GetParameters())
            .Select(parameter => parameter.ParameterType);
}
