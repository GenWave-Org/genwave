using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="ISponsorStore"/> double for <see cref="AdSpotWorker"/> specs (PLAN T432) —
/// <see cref="AdSpotWorker.GenerateOneAsync"/> only ever calls <see cref="GetAsync"/>, once per tick, to
/// resolve a sampled brief's sponsor name; every other member is unreachable from that worker and
/// throws if ever called (the <see cref="FakeAdSpotStore"/> "narrow double, throw on unused" precedent,
/// same Fakes directory).
///
/// <para>
/// <b>No seed method of its own.</b> <see cref="AdSpotWorkerHarness.Build"/> wires this to
/// <see cref="FakeAdBriefStore.SponsorIdsByBrand"/> via <paramref name="nameLookup"/> — a spec already
/// names sponsors by calling <c>harness.Briefs.AddEnabled("Some Brand", ...)</c> (unchanged since before
/// PLAN T432), so this store reads that SAME map rather than asking every spec to seed sponsor identity
/// twice under two different names for the one brief it created.
/// </para>
/// </summary>
public sealed class FakeSponsorStore(Func<long, string?> nameLookup) : ISponsorStore
{
    readonly HashSet<long> pausedSponsorIds = [];
    readonly Dictionary<long, string> packSlugsBySponsorId = new();
    readonly Dictionary<long, string> phonesBySponsorId = new();

    /// <summary>Marks a sponsor paused for a scenario exercising <see cref="AdSpotWorker"/>'s own
    /// paused-sponsor handling — <see cref="GetAsync"/> reflects it back on <see cref="Sponsor.Paused"/>.</summary>
    public FakeSponsorStore Pause(long sponsorId)
    {
        pausedSponsorIds.Add(sponsorId);
        return this;
    }

    /// <summary>Marks a sponsor pack-owned for a scenario exercising <see cref="AdSpotWorker"/>'s own
    /// IsPackOwned wiring (SPEC F172.5, PLAN T438 ruling) — <see cref="GetAsync"/> reflects it back on
    /// <see cref="Sponsor.PackSlug"/>. A sponsor never marked here stays owner-owned (<see
    /// langword="null"/> <see cref="Sponsor.PackSlug"/>), unchanged from before this method existed.</summary>
    public FakeSponsorStore MarkPackOwned(long sponsorId, string packSlug = "test-pack")
    {
        packSlugsBySponsorId[sponsorId] = packSlug;
        return this;
    }

    /// <summary>Sets a sponsor's own phone number for a scenario exercising
    /// <see cref="AdSpotWorker"/>'s own SponsorPhone wiring (SPEC F172.5, PLAN T438 ruling) — <see
    /// cref="GetAsync"/> reflects it back on <see cref="Sponsor.Phone"/>. A sponsor never marked here
    /// stays phone-less (<see langword="null"/> <see cref="Sponsor.Phone"/>), unchanged from before
    /// this method existed.</summary>
    public FakeSponsorStore WithPhone(long sponsorId, string phone)
    {
        phonesBySponsorId[sponsorId] = phone;
        return this;
    }

    public Task<Sponsor?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var name = nameLookup(id);
        if (name is null)
            return Task.FromResult<Sponsor?>(null);

        return Task.FromResult<Sponsor?>(new Sponsor(
            id, name, packSlugsBySponsorId.GetValueOrDefault(id), Tagline: null, About: null,
            phonesBySponsorId.GetValueOrDefault(id), Address: null, Website: null, Tone: null,
            pausedSponsorIds.Contains(id), PausedAt: null,
            CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow, Version: "1"));
    }

    public Task<IReadOnlyList<SponsorListRow>> ListAsync(string? q, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<SponsorWriteResult> CreateOwnerAsync(NewSponsor sponsor, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<SponsorPackWriteResult> UpsertPackSponsorsAsync(
        string packSlug, IReadOnlyList<string> names, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<SponsorWriteResult> FindOrCreateOwnerAsync(string name, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<SponsorWriteResult> UpdateAsync(
        long id, SponsorEdit edit, string expectedVersion, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<Sponsor?> SetPausedAsync(long id, bool paused, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");

    public Task<SponsorDeleteResult> DeleteIfUnreferencedAsync(long id, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");
}
