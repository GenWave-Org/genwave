namespace GenWave.Core.Domain;

/// <summary>
/// Carries the speech kind, resolved pronunciation rules, and speaking pace alongside text/voice for
/// <see cref="GenWave.Core.Abstractions.ITtsSynthesizer"/>'s kind-aware overload (SPEC F70.3, F97.6,
/// F98.1; STORY-191, STORY-252, STORY-255). <see cref="Kind"/> is null for any caller with no
/// <see cref="SegmentRequest"/> to draw one from (authored/safe segments, the admin preview
/// endpoint) — those callers use the plain <c>(text, voice, ct)</c> overload, which stamps
/// <c>Kind: null</c> here, so they see zero behavior change from this feature.
///
/// <see cref="Pace"/> and <see cref="Rules"/> widen this same seam (F96 "Make the DJs sound human").
/// Both are declared as defaulted properties rather than primary-constructor parameters — the
/// natural "no rules" default is a runtime-constructed empty collection, not a compile-time
/// constant, so it cannot be a parameter default — so every existing
/// <c>new TtsRenderContext(text, voice, kind)</c> construction site keeps compiling, unchanged,
/// with <see cref="Pace"/> = <c>1.0</c> ("engine default", <see cref="VoiceSpec.Pace"/>'s own
/// sentinel) and <see cref="Rules"/> = no rules. This is the exact F70.3 precedent <see cref="Kind"/>
/// already established: an implementation that has not opted into reading either new field is
/// reached through <see cref="GenWave.Core.Abstractions.ITtsSynthesizer"/>'s default interface
/// member, which forwards to the plain overload and never sees these facts at all — every existing
/// engine client and test fake compiles and behaves exactly as it did before this widening.
///
/// Persona facts must ride ON this context rather than being re-read from an ambient accessor
/// inside an engine adapter: a segment can render across a segment boundary after the on-air
/// persona has already flipped to the incoming DJ — the same failure the orchestration layer's
/// handoff context (F92.2) exists to prevent. See ARCHITECTURE.md "🗣️ Make the DJs sound human" →
/// "Carrying persona facts to the engine".
/// </summary>
/// <param name="Text">The already-copy-written spoken text to synthesize.</param>
/// <param name="Voice">TTS voice identifier passed through to the synthesizer.</param>
/// <param name="Kind">
/// The speech kind this render belongs to, when known — the TTS segment source (the only caller
/// with a <see cref="SegmentRequest"/> to draw one from) is the only stamper.
/// </param>
public sealed record TtsRenderContext(string Text, string Voice, SegmentKind? Kind)
{
    /// <summary>
    /// Speaking-rate multiplier the engine adapter sends as the OpenAI-compatible <c>speed</c>
    /// field (SPEC F98.1). Defaults to <c>1.0</c> — "engine default", <see cref="VoiceSpec.Pace"/>'s
    /// own sentinel — so a caller that never sets this renders exactly as it did before F98 existed.
    /// Resolving a persona's real pace onto this property is a later task's job (STORY-255); this
    /// type only carries the value forward.
    /// </summary>
    public double Pace { get; init; } = 1.0;

    /// <summary>
    /// The resolved pronunciation rule set for this render (SPEC F97), merged from station and
    /// persona sources elsewhere before reaching here. Defaults to no rules, so a caller that never
    /// sets this renders exactly as it did before F97 existed. Resolving the merged set onto this
    /// property — never re-reading it from an ambient accessor inside an adapter (F97.6) — is a
    /// later task's job (STORY-253); this type only carries the value forward.
    /// </summary>
    public IReadOnlyList<PronunciationRule> Rules { get; init; } = [];
}
