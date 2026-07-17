namespace GenWave.Tts.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Returns a fixed line of copy for every request — proves a consumer sources its text from the seam.
/// <paramref name="freshPerAiring"/> defaults to false (template-style provenance) so existing callers
/// keep exercising the forever-cache path; pass true to simulate LLM-authored blurb copy (STORY-122).
/// </summary>
public sealed class FakeSegmentCopyWriter(string text, bool freshPerAiring = false) : ISegmentCopyWriter
{
    public Task<SegmentCopy> WriteAsync(SegmentRequest request, CancellationToken ct) =>
        Task.FromResult(new SegmentCopy(text, freshPerAiring));
}
