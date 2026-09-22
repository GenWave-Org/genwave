namespace GenWave.Core.Domain;

/// <summary>
/// Everything <see cref="Abstractions.IAudioMixer"/> needs to render one safe-segment artifact
/// (F27.2, F27.4, F27.5). Config-free by design — callers resolve <c>Station:Safe:*</c> values and
/// pass them in; the mixer itself never reads configuration or the database.
/// </summary>
/// <param name="VoicePath">Absolute path to the rendered voice clip.</param>
/// <param name="Bed">The bed to mix under the voice, or null for a voice-only render.</param>
/// <param name="Tags">Brand tags embedded into the output artifact's metadata.</param>
/// <param name="BedDuckDb">
/// Bed attenuation in dB relative to the voice (e.g. -12.0). Ignored when <see cref="Bed"/> is null.
/// </param>
/// <param name="BedPadSeconds">
/// Lead-in/tail-out padding in seconds around the voice. Ignored when <see cref="Bed"/> is null.
/// </param>
/// <param name="OutputPath">Absolute path the rendered wav artifact is written to.</param>
/// <param name="BedFadeSeconds">
/// SPEC F168.4; STORY-403; PLAN T416 — how long, in seconds, the bed fades to silence across the
/// mix's trailing tail. Ignored when <see cref="Bed"/> is null. Defaulted to 0.0 (no fade) so every
/// existing caller of this record keeps compiling and behaving unchanged — <c>GenWave.Ads</c>'s own
/// ad render path is the one caller that varies it (plain text, not a <c>cref</c>: this project is
/// never referenced by GenWave.Ads, so the reverse reference cannot exist either).
/// </param>
/// <param name="TargetLufs">
/// SPEC F196.1; STORY-463; PLAN T542 — the station's own loudness target (<c>Loudness:TargetLufs</c>),
/// used as the bed's reference level ONLY when the voice itself is unmeasurable (gh-#746's own voice
/// reference is preferred whenever it exists — see <c>GenWave.Loudness.FfmpegAudioMixer.ResolveBedGainDb</c>,
/// plain text not a <c>cref</c>: this project is never referenced by GenWave.Loudness). Ignored when
/// <see cref="Bed"/> is null. Defaulted to −16.0 — <c>LoudnessOptions.TargetLufs</c>'s own default
/// (plain text, not a <c>cref</c>: this project is never referenced by GenWave.Host) — so every
/// existing caller of this record keeps compiling and behaving unchanged.
/// </param>
public sealed record AudioMixRequest(
    string VoicePath,
    BedSpec? Bed,
    AudioTags Tags,
    double BedDuckDb,
    double BedPadSeconds,
    string OutputPath,
    double BedFadeSeconds = 0.0,
    double TargetLufs = -16.0);
