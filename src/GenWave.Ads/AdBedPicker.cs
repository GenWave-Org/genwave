namespace GenWave.Ads;

using System.Globalization;

/// <summary>
/// Picks the background-music (bed) row a spot renders with, deterministically, once per spot (SPEC
/// F168.2, F168.3; STORY-403; PLAN T416) — the <see cref="AdCastPicker"/> precedent narrowed to a
/// single index pick: no cast/announcer strip, no thin-pool/empty-pool branching, just a seeded index
/// into an already-filtered, already-ordered pool. Pure: no I/O, no logging —
/// <see cref="AdSpotWorker"/> owns turning a <see langword="null"/> result into an INFO line and
/// persisting a non-null one.
///
/// <para>
/// <b>Deterministic in the spot id alone (SPEC F168.3's own regeneration guarantee)</b> — a retry of
/// the SAME spot against the SAME installed pool always re-picks the SAME row, because
/// <see cref="AdDeterministicSeed.FromTerms"/> is seeded from
/// <paramref name="spotId"/> only, unlike <see cref="AdCastPicker.Pick"/>'s own three-term cast seed:
/// the bed pool has no brand/source dimension worth mixing in — a spot's own id is the only thing
/// that must reproduce the SAME pick across renders.
/// </para>
/// </summary>
internal static class AdBedPicker
{
    /// <summary><see langword="null"/> when <paramref name="orderedPool"/> is empty (SPEC F168.2's
    /// own honest fallback — <see cref="AdSpotWorker"/> logs and renders dry); otherwise the row at a
    /// seeded index into the pool, ordered exactly as <paramref name="orderedPool"/> arrived (the
    /// caller — <c>IAdBedPool.ListReadyBedIdsAsync</c> — already orders by id, so this pick is stable
    /// across calls against an unchanged pool).</summary>
    internal static long? Pick(long spotId, IReadOnlyList<long> orderedPool)
    {
        if (orderedPool.Count == 0)
            return null;

        var rng = new Random(AdDeterministicSeed.FromTerms(spotId.ToString(CultureInfo.InvariantCulture)));
        return orderedPool[rng.Next(orderedPool.Count)];
    }
}
