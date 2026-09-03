using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="IAdBriefStore"/> double for <see cref="AdSpotWorker"/> specs (PLAN T402) — a small,
/// deterministic in-memory brief pool. <see cref="SampleEnabledAsync"/> returns the FIRST enabled brief
/// (never actually random) so a scenario can assert exactly which brief a generation attempt used,
/// mirroring <see cref="FakeAdSpotCatalog"/>'s own "deterministic, not random" precedent one file over.
/// <see cref="UpsertAsync"/> is unreachable from <see cref="AdSpotWorker"/> and throws if ever called.
/// </summary>
public sealed class FakeAdBriefStore : IAdBriefStore
{
    readonly List<AdBrief> briefs = [];
    long nextId = 1;

    public int SampleCallCount { get; private set; }

    public FakeAdBriefStore AddEnabled(string brand, string? packSlug = null, string? premise = null, string? tone = null) =>
        Add(brand, packSlug, premise, tone, enabled: true);

    public FakeAdBriefStore AddDisabled(string brand, string? packSlug = null) =>
        Add(brand, packSlug, premise: null, tone: null, enabled: false);

    FakeAdBriefStore Add(string brand, string? packSlug, string? premise, string? tone, bool enabled)
    {
        briefs.Add(new AdBrief(nextId++, packSlug, brand, premise, tone, Structure: null, enabled, DateTime.UtcNow));
        return this;
    }

    public Task<AdBrief?> SampleEnabledAsync(CancellationToken ct)
    {
        SampleCallCount++;
        return Task.FromResult(briefs.FirstOrDefault(b => b.Enabled));
    }

    public Task<AdBrief> UpsertAsync(
        string? packSlug, string brand, string? premise, string? tone, string? structure, bool enabled,
        CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");
}
