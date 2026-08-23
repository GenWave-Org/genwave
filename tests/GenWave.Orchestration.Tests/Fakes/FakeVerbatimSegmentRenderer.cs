using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IVerbatimSegmentRenderer"/> double for orchestrator unit tests (STORY-358,
/// PLAN T341). Returns a stable <c>tts:{n}</c>-prefixed <see cref="MediaItem"/> per call, or null when
/// <see cref="AlwaysReturnNull"/> is true — mirrors <see cref="FakeTtsSegmentSource"/>'s own shape one
/// seam over. This double NEVER touches <c>ISegmentCopyWriter</c>/an LLM — the same "zero LLM
/// involvement" contract the real <c>TtsSegmentSource.RenderAsync(SegmentRequest, SegmentCopy,
/// CancellationToken)</c> overload carries — so a spec proving "no LLM call occurred" simply asserts
/// the SHARED <see cref="FakeTtsSegmentSource"/> (the ordinary-kind seam) was never invoked for an
/// announcement request, never anything about this type.
/// </summary>
sealed class FakeVerbatimSegmentRenderer : IVerbatimSegmentRenderer
{
    public bool AlwaysReturnNull { get; set; }
    public int RenderCallCount { get; private set; }

    /// <summary>Every (request, copy) pair seen, in call order.</summary>
    public List<(SegmentRequest Request, SegmentCopy Copy)> Calls { get; } = [];

    public Task<MediaItem?> RenderAsync(SegmentRequest request, SegmentCopy copy, CancellationToken ct)
    {
        RenderCallCount++;
        Calls.Add((request, copy));

        if (AlwaysReturnNull) return Task.FromResult<MediaItem?>(null);

        var mediaId = $"tts:{RenderCallCount}";
        var item = new MediaItem(
            mediaId, $"/tts/{mediaId}.wav", $"[{request.Kind}]", new Loudness(-23.0, -1.0, true),
            SegmentKind: request.Kind);
        return Task.FromResult<MediaItem?>(item);
    }
}
