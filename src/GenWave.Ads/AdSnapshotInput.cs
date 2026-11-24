namespace GenWave.Ads;

/// <summary>
/// What the ad writer already holds when it decides on one script attempt, handed to <see cref="AdSnapshotSource"/>
/// unchanged. The source does no reads of its own.
/// </summary>
/// <param name="RawReply">The model's reply as received. The source applies the line-aware hygiene itself, without the phone rewrite.</param>
/// <param name="Request">The validator's own request: posture, per-line budget, spot seconds, tolerance, sponsor name and phone, pack-owned.</param>
/// <param name="StationName">The station name.</param>
/// <param name="StationLocalNow">The station-local time of the attempt.</param>
/// <param name="Timezone">The station timezone id.</param>
/// <param name="Attempt">1 for the first write, 2 for the re-ask.</param>
public sealed record AdSnapshotInput(
    string RawReply,
    AdScriptValidationRequest Request,
    string StationName,
    DateTimeOffset StationLocalNow,
    string Timezone,
    int Attempt);
