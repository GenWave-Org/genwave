using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="IAuthoredCatalogWriter"/> double for <see cref="AdSpotWorker"/> specs (PLAN T402) — the
/// worker/render path only ever calls <see cref="SetEligibleAsync"/> (the repair sweep, the retire
/// flip, and, when <see cref="FakeAdSpotLifecycleStore"/> is built with this writer, its own
/// gh-#854 mirror of <c>AdSpotRepository.SwapRenderedMediaAsync</c>'s best-effort old-then-new flip
/// pair). <see cref="InsertAuthoredAsync"/> is <c>CastSegmentAuthor</c>'s own seam, unreachable
/// through the <see cref="FakeCastSegmentAuthor"/> path these specs render through, and throws if
/// ever called (the <see cref="FakeAdSpotCatalog"/> precedent, this same Fakes directory).
/// </summary>
public sealed class FakeAuthoredCatalogWriter : IAuthoredCatalogWriter
{
    public int SetEligibleCalls { get; private set; }
    public List<(long MediaId, bool Eligible)> SetEligibleHistory { get; } = [];
    public bool SetEligibleResult { get; set; } = true;

    /// <summary>gh-#854 — media ids that make <see cref="SetEligibleAsync"/> throw instead of
    /// returning, so a spec can force the exact "old flip throws after the swap already committed"
    /// scenario <see cref="FakeAdSpotLifecycleStore"/>'s own pending-retire stamp exists to survive.
    /// Empty by default — every pre-existing spec that never wires this keeps its own prior behavior
    /// unchanged.</summary>
    public HashSet<long> ThrowOnSetEligible { get; } = [];

    /// <summary>gh-#854 — media ids that make <see cref="SetEligibleAsync"/> report
    /// <see langword="false"/> rather than throwing or succeeding, so a spec can force the "row already
    /// purged" shape (<c>PurgeUnavailableAsync</c> hard-deletes rows) independently of the global
    /// <see cref="SetEligibleResult"/> — a single scenario can have one marker's flip decline while
    /// another lands. Checked before <see cref="ThrowOnSetEligible"/>; empty by default.</summary>
    public HashSet<long> DeclineOnSetEligible { get; } = [];

    public Task<bool> SetEligibleAsync(long mediaId, bool eligible, CancellationToken ct)
    {
        SetEligibleCalls++;
        if (ThrowOnSetEligible.Contains(mediaId))
            throw new InvalidOperationException($"Injected failure flipping media {mediaId} eligible={eligible}.");
        if (DeclineOnSetEligible.Contains(mediaId))
            return Task.FromResult(false);
        SetEligibleHistory.Add((mediaId, eligible));
        return Task.FromResult(SetEligibleResult);
    }

    public Task<long> InsertAuthoredAsync(AuthoredMediaInsert insert, CancellationToken ct) =>
        throw new NotSupportedException("Not used by AdSpotWorker.");
}
