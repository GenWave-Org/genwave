namespace GenWave.Tts;

/// <summary>
/// The EFFECTIVE ordered fallback chain (gh-#147), resolved from <see cref="TtsFallbackOptions"/>
/// — the single place the new list shape and the legacy flat keys reconcile:
/// <list type="number">
/// <item><c>Tts:Fallback:Profiles</c> non-empty — the operator-built chain, in configured order.
/// The legacy flat <c>Endpoint</c>/<c>Voice</c> keys are ignored entirely.</item>
/// <item>Profiles absent/empty but legacy <c>Tts:Fallback:Endpoint</c> non-empty — the implicit
/// legacy chain: exactly one piper hop carrying the flat keys' endpoint/voice with default hop
/// semantics (always attempted, no per-hop budget). This is behavior-equivalent to the
/// pre-gh-#147 single Kokoro→Piper hop, so a deploy carrying only the old keys — including the
/// shipped compose.yaml — sees zero change on upgrade (pinned by the gh-#147 default-equivalence
/// specs).</item>
/// <item>Neither — <see cref="Empty"/>: no fallback at all; <see cref="FallbackTtsSynthesizer"/>
/// is then a transparent pass-through to the primary (F70.1's "empty endpoint = zero behavior
/// change" contract, now "empty chain").</item>
/// </list>
/// Resolved fresh from <c>IOptionsMonitor.CurrentValue</c> per render/probe, so a live edit to the
/// legacy keys keeps applying with no api restart, exactly as before.
/// </summary>
public sealed class TtsFallbackChain
{
    /// <summary>No fallback configured — the pass-through state.</summary>
    public static readonly TtsFallbackChain Empty = new([]);

    TtsFallbackChain(IReadOnlyList<TtsFallbackProfile> hops) => Hops = hops;

    /// <summary>The ordered hops tried after the primary engine, first to last.</summary>
    public IReadOnlyList<TtsFallbackProfile> Hops { get; }

    public bool IsEmpty => Hops.Count == 0;

    /// <summary>Resolves the effective chain per the precedence in the class remarks.</summary>
    public static TtsFallbackChain Resolve(TtsFallbackOptions options)
    {
        if (options.Profiles.Count > 0)
            return new([.. options.Profiles.Select(Normalize)]);

        if (string.IsNullOrEmpty(options.Endpoint))
            return Empty;

        return new(
        [
            new TtsFallbackProfile
            {
                Engine = DependencyNames.Piper,
                Endpoint = options.Endpoint,
                Voice = options.Voice,
            },
        ]);
    }

    /// <summary>
    /// Index of the first hop running <paramref name="engine"/>, or -1 when no hop does — the
    /// <c>Tts:EngineByKind</c> pin's target lookup (<see cref="FallbackTtsSynthesizer"/>).
    /// </summary>
    public int IndexOfFirstEngine(string engine)
    {
        for (var i = 0; i < Hops.Count; i++)
        {
            if (string.Equals(Hops[i].Engine, engine, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    // Canonical casing/whitespace once at resolve time so execution and renderer lookup never
    // re-normalize — config binding is case-preserving and operators type "Piper" as readily as
    // "piper".
    static TtsFallbackProfile Normalize(TtsFallbackProfile profile) => new()
    {
        Engine = profile.Engine.Trim().ToLowerInvariant(),
        Endpoint = profile.Endpoint.Trim(),
        Voice = profile.Voice,
        SkipWhenUnhealthy = profile.SkipWhenUnhealthy,
        TimeoutSeconds = profile.TimeoutSeconds,
    };
}
