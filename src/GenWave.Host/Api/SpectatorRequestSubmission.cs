namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /spectator/api/requests</c> (SPEC F87.1, STORY-224, PLAN T87;
/// <paramref name="Genre"/>/<paramref name="Mood"/> added by gh-#131). Exactly three fields —
/// nothing else is bindable, so there is no mass-assignment surface here (no id, no status, no
/// expiry the caller could set). At least ONE of the three must be present; all absent ⇒ 400,
/// nothing written.
/// </summary>
/// <param name="Wish">
/// The listener's free-text request. Longer than <c>Requests:WishMaxLength</c> characters ⇒ 400,
/// nothing written (SPEC F87.1). Optional since gh-#131 — a picker-only request carries none.
/// </param>
/// <param name="Genre">
/// The dropdown genre pick (gh-#131) — validated fail-closed against the CURRENT requestable-genre
/// list (the same list <c>GET /spectator/api/request-options</c> publishes), case-insensitively; a
/// non-member ⇒ 400 and the submitted value is never stored or echoed. Becomes a deterministic
/// predicate directly — no LLM ever sees it.
/// </param>
/// <param name="Mood">
/// The dropdown mood pick (gh-#131) — validated fail-closed against <c>MoodVocabulary.Terms</c>
/// (exact membership); a non-member ⇒ 400 and the submitted value is never stored or echoed.
/// Becomes a deterministic predicate directly — no LLM ever sees it.
/// </param>
public sealed record SpectatorRequestSubmission(string? Wish, string? Genre = null, string? Mood = null);
