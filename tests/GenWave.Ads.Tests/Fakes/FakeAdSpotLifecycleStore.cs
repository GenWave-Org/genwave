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

    public int CreateCallCount { get; private set; }
    public int RetireCallCount { get; private set; }
    public int ReArmCallCount { get; private set; }
    public int MarkReadyCallCount { get; private set; }
    public int MarkFailedCallCount { get; private set; }
    public int ClaimCallCount { get; private set; }

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
        string brand = "Acme", string script = "ANNOUNCER: Come on down.\nVOICE1: Prices you won't believe.")
    {
        var stamp = stateChangedAt ?? DateTime.UtcNow;
        return AddExisting(new AdSpot(
            id, brand, $"{brand} spot", Brief: null, script, source, packSlug, SpotSeconds: 30,
            VoicePlan: null, BedMediaId: null, state, failReason, mediaId, Generation: 1,
            CreatedAt: stamp, StateChangedAt: stamp, RenderedAt: state == AdState.Ready ? stamp : null,
            RetiredAt: state == AdState.Retired ? stamp : null, Version: NextVersion()));
    }

    public Task<AdSpot> CreateAsync(NewAdSpot spot, CancellationToken ct)
    {
        CreateCallCount++;
        CreateRequests.Add(spot);

        var now = DateTime.UtcNow;
        var created = new AdSpot(
            nextId++, spot.Brand, spot.Title, spot.Brief, spot.Script, spot.Source, spot.PackSlug,
            spot.SpotSeconds, spot.VoicePlan, spot.BedMediaId, spot.InitialState, spot.FailReason,
            MediaId: null, Generation: 1, now, now, RenderedAt: null, RetiredAt: null, Version: NextVersion());
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
            Brand = edit.Brand ?? current.Brand,
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

    public Task<AdSpotPage> ListByStateAsync(AdState? state, int limit, int offset, CancellationToken ct)
    {
        var filtered = state is null ? spots.AsEnumerable() : spots.Where(s => s.State == state);
        var ordered = filtered.OrderByDescending(s => s.StateChangedAt).ThenByDescending(s => s.Id).ToList();
        var page = ordered.Skip(Math.Max(0, offset)).Take(limit <= 0 ? 1 : limit).ToList();
        return Task.FromResult(new AdSpotPage(page, ordered.Count));
    }

    public Task<int> CountStockGeneratedAsync(CancellationToken ct) =>
        Task.FromResult(spots.Count(s =>
            (s.State is AdState.Draft or AdState.Approved or AdState.Rendering or AdState.Ready)
            && (s.Source is AdSource.Llm or AdSource.Pack)));

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
}
