namespace GenWave.Host.Api;

/// <summary>
/// The 200 body for <c>POST /api/announcements/token</c> (SPEC F145.3) — the ONLY response that ever
/// carries the plaintext. Reveal-once by construction: nothing else in this feature (no settings
/// read-back, no later GET, no log line) ever serializes this record or its <see cref="Token"/> value
/// again — <c>AnnouncementTokenController.GenerateOrRegenerate</c>'s own remarks name every call site
/// this reveal-once contract depends on.
/// </summary>
public sealed record AnnounceTokenGeneratedDto(string Token);
