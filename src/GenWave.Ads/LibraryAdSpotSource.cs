using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads;

/// <summary>
/// SPEC F158.5, F173.1-F173.3 (STORY-388, STORY-418, STORY-419; PLAN T396, T439) — the floor of
/// <see cref="AdSpotPipeline"/> (registered LAST, via
/// <see cref="AdsServiceCollectionExtensions.AddGenWaveAds"/>): vends from
/// <see cref="IMediaCatalog.GetRandomReadyAdSpotAsync"/> within the operator-named Ads library
/// (<see cref="AdsOptions.LibraryName"/>, resolved fresh on every vend — the library's own name is
/// never cached, since an operator rename should take effect without a restart).
///
/// <para>
/// <b>Exclusion, on every pick (F173.1).</b> <see cref="SnapshotRing"/> gives the last
/// <see cref="AdSpotAntiRepeatOptions.AntiRepeatWindow"/> ids THIS instance has itself vended,
/// most-recent-first (an in-memory ring, the <c>PlayoutFeeder.Remember</c> precedent — read the live
/// window fresh on every write, so a shrunk value trims the ring on THIS write and a grown one simply
/// stops evicting sooner; ALSO re-read on every snapshot, a deliberate strengthening of the feeder
/// precedent — see that method's own remarks for why a write-only trim would leave a small pool
/// wedged forever against a since-shrunk window). Those ids are parsed to <see langword="long"/> (a
/// non-numeric ring entry — never produced by this station's own ad media, but not this class's job
/// to assume — is dropped rather than thrown on: <see cref="MediaReference.MediaId"/> is a
/// general-purpose <see langword="string"/> key, while <c>ad_spot.media_id</c> is a plain
/// <see langword="bigint"/> here, so a non-numeric id could never match any row
/// <see cref="IAdSpotStore.ListAiringExclusionsAsync"/> looks up anyway) and handed to that seam
/// alongside the live window; the exclude list passed to the catalog is the ring UNION the ids that
/// call returns, as strings, deduplicated — F173.1's own contract, and F158.5's pre-existing
/// exclude-list seam gains nothing new on the catalog side.
/// </para>
///
/// <para>
/// <b>One-sponsor relaxation, only on an empty strict pick with genuine sponsor pressure (F173.3).</b>
/// When the exclusion-widened pick above comes back <see langword="null"/>, the live window is greater
/// than zero, AND the strict call's own <c>exclusions</c> result was non-empty (PLAN T439
/// ruling — the store contributed at least one id, meaning a real sponsor is paused or repeating, not
/// merely this instance's own ring), exactly ONE more call to
/// <see cref="IAdSpotStore.ListAiringExclusionsAsync"/> is made with <c>window: 0</c> — that
/// interface's own remarks say a zero window excludes paused-sponsor media only — and a second catalog
/// pick runs against THAT set alone (the ring is dropped too: F173.3's "the window is ignored for that
/// pick"). A hit there is aired and logged once at Information
/// (<c>"Ad anti-repeat relaxed: one sponsor in rotation"</c>) before <see cref="Remember"/> runs the
/// same as any other vend; a miss returns <see langword="null"/> with no log line — a paused sponsor's
/// own media stays excluded on BOTH calls, so pausing can never be relaxed away. The non-empty-store
/// guard matters because a strict pick can also fail purely on THIS instance's own ring (no sponsor
/// paused or repeating at all — every remaining candidate simply happens to sit in the ring already);
/// dropping the ring on relaxation in that case would silently discard the ring's own exclusion for no
/// reason, so relaxation is scoped to overriding SPONSOR pressure only, never plain ring pressure. This
/// second query only ever runs when the first came up empty (with sponsor pressure), so F173.1's "one
/// query" describes the seam's own per-call shape, not a per-pick budget. A window of zero already asks
/// <see cref="IAdSpotStore.ListAiringExclusionsAsync"/> for the paused-only set on the FIRST call, so
/// there is nothing left to relax on a miss (a paused sponsor's exclusion is identical on both calls).
/// </para>
///
/// <para>
/// <b>Repair stays unaware of pause (F173.5).</b> Nothing this class touches feeds
/// <c>AdSpotWorker.RepairReadyEligibilityAsync</c> or the lifecycle guardian — both read only
/// <c>eligible</c>/rendering-age state, never <c>sponsor.paused</c>, so pausing a sponsor changes
/// airing (this class) and stock counting (<c>AdSpotWorker.RefillIfNeededAsync</c>) without touching
/// either repair path.
/// </para>
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
    IOptionsMonitor<AdSpotAntiRepeatOptions> antiRepeatOptions,
    IAdSpotStore spotStore,
    ILogger<LibraryAdSpotSource> logger) : IAdSpotSource
{
    const string RelaxationMessage = "Ad anti-repeat relaxed: one sponsor in rotation";

    readonly object gate = new();
    readonly Queue<string> recentlyVended = new();

    public async ValueTask<MediaItem?> GetNextSpotAsync(CancellationToken ct)
    {
        var libraryId = await ResolveAdsLibraryIdAsync(ct).ConfigureAwait(false);
        if (libraryId is not { } id)
            return null; // No ads library yet (boot seed not run, or renamed away) — no dead-air excuse (F158.1).

        var window = LiveAntiRepeatWindow;
        var scope = new LibraryScope([id]);
        var ring = SnapshotRing();
        var recent = ParseRecentMediaIds(ring);

        var exclusions = await spotStore.ListAiringExclusionsAsync(recent, window, ct).ConfigureAwait(false);
        var exclude = UnionAsStrings(ring, exclusions);
        var reference = await catalog.GetRandomReadyAdSpotAsync(scope, exclude, ct).ConfigureAwait(false);

        if (reference is null && window > 0 && exclusions.Count > 0)
            reference = await RelaxedPickAsync(scope, ct).ConfigureAwait(false);

        if (reference is null)
            return null; // Empty pool — a normal day (F158.3), never an error.

        Remember(reference.MediaId);

        return reference.ToMediaItem() with { SegmentKind = SegmentKind.Ad };
    }

    /// <summary>
    /// F173.3's own one-sponsor relaxation: paused-sponsor media ONLY (<c>window: 0</c>, per
    /// <see cref="IAdSpotStore.ListAiringExclusionsAsync"/>'s own remarks) — the ring plays no part
    /// here, so a sponsor excluded only by the anti-repeat window becomes airable again the moment it
    /// is the sole remaining candidate. The call passes an EMPTY <c>recentMediaIds</c> list rather than
    /// the ring itself (PLAN T439 ruling): paused-only stays structurally true regardless of how the
    /// seam handles <c>window</c>, rather than relying on the seam's own <c>Take(0)</c> discarding
    /// whatever list it was handed. Logs once at Information, and only when this second pick actually
    /// finds something — a miss here means the pool is either empty or entirely paused, and STORY-419
    /// AC4 requires silence in that case, not a log line for a relaxation that changed nothing.
    /// </summary>
    async Task<MediaReference?> RelaxedPickAsync(LibraryScope scope, CancellationToken ct)
    {
        var pausedOnly = await spotStore.ListAiringExclusionsAsync([], window: 0, ct).ConfigureAwait(false);
        var reference = await catalog.GetRandomReadyAdSpotAsync(scope, ToStrings(pausedOnly), ct).ConfigureAwait(false);
        if (reference is not null)
            logger.LogInformation(RelaxationMessage);

        return reference;
    }

    async Task<long?> ResolveAdsLibraryIdAsync(CancellationToken ct)
    {
        var name = adsOptions.CurrentValue.LibraryName;
        var library = await libraryRepository.GetByNameAsync(name, ct).ConfigureAwait(false);
        return library?.Id;
    }

    /// <summary>The live anti-repeat window, clamped non-negative — read fresh at every call site
    /// (<see cref="SnapshotRing"/>, <see cref="Remember"/>, and <see cref="GetNextSpotAsync"/>'s own
    /// exclusion/relaxation decision) rather than cached once per pick, so a config edit an operator
    /// makes mid-pick is visible to whichever of those reads happens to run next — the same "always
    /// live" posture <see cref="SnapshotRing"/>'s own remarks describe.</summary>
    int LiveAntiRepeatWindow => Math.Max(0, antiRepeatOptions.CurrentValue.AntiRepeatWindow);

    /// <summary>
    /// The exclude list for the next vend — bounded by the LIVE anti-repeat window, not merely the
    /// ring's own current size. <see cref="Remember"/> already trims the ring to capacity on every
    /// write, so in steady state this is a plain snapshot; but if an operator SHRINKS the window while
    /// the pool is at or below the ring's PRE-shrink size, every id in the pool would otherwise stay
    /// excluded forever — nothing ever vends again to trigger the write-time trim that would free
    /// room. Applying the live cap here too closes that wedge: the very next read already reflects
    /// the smaller window, with no vend needed to unstick it.
    ///
    /// <para>
    /// Most-recent-first (PLAN T439): <see cref="Remember"/> enqueues the newest vend at the BACK of
    /// the queue, so a plain front-to-back enumeration would read oldest-first — reversed here so the
    /// order satisfies <see cref="IAdSpotStore.ListAiringExclusionsAsync"/>'s own contract (that
    /// interface's remarks), which takes the FIRST <c>window</c> entries as "most recently aired." The
    /// order is unobservable while this snapshot stays capped to that same <c>window</c> the seam
    /// takes — every entry counts as "recent" regardless of position — but the contract, not that
    /// coincidence, is what the reversal satisfies.
    /// </para>
    /// </summary>
    IReadOnlyList<string> SnapshotRing()
    {
        lock (gate)
        {
            var capacity = LiveAntiRepeatWindow;
            var kept = recentlyVended.Skip(Math.Max(0, recentlyVended.Count - capacity));
            return kept.Reverse().ToArray();
        }
    }

    void Remember(string mediaId)
    {
        lock (gate)
        {
            recentlyVended.Enqueue(mediaId);

            var capacity = LiveAntiRepeatWindow;
            while (recentlyVended.Count > capacity)
                recentlyVended.Dequeue();
        }
    }

    /// <summary>Parses the ring's own media-id strings to the <see langword="long"/>s
    /// <see cref="IAdSpotStore.ListAiringExclusionsAsync"/> takes — a non-numeric entry is dropped
    /// silently rather than thrown on: media ids are plain <see langword="bigint"/> on this station
    /// (<c>ad_spot.media_id</c>), while <see cref="MediaReference.MediaId"/> is a general-purpose
    /// <see langword="string"/> key, so a non-numeric ring entry could never correspond to any row
    /// that call's own bigint[] parameter could match regardless.</summary>
    static IReadOnlyList<long> ParseRecentMediaIds(IReadOnlyList<string> ring)
    {
        var recent = new List<long>(ring.Count);
        foreach (var mediaId in ring)
        {
            if (long.TryParse(mediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                recent.Add(parsed);
        }

        return recent;
    }

    static IReadOnlyList<string> UnionAsStrings(IReadOnlyList<string> ring, IReadOnlyList<long> exclusions) =>
        ring.Concat(ToStrings(exclusions)).Distinct(StringComparer.Ordinal).ToArray();

    static IReadOnlyList<string> ToStrings(IReadOnlyList<long> mediaIds) =>
        mediaIds.Select(mediaId => mediaId.ToString(CultureInfo.InvariantCulture)).ToArray();
}
