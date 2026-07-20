namespace GenWave.Orchestration;

/// <summary>
/// A single pending "speak at the next boundary" request held by <see cref="SpeechDeferralQueue"/>
/// (SPEC F74.1). <paramref name="Due"/> is the wall-clock instant the deferral became due — for
/// today's only producer (the station-id cadence check) that is always "now", since the trigger
/// and the boundary decision are the same per-unit planning pass; a future producer (e.g. a
/// wall-clock-scheduled handoff) can enqueue a future <paramref name="Due"/> instead.
/// </summary>
/// <param name="Kind">Which scheduled speech this is.</param>
/// <param name="Due">The instant this deferral becomes eligible to air (SPEC F74.1).</param>
/// <param name="Reason">A short, human-readable note for logs/diagnostics — never parsed.</param>
public sealed record SpeechDeferral(SpeechDeferralKind Kind, DateTimeOffset Due, string Reason);
