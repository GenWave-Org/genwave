namespace GenWave.Host.Api;

/// <summary>
/// The fixed 202 body for <c>POST /spectator/api/requests</c> (SPEC F87.1, STORY-224, PLAN T87) —
/// byte-identical for every accepted wish, matchable, unmatchable, or gibberish: no row id, no wish
/// echo, no queue position, no hint about whether the wish will ever be matched. This type
/// deliberately has NO constructor parameters — there is nothing here for a caller to influence, so
/// nothing here can vary by accident (F87.1's "not a catalog oracle and not a request-state oracle").
/// </summary>
public sealed record SpectatorRequestAccepted
{
    /// <summary>Always <c>"received"</c> — the one fixed status literal (SPEC F87.1).</summary>
    public string Status => "received";

    /// <summary>
    /// Always the same disclaimer: requests are best-effort, not guaranteed to play, and no further
    /// status is exposed for this wish (SPEC F87.1 — no oracle, ever, for any wish).
    /// </summary>
    public string Note => "Requests are best-effort and not guaranteed to play.";
}
