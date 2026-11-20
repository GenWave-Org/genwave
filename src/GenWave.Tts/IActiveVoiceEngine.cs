namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// The engine every speech render uses right now, read per request so a saved <c>Tts:Engine</c> applies
/// to the next render (SPEC F219.3). The stored name can outlive its registry entry (a launch flag
/// dropped), so <see cref="Current"/> is null then and callers degrade (a failed render, a red tile)
/// instead of throwing: the station keeps playing music.
/// </summary>
public interface IActiveVoiceEngine
{
    /// <summary>The configured engine name after the piper-primary rule, present even when no such engine is registered.</summary>
    string Name { get; }

    /// <summary>The registered engine named <see cref="Name"/>, or null when it is not configured.</summary>
    VoiceEngine? Current { get; }

    /// <summary>
    /// The engine that actually renders speech of <paramref name="kind"/> right now (SPEC F219.21): the active engine,
    /// unless <c>Tts:EngineByKind</c> pins the kind to a fallback-chain hop that exists. Every speech cache key and the
    /// ad render marker fold this name, so pinning or unpinning a kind makes its old files stale. The default is the
    /// active engine, right for any lookup with no per-kind routing.
    /// </summary>
    string EngineFor(SegmentKind kind) => Name;

    /// <summary>The health-probe / dependency key of the active engine: its profile name when registered, else <see cref="Name"/>.</summary>
    string DependencyName => Current?.DependencyName ?? Name;
}
