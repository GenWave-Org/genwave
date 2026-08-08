using System.Reflection;

namespace GenWave.SeamIndexGenerator;

/// <summary>
/// Best-effort decorator-chain derivation for a port's effective adapter (STORY-294 AC1's literal
/// "→ decorators where layered" clause). Walks constructor parameters whose DECLARED type is a
/// CONCRETE class implementing the same port interface — e.g. <c>DegradationGatedCopyWriter</c>'s
/// <c>ISegmentCopyWriter</c> parameters are typed <c>LlmCopyWriter</c>/<c>TemplateCopyWriter</c>
/// directly, not <c>ISegmentCopyWriter</c>, so reflection alone names them unambiguously.
///
/// Deliberately stops at any INTERFACE-typed constructor parameter, even one assignable to the same
/// port. Which concrete instance actually lands there is decided inside a hand-written factory
/// closure (<c>sp =&gt; new X(sp.GetRequiredService&lt;SomeConcreteType&gt;(), ...)</c>), and nothing
/// on the constructor's own metadata records that binding — walking further would be a guess dressed
/// up as a fact. <c>ITtsSynthesizer</c> is the known example this stops on:
/// <c>NormalizingTtsSynthesizer</c> takes an <c>ITtsSynthesizer inner</c> parameter that
/// <c>TtsServiceCollectionExtensions.AddGenWaveTts</c>'s factory binds to <c>FallbackTtsSynthesizer</c>,
/// which itself takes an <c>ITtsSynthesizer primary</c> parameter the SAME file's factory binds to
/// <c>KokoroTtsSynthesizer</c> — a real, three-deep chain, invisible to this type (SEAMS.md's header
/// says so; read the registration comment for the full picture).
/// </summary>
internal static class DecoratorChain
{
    /// <summary>Every concrete type reachable from <paramref name="effectiveAdapterType"/> by
    /// following constructor parameters typed as a concrete class implementing
    /// <paramref name="portInterface"/>, in discovery order, each appearing once.</summary>
    public static IReadOnlyList<Type> Derive(Type effectiveAdapterType, Type portInterface)
    {
        var found = new List<Type>();
        var seen = new HashSet<Type> { effectiveAdapterType };
        var frontier = new Queue<Type>();
        frontier.Enqueue(effectiveAdapterType);

        while (frontier.Count > 0)
        {
            var constructor = PickConstructor(frontier.Dequeue());
            if (constructor is null)
                continue;

            foreach (var parameter in constructor.GetParameters())
            {
                var parameterType = parameter.ParameterType;

                // The opaque-closure boundary: an interface-typed parameter's real argument is
                // chosen inside a factory delegate body, not reflectable from the constructor alone.
                if (parameterType.IsInterface)
                    continue;

                if (!portInterface.IsAssignableFrom(parameterType))
                    continue;

                if (!seen.Add(parameterType))
                    continue;

                found.Add(parameterType);
                frontier.Enqueue(parameterType);
            }
        }

        return found;
    }

    static ConstructorInfo? PickConstructor(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .ThenBy(c => string.Join(",", c.GetParameters().Select(p => p.ParameterType.FullName)), StringComparer.Ordinal)
            .FirstOrDefault();
}
