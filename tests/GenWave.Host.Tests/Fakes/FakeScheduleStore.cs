using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// Stateful <see cref="IScheduleStore"/> double for <c>ScheduleController</c>'s wire-layer specs
/// (STORY-240/247, PLAN T122). Deliberately NOT a re-implementation of
/// <see cref="GenWave.MediaLibrary.Station.ScheduleRepository"/>'s own per-cell validation — that
/// validation is proven against the real repository in
/// <c>GenWave.MediaLibrary.Tests/Specs/Story240_ScheduleStore.cs</c>. This double proves the OTHER
/// half instead: that a wire round trip (PUT then GET) carries every field faithfully and that the
/// store's own contract (a rejected write changes nothing, a successful write assigns ids and raises
/// <see cref="WeekChanged"/>) is what the controller relies on.
///
/// <para>
/// Default (unscripted) <see cref="ReplaceWeekAsync"/> behavior: accept whatever is submitted,
/// assign each row a fresh id, replace <see cref="LoadWeekAsync"/>'s answer with exactly that set —
/// mirrors <see cref="ScheduleRepository"/>'s own "delete everything, insert what was submitted"
/// shape (SPEC F91.8) without judging any row's validity. A scenario that needs to prove the
/// controller's 400/409 handling scripts <see cref="NextReplaceResult"/> instead: the NEXT
/// <see cref="ReplaceWeekAsync"/> call returns that value verbatim (and, for
/// <see cref="ScheduleReplaceResult.ValidationFailed"/>, leaves <see cref="LoadWeekAsync"/>'s answer
/// untouched — exactly IScheduleStore's own documented "nothing changes" contract for a rejection).
/// </para>
/// </summary>
sealed class FakeScheduleStore(ScheduleWeekSnapshot? initial = null) : IScheduleStore
{
    ScheduleWeekSnapshot current = initial ?? new ScheduleWeekSnapshot([]);
    long nextId = 1;

    /// <summary>Scripts the NEXT <see cref="ReplaceWeekAsync"/> call's outcome verbatim, bypassing
    /// the default echo-and-assign-ids behavior. Cleared after one use.</summary>
    public ScheduleReplaceResult? NextReplaceResult { get; set; }

    /// <summary>Scripts the NEXT <see cref="ReplaceWeekAsync"/> call to throw this
    /// <see cref="PostgresException"/> instead of returning — proves
    /// <c>ScheduleController</c>'s own PostgresException handling (the persona-race 409 for a
    /// 23503 foreign-key violation, the generic 500 for everything else) without a real Postgres
    /// fixture, which this project has none of. Checked before <see cref="NextReplaceResult"/>;
    /// cleared after one use.</summary>
    public PostgresException? NextThrow { get; set; }

    public int ReplaceWeekAsyncCallCount { get; private set; }

    /// <summary>What <see cref="GetSlotsByShowIdAsync"/> answers for a given show id (PLAN T240's
    /// show delete guard) — keyed by showId so a scenario can seed the exact referencing blocks a
    /// <see cref="ShowWriteResult.Referenced"/>-scripted delete should be named by, without
    /// re-deriving <c>ScheduleRepository</c>'s own SQL. Unseeded ids answer empty, never an error.</summary>
    public Dictionary<long, IReadOnlyList<ScheduledSlot>> SlotsByShowId { get; } = [];

    /// <summary>Counts every <see cref="LoadWeekAsync"/> call (SPEC F93.4, STORY-244, PLAN T125) — the
    /// structural proof that <see cref="GenWave.Orchestration.CachingScheduleResolver.TryGetCurrent"/>
    /// never reloads: only an explicit <see cref="GenWave.Orchestration.CachingScheduleResolver.ResolveAsync"/>
    /// call (production's per-unit warm-up, or a test's one-time equivalent) should ever advance this.</summary>
    public int LoadWeekAsyncCallCount { get; private set; }

    public event Action? WeekChanged;

