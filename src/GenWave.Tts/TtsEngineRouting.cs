namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// The one rule for "which chain hop does <c>Tts:EngineByKind</c> send this kind to" (SPEC F70.3), shared by the
/// render router (<see cref="FallbackTtsSynthesizer"/>) and the cache-key lookup
/// (<see cref="RoutedActiveVoiceEngine"/>) so a key can never name an engine the router would not use.
/// </summary>
static class TtsEngineRouting
{
    /// <summary>
    /// The index of the chain hop the kind is pinned to, or -1 when it takes the active engine: no kind, no pin, a
    /// "kokoro" pin (not a distinct path), or a pinned engine no hop runs. An empty chain has no hops, so is always -1.
    /// </summary>
    public static int PinnedHopIndex(TtsFallbackChain chain, TtsEngineOverrideMap overrides, SegmentKind? kind)
    {
        var mappedEngine = kind is { } k ? overrides.Resolve(k) : null;
        return mappedEngine is not null
            && !string.Equals(mappedEngine, DependencyNames.Kokoro, StringComparison.OrdinalIgnoreCase)
                ? chain.IndexOfFirstEngine(mappedEngine)
                : -1;
    }
}
