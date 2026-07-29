namespace GenWave.Core.Abstractions;

/// <summary>
/// gh-#117 — the station's wall clock: the ONE seam every "what time is it at the station" read
/// goes through — the LLM/patter clocks (gh-#117) and, since gh-#224, the format-clock schedule
/// grid and taste day/hour gating too — so all of them follow <c>Station:Timezone</c>
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

    /// <summary>
    /// The station's timezone itself, resolved fresh on every call (gh-#224) — for the consumer
    /// that must do wall-clock ARITHMETIC in the station's zone rather than merely read "now":
    /// the schedule grid's DST-aware boundary resolution needs the full <see cref="TimeZoneInfo"/>
    /// (spring-forward gaps, fall-back overlaps), which no single <see cref="LocalNow"/> offset
    /// can reconstruct.
    /// </summary>
    TimeZoneInfo Zone { get; }
}
