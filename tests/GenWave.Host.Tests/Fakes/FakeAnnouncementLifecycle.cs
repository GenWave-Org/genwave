using GenWave.Core.Abstractions;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Stateful double for <see cref="IAnnouncementLifecycle"/> (STORY-358/359, PLAN T343) — mirrors
/// <see cref="FakeAnnouncementStore"/>'s own "record every call, script every outcome" shape one seam
/// over. Shared across the lifecycle guardians' own spec files (the aired-confirmation drain, the
/// guardian sweep loop, the privacy-flip drain) rather than re-declared per file, since all three
/// exercise this same seam.
/// </summary>
sealed class FakeAnnouncementLifecycle : IAnnouncementLifecycle
{
    /// <summary>Every id <see cref="MarkAiredAsync"/> was asked to stamp aired, in call order.</summary>
    public List<long> MarkAiredCalls { get; } = [];

    /// <summary>Scripts <see cref="MarkAiredAsync"/>'s own collapse-count return per id — an id with
    /// no entry here defaults to 1 (the DDL's own "no collapse" default). An id listed in
    /// <see cref="AiredOutcomeIsNull"/> ignores this and answers <see langword="null"/> instead
    /// (already aired/re-armed/unknown).</summary>
    public Dictionary<long, int> CollapseCountByAnnouncementId { get; } = [];

    /// <summary>Ids for which <see cref="MarkAiredAsync"/> reports "no matching claimed row" —
    /// the total, idempotent-safe transition's own null outcome.</summary>
    public HashSet<long> AiredOutcomeIsNull { get; } = [];

    /// <summary>Every id <see cref="ReArmAsync"/> was asked to re-arm, in call order.</summary>
    public List<long> ReArmCalls { get; } = [];

    /// <summary>Ids for which <see cref="ReArmAsync"/> reports success (a row genuinely was
    /// <c>claimed</c>). Any id not listed here reports <see langword="false"/> — the "nothing to
    /// re-arm" total-transition default.</summary>
    public HashSet<long> ReArmSucceedsFor { get; } = [];

    /// <summary>Every <c>(grace, now)</c> pair <see cref="FindClaimedPastGraceAsync"/> was called
    /// with, in call order — proves the guardian sweep passes the SAME <c>now</c> it read once,
    /// never re-reading the clock between calls.</summary>
    public List<(TimeSpan Grace, DateTimeOffset Now)> FindClaimedPastGraceCalls { get; } = [];

    /// <summary>Scripts <see cref="FindClaimedPastGraceAsync"/>'s own return.</summary>
    public IReadOnlyList<long> ClaimedPastGraceResult { get; set; } = [];

    /// <summary>Every <c>now</c> <see cref="ExpireStaleAsync"/> was called with, in call order.</summary>
    public List<DateTimeOffset> ExpireStaleCalls { get; } = [];

    /// <summary>Scripts <see cref="ExpireStaleAsync"/>'s own return count.</summary>
    public int ExpireStaleResult { get; set; }

    /// <summary>Every reason <see cref="DeclineAllLiveAsync"/> was called with, in call order.</summary>
    public List<string> DeclineAllLiveCalls { get; } = [];

    /// <summary>The reason stamped by the MOST RECENT <see cref="DeclineAllLiveAsync"/> call, or
    /// <see langword="null"/> if it was never called.</summary>
    public string? LastDeclineReason { get; private set; }

    /// <summary>Seeds the ids <see cref="DeclineAllLiveAsync"/> should treat as currently
    /// <c>pending</c> — a scenario populates this to prove the flip declines them.</summary>
    public List<long> PendingIds { get; } = [];

    /// <summary>Seeds the ids <see cref="DeclineAllLiveAsync"/> should treat as currently
    /// <c>claimed</c> — a scenario populates this to prove the flip declines them too, alongside
    /// <see cref="PendingIds"/>, in the SAME bulk call.</summary>
    public List<long> ClaimedIds { get; } = [];

    /// <summary>Every id <see cref="DeclineAllLiveAsync"/> actually declined on its most recent call
    /// — <see cref="PendingIds"/> ∪ <see cref="ClaimedIds"/> at the moment it ran.</summary>
    public IReadOnlyList<long> DeclinedIds { get; private set; } = [];

    /// <summary>Every method name, in call order — proves ordering claims (e.g. the guardian sweep
    /// runs <see cref="ExpireStaleAsync"/> before <see cref="FindClaimedPastGraceAsync"/>) without
    /// needing a separate mock-framework call-sequence feature.</summary>
    public List<string> CallOrder { get; } = [];

    public Task<int?> MarkAiredAsync(long id, CancellationToken ct)
    {
        CallOrder.Add(nameof(MarkAiredAsync));
        MarkAiredCalls.Add(id);
        if (AiredOutcomeIsNull.Contains(id))
            return Task.FromResult((int?)null);

        return Task.FromResult((int?)CollapseCountByAnnouncementId.GetValueOrDefault(id, 1));
    }

    public Task<IReadOnlyList<long>> FindClaimedPastGraceAsync(TimeSpan grace, DateTimeOffset now, CancellationToken ct)
    {
        CallOrder.Add(nameof(FindClaimedPastGraceAsync));
        FindClaimedPastGraceCalls.Add((grace, now));
        return Task.FromResult(ClaimedPastGraceResult);
    }

    public Task<bool> ReArmAsync(long id, CancellationToken ct)
    {
        CallOrder.Add(nameof(ReArmAsync));
        ReArmCalls.Add(id);
        return Task.FromResult(ReArmSucceedsFor.Contains(id));
    }

    public Task<int> ExpireStaleAsync(DateTimeOffset now, CancellationToken ct)
    {
        CallOrder.Add(nameof(ExpireStaleAsync));
        ExpireStaleCalls.Add(now);
        return Task.FromResult(ExpireStaleResult);
    }

    public Task<int> DeclineAllLiveAsync(string reason, CancellationToken ct)
    {
        CallOrder.Add(nameof(DeclineAllLiveAsync));
        DeclineAllLiveCalls.Add(reason);
        LastDeclineReason = reason;

        var declined = new List<long>(PendingIds.Count + ClaimedIds.Count);
        declined.AddRange(PendingIds);
        declined.AddRange(ClaimedIds);
        DeclinedIds = declined;
        PendingIds.Clear();
        ClaimedIds.Clear();

        return Task.FromResult(declined.Count);
    }
}
