namespace GenWave.Ads;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;

/// <summary>
/// The cast/bed stamping pair — the ONE home for this stamp-once-before-render shape (PLAN T442
/// ruling), shared by BOTH <see cref="AdSpotWorker"/>'s own write-render pass (SPEC F161.1) and
/// <see cref="AdSpotJobService"/>'s own preview-render pass (SPEC F174.4): a claimed row's voice
/// plan/bed pick is either already set (an owner draft's own explicit choice, or a previous stamp) and
/// left untouched, or picked and stamped exactly once. Neither caller keeps its own copy of this
/// logic.
/// </summary>
/// <remarks>
/// Public, not internal (PLAN T442 ruling, the SAME CS0051 shape <see cref="AdLiveSettings"/>'s own
/// remarks document one class over): <see cref="AdSpotJobService"/>'s own constructor — public,
/// because <c>GenWave.Host</c>'s <c>AdsController</c> injects that type directly — takes this class
/// as a parameter, and a public constructor cannot expose a less-accessible parameter type. No caller
/// outside this assembly actually reaches an <see cref="AdSpotStamper"/> instance; the visibility is
/// wider than the real usage only because the C# rule is mechanical, not because Host now depends on
/// this seam.
/// </remarks>
public sealed class AdSpotStamper(
    IAdSpotStore spotStore,
    IAdBedPool bedPool,
    ILibraryRepository libraryRepository,
    IStationIdentityProvider stationIdentity,
    IOptionsMonitor<AdsOptions> adsOptions,
    ILogger<AdSpotStamper> logger)
{
    /// <summary>
    /// SPEC F167; STORY-402; PLAN T415 — casts <see cref="AdCastPicker"/> exactly once,
    /// ONLY when <paramref name="spot"/> does not already carry a plan (an owner draft's own explicit
    /// <see cref="AdSpot.VoicePlan"/>, or a plan a previous stamp already wrote, is never re-cast; the
    /// C# check here is doubled by <see cref="IAdSpotStore.StampVoicePlanIfNullAsync"/>'s own SQL
    /// <c>coalesce</c> — belt-and-suspenders, not redundant, since the SQL guard is what closes a race
    /// this C# check alone cannot). Returns the row the caller should actually render: the freshly
    /// stamped row when the store returned one, or the ORIGINAL spot when it returned
    /// <see langword="null"/> — a benign race (the row left <see cref="AdState.Rendering"/> between
    /// claim and stamp) that must never abort a render; <see cref="AdRenderService"/>'s own
    /// <c>ResolveCast</c> already degrades gracefully from a null <see cref="AdSpot.VoicePlan"/>.
    /// </summary>
    public async Task<AdSpot> StampCastIfNeededAsync(AdSpot spot, AdLiveSettings liveSettings, CancellationToken ct)
    {
        if (spot.VoicePlan is not null)
            return spot;

        var pick = AdCastPicker.Pick(spot, liveSettings, stationIdentity.Current.Voice);
        LogCastOutcome(spot, pick.Outcome);

        var stamped = await spotStore.StampVoicePlanIfNullAsync(spot.Id, AdVoicePlanJson.Serialize(pick.Entries), ct);
        return stamped ?? spot;
    }

    /// <summary>PLAN T415: one INFO line for a degraded pick, never for the happy path.</summary>
    void LogCastOutcome(AdSpot spot, AdCastOutcome outcome)
    {
        switch (outcome)
        {
            case AdCastOutcome.Cast:
                break;
            case AdCastOutcome.ThinPool:
                logger.LogInformation(
                    "Ad cast pool has only one non-announcer voice for spot {Id} ({Sponsor}); the same voice reads every part",
                    spot.Id, LogSanitize.Strip(spot.SponsorName));
                break;
            case AdCastOutcome.EmptyPool:
                logger.LogInformation(
                    "Ad cast pool is empty for spot {Id} ({Sponsor}); every part uses the station voice",
                    spot.Id, LogSanitize.Strip(spot.SponsorName));
                break;
        }
    }

    /// <summary>
    /// SPEC F168.1, F168.2, F168.5; STORY-403; PLAN T416 — picks <see cref="AdBedPicker"/> exactly
    /// once, ONLY when <paramref name="spot"/> does not already carry a bed (an owner's own explicit
    /// <see cref="AdSpot.BedMediaId"/>, or a pick a previous stamp already wrote, is never re-picked —
    /// the SAME never-overwrite posture <see cref="StampCastIfNeededAsync"/> already keeps for a voice
    /// plan, doubled by <see cref="IAdSpotStore.StampBedIfNullAsync"/>'s own SQL <c>coalesce</c>).
    /// Resolves the ads library id itself (<see cref="AdRenderService"/> resolves it again later for
    /// the render call proper — the two owners never share a request-scoped cache, so no shared state
    /// crosses this method boundary): when the library does not exist yet, this method has nothing to
    /// pick against and returns <paramref name="spot"/> unchanged — <see cref="AdRenderService"/>'s own
    /// <c>ResolveLibraryIdAsync</c> reaches the SAME "the ads library does not exist yet" failure a
    /// moment later and fails the render with the honest reason, so nothing is lost by staying silent
    /// here. An empty pool degrades to an unbedded render with one INFO line (SPEC F168.2's own honest
    /// fallback) rather than a failure — the SAME "render dry, don't refuse" posture an empty cast pool
    /// already gets.
    /// </summary>
    public async Task<AdSpot> StampBedIfNeededAsync(AdSpot spot, CancellationToken ct)
    {
        if (spot.BedMediaId is not null)
            return spot;

        var library = await libraryRepository.GetByNameAsync(adsOptions.CurrentValue.LibraryName, ct);
        if (library is null)
            return spot;

        var pool = await bedPool.ListReadyBedIdsAsync(library.Id, ct);
        var pick = AdBedPicker.Pick(spot.Id, pool);
        if (pick is null)
        {
            logger.LogInformation(
                "No background music is installed; spot {Id} ({Sponsor}) renders without it",
                spot.Id, LogSanitize.Strip(spot.SponsorName));
            return spot;
        }

        var stamped = await spotStore.StampBedIfNullAsync(spot.Id, pick.Value, ct);
        return stamped ?? spot;
    }
}
