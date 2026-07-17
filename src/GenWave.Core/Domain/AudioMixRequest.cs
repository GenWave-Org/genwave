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
public sealed record AudioMixRequest(
    string VoicePath,
    BedSpec? Bed,
    AudioTags Tags,
    double BedDuckDb,
    double BedPadSeconds,
    string OutputPath);
