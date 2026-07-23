using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted TTS source double for orchestrator unit tests.  Returns a pre-built segment with a
/// <c>tts:</c>-prefixed MediaId, or null when <see cref="AlwaysReturnNull"/> is true.
/// When <see cref="RenderDelay"/> is set the task waits for that duration before completing,
/// simulating a slow render that may exceed the budget.
/// </summary>
sealed class FakeTtsSegmentSource : ITtsSegmentSource
{
    public bool AlwaysReturnNull { get; set; }
    public int RenderCallCount { get; private set; }
    public SegmentRequest? LastRequest { get; private set; }

    /// <summary>Every request seen, in call order — for specs that assert on a specific
    /// <see cref="SegmentKind"/> within a multi-segment unit (gh-#96).</summary>
    public List<SegmentRequest> Requests { get; } = [];

    /// <summary>When non-null, each RenderAsync waits this long before returning.</summary>
    public TimeSpan? RenderDelay { get; set; }

    public async Task<MediaItem?> RenderAsync(SegmentRequest request, CancellationToken ct)
    {
        RenderCallCount++;
        LastRequest = request;
        Requests.Add(request);

        if (RenderDelay is { } delay)
            await Task.Delay(delay, ct);

        if (AlwaysReturnNull) return null;

        var mediaId = $"tts:{request.Kind.ToString().ToLowerInvariant()}-{RenderCallCount}";
        var item = new MediaItem(
            mediaId,
            $"/tts/{mediaId}.wav",
            $"[{request.Kind}]",
            new Loudness(-23.0, -1.0, true));

        return item;
    }
}
