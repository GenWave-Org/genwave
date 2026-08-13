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

    /// <summary>
    /// True when this render is an AUDITION — an operator proving a rule/persona/pace before it
    /// ever airs — rather than a real on-air render (SPEC F97.5, F126.1, F126.3; STORY-253 AC6,
    /// STORY-323, STORY-325). Defaults to <see langword="false"/>: every existing construction site
    /// (<c>TtsSegmentSource</c>'s on-air render, and — until it is widened onto this same context
    /// overload — <c>SafeSegmentAuthor</c>'s authoring, which today still uses the context-less
    /// overload entirely) keeps counting exactly as it always has.
    ///
    /// <para>
    /// Until PLAN T274, <c>Rules</c> above was ALWAYS empty for the admin preview
    /// (<c>POST /api/tts/preview</c>) by construction — no separate exclusion flag existed because
    /// none was needed (see <see cref="GenWave.Tts.PronunciationRuleHitReporter"/>'s pre-T274
    /// history). T274 resolves real rules onto a preview's context (the audition must sound like
    /// air), which makes that construction-based exclusion false — <see cref="IsAudition"/> is the
    /// flag that now carries the SAME "never counts, never logs a hit" posture explicitly.
    /// <see cref="GenWave.Tts.PronunciationRuleHitReporter"/> is the ONE seam that reads it: it
    /// counts and logs a rule hit for every render EXCEPT one that carries
    /// <see cref="IsAudition"/> = <see langword="true"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Ruling (T274, for T276's authoring task):</b> the identical posture applies to
    /// <c>SafeSegmentAuthor</c> once it is widened onto this context overload — authoring is not
    /// airing, the same "fired means aired" principle SPEC F97.5/PLAN T142 already settled for the
    /// on-air render path. Whichever task widens <c>SafeSegmentAuthor</c> onto the context overload
    /// must set <see cref="IsAudition"/> = <see langword="true"/> there too, reusing this SAME flag
    /// rather than inventing a second exclusion mechanism.
    /// </para>
    /// </summary>
    public bool IsAudition { get; init; }
}
