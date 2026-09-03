using Microsoft.Extensions.Configuration;

namespace GenWave.Ads;

/// <summary>
/// The two filesystem roots a vended <c>MediaItem.Locator</c> must resolve under (PLAN T396, T390
/// review carry-forward 2) — the media root a scan discovers into, and the authored root every
/// offline-rendered artifact (safe segments, station imaging, and — per SPEC F161.3 — ready ad spots
/// at <c>/authored/ads/{guid}.{format}</c>) lands under.
///
/// <para>
/// <b>Why a duplicate read, not a shared type:</b> the two config keys these values come from are
/// each owned by a project <c>GenWave.Ads</c> must not reference —
/// <c>GenWave.MediaLibrary.Options.LibraryOptions.MediaRoot</c> (<c>Library:MediaRoot</c>) and
/// <c>GenWave.Host.Options.StationSafeOptions.AuthoredRoot</c> (<c>Station:Safe:AuthoredRoot</c>).
/// Referencing either project from here would invert the dependency direction (Host/MediaLibrary
/// depend on the ads seam eventually, per SPEC F163.3's "ads logic lives in GenWave.Ads", never the
/// reverse). Reading the SAME two keys independently, with matching defaults, is the established
/// posture in this codebase already — <c>GenWave.MediaLibrary.Options.ScanOptions.QuarantineExemptRoots</c>'s
/// own remarks document an identical duplicate default ("the default matches
/// Station:Safe:AuthoredRoot's own default; a deployment that relocates the authored volume must
/// update both").
/// </para>
///
/// <para>
/// Resolved ONCE, at composition time (<see cref="FromConfiguration"/>, called only from
/// <see cref="AdsServiceCollectionExtensions.AddGenWaveAds"/>), never from inside
/// <see cref="AdSpotPipeline"/> itself — neither root is Live-allowlisted (both are boot-time
/// filesystem topology, not an ear-tuning knob), so a plain composition-root read, passed down as a
/// resolved value, is truer than threading <c>IConfiguration</c> into business logic.
/// </para>
/// </summary>
/// <param name="MediaRoot">Mirrors <c>Library:MediaRoot</c>'s own default ("/media") when unset.</param>
/// <param name="AuthoredRoot">Mirrors <c>Station:Safe:AuthoredRoot</c>'s own default ("/authored")
/// when unset.</param>
public sealed record AdSpotLocatorRoots(string MediaRoot, string AuthoredRoot)
{
    const string DefaultMediaRoot = "/media";
    const string DefaultAuthoredRoot = "/authored";

    /// <summary>Reads the two root keys straight off <paramref name="configuration"/>, falling back
    /// to each key's own established default when blank/absent.</summary>
    public static AdSpotLocatorRoots FromConfiguration(IConfiguration configuration) =>
        new(
            configuration["Library:MediaRoot"] is { Length: > 0 } mediaRoot ? mediaRoot : DefaultMediaRoot,
            configuration["Station:Safe:AuthoredRoot"] is { Length: > 0 } authoredRoot ? authoredRoot : DefaultAuthoredRoot);
}
