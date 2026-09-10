using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="IAdBriefStore"/> double for <see cref="AdSpotWorker"/> specs (PLAN T402) — a small,
/// deterministic in-memory brief pool. <see cref="SampleEnabledAsync"/> returns the FIRST enabled brief
/// of an unpaused sponsor (never actually random) so a scenario can assert exactly which brief a
/// generation attempt used, mirroring <see cref="FakeAdSpotCatalog"/>'s own "deterministic, not
/// random" precedent one file over.
/// <see cref="UpsertAsync"/>/<see cref="ListAllAsync"/>/<see cref="CreateOwnerAsync"/>/
/// <see cref="SetEnabledAsync"/> (PLAN T403b's own Briefs-admin widening),
/// <see cref="UpsertAllAsync"/> (PLAN T405's own ad-pack install widening), and
/// <see cref="UninstallPackAsync"/> (PLAN T437's own ad-pack uninstall widening) are all unreachable
/// from <see cref="AdSpotWorker"/> and throw if ever called.
/// </summary>
public sealed class FakeAdBriefStore : IAdBriefStore
{
    readonly List<AdBrief> briefs = [];
    readonly Dictionary<string, long> sponsorIdsByBrand = new(StringComparer.Ordinal);
    long nextId = 1;
    long nextSponsorId = 1;

    /// <summary>Models <see cref="GenWave.MediaLibrary.Station.AdBriefRepository.SampleEnabledAsync"/>'s
    /// own <c>join station.sponsor s ... and not s.paused</c> predicate (SPEC F173.2, PLAN T440):
    /// <see cref="SampleEnabledAsync"/> below skips any brief whose sponsor this function reports
    /// paused. Defaults to "nobody paused" so a scenario that never calls
    /// <see cref="ExcludePausedSponsors"/> keeps this fake's pre-T440 behavior exactly.</summary>
    Func<long, bool> isPausedSponsor = _ => false;

    public int SampleCallCount { get; private set; }

    /// <summary>Wires this store's own <see cref="SampleEnabledAsync"/> to a paused-sponsor check —
    /// <see cref="AdSpotWorkerHarness.Build"/>'s own construction order (this store is built BEFORE
    /// <see cref="FakeSponsorStore"/>, whose <see cref="FakeSponsorStore.IsPaused"/> it wires here) is
    /// why this is a post-construction setter rather than a constructor parameter.</summary>
    public FakeAdBriefStore ExcludePausedSponsors(Func<long, bool> isPaused)
    {
        isPausedSponsor = isPaused;
        return this;
    }

    /// <summary>Every brand this fake has ever seen, mapped to a synthetic sponsor id it fabricates on
    /// first use (PLAN T432) — specs still seed by brand STRING (the pre-sponsors call shape, unchanged
    /// so <c>Story389_AdStockKeeping</c>'s own 8 call sites stay untouched), so this is where that
    /// string resolves to the <see cref="AdBrief.SponsorId"/> the domain type now actually carries.</summary>
    public IReadOnlyDictionary<string, long> SponsorIdsByBrand => sponsorIdsByBrand;

    public FakeAdBriefStore AddEnabled(string brand, string? packSlug = null, string? premise = null, string? tone = null) =>
        Add(brand, packSlug, premise, tone, enabled: true);

    public FakeAdBriefStore AddDisabled(string brand, string? packSlug = null) =>
        Add(brand, packSlug, premise: null, tone: null, enabled: false);

    FakeAdBriefStore Add(string brand, string? packSlug, string? premise, string? tone, bool enabled)
    {
        var sponsorId = SponsorIdFor(brand);
        briefs.Add(new AdBrief(nextId++, packSlug, sponsorId, premise, tone, Structure: null, enabled, DateTime.UtcNow));
        return this;
    }

    long SponsorIdFor(string brand)
    {
        if (!sponsorIdsByBrand.TryGetValue(brand, out var sponsorId))
        {
            sponsorId = nextSponsorId++;
            sponsorIdsByBrand[brand] = sponsorId;
        }
        return sponsorId;
    }

    public Task<AdBrief?> SampleEnabledAsync(CancellationToken ct)
    {
        SampleCallCount++;
        return Task.FromResult(briefs.FirstOrDefault(b => b.Enabled && !isPausedSponsor(b.SponsorId)));
    }

    public Task<AdBrief> UpsertAsync(
        string? packSlug, long sponsorId, string? premise, string? tone, string? structure, bool enabled,
        CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<IReadOnlyList<AdBrief>> ListAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<AdBrief?> CreateOwnerAsync(
        long sponsorId, string? premise, string? tone, string? structure, bool enabled, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<AdBrief?> SetEnabledAsync(long id, bool enabled, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<IReadOnlyList<AdBrief>> UpsertAllAsync(
        string packSlug, IReadOnlyList<AdBriefUpsertInput> briefs, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<AdPackUninstallResult> UninstallPackAsync(string packSlug, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");
}
