namespace GenWave.Core.Abstractions;

/// <summary>
/// gh-#117 — the station's wall clock: the ONE seam every "what time is it at the station" read on
/// the LLM/patter path goes through, so the DJ's spoken date/time follows <c>Station:Timezone</c>
/// rather than whatever timezone the container happens to run in. The thin accessor shape mirrors
/// <see cref="IStationIdentityProvider"/>/<see cref="IAudiencePostureProvider"/>: Orchestration and
/// Tts reference only <c>GenWave.Core</c> and cannot see the Host's
/// <c>IOptionsMonitor&lt;StationOptions&gt;</c>, so the Host implementation wraps the live options
/// monitor behind this Core-visible seam.
///
/// <para>
/// Implementations MUST re-resolve the timezone on every read — never cache the resolved
/// <see cref="TimeZoneInfo"/> in a field — so a live <c>Station:Timezone</c> edit governs the very
/// next segment request / prompt build with no api restart (the same contract every sibling
/// provider seam carries). An empty or unresolvable configured timezone falls back to the
/// container's own local zone — the pre-gh-#117 behavior, unchanged.
/// </para>
/// </summary>
public interface IStationClockProvider
{
    /// <summary>Station-local now, resolved fresh on every call.</summary>
    DateTimeOffset LocalNow { get; }
}
