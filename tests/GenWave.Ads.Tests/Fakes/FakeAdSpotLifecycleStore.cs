using GenWave.Ads;
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
/// <param name="adminLookup">gh-#854 — optional; when set, <see cref="SwapRenderedMediaAsync"/> mirrors
/// <see cref="GenWave.MediaLibrary.Station.AdSpotRepository"/>'s own guard-read of
/// <paramref name="adminLookup"/>'s underlying facts BEFORE the state-guarded swap, declining rather
/// than swapping when the row is missing, ineligible, or never_play. <see langword="null"/> by
/// default — every pre-existing spec that never wires this keeps its own prior behavior unchanged.</param>
/// <param name="catalogWriter">gh-#854 — optional; when set, a swap that passes both guards stamps
/// both pending markers in the SAME call that lands the swap, then attempts both eligibility flips
/// through it, old-then-new (see that method's own remarks for why that order matters). Each flip is
/// independent and best-effort: a thrown or declined flip leaves its own marker in place for
/// <see cref="ListPendingRetiresAsync"/>/<see cref="ListPendingConfirmsAsync"/>'s own later drain to
/// retry, rather than rolling back the swap itself or the other flip.</param>
public sealed class FakeAdSpotLifecycleStore(
    IAdminMediaLookup? adminLookup = null, IAuthoredCatalogWriter? catalogWriter = null) : IAdSpotStore
{
    readonly List<AdSpot> spots = [];
    long nextId = 1;
    int nextXmin = 1;

    /// <summary>gh-#854 — the in-memory mirror of db/48's own <c>pending_retire_media_id</c> column:
    /// spot id → the old media id its own last swap displaced, still waiting on its own eventual
    /// turn-off. Populated by <see cref="SwapRenderedMediaAsync"/> in the SAME call that lands the
    /// swap, and drained by <see cref="ListPendingRetiresAsync"/>/<see cref="ClearPendingRetireAsync"/>
    /// exactly like <see cref="GenWave.Ads.AdSpotWorker"/>'s own tick does against the real store.
    /// </summary>
    readonly Dictionary<long, long> pendingRetireBySpot = [];

    /// <summary>gh-#854 — the in-memory mirror of db/48's own <c>pending_confirm_media_id</c> column:
    /// spot id → the new media id its own last swap landed, still waiting on its own eventual
    /// turn-on. Populated by <see cref="SwapRenderedMediaAsync"/> in the SAME call that lands the
    /// swap, and drained by <see cref="ListPendingConfirmsAsync"/>/<see cref="ClearPendingConfirmAsync"/>
    /// — <see cref="pendingRetireBySpot"/>'s own shape, one marker over.</summary>
    readonly Dictionary<long, long> pendingConfirmBySpot = [];

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
        string script = "ANNOUNCER: Come on down.\nVOICE1: Prices you won't believe.",
        int renderVersion = AdRenderVersion.Current)
    {
        var stamp = stateChangedAt ?? DateTime.UtcNow;
        return AddExisting(new AdSpot(
            id, sponsorId, sponsorName, $"{sponsorName} spot", Brief: null, script, source, packSlug,
            SpotSeconds: 30, VoicePlan: null, BedMediaId: null, state, failReason, mediaId, Generation: 1,
            CreatedAt: stamp, StateChangedAt: stamp, RenderedAt: state == AdState.Ready ? stamp : null,
            RetiredAt: state == AdState.Retired ? stamp : null, Version: NextVersion(),
            RenderVersion: renderVersion));
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

    /// <summary>gh-#854 — fires exactly once per call that finds the approved queue empty
    /// (<c>candidate is null</c>) — lets a spec flip <see cref="FakeOnAirRenderSignal.InFlight"/> at
    /// precisely the moment <see cref="AdSpotWorker.RenderDueAsync"/> would hand off to the stale
    /// re-render pass, so a spec proving that pass re-checks the gate for itself actually fails if that
    /// check were ever deleted, rather than passing for the unrelated reason the queue was already
    /// empty when the tick began.</summary>
    public Action? OnApprovedQueueObservedEmpty { get; set; }

    public Task<AdSpot?> ClaimNextApprovedAsync(CancellationToken ct)
    {
        ClaimCallCount++;
        var candidate = spots
            .Where(s => s.State == AdState.Approved)
            .OrderBy(s => s.StateChangedAt).ThenBy(s => s.Id)
            .FirstOrDefault();
        if (candidate is null)
        {
            OnApprovedQueueObservedEmpty?.Invoke();
            return Task.FromResult<AdSpot?>(null);
        }

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

    public Task<bool> MarkReadyAsync(long id, long mediaId, int renderVersion, CancellationToken ct)
    {
        MarkReadyCallCount++;
        return Task.FromResult(TryTotalTransition(id, AdState.Rendering, s => s with
        {
            State = AdState.Ready, MediaId = mediaId, RenderedAt = DateTime.UtcNow,
            StateChangedAt = DateTime.UtcNow, Version = NextVersion(), RenderVersion = renderVersion,
        }));
    }

    public int FindStaleReadyCallCount { get; private set; }
    public int SwapRenderedMediaCallCount { get; private set; }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.FindStaleReadyAsync"/>
    /// in plain C# (gh-#854): the oldest <see cref="AdState.Ready"/> row whose <see cref="AdSpot.RenderVersion"/>
    /// trails <paramref name="currentVersion"/> and whose id is not in <paramref name="excludeIds"/>. A
    /// spot already carrying either pending marker is excluded too — it has already been swapped once
    /// and is still waiting on its own eligibility drain, not a fresh candidate for a second swap.</summary>
    public Task<AdSpot?> FindStaleReadyAsync(int currentVersion, IReadOnlyCollection<long> excludeIds, CancellationToken ct)
    {
        FindStaleReadyCallCount++;
        var candidate = spots
            .Where(s => s.State == AdState.Ready && s.RenderVersion < currentVersion && !excludeIds.Contains(s.Id)
                && !pendingRetireBySpot.ContainsKey(s.Id) && !pendingConfirmBySpot.ContainsKey(s.Id))
            .OrderBy(s => s.StateChangedAt).ThenBy(s => s.Id)
            .FirstOrDefault();
        return Task.FromResult(candidate);
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.SwapRenderedMediaAsync"/>
    /// in plain C# (gh-#854): the <see cref="adminLookup"/> guard runs FIRST (when wired) — missing,
    /// ineligible, or never_play declines before <see cref="SwapRenderedMediaCallCount"/> ever
    /// increments, so a spec asserting "no swap was attempted" against a disabled old row still passes.
    /// Only once that guard clears does the state-guarded swap itself run, guarded on <see cref="AdState.Ready"/>,
    /// the row's current <see cref="AdSpot.MediaId"/> matching <paramref name="oldMediaId"/>, AND no
    /// pending retire marker already stamped on it (an earlier swap's own old-media turn-off still
    /// outstanding) — a spot edited, retired, already re-rendered, or already mid-swap out from under
    /// the caller leaves this a no-op, reporting <see langword="false"/>. <c>state_changed_at</c>
    /// is deliberately never touched here (nor by the real repository) — that clock is a separate,
    /// product-level decision. gh-#854: the swap stamps BOTH <see cref="pendingRetireBySpot"/> and
    /// <see cref="pendingConfirmBySpot"/> in the SAME call that lands it, mirroring the real repository's
    /// own durable stamp landing inside the guarded UPDATE itself. Each flip through
    /// <see cref="catalogWriter"/> (when wired) then runs old-then-new, independently best-effort — see
    /// <see cref="RetireOldMediaBestEffortAsync"/>/<see cref="ConfirmNewMediaEligibleBestEffortAsync"/>
    /// for each one's own clear-on-false, leave-set-on-throw posture.</summary>
    public async Task<bool> SwapRenderedMediaAsync(long id, long oldMediaId, long newMediaId, int renderVersion, CancellationToken ct)
    {
        if (adminLookup is not null)
        {
            var oldMedia = await adminLookup.GetByIdWithLibraryAsync(oldMediaId, ct);
            if (oldMedia is not { } found || !found.Row.Eligible || found.Row.NeverPlay)
                return false;
        }

        SwapRenderedMediaCallCount++;
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0 || spots[index].State != AdState.Ready || spots[index].MediaId != oldMediaId
            || pendingRetireBySpot.ContainsKey(id))
            return false;

        spots[index] = spots[index] with
        {
            MediaId = newMediaId, RenderVersion = renderVersion, RenderedAt = DateTime.UtcNow,
            Version = NextVersion(),
        };
        pendingRetireBySpot[id] = oldMediaId;
        pendingConfirmBySpot[id] = newMediaId;

        if (catalogWriter is not null)
        {
            await RetireOldMediaBestEffortAsync(catalogWriter, id, oldMediaId);
            await ConfirmNewMediaEligibleBestEffortAsync(catalogWriter, id, newMediaId);
        }

        return true;
    }

    /// <summary>gh-#854 — mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository"/>'s own
    /// post-commit old-media turn-off exactly: <see cref="IAuthoredCatalogWriter.SetEligibleAsync"/>
    /// returning <see langword="false"/> means the row was already purged (a row that no longer exists
    /// can't air regardless) — that clears <paramref name="id"/> out of <see cref="pendingRetireBySpot"/>
    /// in the SAME call exactly as a successful flip does. Only a THROWN flip leaves the stamp in place,
    /// never rethrown, for <see cref="ListPendingRetiresAsync"/>'s own later drain to retry — the real
    /// repository's own posture, so <see cref="SwapRenderedMediaAsync"/>'s own caller never sees the
    /// exception either. Always runs on <see cref="CancellationToken.None"/>, matching the real store.</summary>
    async Task RetireOldMediaBestEffortAsync(IAuthoredCatalogWriter writer, long id, long oldMediaId)
    {
        try
        {
            await writer.SetEligibleAsync(oldMediaId, eligible: false, CancellationToken.None);
        }
        catch
        {
            // Leave pendingRetireBySpot set — ListPendingRetiresAsync's own later drain retries it.
            return;
        }

        pendingRetireBySpot.Remove(id);
    }

    /// <summary>gh-#854 — the confirm half; <see cref="RetireOldMediaBestEffortAsync"/>'s own remarks,
    /// one marker over. Guarded by <see cref="IsReadyOnMediaAsync"/> before ever attempting the flip — an
    /// operator retiring the spot, or a second swap moving it on again, between the swap's own commit
    /// and this call must never revive a row the operator meant to pull. A guard-false, a flip that
    /// lands, or a flip that reports <see langword="false"/> (a purged new row) all clear
    /// <see cref="pendingConfirmBySpot"/> in the SAME call; only a thrown exception leaves it set for
    /// <see cref="ListPendingConfirmsAsync"/>'s own later drain. Always runs on
    /// <see cref="CancellationToken.None"/>, matching the real store.</summary>
    async Task ConfirmNewMediaEligibleBestEffortAsync(IAuthoredCatalogWriter writer, long id, long newMediaId)
    {
        try
        {
            if (await IsReadyOnMediaAsync(id, newMediaId, CancellationToken.None))
                await writer.SetEligibleAsync(newMediaId, eligible: true, CancellationToken.None);
        }
        catch
        {
            // Leave pendingConfirmBySpot set — ListPendingConfirmsAsync's own later drain retries it.
            return;
        }

        pendingConfirmBySpot.Remove(id);
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ClearReferencedPendingRetiresAsync"/>
    /// in plain C# (gh-#854): clears any pending row whose old media id is now some OTHER spot's own
    /// CURRENT <see cref="AdSpot.MediaId"/> (an operator re-pointed a spot at it), without ever handing
    /// it back to the caller to skip itself. The caller runs this FIRST, every tick, before
    /// <see cref="ListPendingRetiresAsync"/>'s own read.</summary>
    public Task ClearReferencedPendingRetiresAsync(CancellationToken ct)
    {
        var currentlyReferenced = new HashSet<long>();
        foreach (var spot in spots)
        {
            if (spot.MediaId is long mediaId)
                currentlyReferenced.Add(mediaId);
        }

        foreach (var spotId in pendingRetireBySpot.Keys.ToList())
        {
            if (currentlyReferenced.Contains(pendingRetireBySpot[spotId]))
                pendingRetireBySpot.Remove(spotId);
        }

        return Task.CompletedTask;
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ListPendingRetiresAsync"/>
    /// in plain C# (gh-#854): a pure read — see <see cref="ClearReferencedPendingRetiresAsync"/> for the
    /// self-heal the real store now runs as its own separate call — ordered the same way the real
    /// store's own query is (oldest state change first, then id).</summary>
    public Task<IReadOnlyList<PendingAdSpotRetire>> ListPendingRetiresAsync(CancellationToken ct)
    {
        IReadOnlyList<PendingAdSpotRetire> pending = pendingRetireBySpot
            .OrderBy(kvp => StateChangedAtOf(kvp.Key)).ThenBy(kvp => kvp.Key)
            .Select(kvp => new PendingAdSpotRetire(kvp.Key, kvp.Value))
            .ToList();
        return Task.FromResult(pending);
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ClearPendingRetireAsync"/>
    /// in plain C# (gh-#854): guarded on BOTH <paramref name="id"/> AND <paramref name="mediaId"/> still
    /// matching the stamped value — total, reports <see langword="false"/> rather than throwing when it
    /// no longer matches.</summary>
    public Task<bool> ClearPendingRetireAsync(long id, long mediaId, CancellationToken ct)
    {
        if (pendingRetireBySpot.TryGetValue(id, out var stamped) && stamped == mediaId)
        {
            pendingRetireBySpot.Remove(id);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ListPendingConfirmsAsync"/>
    /// in plain C# (gh-#854): <see cref="ListPendingRetiresAsync"/>'s own shape, one marker over — a pure
    /// read, no self-heal of its own (the confirm marker's own guard, <see cref="IsReadyOnMediaAsync"/>,
    /// is checked by the caller per row, not filtered out here) — same ordering, oldest state change
    /// first, then id.</summary>
    public Task<IReadOnlyList<PendingAdSpotConfirm>> ListPendingConfirmsAsync(CancellationToken ct)
    {
        IReadOnlyList<PendingAdSpotConfirm> pending = pendingConfirmBySpot
            .OrderBy(kvp => StateChangedAtOf(kvp.Key)).ThenBy(kvp => kvp.Key)
            .Select(kvp => new PendingAdSpotConfirm(kvp.Key, kvp.Value))
            .ToList();
        return Task.FromResult(pending);
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ClearPendingConfirmAsync"/>
    /// in plain C# (gh-#854): <see cref="ClearPendingRetireAsync"/>'s own guarded shape, one marker
    /// over.</summary>
    public Task<bool> ClearPendingConfirmAsync(long id, long mediaId, CancellationToken ct)
    {
        if (pendingConfirmBySpot.TryGetValue(id, out var stamped) && stamped == mediaId)
        {
            pendingConfirmBySpot.Remove(id);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.IsReadyOnMediaAsync"/> in
    /// plain C# (gh-#854): the shared guard a confirm marker's own flip must pass before it is ever
    /// attempted, from either call site (this fake's own inline post-commit confirm, or a worker spec's
    /// own confirm-marker drain).</summary>
    public Task<bool> IsReadyOnMediaAsync(long id, long mediaId, CancellationToken ct)
    {
        var spot = spots.FirstOrDefault(s => s.Id == id);
        return Task.FromResult(spot is not null && spot.State == AdState.Ready && spot.MediaId == mediaId);
    }

    /// <summary>The <see cref="AdSpot.StateChangedAt"/> of <paramref name="id"/>, or the latest possible
    /// instant when the row is somehow gone — ordering support for
    /// <see cref="ListPendingRetiresAsync"/>/<see cref="ListPendingConfirmsAsync"/>, mirroring the real
    /// store's own <c>order by state_changed_at asc, id asc</c>.</summary>
    DateTime StateChangedAtOf(long id) => spots.FirstOrDefault(s => s.Id == id)?.StateChangedAt ?? DateTime.MaxValue;

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
    /// plain C# (PLAN T432, T463): guarded on <c>JobKind is null</c> — an already-claimed row reports
    /// <see cref="AdSpotJobStampResult.Busy"/> rather than stealing the claim; a successful claim
    /// nulls <see cref="AdSpot.JobFailedKind"/> alongside <see cref="AdSpot.JobError"/>, even on a row
    /// whose previous job failed.</summary>
    public Task<AdSpotJobStampOutcome> StampJobAsync(long id, string kind, CancellationToken ct)
    {
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0)
            return Task.FromResult(new AdSpotJobStampOutcome(AdSpotJobStampResult.NotFound, null));

        if (spots[index].JobKind is not null)
            return Task.FromResult(new AdSpotJobStampOutcome(AdSpotJobStampResult.Busy, null));

        var updated = Replace(id, s => s with
        {
            JobKind = kind, JobStartedAt = DateTime.UtcNow, JobError = null, JobFailedKind = null,
            Version = NextVersion(),
        });
        return Task.FromResult(new AdSpotJobStampOutcome(AdSpotJobStampResult.Stamped, updated));
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ClearJobAsync"/> in
    /// plain C# (PLAN T432, T463): total by id — clearing an already-clear job is a harmless no-op, not
    /// a conflict; reports <see langword="false"/> only when no row exists. Stamps
    /// <see cref="AdSpot.JobFailedKind"/> from the row's OWN pre-clear <see cref="AdSpot.JobKind"/> when
    /// <paramref name="error"/> is non-null, else nulls it.</summary>
    public Task<bool> ClearJobAsync(long id, string? error, CancellationToken ct)
    {
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0)
            return Task.FromResult(false);

        Replace(id, s => s with
        {
            JobKind = null, JobStartedAt = null, JobError = error,
            JobFailedKind = error is null ? null : s.JobKind,
            Version = NextVersion(),
        });
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

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ListPreviewsToSweepAsync"/>
    /// in plain C# (PLAN T442): the interface's own predicate verbatim — a stamped preview
    /// (<see cref="AdSpot.PreviewPath"/> not <see langword="null"/>) whose spot has left the editable
    /// lifecycle (<see cref="AdState.Ready"/> or <see cref="AdState.Retired"/>), or has simply aged past
    /// <paramref name="retention"/> since <see cref="AdSpot.PreviewAt"/>.</summary>
    public Task<IReadOnlyList<AdSpot>> ListPreviewsToSweepAsync(TimeSpan retention, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - retention;
        IReadOnlyList<AdSpot> candidates = spots
            .Where(s => s.PreviewPath is not null &&
                (s.State is AdState.Ready or AdState.Retired ||
                 (s.PreviewAt is DateTime previewAt && new DateTimeOffset(previewAt, TimeSpan.Zero) < cutoff)))
            .OrderBy(s => s.PreviewAt ?? DateTime.MaxValue).ThenBy(s => s.Id)
            .ToList();
        return Task.FromResult(candidates);
    }

    /// <summary>Mirrors <see cref="GenWave.MediaLibrary.Station.AdSpotRepository.ClearPreviewAsync"/> in
    /// plain C# (PLAN T442): total by id — nulls the preview trio together, harmless on a row with no
    /// preview stamped; reports <see langword="false"/> only when no row exists.</summary>
    public Task<bool> ClearPreviewAsync(long id, CancellationToken ct)
    {
        var index = spots.FindIndex(s => s.Id == id);
        if (index < 0)
            return Task.FromResult(false);

        Replace(id, s => s with { PreviewPath = null, PreviewAt = null, PreviewKey = null });
        return Task.FromResult(true);
    }
}
