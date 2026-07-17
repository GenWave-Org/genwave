using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// High-level TTS contract: a <see cref="SegmentRequest"/> becomes a ready, loudness-measured,
/// cached <see cref="MediaItem"/> (§0.1).
/// Returns null on any failure — NEVER throws toward the playout loop.
/// </summary>
public interface ITtsSegmentSource
{
    /// <summary>
    /// Renders the requested segment and returns a <see cref="MediaItem"/> that is immediately
    /// safe to queue, or null when rendering fails for any reason.
    /// </summary>
    Task<MediaItem?> RenderAsync(SegmentRequest request, CancellationToken ct);
}
