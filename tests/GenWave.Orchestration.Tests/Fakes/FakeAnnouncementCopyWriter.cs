using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IAnnouncementCopyWriter"/> double for orchestrator unit tests (STORY-358, PLAN
/// T342). <see cref="Reply"/> is returned verbatim from every call; <see langword="null"/> (the
/// default) simulates every SPEC F144.4 degrade trigger at once — an exhausted re-ask ladder, an
/// unreachable LLM, or a blown render budget all resolve to the SAME null signal at this seam, so a
/// fake proving the Orchestrator's own <c>??</c> fallback needs only script the ONE shared outcome,
/// not each trigger separately (those triggers are GenWave.Tts.Tests' own concern — see
/// Story358_AnnouncementCopyDiscipline.cs).
/// </summary>
sealed class FakeAnnouncementCopyWriter : IAnnouncementCopyWriter
{
    public string? Reply { get; set; }
    public int CallCount { get; private set; }

    /// <summary>Every (request, message) pair seen, in call order.</summary>
    public List<(SegmentRequest Request, string Message)> Calls { get; } = [];

    public Task<string?> WriteAnnouncementAsync(SegmentRequest request, string message, CancellationToken ct)
    {
        CallCount++;
        Calls.Add((request, message));
        return Task.FromResult(Reply);
    }
}
