namespace GenWave.Host.Playout;

/// <summary>
/// Immutable Host-layer read model of what is currently on-air for a station. Built from
/// <see cref="GenWave.Core.Playout.OnAirState"/> after each feeder tick and stored in
/// <see cref="NowPlayingService"/>. Served directly by the API — no engine telnet calls at
/// request time.
/// </summary>
/// <param name="MediaId">Null when a drain token is on-air.</param>
/// <param name="Title">Track title, if known.</param>
/// <param name="Artist">Track artist, if known.</param>
/// <param name="GainDb">Applied loudness-normalisation gain.</param>
/// <param name="StartedAt">UTC wall-clock instant when the current on-air item was detected.</param>
/// <param name="DurationMs">
/// Track duration, if known (SPEC F50.3). <c>tts:*</c> patter carries its measured duration (SPEC
/// F66.1); an engine-initiated play starts null and is patched in place once <see cref="DurationRehydrator"/>
/// recovers it from the catalog (SPEC F66.2) — never fabricated.
/// </param>
/// <param name="IsDrain">True when the safe-rotation/drain token is on-air (no real track).</param>
/// <param name="ArtworkUrl">
/// The F88 token artwork URL for this airing, if known (SPEC F88.4, F93.3, STORY-245, PLAN T125) —
/// carried straight through from <see cref="GenWave.Core.Playout.OnAirState.ArtworkUrl"/>, itself
/// stamped at push time or recovered from a trust-gated echo of the output metadata; never a fresh
/// lookup here. Null for a drain, no art, or no <c>Station:PublicBaseUrl</c>.
/// <para>
/// A <c>tts:*</c> patter airing DOES carry a value here — the reserved station-icon URL
/// <c>ArtworkUrlResolver</c> resolves for every TTS push (SPEC F88.3) — this snapshot never
/// suppresses it. It is the SPECTATOR DTO, <see cref="GenWave.Host.Api.SpectatorPatterNowPlaying"/>,
/// that never exposes <c>artworkUrl</c> for patter, by construction (no such property at all — SPEC
/// F93.3): the page shows the station icon unconditionally for patter, with nothing to null-check.
/// </para>
/// Defaults to null so every pre-F93 call site that constructs this record positionally keeps
/// compiling unchanged.
/// </param>
/// <param name="DjName">
/// gh-#259 — the airing item's own plan-time DJ attribution, carried straight through from
/// <see cref="GenWave.Core.Playout.OnAirState.DjName"/>; the spectator <c>dj</c> field reads THIS,
/// never the schedule's live answer, so the displayed DJ tracks the voice/show actually on air even
/// while a prior schedule's queued items drain after a boundary. Null for a drain, an
/// engine-initiated play, or an item planned with no DJ on shift. Defaults to null so every
/// pre-gh-#259 positional construction site keeps compiling unchanged.
/// </param>
/// <param name="Airing">
/// SPEC F149.4/F150.4 (STORY-369, PLAN T358) — the current on-air music item's opaque airing token,
/// stamped by <see cref="PlayoutFeederService"/> from <see cref="AiringTokenRing.Current"/> at the
/// SAME construction site as every other field here, so a reader can never observe this token
/// paired with a different airing's title/artist (see <see cref="AiringTokenRing"/>'s own
/// "token↔snapshot consistency" remarks). Null BY CONSTRUCTION for a drain, TTS, or before the
/// first advance (PLAN T358 review MED-1): <see cref="IAiringTokenResolver.Current"/> alone
/// survives an intervening non-music item (SPEC F150.4's grace — see that property's own remarks),
/// so <c>PublishSnapshot</c> gates the stamp on <see cref="MusicAiring.IsMusicMediaId"/> rather than
/// forwarding <c>Current</c> unconditionally — a stale-but-still-resolvable token from a prior track
/// must never be stamped onto a snapshot describing an ident/patter/crosstalk/announcement/drain.
/// <see cref="GenWave.Host.Api.SpectatorController"/> only ever surfaces it for a music item.
/// Defaults to null so every pre-T358 positional construction site keeps compiling unchanged.
/// </param>
public sealed record NowPlayingSnapshot(
    string? MediaId,
    string? Title,
    string? Artist,
    double GainDb,
    DateTimeOffset StartedAt,
    int? DurationMs,
    bool IsDrain,
    string? ArtworkUrl = null,
    string? DjName = null,
    string? Airing = null);
