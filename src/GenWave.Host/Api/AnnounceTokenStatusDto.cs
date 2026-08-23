namespace GenWave.Host.Api;

/// <summary>
/// The 200 body for <c>GET /api/announcements/token/status</c> (SPEC F146.3, STORY-361, PLAN T344) —
/// the Announcements page's own token panel reads this to render "no token yet" / "token active" plus
/// a last-used indicator, without ever seeing the hash or plaintext: this route answers PRESENCE and
/// RECENCY only, so the reveal-once contract (SPEC F145.3, <see cref="AnnounceTokenGeneratedDto"/>'s
/// own remarks) stays intact — nothing about this record can ever be compared against a caller-
/// supplied guess to confirm or deny a specific token value.
/// </summary>
/// <param name="HasToken">Whether a hash row currently exists — <see langword="false"/> for a
/// station that has never generated a token, or one whose token was revoked and not regenerated.</param>
/// <param name="LastUsedAt">The last instant a Bearer request authenticated successfully, or
/// <see langword="null"/> when the current token (if any) has never been used yet.</param>
public sealed record AnnounceTokenStatusDto(bool HasToken, DateTimeOffset? LastUsedAt);
