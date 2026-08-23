using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IAnnouncementSource"/> double for orchestrator unit tests (STORY-358, PLAN
/// T341). <see cref="Pending"/> is a FIFO queue — <see cref="ClaimDeliverableAsync"/> dequeues up to
/// <c>max</c> items oldest-first, mirroring the real <c>AnnouncementRepository.ClaimOldestAsync</c>'s
/// own claim order, and NEVER hands the same item back twice (a genuine dequeue, not a peek) — the
/// same "claiming is a state transition" contract the real seam's own remarks require.
/// <see cref="RefuseVend"/> stands in for SPEC F145.2's SpectatorMode refusal (the real behavior lives
/// behind a Host-side decorator this test assembly cannot reach) — while true, every claim reads back
/// empty without touching <see cref="Pending"/> at all, exactly like the real refusal.
/// <see cref="Throw"/> (T341 review finding F1) simulates the claim itself faulting — a Host-side
/// decorator's own DB round trip or options read, either of which can throw — so a spec can pin
/// <c>Orchestrator</c>'s own fault-isolation: the unit still assembles normally, only the announcement
/// step goes dark for this one pull.
/// </summary>
sealed class FakeAnnouncementSource : IAnnouncementSource
{
    public Queue<AnnouncementItem> Pending { get; } = new();
    public bool RefuseVend { get; set; }
    public bool Throw { get; set; }
    public int ClaimCallCount { get; private set; }

    /// <summary>Every <c>max</c> value this fake was actually called with, in call order — lets a
    /// spec pin the CALLER's own vend ceiling without hard-coding the Orchestrator's constant.</summary>
    public List<int> MaxRequested { get; } = [];

    public Task<IReadOnlyList<AnnouncementItem>> ClaimDeliverableAsync(int max, CancellationToken ct)
    {
        ClaimCallCount++;
        MaxRequested.Add(max);

        if (Throw)
            throw new InvalidOperationException("Simulated announcement claim fault (test double).");

        if (RefuseVend)
            return Task.FromResult<IReadOnlyList<AnnouncementItem>>([]);

        var claimed = new List<AnnouncementItem>();
        while (claimed.Count < max && Pending.Count > 0)
            claimed.Add(Pending.Dequeue());

        return Task.FromResult<IReadOnlyList<AnnouncementItem>>(claimed);
    }
}
