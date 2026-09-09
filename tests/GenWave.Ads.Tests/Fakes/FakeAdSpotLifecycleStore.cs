using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// A full, stateful <see cref="IAdSpotStore"/> double (PLAN T402) — unlike <see cref="FakeAdSpotStore"/>
/// (deliberately narrow, T401's own render-only spec double, every other member throwing), this one is
/// the WORKER's own double: <see cref="AdSpotWorker"/>/<see cref="AdSpotLifecycleGuardianService"/>
/// exercise the FULL state machine across one or many ticks (create → approve/draft → claim → render →
/// ready|failed, retire, the guardian re-arm), so a throw-on-unused-member shape would fail every real
/// scenario immediately. An in-memory list plus a monotonically increasing <c>xmin</c>-shaped
/// <see cref="AdSpot.Version"/> string is enough to mirror every transition
/// <see cref="GenWave.MediaLibrary.Station.AdSpotRepository"/> enforces in SQL, in plain C#.
/// </summary>
public sealed class FakeAdSpotLifecycleStore : IAdSpotStore
{
    readonly List<AdSpot> spots = [];
    long nextId = 1;
    int nextXmin = 1;

    public IReadOnlyList<AdSpot> Spots => spots;

    /// <summary>Wired to <see cref="FakeSponsorStore.IsPaused"/> by
    /// <see cref="AdSpotWorkerHarness.Build"/> (PLAN T440 ruling), the SAME single-source-of-
    /// pause-truth seam <see cref="FakeAdBriefStore.ExcludePausedSponsors"/> already carries one file
    /// over — a Story420 scenario that only ever calls <c>harness.Sponsors.Pause(id)</c> reaches this
    /// fake's own <see cref="CountStockGeneratedAsync"/> and <see cref="ListAiringExclusionsAsync"/>
    /// through the wiring. Defaults to "nobody paused" so a scenario that never calls
    /// <see cref="ExcludePausedSponsors"/> keeps this fake's pre-T440 behavior exactly.</summary>
    Func<long, bool> isPausedSponsor = _ => false;

    /// <summary>Wires this store's own paused-sponsor reads (<see cref="CountStockGeneratedAsync"/>,
    /// <see cref="ListAiringExclusionsAsync"/>) to a paused-sponsor check — the
    /// <see cref="FakeAdBriefStore.ExcludePausedSponsors"/> precedent, applied here.</summary>
    public FakeAdSpotLifecycleStore ExcludePausedSponsors(Func<long, bool> isPaused)
    {
        isPausedSponsor = isPaused;
        return this;
    }

    /// <summary>Sponsor id → current name, read by <see cref="UpdateAsync"/> to mirror
    /// <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.UpdateAsync"/>'s own "refresh
    /// <c>sponsor_name</c> from <c>station.sponsor</c>" behavior when <see cref="AdSpotEdit.SponsorId"/>
    /// is set (PLAN T432, SPEC F171.7). An unseeded id leaves the row's existing
    /// <see cref="AdSpot.SponsorName"/> unchanged rather than nulling it out — no spec exercising a
    /// sponsor-changing edit exists yet on this fake, so this stays a documented, narrow gap rather
    /// than a real subquery.</summary>
    public Dictionary<long, string> SponsorNames { get; } = [];

    public int CreateCallCount { get; private set; }
    public int RetireCallCount { get; private set; }
    public int ReArmCallCount { get; private set; }
    public int MarkReadyCallCount { get; private set; }
    public int MarkFailedCallCount { get; private set; }
    public int ClaimCallCount { get; private set; }
    public int StampVoicePlanCallCount { get; private set; }
    public int StampBedCallCount { get; private set; }

    /// <summary>Every argument a caller passed to <see cref="CreateAsync"/>, in call order — lets a
    /// spec assert the exact <see cref="AdSource"/>/<see cref="AdState"/> generation actually chose
    /// without re-deriving it from the resulting row alone.</summary>
    public List<NewAdSpot> CreateRequests { get; } = [];

    string NextVersion() => (nextXmin++).ToString();

