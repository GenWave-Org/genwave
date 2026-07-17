using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Renders the final safe-segment artifact in a single offline ffmpeg pass — voice alone, or voice
/// mixed over a ducked, padded bed — with brand tags embedded in the output file's metadata (F27.2,
/// F27.4, F27.5). This is the offline path — it must never run on the real-time playout loop.
/// </summary>
public interface IAudioMixer
{
    /// <summary>
    /// Renders <paramref name="request"/> to <see cref="AudioMixRequest.OutputPath"/>.
    /// Throws on failure (missing/unreadable input, ffmpeg non-zero exit) and leaves no partial
    /// output file behind.
    /// </summary>
    Task MixAsync(AudioMixRequest request, CancellationToken ct);
}
