namespace GenWave.Tts;

using Microsoft.Extensions.Options;
using GenWave.Core.Domain;

/// <summary>
/// The <see cref="IActiveVoiceEngine"/> every caller resolves: the active engine, plus the per-kind answer
/// (<see cref="EngineFor"/>) that honours <c>Tts:EngineByKind</c> pins to a hop the fallback chain actually has
/// (SPEC F219.21). Read per call, nothing cached.
/// </summary>
public sealed class RoutedActiveVoiceEngine(
    ActiveVoiceEngine active,
    TtsEngineByKindProvider overrides,
    IOptionsMonitor<TtsFallbackOptions> fallbackOptions) : IActiveVoiceEngine
{
    public string Name => active.Name;

    public VoiceEngine? Current => active.Current;

    public string EngineFor(SegmentKind kind)
    {
        var chain = TtsFallbackChain.Resolve(fallbackOptions.CurrentValue);
        var pinned = TtsEngineRouting.PinnedHopIndex(chain, overrides.Current, kind);
        return pinned < 0 ? active.Name : chain.Hops[pinned].Engine;
    }
}