    /// <summary>Seeds one fully custom row — the escape hatch for a spec that needs exact control over
    /// every column (e.g. <see cref="AdSpot.Script"/> for a render-path test).</summary>
    public FakeAdSpotLifecycleStore AddExisting(AdSpot spot)
    {
        spots.Add(spot);
        if (spot.Id >= nextId)
            nextId = spot.Id + 1;
        return this;
    }

    /// <summary>Seeds one row with sane defaults for everything a scenario does not care about — the
    /// common case (state/source/media id/age are the only things most specs vary).</summary>
    public FakeAdSpotLifecycleStore AddSpot(
        long id, AdState state, AdSource source = AdSource.Llm, long? mediaId = null,
        DateTime? stateChangedAt = null, string? failReason = null, string? packSlug = null,
        long sponsorId = 1, string sponsorName = "Acme",
        string script = "ANNOUNCER: Come on down.\nVOICE1: Prices you won't believe.")
    {
        var stamp = stateChangedAt ?? DateTime.UtcNow;
        return AddExisting(new AdSpot(
            id, sponsorId, sponsorName, $"{sponsorName} spot", Brief: null, script, source, packSlug,
            SpotSeconds: 30, VoicePlan: null, BedMediaId: null, state, failReason, mediaId, Generation: 1,
            CreatedAt: stamp, StateChangedAt: stamp, RenderedAt: state == AdState.Ready ? stamp : null,
            RetiredAt: state == AdState.Retired ? stamp : null, Version: NextVersion()));
    }

    public Task<AdSpot> CreateAsync(NewAdSpot spot, CancellationToken ct)
    {
        CreateCallCount++;
        CreateRequests.Add(spot);

        var now = DateTime.UtcNow;
        var sponsorName = SponsorNames.GetValueOrDefault(spot.SponsorId, "Acme");
        var created = new AdSpot(
            nextId++, spot.SponsorId, sponsorName, spot.Title, spot.Brief, spot.Script, spot.Source,
            spot.PackSlug, spot.SpotSeconds, spot.VoicePlan, spot.BedMediaId, spot.InitialState,
            spot.FailReason, MediaId: null, Generation: 1, now, now, RenderedAt: null, RetiredAt: null,
            Version: NextVersion());
        spots.Add(created);
        return Task.FromResult(created);
    }

    public Task<AdSpotTransitionOutcome> ApproveAsync(long id, string expectedVersion, CancellationToken ct) =>
        Task.FromResult(GuardedTransition(id, expectedVersion, [AdState.Draft], AdState.Approved));

    public Task<AdSpotTransitionOutcome> RetryAsync(long id, string expectedVersion, CancellationToken ct) =>
        Task.FromResult(GuardedTransition(id, expectedVersion, [AdState.Failed], AdState.Approved, clearFailReason: true));

    public Task<AdSpotTransitionOutcome> RetireAsync(long id, string expectedVersion, CancellationToken ct)
    {
        RetireCallCount++;
        return Task.FromResult(
            GuardedTransition(
                id, expectedVersion,
                [AdState.Ready, AdState.Draft, AdState.Approved, AdState.Failed],
                AdState.Retired, stampRetired: true, clearFailReason: true));
    }

    public Task<AdSpot?> GetByIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(spots.FirstOrDefault(s => s.Id == id));

    public Task<AdSpotTransitionOutcome> UpdateAsync(long id, AdSpotEdit edit, string expectedVersion, CancellationToken ct)
    {
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0)
            return Task.FromResult(new AdSpotTransitionOutcome(AdSpotWriteResult.NotFound, null));

        var current = spots[index];
        var legalFromState = current.State is AdState.Draft or AdState.Failed;
        if (!string.Equals(current.Version, expectedVersion, StringComparison.Ordinal) || !legalFromState)
            return Task.FromResult(new AdSpotTransitionOutcome(AdSpotWriteResult.Conflict, null));

