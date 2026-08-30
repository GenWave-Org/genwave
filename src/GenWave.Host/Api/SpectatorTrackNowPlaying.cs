namespace GenWave.Host.Api;

/// <summary>
/// Public shape for <c>GET /spectator/api/now-playing</c> when a real track is on-air (SPEC
/// F62.4). Deliberately a distinct type from <see cref="SpectatorPatterNowPlaying"/> — rather than
/// one shape with nullable title/artist — so a patter airing can omit the properties entirely
/// instead of merely nulling them (F62.9 disclosure-by-construction). Excludes media id, file
/// path, gain/loudness and every admin-only field by simply not having them.
/// <para>
/// <see cref="Airing"/> (SPEC F149.4, STORY-369, PLAN T358) is deliberately NOT an id: it is a
/// 128-bit random, base64url token minted fresh per airing, unique to that one airing, and
/// meaningless off this box — it cannot be reverse-mapped to a catalog id, unlike
/// <see cref="ArtworkUrl"/>'s own per-track-forever token. It exists so a listener can thumb the
/// track currently playing without the payload ever disclosing which catalog row that is.
/// </para>
/// </summary>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Track artist.</param>
/// <param name="StartedAt">UTC wall-clock instant the track started, for elapsed-time computation.</param>
/// <param name="DurationMs">
/// Track duration, if known (SPEC F50.3/F66.2). Null until the Host's duration rehydrator recovers
/// it from the catalog — never fabricated.
/// </param>
/// <param name="Listeners">
/// Live listener count (SPEC F62.12 addendum, STORY-179, gitea-#10), read from
/// <see cref="GenWave.Core.Abstractions.IListenerStatsSource"/>. Null when Icecast's admin stats
/// are unconfigured or unreachable — never fabricated, never surfaced as an error.
/// </param>
/// <param name="Dj">
/// The On-The-Air persona display name (SPEC F67.5-public, F93.1, STORY-244, PLAN T125), or null in
/// a music-only segment or grid gap — never the admin persona id, backstory, or any other field.
/// </param>
/// <param name="DjAvatarUrl">
/// The ON-AIR persona's worn-face token URL (SPEC F129.2, STORY-335, PLAN T299 — the disclosure
/// ruling extending F67.5: "the face is on-air identity"), or null when that persona wears no face
/// or <c>Station:PublicBaseUrl</c> is unset — see <see cref="SpectatorController"/>'s own
/// <c>ResolveDjAvatarUrlAsync</c> remarks. The page renders a placeholder for null, exactly like
/// <see cref="ArtworkUrl"/>'s own station-icon fallback.
/// </param>
/// <param name="Show">
/// The on-air show's <c>{name, tagline}</c> (SPEC F116.4, F115.3; STORY-311, PLAN T251), or null on
/// a grid gap or an unnamed block — read straight off
/// <see cref="GenWave.Abstractions.Playout.OnAirSnapshot.Show"/> (the resolver's own snapshot,
/// F116.1), never a store read on the poll path (F93.4). <see cref="GenWave.Core.Domain.ShowSummary.Flavor"/>
/// never rides here — this type simply has no member for it (F115.3, the persona-soul precedent).
/// </param>
/// <param name="UpNext">
/// Exactly one upcoming segment (SPEC F93.2), or null when there is nothing to announce — see
/// <see cref="SpectatorUpNext"/>'s own remarks for the same-persona collapse rule.
/// </param>
/// <param name="ArtworkUrl">
/// The F88 token artwork URL for this track (SPEC F93.3, STORY-245, PLAN T125), or null when there
/// is no art or <c>Station:PublicBaseUrl</c> is unset — the page falls back to the station icon.
/// </param>
/// <param name="Airing">
/// The current airing's opaque token (SPEC F149.4, F150.4, STORY-369, PLAN T358), read straight off
/// <see cref="GenWave.Host.Playout.NowPlayingSnapshot.Airing"/> — never fabricated here, never
/// re-derived. Null only when nothing music is currently on air (see
/// <see cref="GenWave.Host.Playout.AiringTokenRing"/>'s own remarks for the one deliberate caveat: a
/// safe-loop row still mints one today).
/// </param>
public sealed record SpectatorTrackNowPlaying(
    string? Title, string? Artist, DateTimeOffset StartedAt, int? DurationMs, int? Listeners,
    string? Dj, string? DjAvatarUrl, SpectatorShow? Show, SpectatorUpNext? UpNext, string? ArtworkUrl,
    string? Airing)
{
    public string State => "onAir";
    public string Kind => "track";
}
