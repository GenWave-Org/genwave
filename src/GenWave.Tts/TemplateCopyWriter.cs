namespace GenWave.Tts;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The terminal <see cref="ISegmentCopyWriter"/> rung (SPEC F34.1): wraps
/// <see cref="PatterTemplateRenderer"/> verbatim. Pure string interpolation — no I/O, never fails
/// for any <see cref="SegmentRequest"/> (the renderer already handles a null <c>Track</c>).
/// </summary>
public sealed class TemplateCopyWriter(PatterTemplateRenderer renderer) : ISegmentCopyWriter
{
    public Task<SegmentCopy> WriteAsync(SegmentRequest request, CancellationToken ct) =>
        Task.FromResult(new SegmentCopy(renderer.Expand(request), FreshPerAiring: false));
}