        var updated = current with
        {
            SponsorId = edit.SponsorId ?? current.SponsorId,
            SponsorName = edit.SponsorId is long newSponsorId
                ? SponsorNames.GetValueOrDefault(newSponsorId, current.SponsorName)
                : current.SponsorName,
            Title = edit.Title ?? current.Title,
            Brief = edit.Brief ?? current.Brief,
            Script = edit.Script ?? current.Script,
            VoicePlan = edit.VoicePlan ?? current.VoicePlan,
            SpotSeconds = edit.SpotSeconds ?? current.SpotSeconds,
            BedMediaId = edit.BedMediaId ?? current.BedMediaId,
            Version = NextVersion(),
        };
        spots[index] = updated;
        return Task.FromResult(new AdSpotTransitionOutcome(AdSpotWriteResult.Updated, updated));
    }

    public Task<AdSpot?> ClaimNextApprovedAsync(CancellationToken ct)
    {
        ClaimCallCount++;
        var candidate = spots
            .Where(s => s.State == AdState.Approved)
            .OrderBy(s => s.StateChangedAt).ThenBy(s => s.Id)
            .FirstOrDefault();
        if (candidate is null)
            return Task.FromResult<AdSpot?>(null);

        var updated = Replace(candidate.Id, s => s with
        {
            State = AdState.Rendering, StateChangedAt = DateTime.UtcNow, Version = NextVersion(),
        });
        return Task.FromResult<AdSpot?>(updated);
    }

    /// <summary>
    /// Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.StampVoicePlanSql"/>'s own two
    /// guards in plain C#: never-overwrite (<c>current.VoicePlan ?? voicePlanJson</c>, the coalesce) and
    /// rendering-only (a row not currently <see cref="AdState.Rendering"/> returns
    /// <see langword="null"/>, no state mutated) — <see cref="StampVoicePlanCallCount"/> counts every
    /// INVOCATION regardless of outcome, so a spec asserting "the worker never even calls this for an
    /// owner draft that already carries a plan" (PLAN T415 review R11(b)) can tell that apart from "it
    /// called in and the coalesce declined to overwrite".
    /// </summary>
    public Task<AdSpot?> StampVoicePlanIfNullAsync(long id, string voicePlanJson, CancellationToken ct)
    {
        StampVoicePlanCallCount++;
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0 || spots[index].State != AdState.Rendering)
            return Task.FromResult<AdSpot?>(null);

        var updated = Replace(id, s => s with { VoicePlan = s.VoicePlan ?? voicePlanJson, Version = NextVersion() });
        return Task.FromResult<AdSpot?>(updated);
    }

    /// <summary>
    /// Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.StampBedSql"/>'s own two guards
    /// in plain C#, the SAME shape <see cref="StampVoicePlanIfNullAsync"/> already keeps for the voice
    /// plan a member above: never-overwrite (<c>current.BedMediaId ?? bedMediaId</c>, the coalesce) and
    /// rendering-only (a row not currently <see cref="AdState.Rendering"/> returns
    /// <see langword="null"/>, no state mutated) — <see cref="StampBedCallCount"/> counts every
    /// INVOCATION regardless of outcome, so a spec asserting "the worker never even calls this for a
    /// spot that already carries a bed" (PLAN T416 review R3) can tell that apart from "it called in
    /// and the coalesce declined to overwrite".
    /// </summary>
    public Task<AdSpot?> StampBedIfNullAsync(long id, long bedMediaId, CancellationToken ct)
    {
        StampBedCallCount++;
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0 || spots[index].State != AdState.Rendering)
            return Task.FromResult<AdSpot?>(null);

        var updated = Replace(id, s => s with { BedMediaId = s.BedMediaId ?? bedMediaId, Version = NextVersion() });
        return Task.FromResult<AdSpot?>(updated);
    }

    public Task<bool> MarkReadyAsync(long id, long mediaId, CancellationToken ct)
    {
        MarkReadyCallCount++;
        return Task.FromResult(TryTotalTransition(id, AdState.Rendering, s => s with
        {
            State = AdState.Ready, MediaId = mediaId, RenderedAt = DateTime.UtcNow,
            StateChangedAt = DateTime.UtcNow, Version = NextVersion(),
        }));
    }

    public Task<bool> MarkFailedAsync(long id, string failReason, CancellationToken ct)
    {
        MarkFailedCallCount++;
        return Task.FromResult(TryTotalTransition(id, AdState.Rendering, s => s with
        {
            State = AdState.Failed, FailReason = failReason, StateChangedAt = DateTime.UtcNow, Version = NextVersion(),
        }));
    }

    public Task<bool> ReArmAsync(long id, CancellationToken ct)
    {
        ReArmCallCount++;
        return Task.FromResult(TryTotalTransition(id, AdState.Rendering, s => s with
        {
            State = AdState.Approved, StateChangedAt = DateTime.UtcNow, Version = NextVersion(),
        }));
    }

    public Task<AdSpotPage> ListByStateAsync(AdState? state, long? sponsorId, int limit, int offset, CancellationToken ct)
    {
        var filtered = state is null ? spots.AsEnumerable() : spots.Where(s => s.State == state);
        if (sponsorId is not null)
            filtered = filtered.Where(s => s.SponsorId == sponsorId.Value);
        var ordered = filtered.OrderByDescending(s => s.StateChangedAt).ThenByDescending(s => s.Id).ToList();
        var page = ordered.Skip(Math.Max(0, offset)).Take(limit <= 0 ? 1 : limit).ToList();
        return Task.FromResult(new AdSpotPage(page, ordered.Count));
    }

    public Task<int> CountStockGeneratedAsync(CancellationToken ct) =>
        Task.FromResult(spots.Count(s =>
            (s.State is AdState.Draft or AdState.Approved or AdState.Rendering or AdState.Ready)
            && (s.Source is AdSource.Llm or AdSource.Pack)
            && !IsPaused(s.SponsorId)));

    /// <summary>The wired paused-sponsor check (a Story420 scenario reaches this through
    /// <c>harness.Sponsors.Pause(id)</c> alone, PLAN T440 ruling).</summary>
    bool IsPaused(long sponsorId) => isPausedSponsor(sponsorId);

    public Task<IReadOnlyList<AdSpot>> ListReadyOlderThanAsync(TimeSpan age, CancellationToken ct)
    {
        var threshold = DateTime.UtcNow - age;
        IReadOnlyList<AdSpot> candidates = spots
            .Where(s => s.State == AdState.Ready && s.Source != AdSource.Owner && s.StateChangedAt < threshold)
            .OrderBy(s => s.StateChangedAt).ThenBy(s => s.Id)
            .ToList();
        return Task.FromResult(candidates);
    }

    public Task<IReadOnlyList<long>> FindRenderingPastGraceAsync(TimeSpan grace, DateTimeOffset now, CancellationToken ct)
    {
        var threshold = now - grace;
        IReadOnlyList<long> ids = spots
            .Where(s => s.State == AdState.Rendering && new DateTimeOffset(s.StateChangedAt, TimeSpan.Zero) < threshold)
            .OrderBy(s => s.StateChangedAt).ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToList();
        return Task.FromResult(ids);
    }

    AdSpotTransitionOutcome GuardedTransition(
        long id, string expectedVersion, AdState[] fromStates, AdState toState,
        bool stampRetired = false, bool clearFailReason = false)
    {
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0)
            return new AdSpotTransitionOutcome(AdSpotWriteResult.NotFound, null);

        var current = spots[index];
        if (!string.Equals(current.Version, expectedVersion, StringComparison.Ordinal) || !fromStates.Contains(current.State))
            return new AdSpotTransitionOutcome(AdSpotWriteResult.Conflict, null);

        var now = DateTime.UtcNow;
        var updated = current with
        {
            State = toState,
            StateChangedAt = now,
            RetiredAt = stampRetired ? now : current.RetiredAt,
            FailReason = clearFailReason ? null : current.FailReason,
            Version = NextVersion(),
        };
        spots[index] = updated;
        return new AdSpotTransitionOutcome(AdSpotWriteResult.Updated, updated);
    }

    /// <summary>The system-driven "total" transition shape every <c>ClaimNextApprovedAsync</c>/
    /// <c>MarkReadyAsync</c>/<c>MarkFailedAsync</c>/<c>ReArmAsync</c> call above shares — no xmin, a
    /// row not currently in <paramref name="fromState"/> reports <see langword="false"/>, never
    /// throws (mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository"/>'s own guarded
    /// <c>WHERE</c> shape exactly).</summary>
    bool TryTotalTransition(long id, AdState fromState, Func<AdSpot, AdSpot> apply)
    {
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0 || spots[index].State != fromState)
            return false;

        spots[index] = apply(spots[index]);
        return true;
    }

    AdSpot Replace(long id, Func<AdSpot, AdSpot> apply)
    {
        var index = spots.FindIndex(s => s.Id == id);
        var updated = apply(spots[index]);
        spots[index] = updated;
        return updated;
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ListAiringExclusionsAsync"/>
    /// in plain C# (PLAN T432): a <see cref="AdState.Ready"/> row's <see cref="AdSpot.MediaId"/> is
    /// withheld when <see cref="IsPaused"/> reports its sponsor paused, or when its sponsor also owns
    /// any spot among the first <paramref name="window"/> entries of <paramref name="recentMediaIds"/>.</summary>
    public Task<IReadOnlyList<long>> ListAiringExclusionsAsync(
        IReadOnlyList<long> recentMediaIds, int window, CancellationToken ct)
    {
        var recentWindow = recentMediaIds.Take(Math.Max(0, window)).ToHashSet();
        var recentSponsorIds = spots
            .Where(s => s.MediaId is long mediaId && recentWindow.Contains(mediaId))
            .Select(s => s.SponsorId)
            .ToHashSet();

        var excluded = new List<long>();
        foreach (var spot in spots)
        {
            if (spot.State == AdState.Ready && spot.MediaId is long readyMediaId &&
                (IsPaused(spot.SponsorId) || recentSponsorIds.Contains(spot.SponsorId)))
            {
                excluded.Add(readyMediaId);
            }
        }
        return Task.FromResult<IReadOnlyList<long>>(excluded);
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.StampJobAsync"/> in
    /// plain C# (PLAN T432): guarded on <c>JobKind is null</c> — an already-claimed row reports
    /// <see cref="AdSpotJobStampResult.Busy"/> rather than stealing the claim.</summary>
    public Task<AdSpotJobStampOutcome> StampJobAsync(long id, string kind, CancellationToken ct)
    {
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0)
            return Task.FromResult(new AdSpotJobStampOutcome(AdSpotJobStampResult.NotFound, null));

        if (spots[index].JobKind is not null)
            return Task.FromResult(new AdSpotJobStampOutcome(AdSpotJobStampResult.Busy, null));

        var updated = Replace(id, s => s with
        {
            JobKind = kind, JobStartedAt = DateTime.UtcNow, JobError = null, Version = NextVersion(),
        });
        return Task.FromResult(new AdSpotJobStampOutcome(AdSpotJobStampResult.Stamped, updated));
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ClearJobAsync"/> in
    /// plain C# (PLAN T432): total by id — clearing an already-clear job is a harmless no-op, not a
    /// conflict; reports <see langword="false"/> only when no row exists.</summary>
    public Task<bool> ClearJobAsync(long id, string? error, CancellationToken ct)
    {
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0)
            return Task.FromResult(false);

        Replace(id, s => s with { JobKind = null, JobStartedAt = null, JobError = error, Version = NextVersion() });
        return Task.FromResult(true);
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.StampPreviewAsync"/> in
    /// plain C# (PLAN T432): unconditional by id, no state guard — reports <see langword="false"/> only
    /// when no row exists.</summary>
    public Task<bool> StampPreviewAsync(long id, string path, string key, CancellationToken ct)
    {
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0)
            return Task.FromResult(false);

        Replace(id, s => s with { PreviewPath = path, PreviewKey = key, PreviewAt = DateTime.UtcNow, Version = NextVersion() });
        return Task.FromResult(true);
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ClaimForPromotionAsync"/>
    /// in plain C# (PLAN T432): xmin-guarded <see cref="AdState.Approved"/>-to-<see cref="AdState.Rendering"/>
    /// for exactly the named row — "not approved" and "stale version" collapse to one
    /// <see langword="null"/> outcome, the interface's own contract.</summary>
    public Task<AdSpot?> ClaimForPromotionAsync(long id, string expectedVersion, CancellationToken ct)
    {
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0)
            return Task.FromResult<AdSpot?>(null);

        var current = spots[index];
        if (current.State != AdState.Approved || !string.Equals(current.Version, expectedVersion, StringComparison.Ordinal))
            return Task.FromResult<AdSpot?>(null);

        var updated = Replace(id, s => s with { State = AdState.Rendering, StateChangedAt = DateTime.UtcNow, Version = NextVersion() });
        return Task.FromResult<AdSpot?>(updated);
    }
}
