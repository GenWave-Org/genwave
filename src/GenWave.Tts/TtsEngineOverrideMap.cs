namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// Immutable, compiled snapshot of the operator's <c>Tts:EngineByKind</c> override map (SPEC F70.3,
/// STORY-191) — kind → engine name (<see cref="DependencyNames.Kokoro"/>/
/// <see cref="DependencyNames.Piper"/>). <see cref="FallbackTtsSynthesizer"/> is the sole reader: an
/// unmapped kind (or <see cref="Empty"/>) falls through to the existing health-based Kokoro/Piper
/// routing unchanged (F70.1) — this map only ever ADDS a forward-direction pre-emption for the
/// kinds an operator has explicitly mapped.
/// </summary>
public sealed class TtsEngineOverrideMap
{
    /// <summary>
    /// No operator overrides configured — every kind falls through to the default engine routing,
    /// byte-identical to pre-feature behavior (STORY-191 AC3).
    /// </summary>
    public static readonly TtsEngineOverrideMap Empty = new(new Dictionary<SegmentKind, string>());

    readonly IReadOnlyDictionary<SegmentKind, string> byKind;

    TtsEngineOverrideMap(IReadOnlyDictionary<SegmentKind, string> byKind) => this.byKind = byKind;

    /// <summary>Compiles a validated kind → engine-name dictionary into an immutable snapshot.</summary>
    public static TtsEngineOverrideMap Create(IReadOnlyDictionary<SegmentKind, string> byKind) => new(byKind);

    /// <summary>
    /// The mapped engine name for <paramref name="kind"/> (<c>"kokoro"</c>/<c>"piper"</c>), or null
    /// when this kind carries no override — the fallthrough case (F70.3 AC2).
    /// </summary>
    public string? Resolve(SegmentKind kind) => byKind.TryGetValue(kind, out var engine) ? engine : null;
}
