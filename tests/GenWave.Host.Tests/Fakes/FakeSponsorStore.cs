using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ISponsorStore"/> double (STORY-406, PLAN T449) — the <see cref="FakeShowStore"/>
/// precedent one seam over: a fake-store-based wire-layer spec (<c>Story305_ShowsApi.cs</c>) that
/// predates <c>ShowsController</c>'s own <c>ISponsorStore</c> dependency (PLAN T449) needs SOME
/// registered implementation to resolve, and the real <see cref="ISponsorStore"/> is Postgres-backed —
/// this double is that registration. <c>ShowsController</c> only ever calls <see cref="GetAsync"/> and
/// <see cref="ListAsync"/>, so those two are the only members this double actually simulates; the
/// constructor's <c>seed</c> parameter is the sole way a fact populates a row, and every other member
/// throws rather than pretend to a write semantics no <c>Story305_ShowsApi.cs</c> fact exercises.
/// </summary>
sealed class FakeSponsorStore : ISponsorStore
{
    readonly Dictionary<long, Sponsor> byId;

    /// <summary>Seeds the store with pre-existing rows.</summary>
    public FakeSponsorStore(IEnumerable<Sponsor>? seed = null)
    {
        byId = (seed ?? []).ToDictionary(s => s.Id);
    }

    public Task<IReadOnlyList<SponsorListRow>> ListAsync(string? q, CancellationToken cancellationToken)
    {
        var folded = q?.Trim();
        IReadOnlyList<SponsorListRow> rows = byId.Values
            .Where(s => string.IsNullOrEmpty(folded) || s.Name.Contains(folded, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new SponsorListRow(s, Briefs: 0, SpotsByState: new Dictionary<AdState, int>(), Shows: 0))
            .ToList();
        return Task.FromResult(rows);
    }

    public Task<Sponsor?> GetAsync(long id, CancellationToken cancellationToken) =>
        Task.FromResult(byId.GetValueOrDefault(id));

    public Task<SponsorWriteResult> CreateOwnerAsync(NewSponsor sponsor, CancellationToken cancellationToken) =>
        throw Unreachable();

    public Task<SponsorPackWriteResult> UpsertPackSponsorsAsync(
        string packSlug, IReadOnlyList<string> names, CancellationToken cancellationToken) =>
        throw Unreachable();

    public Task<SponsorWriteResult> FindOrCreateOwnerAsync(string name, CancellationToken cancellationToken) =>
        throw Unreachable();

    public Task<SponsorWriteResult> UpdateAsync(
        long id, SponsorEdit edit, string expectedVersion, CancellationToken cancellationToken) =>
        throw Unreachable();

    public Task<Sponsor?> SetPausedAsync(long id, bool paused, CancellationToken cancellationToken) =>
        throw Unreachable();

    public Task<SponsorDeleteResult> DeleteIfUnreferencedAsync(long id, CancellationToken cancellationToken) =>
        throw Unreachable();

    static NotSupportedException Unreachable() =>
        new("FakeSponsorStore only serves ShowsController's GetAsync/ListAsync — see Story305_ShowsApi.");
}
