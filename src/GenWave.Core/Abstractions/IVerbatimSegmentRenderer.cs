using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Renders segment copy that is ALREADY AUTHORED — <see cref="ISegmentCopyWriter.WriteAsync"/> is
/// never called on this path, so no LLM can ever be involved (SPEC F144.2, "no LLM anywhere on the
/// path") — through the SAME production pipeline (caching, loudness, cue analysis, blurb-dir
/// routing) an ordinary <see cref="ITtsSegmentSource.RenderAsync"/> call uses.
///
/// <para>
/// <b>Why this is a separate seam, not a <see cref="SegmentRequest"/> field or an
/// <see cref="ISegmentCopyWriter"/> implementation (PLAN T341 review):</b> neither carrier can hold a
/// caller-supplied exact text without either widening the published <c>GenWave.Abstractions</c>
/// <see cref="SegmentRequest"/> record (ruled out — this task's own carry-forward) or routing every
/// render through the SAME copy-writer chain an LLM writer sits in front of (which is exactly the
/// involvement F144.2 forbids for a verbatim announcement). This port instead takes the already-
/// decided <see cref="SegmentCopy"/> directly, so the CALLER (never this seam) decides both the text
/// and whether it is fresh-per-airing.
/// </para>
/// </summary>
public interface IVerbatimSegmentRenderer
{
    /// <summary>
    /// Renders <paramref name="copy"/> for <paramref name="request"/> and returns a ready,
    /// loudness-measured, cached <see cref="MediaItem"/>, or <see langword="null"/> when rendering
    /// fails for any reason — the SAME never-throws-toward-the-caller contract
    /// <see cref="ITtsSegmentSource.RenderAsync"/> already carries.
    /// </summary>
    Task<MediaItem?> RenderAsync(SegmentRequest request, SegmentCopy copy, CancellationToken ct);
}