    public Task<ScheduleWeekSnapshot> LoadWeekAsync(CancellationToken ct)
    {
        LoadWeekAsyncCallCount++;
        return Task.FromResult(current);
    }

    public Task<ScheduleReplaceResult> ReplaceWeekAsync(
        IReadOnlyList<ScheduleSegment> week, string? expectedVersion, CancellationToken ct)
    {
        ReplaceWeekAsyncCallCount++;

        if (NextThrow is { } toThrow)
        {
            NextThrow = null;
            throw toThrow;
        }

        // Mirrors ScheduleRepository's gh-#255 staleness guard: a non-null expectedVersion that no
        // longer matches the stored week's content fingerprint rejects the replace and changes
        // nothing — proven against the real repository in Story240_ScheduleStore; this double only
        // has to reproduce the contract for the controller's wire specs.
        if (expectedVersion is not null)
        {
            var storedVersion = ScheduleWeekVersion.Compute(current.Segments);
            if (storedVersion != expectedVersion)
                return Task.FromResult<ScheduleReplaceResult>(
                    new ScheduleReplaceResult.VersionConflict(storedVersion));
        }

        if (NextReplaceResult is { } scripted)
        {
            NextReplaceResult = null;

            // Mirrors IScheduleStore's own contract: WeekChanged fires only on a successful
            // replace, never on ValidationFailed — and only Replaced ever changes what the next
            // LoadWeekAsync answers.
            if (scripted is ScheduleReplaceResult.Replaced replaced)
            {
                current = replaced.Snapshot;
                WeekChanged?.Invoke();
            }

            return Task.FromResult(scripted);
        }

        var stored = week.Select(segment => segment with { Id = nextId++ }).ToList();
        current = new ScheduleWeekSnapshot(stored);
        WeekChanged?.Invoke();
        return Task.FromResult<ScheduleReplaceResult>(new ScheduleReplaceResult.Replaced(current));
    }

    public Task<IReadOnlyList<ScheduledSlot>> GetSlotsByShowIdAsync(long showId, CancellationToken ct) =>
        Task.FromResult(SlotsByShowId.TryGetValue(showId, out var slots) ? slots : []);

    /// <summary>Scripts the NEXT <see cref="AssignShowAsync"/> call's outcome verbatim. Cleared after
    /// one use. Deliberately NOT a reimplementation of <see cref="GenWave.MediaLibrary.Station.ScheduleRepository"/>'s
    /// own F119.2 run-span algorithm (that algorithm is proven for real, against a real Postgres
    /// fixture, in <c>GenWave.MediaLibrary.Tests/Specs/Story313_ScheduleShowAssignment.cs</c> — a
    /// reimplementation here would be a lookalike double, mirrors this class's own posture toward
    /// ReplaceWeekAsync's per-cell validation). <see cref="ScheduleController.AssignShow"/>'s own specs
    /// only need this double to prove the WIRE mapping of a real <see cref="ShowAssignResult"/>.</summary>
    public ShowAssignResult? NextAssignShowResult { get; set; }

    /// <summary>Every <see cref="AssignShowAsync"/> call's own arguments, in call order — lets a spec
    /// assert the controller passed <c>blockId</c>/<c>showId</c>/<c>applyToRun</c> through unchanged
    /// (narrow-to-one, clear) without this double having to judge them itself.</summary>
    public List<(long BlockId, long? ShowId, bool ApplyToRun)> AssignShowAsyncCalls { get; } = [];

    public Task<ShowAssignResult> AssignShowAsync(long blockId, long? showId, bool applyToRun, CancellationToken ct)
    {
        AssignShowAsyncCalls.Add((blockId, showId, applyToRun));

        if (NextAssignShowResult is { } scripted)
        {
            NextAssignShowResult = null;
            return Task.FromResult(scripted);
        }

        return Task.FromResult<ShowAssignResult>(
            new ShowAssignResult.Assigned([blockId], ScheduleWeekVersion.Compute(current.Segments)));
    }
}
