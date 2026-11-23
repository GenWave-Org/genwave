using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.TalkRules.Laws;

/// <summary>
/// Law R2 (F220.4): a type that implements only <see cref="IJudgingTalkRule"/> takes no constructor parameters
/// beyond an allowlist, never a store, <c>HttpClient</c>, <c>TimeProvider</c> or <c>Random</c>. An allowlist, not a
/// denylist, so a new kind of I/O is refused until someone decides it is pure.
/// The allowlist is exactly <see cref="PureCollaborators"/>, empty today: a judge takes no parameters, its numbers are its
/// own constants (R6 makes it state them). The catalog's construction seam can supply only registered services and
/// interface stand-ins, so a judge's parameters could never be values anyway. A shipped judge that needs a pure
/// collaborator adds its type to the list in review.
/// </summary>
public static class JudgesArePureLaw
{
    /// <summary>Pure collaborator types a judge may take. Empty until a shipped judge proves it needs one.</summary>
    public static readonly IReadOnlyList<Type> PureCollaborators = [];

    public static IReadOnlyList<string> Violations(IEnumerable<Type> ruleTypes) =>
        ruleTypes
            .Where(IsJudgeOnly)
            .SelectMany(type => ConstructorParameters.TypesOf(type)
                .Where(parameterType => !PureCollaborators.Contains(parameterType))
                .Distinct()
                .Select(taken => $"R2 {type.Name}: a judge takes {taken.Name}, which is not on the pure allowlist."))
            .ToList();

    private static bool IsJudgeOnly(Type type) =>
        typeof(IJudgingTalkRule).IsAssignableFrom(type)
        && !typeof(IShapingTalkRule).IsAssignableFrom(type)
        && !typeof(IRecordingTalkRule).IsAssignableFrom(type);
}
