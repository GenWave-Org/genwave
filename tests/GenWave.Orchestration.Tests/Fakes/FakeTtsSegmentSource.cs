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

    /// <summary>
    /// When non-null, every rendered segment carries this measured <see cref="MediaItem.DurationMs"/>
    /// (gh-#253) — stands in for the real <c>TtsSegmentSource</c>'s cue-derived F66.1 stamp so specs
    /// can drive the Orchestrator's ObserveRendered feed. The default (<see langword="null"/>)
    /// mirrors a failed cue analysis: no duration, nothing observed.
    /// </summary>
    public int? DurationMs { get; set; }

    /// <summary>
    /// Per-request null override (STORY-243, PLAN T124) — narrower than <see cref="AlwaysReturnNull"/>'s
    /// blanket switch: lets a spec fail just ONE segment kind (e.g. a SignOff render, mirroring
    /// <c>TtsSegmentSource</c>'s own real F92.4 drop of non-LLM-authored handoff copy) while every
    /// other request still renders normally. <see langword="null"/> (the default) never overrides
    /// anything.
    /// </summary>
    public Func<SegmentRequest, bool>? ShouldReturnNull { get; set; }

    /// <summary>
    /// Per-request fault override (T124 review finding F6) — simulates a genuine synth outage
    /// (distinct from <see cref="ShouldReturnNull"/>'s "completed with null" shape) so a spec can pin
    /// <c>Orchestrator</c>'s drop-cause classification: a faulted render must log "render faulted",
    /// never "render returned null". <see langword="null"/> (the default) never overrides anything.
    /// </summary>
    public Func<SegmentRequest, bool>? ShouldThrow { get; set; }

    public async Task<MediaItem?> RenderAsync(SegmentRequest request, CancellationToken ct)
    {
        RenderCallCount++;
        LastRequest = request;
        Requests.Add(request);

        if (RenderDelay is { } delay)
            await Task.Delay(delay, ct);

        if (ShouldThrow?.Invoke(request) ?? false)
            throw new InvalidOperationException("Simulated TTS render fault (test double).");

        if (AlwaysReturnNull || (ShouldReturnNull?.Invoke(request) ?? false)) return null;

        var mediaId = $"tts:{request.Kind.ToString().ToLowerInvariant()}-{RenderCallCount}";
        var item = new MediaItem(
            mediaId,
            $"/tts/{mediaId}.wav",
            $"[{request.Kind}]",
            new Loudness(-23.0, -1.0, true),
            DurationMs: DurationMs,
            SegmentKind: request.Kind);

        return item;
    }
}
