using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads;

/// <summary>
/// SPEC F158.5 (STORY-388, PLAN T396) — the floor of <see cref="AdSpotPipeline"/> (registered LAST,
/// via <see cref="AdsServiceCollectionExtensions.AddGenWaveAds"/>): vends from
/// <see cref="IMediaCatalog.GetRandomReadyAdSpotAsync"/> within the operator-named Ads library
/// (<see cref="AdsOptions.LibraryName"/>, resolved fresh on every vend — the library's own name is
/// never cached, since an operator rename should take effect without a restart), excluding the last
/// <see cref="AdSpotAntiRepeatOptions.AntiRepeatWindow"/> ids this instance has itself vended (an
/// in-memory ring, the <c>PlayoutFeeder.Remember</c> precedent — read the live window fresh on every
/// write, so a shrunk value trims the ring on THIS write and a grown one simply stops evicting
/// sooner). ALSO re-read on every snapshot (a deliberate strengthening of the feeder precedent, not
/// merely a copy of it — see <see cref="SnapshotRing"/>'s own remarks for why a write-only trim would
/// leave a small pool wedged against a since-shrunk window with no vend ever able to happen again).
///
/// <para>
/// <b>Story301-mirroring posture (PLAN T395 review carry-forward, RULED at T395):</b> the catalog
/// method this source calls already ANDs in the same <c>ExplicitPredicate</c> every other
/// pool-predicate query on <see cref="IMediaCatalog"/> carries — an explicit-marked ad row never
/// vends on an <c>Everyone</c> station either, pinned by test in
/// <c>GenWave.MediaLibrary.Tests.Specs.Story387_ImagingNeverAirsAsMusic</c>. Nothing in THIS class
/// re-implements or re-checks that predicate — it is entirely the catalog query's own job.
/// </para>
/// </summary>
public sealed class LibraryAdSpotSource(
    IMediaCatalog catalog,
    ILibraryRepository libraryRepository,
    IOptionsMonitor<AdsOptions> adsOptions,
    IOptionsMonitor<AdSpotAntiRepeatOptions> antiRepeatOptions) : IAdSpotSource
{
    readonly object gate = new();
    readonly Queue<string> recentlyVended = new();

    public async ValueTask<MediaItem?> GetNextSpotAsync(CancellationToken ct)
    {
        var libraryId = await ResolveAdsLibraryIdAsync(ct).ConfigureAwait(false);
        if (libraryId is not { } id)
            return null; // No ads library yet (boot seed not run, or renamed away) — no dead-air excuse (F158.1).

        var exclude = SnapshotRing();
        var reference = await catalog.GetRandomReadyAdSpotAsync(new LibraryScope([id]), exclude, ct).ConfigureAwait(false);
        if (reference is null)
            return null; // Empty pool — a normal day (F158.3), never an error.

        Remember(reference.MediaId);

        return reference.ToMediaItem() with { SegmentKind = SegmentKind.Ad };
    }

    async Task<long?> ResolveAdsLibraryIdAsync(CancellationToken ct)
    {
        var libraries = await libraryRepository.GetAllWithMediaCountAsync(ct).ConfigureAwait(false);
        var name = adsOptions.CurrentValue.LibraryName;

        foreach (var library in libraries)
        {
            if (string.Equals(library.Name, name, StringComparison.Ordinal))
                return library.Id;
        }

        return null;
    }

    /// <summary>
    /// The exclude list for the next vend — bounded by the LIVE anti-repeat window, not merely the
    /// ring's own current size. <see cref="Remember"/> already trims the ring to capacity on every
    /// write, so in steady state this is a plain snapshot; but if an operator SHRINKS the window while
    /// the pool is at or below the ring's PRE-shrink size, every id in the pool would otherwise stay
    /// excluded forever — nothing ever vends again to trigger the write-time trim that would free
    /// room. Applying the live cap here too closes that wedge: the very next read already reflects
    /// the smaller window, with no vend needed to unstick it.
    /// </summary>
    IReadOnlyList<string> SnapshotRing()
    {
        lock (gate)
        {
            var capacity = Math.Max(0, antiRepeatOptions.CurrentValue.AntiRepeatWindow);
            return recentlyVended.Skip(Math.Max(0, recentlyVended.Count - capacity)).ToArray();
        }
    }

    void Remember(string mediaId)
    {
        lock (gate)
        {
            recentlyVended.Enqueue(mediaId);

            var capacity = Math.Max(0, antiRepeatOptions.CurrentValue.AntiRepeatWindow);
            while (recentlyVended.Count > capacity)
                recentlyVended.Dequeue();
        }
    }
}
