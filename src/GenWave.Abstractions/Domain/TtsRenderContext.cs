namespace GenWave.Core.Domain;

/// <summary>
/// Carries the speech kind alongside text/voice for
/// <see cref="GenWave.Core.Abstractions.ITtsSynthesizer"/>'s kind-aware overload (SPEC F70.3,
/// STORY-191). <see cref="Kind"/> is null for any caller with no <see cref="SegmentRequest"/> to
/// draw one from (authored/safe segments, the admin preview endpoint) — those callers use the plain
/// <c>(text, voice, ct)</c> overload, which stamps <c>Kind: null</c> here, so they see zero behavior
/// change from this feature.
/// </summary>
/// <param name="Text">The already-copy-written spoken text to synthesize.</param>
/// <param name="Voice">TTS voice identifier passed through to the synthesizer.</param>
/// <param name="Kind">
/// The speech kind this render belongs to, when known — the TTS segment source (the only caller
/// with a <see cref="SegmentRequest"/> to draw one from) is the only stamper.
/// </param>
public sealed record TtsRenderContext(string Text, string Voice, SegmentKind? Kind);
