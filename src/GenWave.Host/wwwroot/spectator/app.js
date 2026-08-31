// GenWave spectator page — vanilla JS, no build step (SPEC F63.3–F63.5).
//
// Every network call targets the public surface at /spectator/api/* — never /api/* (the admin
// plane). Three independent read cadences, plus one write:
//   - now-playing: polled every 5s, plus a 1s-tick clock so the progress bar/elapsed
//     readout advances between polls without hammering the server.
//   - play-history + stats: polled every 30s (their SpectatorCacheControl/OutputCache
//     policies match this cadence server-side).
//   - about: fetched once — station identity, license, live stream URL, and the
//     listener-requests toggle rarely change, and a poll would gain nothing.
//   - the wish form (SPEC F87.11, STORY-229): a one-shot POST on submit, gated on
//     about.requestsEnabled from the fetch above; see that section's own remarks.
// A failed poll is swallowed and retried on the next tick; the page never swaps to an
// error state — it just keeps showing the last known-good render (or the initial
// "Loading…" placeholder if nothing has resolved yet).

const NOW_PLAYING_POLL_MS = 5000;
const HISTORY_STATS_POLL_MS = 30000;
const CLOCK_TICK_MS = 1000;
// The API serves up to 20 entries (SPEC F62.6); the pane shows only the freshest few and
// older rows simply fall off as new tracks air (operator request, 2026-07-19).
const MAX_HISTORY_ROWS = 6;

// Live-player recovery cadence (gh-#114) — see the "Live player recovery" section. A stall must
// survive STALL_CONFIRM_MS without timeupdate progress before the src is torn down; attempts back
// off exponentially from the base delay up to the cap while the mount stays down.
const RECOVERY_BASE_DELAY_MS = 1000;
const RECOVERY_MAX_DELAY_MS = 30000;
const STALL_CONFIRM_MS = 2000;

// The station's own art (also the artwork endpoint's no-oracle fallback, SPEC F88.3) — the
// "loading" state for a track's real cover art and the terminal state for everything else
// (DJ break, a track with no embedded art, standby). The card-sized 256px PNG, NOT
// /spectator/favicon.ico: the favicon's largest frame is 32px, and upscaling it to the 72px
// art slot is exactly the fuzzy DJ-break art of gh-#258.
const STATION_ICON_PATH = "/spectator/logo.png";

/** @type {{kind: "standby"} | {kind: "track"|"patter", title?: string, artist?: string, startedAt: Date, durationMs: number|null, dj?: string|null, djAvatarUrl?: string|null, show?: {name: string, tagline: string|null}|null, upNext?: {startsAt: string, dj: string|null}|null, artworkUrl?: string|null, airing?: string|null}} */
let nowPlaying = { kind: "standby" };
let stationName = "GenWave";

async function fetchJson(path) {
  const response = await fetch(path);
  if (!response.ok) throw new Error(`GET ${path} failed: ${response.status}`);
  return response.json();
}

function clampMs(value, min, max) {
  return Math.min(Math.max(value, min), max);
}

function formatClock(totalMs) {
  const totalSeconds = Math.floor(Math.max(0, totalMs) / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

function formatTimeOfDay(iso) {
  return new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

// ── Now-playing ──────────────────────────────────────────────────────────────

async function pollNowPlaying() {
  try {
    const payload = await fetchJson("/spectator/api/now-playing");
    const previousArtworkUrl = nowPlaying.kind === "standby" ? null : nowPlaying.artworkUrl ?? null;
    const previousDjAvatarUrl = nowPlaying.kind === "standby" ? null : nowPlaying.djAvatarUrl ?? null;
    nowPlaying =
      payload.state === "onAir"
        ? {
            kind: payload.kind,
            title: payload.title,
            artist: payload.artist,
            startedAt: new Date(payload.startedAt),
            durationMs: payload.durationMs ?? null,
            // dj/djAvatarUrl/show/upNext/artworkUrl (SPEC F93.1–F93.3, F116.4, F129.2) —
            // optional-chained/defaulted since an OutputCache entry from before one of these
            // shipped may still be in rotation and simply lack the property.
            dj: payload.dj ?? null,
            djAvatarUrl: payload.djAvatarUrl ?? null,
            show: payload.show ?? null,
            upNext: payload.upNext ?? null,
            artworkUrl: payload.artworkUrl ?? null,
            // SPEC F149.4/F150.10, STORY-369: undefined for a patter airing (SpectatorPatterNowPlaying
            // simply has no member for it, F62.9's own absence-by-construction), normalized to null —
            // the same shape null already carries for "no music airing to thumb".
            airing: payload.airing ?? null,
          }
        : { kind: "standby" };
    // A changed artwork URL means a new track (or a transition to/from standby) — the previous
    // failure, if any, no longer applies, so give the new URL a fresh attempt.
    const nextArtworkUrl = nowPlaying.kind === "standby" ? null : nowPlaying.artworkUrl ?? null;
    if (nextArtworkUrl !== previousArtworkUrl) failedArtworkUrl = null;
    // Same reset, for the DJ avatar URL (SPEC F129.2): a new on-air persona (or a transition
    // to/from standby) always gets a fresh load attempt.
    const nextDjAvatarUrl = nowPlaying.kind === "standby" ? null : nowPlaying.djAvatarUrl ?? null;
    if (nextDjAvatarUrl !== previousDjAvatarUrl) failedDjAvatarUrl = null;
    renderListenerCount(payload.listeners ?? null);
  } catch (error) {
    console.error(error);
  }
  renderNowPlaying();
}

/** @param {number|null} listeners — null = Icecast stats unavailable; hide rather than guess. */
function renderListenerCount(listeners) {
  const element = document.getElementById("listener-count");
  if (listeners == null) {
    element.hidden = true;
    return;
  }
  element.hidden = false;
  element.textContent = listeners === 1 ? "1 listener tuned in" : `${listeners} listeners tuned in`;
}

function renderNowPlaying() {
  const dot = document.getElementById("now-playing-dot");
  const art = document.getElementById("now-playing-art");
  const kicker = document.getElementById("now-playing-kicker");
  const title = document.getElementById("now-playing-title");
  const artist = document.getElementById("now-playing-artist");
  const meta = document.getElementById("now-playing-meta");
  const progress = document.getElementById("progress");
  const fill = document.getElementById("progress-fill");
  const clock = document.getElementById("now-playing-clock");
  const upNext = document.getElementById("now-playing-upnext");

  if (nowPlaying.kind === "standby") {
    dot.classList.remove("now-playing__dot--live");
    art.hidden = true;
    kicker.textContent = "Stand by";
    title.textContent = stationName;
    artist.textContent = "";
    renderDjCard(null, null, null, false);
    meta.hidden = true;
    renderUpNextLine(upNext, null);
    renderThumbs();
    return;
  }

  dot.classList.add("now-playing__dot--live");
  art.hidden = false;

  if (nowPlaying.kind === "patter") {
    kicker.textContent = "DJ break";
    title.textContent = "";
    artist.textContent = "";
  } else {
    kicker.textContent = "On air";
    title.textContent = nowPlaying.title || "Untitled";
    artist.textContent = nowPlaying.artist || "";
  }

  // gh-#582: "speaking" is narrower than "has a dj" — the byline shows a scheduled host
  // through an entire music segment too (SPEC F129.3's own host-of-the-hour posture), but the
  // enlarged face only earns its keep for the moment that persona is actually the one talking.
  renderDjCard(
    nowPlaying.dj ?? null,
    nowPlaying.show ?? null,
    nowPlaying.djAvatarUrl ?? null,
    nowPlaying.kind === "patter",
  );
  renderArt(art, nowPlaying.artworkUrl ?? null);

  meta.hidden = false;

  const elapsedMs = Date.now() - nowPlaying.startedAt.getTime();

  if (nowPlaying.durationMs == null) {
    progress.hidden = true;
    clock.textContent = formatClock(Math.max(0, elapsedMs));
  } else {
    const clamped = clampMs(elapsedMs, 0, nowPlaying.durationMs);
    progress.hidden = false;
    fill.style.width = `${(clamped / nowPlaying.durationMs) * 100}%`;
    clock.textContent = `${formatClock(clamped)} / ${formatClock(nowPlaying.durationMs)}`;
  }

  renderUpNextLine(upNext, nowPlaying.upNext ?? null);
  renderThumbs();
}

/**
 * Renders the DJ card (SPEC F129.3, STORY-335, PLAN T299): face + name + show, replacing the
 * pre-F129 plain "with {name}" text line. The whole card hides exactly when there is no dj —
 * same condition the retired renderDjLine used, so a music-only segment or grid gap still shows
 * nothing here.
 * <p>Host-only for v1 (PLAN T299 build note, SPEC F129.3's own RECORDED DEFERRAL): dj/djAvatarUrl/
 * show already name the SCHEDULED host of whatever is airing, and a crosstalk exchange airs
 * entirely under that same attribution today — this function never special-cases it. Attributing
 * the crosstalk GUEST voice separately is future work, not a gap in this render.</p>
 * @param {string|null} dj — the on-air persona's display name, or null for a music-only segment.
 * @param {{name: string, tagline: string|null}|null} show — NAME only rides this card; tagline
 *   has no render site yet anywhere on this page.
 * @param {string|null} djAvatarUrl — the on-air persona's worn-face token URL (SPEC F129.2), or
 *   null when faceless — renderDjAvatar below shows the placeholder glyph either way.
 * @param {boolean} isSpeaking — true only for kind === "patter" (gh-#582): the moment this persona
 *   is the one actually talking, as opposed to merely being the hour's scheduled host while a
 *   track plays. Threaded through to renderDjAvatar, which is what actually decides whether the
 *   enlarged treatment applies (it also requires a real, successfully-attempted face — see its own
 *   remarks).
 */
function renderDjCard(dj, show, djAvatarUrl, isSpeaking) {
  const card = document.getElementById("dj-card");
  const name = document.getElementById("dj-card-name");
  const showLine = document.getElementById("dj-card-show");

  card.hidden = !dj;
  name.textContent = dj ? `with ${dj}` : "";
  showLine.hidden = !show;
  showLine.textContent = show ? `· ${show.name}` : "";

  renderDjAvatar(djAvatarUrl, isSpeaking);
}

// Sticky failure memory for the current DJ avatar URL (SPEC F129.2) — the same
// failedArtworkUrl idiom below, one section down, applied to the DJ card's own face slot: once a
// URL has failed to load, renderDjAvatar keeps showing the placeholder glyph for it on every
// subsequent 1s clock tick instead of re-arming the same known-bad URL. Cleared by pollNowPlaying
// whenever djAvatarUrl actually changes (a new on-air persona, or a transition to/from standby,
// always gets a fresh attempt).
let failedDjAvatarUrl = null;

/**
 * Renders the DJ card's face slot (SPEC F129.2/F129.3, gh-#582): the real face when djAvatarUrl is
 * set and has not already failed to load this session, the placeholder glyph otherwise
 * (null/faceless, or a load failure recorded by initDjAvatarFallback). Idempotent on every 1s
 * clock tick, the same discipline renderArt already follows for track artwork.
 *
 * gh-#582 (Dean's ruling: enlarge, don't swap the album-art slot — a circular avatar in a square
 * art box "might not be very visually appealing"): the `dj-card--speaking` class doubles the face
 * in place for the moment the persona is actually talking. It is gated on isSpeaking AND a real
 * face (target !== null) together — RIGHT FACE OR NO FACE means the honest no-face placeholder
 * never inflates to fill the treatment; a faceless persona's patter keeps the small, always-on
 * show-host byline size instead, exactly as before this change.
 * @param {string|null} djAvatarUrl
 * @param {boolean} isSpeaking
 */
function renderDjAvatar(djAvatarUrl, isSpeaking) {
  const card = document.getElementById("dj-card");
  const img = document.getElementById("dj-card-avatar-img");
  const placeholder = document.getElementById("dj-card-avatar-placeholder");
  const target = djAvatarUrl && djAvatarUrl !== failedDjAvatarUrl ? djAvatarUrl : null;

  card.classList.toggle("dj-card--speaking", isSpeaking && target !== null);

  if (target === null) {
    img.hidden = true;
    placeholder.hidden = false;
    return;
  }

  if (img.getAttribute("src") !== target) img.setAttribute("src", target);
  img.hidden = false;
  placeholder.hidden = true;
}

/** Wires the fallback for a real DJ avatar URL that fails to load (SPEC F129.2/F129.3): records
 * the failing URL as failedDjAvatarUrl (so renderDjAvatar stops re-arming it) and swaps back to
 * the placeholder glyph — mirrors initArtworkFallback's own idiom below. Also drops the gh-#582
 * `dj-card--speaking` class immediately rather than waiting for the next 1s tick's renderDjAvatar
 * call to notice the now-failed URL — RIGHT FACE OR NO FACE holds with no visible lag: an enlarged
 * placeholder glyph never renders even for the one tick between the failure and the next poll. */
function initDjAvatarFallback() {
  const img = document.getElementById("dj-card-avatar-img");
  img.addEventListener("error", () => {
    const failing = img.getAttribute("src");
    if (failing) failedDjAvatarUrl = failing;
    img.hidden = true;
    document.getElementById("dj-card-avatar-placeholder").hidden = false;
    document.getElementById("dj-card").classList.remove("dj-card--speaking");
  });
}

/** @param {{startsAt: string, dj: string|null}|null} upNext */
function renderUpNextLine(element, upNext) {
  if (!upNext) {
    element.hidden = true;
    element.textContent = "";
    return;
  }
  element.hidden = false;
  const label = upNext.dj || "Nonstop music";
  element.textContent = `Up next: ${label} · ${formatTimeOfDay(upNext.startsAt)}`;
}

// Sticky failure memory for the current artwork URL (SPEC F93.3): once a URL has failed to load,
// renderArt keeps serving the station icon for it on every subsequent 1s clock tick instead of
// re-arming the same known-bad URL. Cleared by pollNowPlaying whenever the artwork URL actually
// changes (new track = fresh attempt).
let failedArtworkUrl = null;

/**
 * Renders the now-playing thumbnail (SPEC F93.3). Called on every 1s clock tick, so it must be
 * idempotent and must never re-attempt a URL that has already failed:
 *   - success path: the real artwork URL loads once; later ticks reassign the identical target,
 *     which is a no-op guarded below, so there is no repeat fetch.
 *   - failure path: initArtworkFallback records the failing URL in failedArtworkUrl before
 *     swapping the element to the station icon; every later tick sees artworkUrl === failedArtworkUrl
 *     and renders the icon directly — one failed request per URL, not one per tick.
 *   - a new track (artworkUrl changes) resets failedArtworkUrl in pollNowPlaying, so the new URL
 *     always gets a fresh attempt.
 * @param {string|null} artworkUrl
 */
function renderArt(element, artworkUrl) {
  const target = artworkUrl && artworkUrl !== failedArtworkUrl ? artworkUrl : STATION_ICON_PATH;
  if (element.getAttribute("src") !== target) element.setAttribute("src", target);
}

/** Wires the fallback for a real artwork URL that fails to load (SPEC F93.3): records the failing
 * URL as failedArtworkUrl (so renderArt stops re-arming it) before swapping the element to the
 * station icon. Guarded against looping back on itself if the station icon somehow ever 404s. */
function initArtworkFallback() {
  const art = document.getElementById("now-playing-art");
  art.addEventListener("error", () => {
    const failing = art.getAttribute("src");
    if (failing !== STATION_ICON_PATH) {
      failedArtworkUrl = failing;
      art.setAttribute("src", STATION_ICON_PATH);
    }
  });
}

// ── Play history ─────────────────────────────────────────────────────────────

async function pollHistory() {
  try {
    const payload = await fetchJson("/spectator/api/play-history");
    renderHistory(payload.entries.slice(0, MAX_HISTORY_ROWS));
  } catch (error) {
    console.error(error);
  }
}

function renderHistory(entries) {
  const list = document.getElementById("history-list");
  list.textContent = "";

  if (entries.length === 0) {
    const empty = document.createElement("li");
    empty.className = "history__empty";
    empty.textContent = "Nothing has aired yet.";
    list.appendChild(empty);
    return;
  }

  for (const entry of entries) {
    const row = document.createElement("li");
    row.className = "history__row";

    const label = document.createElement("span");
    label.className = "history__label";
    if (entry.kind === "patter") {
      label.textContent = "DJ break";
    } else {
      label.textContent = entry.title || "Untitled";
      if (entry.artist) {
        const artistSpan = document.createElement("span");
        artistSpan.className = "history__artist";
        artistSpan.textContent = ` — ${entry.artist}`;
        label.appendChild(artistSpan);
      }
    }

    const time = document.createElement("span");
    time.className = "history__time";
    time.textContent = formatTimeOfDay(entry.airedAt);

    row.appendChild(label);
    row.appendChild(time);
    list.appendChild(row);
  }
}

// ── Stats ────────────────────────────────────────────────────────────────────

async function pollStats() {
  try {
    const stats = await fetchJson("/spectator/api/stats");
    renderDefinitionList("stats-grid", [
      ["Ready", stats.ready],
      ["Enriching", stats.enriching],
      ["Failed", stats.failed],
    ]);
  } catch (error) {
    console.error(error);
  }
}

/** @param {[string, string|number|Node][]} rows */
function renderDefinitionList(elementId, rows) {
  const dl = document.getElementById(elementId);
  dl.textContent = "";
  for (const [label, value] of rows) {
    const dt = document.createElement("dt");
    dt.textContent = label;
    const dd = document.createElement("dd");
    if (value instanceof Node) dd.appendChild(value);
    else dd.textContent = String(value);
    dl.appendChild(dt);
    dl.appendChild(dd);
  }
}

// ── About (fetched once) ─────────────────────────────────────────────────────

async function loadAbout() {
  try {
    const about = await fetchJson("/spectator/api/about");
    stationName = about.stationName || stationName;

    document.title = `${stationName} — Spectator`;
    document.getElementById("station-name").textContent = stationName;
    updateMediaSessionMetadata();
    if (nowPlaying.kind === "standby") renderNowPlaying();
    renderRequestVisibility(about.requestsEnabled === true);

    const sourceLink = document.createElement("a");
    sourceLink.href = about.projectUrl;
    sourceLink.textContent = about.projectUrl;
    sourceLink.rel = "noopener noreferrer";

    renderDefinitionList("about-grid", [
      ["Station", about.stationName],
      ["Version", about.version],
      ["License", about.license],
      ["Source", sourceLink],
    ]);

    const player = document.getElementById("player");
    const hint = document.getElementById("player-hint");
    if (about.streamUrl) {
      streamUrl = about.streamUrl;
      player.src = about.streamUrl;
      player.hidden = false;
      hint.hidden = true;
    } else {
      player.hidden = true;
      hint.hidden = false;
    }
  } catch (error) {
    console.error(error);
  }
}

// ── Live player recovery (gh-#114) ───────────────────────────────────────────
//
// When the icecast mount drops (engine restart), Chrome tries to resume the live stream with
// `Range: bytes=N-`; icecast answers 200 + fresh live data instead of 206, so Chrome aborts and
// retries — an audible cut-in/cut-out loop until the tab is refreshed. Recovery sidesteps the
// native resume entirely: tear down the src and reattach the stream URL with a changing query
// param, so Chrome fetches the reconnect as a brand-new resource (no Range header to flail with).
//   - only when playback is user-intended: armed by the play event, disarmed by a user pause —
//     a stopped player is never auto-started, and a NotAllowedError from play() disarms rather
//     than fighting the browser's autoplay policy.
//   - one recovery per burst: stalled/error/ended arriving together share the single pending
//     timer, and a transient stall that self-heals (timeupdate progressed while the timer was
//     pending, no element error) cancels itself without an audible reconnect.
//   - backoff: the delay doubles per attempt from RECOVERY_BASE_DELAY_MS up to
//     RECOVERY_MAX_DELAY_MS while the mount stays down, and resets to the base once the playing
//     event confirms real playback resumed.
//   - live pause semantics (gh-#298): a user pause detaches the src (honest stop — kills
//     Chrome's paused-stream background download and its banked buffer), then immediately
//     re-arms a fresh cache-busted src by bare assignment (no load/play — preload="none"
//     fetches nothing) so the native play button stays alive; the next play fetches the armed
//     URL, rejoining the live head instead of the bank.

/** @type {string|null} — the live stream URL from the one-shot about fetch; null = no stream. */
let streamUrl = null;
let playIntended = false;
let lastProgressAt = 0;
let recoveryDelayMs = RECOVERY_BASE_DELAY_MS;
/** @type {number|null} */
let recoveryTimer = null;

/** Schedules one recovery attempt after delayMs, unless one is already pending (a stalled+error
 * burst collapses into a single attempt). At fire time the attempt is skipped if the user has
 * paused meanwhile, or if the stall self-healed — playback progressed after scheduling and the
 * element carries no error (stalled fires transiently on live streams; error/ended never see a
 * later timeupdate, so they always proceed). */
function schedulePlayerRecovery(player, delayMs) {
  if (!streamUrl || !playIntended || recoveryTimer !== null) return;
  const scheduledAt = Date.now();
  recoveryTimer = setTimeout(() => {
    recoveryTimer = null;
    if (!playIntended) return;
    if (player.error === null && !player.ended && lastProgressAt > scheduledAt) return;
    recoveryDelayMs = Math.min(recoveryDelayMs * 2, RECOVERY_MAX_DELAY_MS);
    recoverPlayer(player);
  }, delayMs);
}

/** Composes the stream URL with a unique cache-buster — the fresh URL is what breaks Chrome's
 * Range-resume behavior (gh-#114) and marks every (re)attach as a brand-new resource. The single
 * composition point: recovery and the honest-stop re-arm (gh-#298) both go through here. */
function freshStreamSrc() {
  return `${streamUrl}${streamUrl.includes("?") ? "&" : "?"}reconnect=${Date.now()}`;
}

/** Tears down the src and reattaches a fresh cache-busted stream URL. An AbortError from play()
 * (user paused, or a newer load superseded this one) is deliberately swallowed; a NotAllowedError
 * means the browser revoked autoplay credit, so the player disarms and waits for a real press of
 * play. */
function recoverPlayer(player) {
  player.src = freshStreamSrc();
  player.load();
  player.play().catch((error) => {
    if (error.name === "NotAllowedError") playIntended = false;
  });
}

function initPlayerRecovery() {
  const player = document.getElementById("player");
  player.addEventListener("play", () => {
    playIntended = true;
    // Belt for the truly src-less edge (gh-#298): the honest-stop pause below normally re-arms
    // a fresh src, so this guard stays quiet — it only catches a play reaching an element with
    // no source attached (a pause taken before the about fetch resolved streamUrl, or any other
    // path that left the attribute absent) and rejoins live through the same cache-busted path
    // recovery uses. recoverPlayer's own play() makes this handler re-enter, but by then the
    // src attribute is set, so the guard ends the loop.
    if (streamUrl && !player.getAttribute("src")) recoverPlayer(player);
  });
  player.addEventListener("playing", () => {
    recoveryDelayMs = RECOVERY_BASE_DELAY_MS;
  });
  player.addEventListener("timeupdate", () => {
    lastProgressAt = Date.now();
  });
  player.addEventListener("pause", () => {
    // End-of-stream fires pause before ended (player.ended is already true here) — that is the
    // mount dropping, not the user stopping, so the ended handler below still gets to recover
    // (and the src must stay attached for it: the early return skips the detach too).
    if (player.ended) return;
    playIntended = false;
    if (recoveryTimer !== null) {
      clearTimeout(recoveryTimer);
      recoveryTimer = null;
    }
    // Honest stop (gh-#298): detach the source entirely. A paused progressive stream keeps
    // downloading into Chrome's media cache and resume replays that bank — field-measured two
    // songs behind the live head. Detaching kills the ongoing background download AND the
    // banked buffer. Then RE-ARM with a fresh cache-busted src — assignment only, no load(),
    // no play(): with preload="none" the bare assignment fetches zero bytes (loadstart fires,
    // then the network idles), but it keeps the native controls' play button alive — Chromium
    // refuses a trusted play on a src-less element (verified via Playwright), so a bare detach
    // would dead-end the on-page resume. The pause-time cache-buster stays unique however long
    // the pause lasts; the next play fetches the armed URL fresh, at the live head. Neither
    // step can re-arm recovery: loadstart is not a recovery-scheduled event, playIntended is
    // already false here, and the pending timer (if any) was just cleared.
    player.removeAttribute("src");
    player.load();
    if (streamUrl) player.src = freshStreamSrc();
  });
  // stalled waits out the longer confirm window before the no-progress check above; error/ended
  // are definitively stuck and only need the backoff delay.
  player.addEventListener("stalled", () =>
    schedulePlayerRecovery(player, Math.max(recoveryDelayMs, STALL_CONFIRM_MS)),
  );
  player.addEventListener("error", () => schedulePlayerRecovery(player, recoveryDelayMs));
  player.addEventListener("ended", () => schedulePlayerRecovery(player, recoveryDelayMs));
}

// ── MediaSession (gh-#298) ───────────────────────────────────────────────────
//
// OS media controls (lock screen, media keys, earbuds) present the stream as live radio: the
// station name plus the sharp station mark as artwork, an infinite duration so no scrubber is
// offered, and play/pause actions routed through the <audio> element so they inherit the
// honest-stop / rejoin-live semantics above. Everything is feature-detected — a browser
// without any piece of the API just skips the garnish, never throws.

/** Sets (or refreshes) the MediaSession label — called once at init with the default station
 * name and again from loadAbout when the real one arrives. */
function updateMediaSessionMetadata() {
  if (!("mediaSession" in navigator) || typeof MediaMetadata !== "function") return;
  navigator.mediaSession.metadata = new MediaMetadata({
    title: stationName,
    artwork: [{ src: STATION_ICON_PATH, sizes: "256x256", type: "image/png" }],
  });
}

function initMediaSession() {
  if (!("mediaSession" in navigator)) return;
  const player = document.getElementById("player");
  updateMediaSessionMetadata();
  // Live media has no seekable timeline: an infinite duration tells the OS controls to drop
  // the scrubber. Optional-chained AND try/caught — engines without setPositionState skip it,
  // and an engine that rejects an infinite duration still gets the metadata label above.
  try {
    navigator.mediaSession.setPositionState?.({ duration: Infinity });
  } catch {
    // Garnish only — nothing to recover.
  }
  try {
    navigator.mediaSession.setActionHandler("play", () => {
      // Same catch discipline as recoverPlayer: AbortError (our own rejoin load() superseding
      // this play()) is swallowed; NotAllowedError disarms rather than fighting the browser.
      player.play().catch((error) => {
        if (error.name === "NotAllowedError") playIntended = false;
      });
    });
    navigator.mediaSession.setActionHandler("pause", () => player.pause());
  } catch {
    // setActionHandler throws for unrecognized actions on some engines; garnish only.
  }
}

// ── Request form (SPEC F87.11, STORY-229) ────────────────────────────────────
//
// Visibility is decided once, from the same one-shot about fetch above: requestsEnabled changes
// rarely (an operator flipping Station:Requests:Enabled), so a page reload picking up the change
// is an acceptable lag. Submission never renders the server's response body — constant thank-you
// text on any 2xx, and the same gentle message for a 429 or any other failure, so no state (was it
// throttled? malformed? a server error?) ever leaks to the caller, matching the endpoint's own
// no-oracle discipline (SPEC F87.1).

const REQUEST_THANK_YOU = "Thanks — your request is in the queue.";
const REQUEST_BUSY = "The request line is busy — try again in a few minutes.";

let requestOptionsLoaded = false;

function renderRequestVisibility(enabled) {
  document.getElementById("request-section").hidden = !enabled;
  // Pickers (gh-#131) load once, and only when the form is actually shown — a station with
  // requests off never spends the fetch.
  if (enabled) loadRequestOptions();
}

// ── Pickers (gh-#131) ────────────────────────────────────────────────────────
//
// One fetch of /spectator/api/request-options fills both dropdowns; a failed fetch leaves them
// holding only their blank "Any …" option, so the free-text line keeps working exactly as before —
// the pickers are strictly additive. Submitted values are only ever values the server itself
// published; the server still re-validates fail-closed either way.

async function loadRequestOptions() {
  if (requestOptionsLoaded) return;
  requestOptionsLoaded = true;
  try {
    const options = await fetchJson("/spectator/api/request-options");
    populatePicker("request-genre", "Any genre", options.genres);
    populatePicker("request-mood", "Any mood", options.moods);
  } catch (error) {
    console.error(error);
  }
}

/** @param {string[]|undefined} values */
function populatePicker(elementId, blankLabel, values) {
  const select = document.getElementById(elementId);
  select.textContent = "";
  const blank = document.createElement("option");
  blank.value = "";
  blank.textContent = blankLabel;
  select.appendChild(blank);
  for (const value of values || []) {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = value;
    select.appendChild(option);
  }
}

async function submitRequest(wish, genre, mood) {
  const response = await fetch("/spectator/api/requests", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ wish: wish || null, genre: genre || null, mood: mood || null }),
  });
  return response.ok;
}

function initRequestForm() {
  const form = document.getElementById("request-form");
  const input = document.getElementById("request-wish");
  const genreSelect = document.getElementById("request-genre");
  const moodSelect = document.getElementById("request-mood");
  const message = document.getElementById("request-message");

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const wish = input.value.trim();
    const genre = genreSelect.value;
    const mood = moodSelect.value;
    if (!wish && !genre && !mood) return; // at least one of text/genre/mood (gh-#131)

    let accepted = false;
    try {
      accepted = await submitRequest(wish, genre, mood);
    } catch (error) {
      console.error(error);
    }

    message.textContent = accepted ? REQUEST_THANK_YOU : REQUEST_BUSY;
    if (accepted) {
      input.value = "";
      genreSelect.value = "";
      moodSelect.value = "";
    }
  });
}

// ── Thumbs (SPEC F150.10, STORY-369) ─────────────────────────────────────────
//
// Thumbs discloses nothing over a read (SPEC F150.6's "no read endpoint" — this probe carries no
// counts, no state, nothing about thumbs at all) — GET /spectator/api/thumbs
// (SpectatorThumbsController.ProbeThumbsPresence) exists purely so SurfaceGateMiddleware has a
// real, correctly-tagged endpoint to gate: 404 when Station:Thumbs:Enabled is false (the same F61
// kill-switch semantics every surface on this page uses), 204 with no body when it's true. Rides
// the Spectator read budget (120/min/IP), never the write path's per-IP daily cap — a 5-minute
// re-probe must never spend that budget (see the controller's own remarks). Anything other than
// exactly 204/404 (a network hiccup, an unexpected status) leaves thumbsPresent at its last known
// value rather than guessing. Re-probed every THUMBS_PRESENCE_POLL_MS so an operator flipping the
// switch live is honoured without a reload, the same "no reload needed" posture NOW_PLAYING_POLL_MS
// already gives the rest of the card.
//
// The pair is keyed on `airing` (SPEC F149.4): a changed token means a new track, which resets
// both buttons' aria-pressed state (F150.10 "reset on track change") and clears any pending
// throttle message left over from the previous track. A click always POSTs the token the pair
// CURRENTLY holds (thumbsAiring) — captured synchronously before the fetch, so a track change
// mid-flight can never smuggle the wrong token into the request. The response is checked against
// that SAME captured token before marking a button pressed: the F150.4 grace means a thumb for the
// track that just ended still lands against its own media, but the pair may already have reset
// for the NEW token by the time the response arrives, and that new pair must never show as
// pressed for a click that was about the old one.
//
// The response BODY is never read as an oracle (SPEC F150.3) — only the STATUS decides what the
// page does: 404 means the switch flipped off mid-session (hide), 429 shows a quiet constant
// message, and every other status (the fixed 202, or the 400 this well-formed client should never
// actually trigger) marks the click as landed. localStorage is this page's own record, never the
// server's — every read/write is try/caught (private browsing can throw on either), and nodes are
// built with textContent only, the same no-innerHTML-from-data discipline renderHistory follows.

const THUMBS_PRESENCE_POLL_MS = 5 * 60 * 1000;
const THUMBS_STORAGE_KEY = "genwave-thumbs";
const THUMBS_STORAGE_LIMIT = 10;
const THUMBS_THROTTLE_MESSAGE = "Thanks — one thumb at a time.";
const THUMBS_MESSAGE_DURATION_MS = 4000;

/** @type {boolean} — true once a presence probe has answered anything other than 404. */
let thumbsPresent = false;
/** @type {string|null} — the airing token the rendered pair currently holds. */
let thumbsAiring = null;
/** @type {"up"|"down"|null} */
let thumbsPressedDirection = null;
/** @type {number|null} */
let thumbsMessageTimer = null;
let thumbsSectionBuilt = false;

async function probeThumbsPresence() {
  try {
    const response = await fetch("/spectator/api/thumbs", { method: "GET", credentials: "same-origin" });
    // A real, correctly-gated GET now exists on this route (SpectatorThumbsController.ProbeThumbsPresence):
    // 204 means the surface is on, 404 means the switch is off — the SurfaceGate's standard F61
    // silence. Anything else (a network hiccup below, or an unexpected status this client has no
    // opinion about) leaves thumbsPresent at its last known value rather than guessing either way.
    if (response.status === 204) thumbsPresent = true;
    else if (response.status === 404) thumbsPresent = false;
  } catch (error) {
    console.error(error);
  }
  renderThumbs();
  renderThumbsStrip();
}

/**
 * Builds the pair + strip DOM once, the first time thumbsPresent is confirmed true — the served
 * index.html carries none of this markup (STORY-369 AC9): it exists only after this runs, never
 * pre-rendered. Inserted into now-playing__body between now-playing__meta and
 * now-playing__upnext, the same structural slot the DJ card/meta/upnext trio already occupies.
 */
function ensureThumbsSection() {
  if (thumbsSectionBuilt) return;
  thumbsSectionBuilt = true;

  const meta = document.getElementById("now-playing-meta");
  const upNext = document.getElementById("now-playing-upnext");

  const pair = document.createElement("div");
  pair.className = "thumbs";
  pair.id = "now-playing-thumbs";
  pair.hidden = true;

  const buttons = document.createElement("div");
  buttons.className = "thumbs__buttons";
  buttons.setAttribute("role", "group");
  buttons.setAttribute("aria-label", "Rate this track");

  const upButton = document.createElement("button");
  upButton.type = "button";
  upButton.id = "thumbs-up";
  upButton.className = "thumbs__button";
  upButton.setAttribute("aria-label", "Thumbs up");
  upButton.setAttribute("aria-pressed", "false");
  upButton.textContent = "👍";
  upButton.addEventListener("click", () => submitThumb("up"));

  const downButton = document.createElement("button");
  downButton.type = "button";
  downButton.id = "thumbs-down";
  downButton.className = "thumbs__button";
  downButton.setAttribute("aria-label", "Thumbs down");
  downButton.setAttribute("aria-pressed", "false");
  downButton.textContent = "👎";
  downButton.addEventListener("click", () => submitThumb("down"));

  buttons.appendChild(upButton);
  buttons.appendChild(downButton);

  const message = document.createElement("p");
  message.id = "thumbs-message";
  message.className = "thumbs__message";
  message.setAttribute("aria-live", "polite");
  message.setAttribute("role", "status");

  pair.appendChild(buttons);
  pair.appendChild(message);

  const strip = document.createElement("div");
  strip.className = "thumbs-strip";
  strip.id = "thumbs-strip";
  strip.hidden = true;

  const header = document.createElement("div");
  header.className = "thumbs-strip__header";

  const heading = document.createElement("span");
  heading.className = "thumbs-strip__heading";
  heading.textContent = "Your thumbs";

  const clear = document.createElement("button");
  clear.type = "button";
  clear.id = "thumbs-strip-clear";
  clear.className = "thumbs-strip__clear";
  clear.textContent = "Clear";
  clear.addEventListener("click", clearThumbsHistory);

  header.appendChild(heading);
  header.appendChild(clear);

  const list = document.createElement("ul");
  list.id = "thumbs-strip-list";
  list.className = "thumbs-strip__list";

  strip.appendChild(header);
  strip.appendChild(list);

  meta.parentNode.insertBefore(pair, upNext);
  meta.parentNode.insertBefore(strip, upNext);
}

/**
 * Shows/hides the pair and keeps its aria-pressed state in sync with the CURRENT airing token —
 * called from renderNowPlaying on every 1s tick, so it must be cheap and idempotent, the same
 * discipline renderArt/renderDjAvatar already follow. Absent entirely (thumbsPresent false, the
 * probe's own 404 answer) or no music airing right now (nowPlaying.airing null/missing) both hide
 * the pair without touching its state; a CHANGED airing token resets aria-pressed on both buttons
 * and clears any pending throttle message from the previous track.
 */
function renderThumbs() {
  if (!thumbsPresent) {
    hideThumbsSection();
    return;
  }
  ensureThumbsSection();

  const airing = nowPlaying.kind === "standby" ? null : nowPlaying.airing ?? null;
  const pair = document.getElementById("now-playing-thumbs");

  if (airing === null) {
    pair.hidden = true;
    return;
  }

  if (airing !== thumbsAiring) {
    thumbsAiring = airing;
    thumbsPressedDirection = null;
    clearThumbsMessage();
  }

  pair.hidden = false;
  document.getElementById("thumbs-up").setAttribute("aria-pressed", String(thumbsPressedDirection === "up"));
  document.getElementById("thumbs-down").setAttribute("aria-pressed", String(thumbsPressedDirection === "down"));
}

/** SPEC F150.2's "absent with the same silence" extended to the whole feature: when the presence
 * probe answers 404, the pair AND the strip both disappear together, not just the pair. */
function hideThumbsSection() {
  const pair = document.getElementById("now-playing-thumbs");
  const strip = document.getElementById("thumbs-strip");
  if (pair) pair.hidden = true;
  if (strip) strip.hidden = true;
}

/** @param {"up"|"down"} direction */
async function submitThumb(direction) {
  const airing = thumbsAiring;
  if (!airing) return;

  // Snapshotted BEFORE the request, not read again after — a poll landing mid-flight must never
  // change what gets written to localStorage for THIS click's token.
  const title = nowPlaying.kind === "standby" ? "" : nowPlaying.title || "Untitled";
  const artist = nowPlaying.kind === "standby" ? "" : nowPlaying.artist || "";

  // T368 review MED-1(b): disabled for the whole request, not just filtered after the fact — a
  // double-click's second POST must never even get SENT inside the server's own 30s cooldown.
  // aria-disabled mirrors the native attribute for AT users on a control the DOM still exposes.
  setThumbsButtonsDisabled(true);
  try {
    let response;
    try {
      response = await fetch("/spectator/api/thumbs", {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ airing, direction }),
      });
    } catch (error) {
      console.error(error);
      return;
    }

    if (response.status === 404) {
      thumbsPresent = false;
      renderThumbs();
      return;
    }

    if (response.status === 429) {
      showThumbsMessage(THUMBS_THROTTLE_MESSAGE);
      return;
    }

    // T368 review MED-1(c): only a genuine 2xx (the fixed 202) ever reaches the local record or
    // marks a button pressed — a refused thumb (429/404 above, or any other non-2xx this
    // well-formed client should never actually trigger) must never be asserted locally; the local
    // strip only ever mirrors what the server actually accepted.
    if (!response.ok) return;

    // T368 review MED-1(a): captured BEFORE marking pressed — a flip (👍 then 👎 on the SAME
    // airing) must REPLACE the one local entry for that airing rather than add a second,
    // contradictory row: the server holds exactly one row per (media, airing, listener) too (SPEC
    // F150.7's own upsert). Checked against the SAME captured token this click was sent for: a
    // track change mid-flight already reset thumbsAiring/thumbsPressedDirection for the NEW pair
    // (renderThumbs, above), and this late-arriving response must not un-reset it.
    const alreadyRecorded = thumbsAiring === airing && thumbsPressedDirection !== null;
    if (thumbsAiring === airing) thumbsPressedDirection = direction;
    recordThumbLocally(direction, title, artist, alreadyRecorded);
    renderThumbsStrip();
    renderThumbs();
  } finally {
    setThumbsButtonsDisabled(false);
  }
}

/** T368 review MED-1(b). @param {boolean} disabled */
function setThumbsButtonsDisabled(disabled) {
  for (const id of ["thumbs-up", "thumbs-down"]) {
    const button = document.getElementById(id);
    if (!button) continue;
    button.disabled = disabled;
    button.setAttribute("aria-disabled", String(disabled));
  }
}

function showThumbsMessage(text) {
  const message = document.getElementById("thumbs-message");
  if (!message) return;
  message.textContent = text;
  if (thumbsMessageTimer !== null) clearTimeout(thumbsMessageTimer);
  thumbsMessageTimer = setTimeout(() => {
    message.textContent = "";
    thumbsMessageTimer = null;
  }, THUMBS_MESSAGE_DURATION_MS);
}

function clearThumbsMessage() {
  const message = document.getElementById("thumbs-message");
  if (message) message.textContent = "";
  if (thumbsMessageTimer !== null) {
    clearTimeout(thumbsMessageTimer);
    thumbsMessageTimer = null;
  }
}

// ── Thumbs strip (localStorage, client-only, never fetched — SPEC F150.10) ───────────────────

/**
 * T368 review MED-1(a): one entry per airing. When the pair already holds a pressed direction for
 * THIS SAME airing (a flip — 👍 then 👎 without a track change between them), the existing entry
 * at history[0] is the one this airing already wrote; REPLACE it instead of unshifting a second,
 * contradictory row for the same track. A genuinely new airing (alreadyRecorded false) unshifts as
 * before.
 * @param {"up"|"down"} direction @param {string} title @param {string} artist @param {boolean} alreadyRecorded
 */
function recordThumbLocally(direction, title, artist, alreadyRecorded) {
  const entry = { title, artist, direction, at: new Date().toISOString() };
  const history = loadThumbsHistory();
  if (alreadyRecorded && history.length > 0) history[0] = entry;
  else history.unshift(entry);
  saveThumbsHistory(history.slice(0, THUMBS_STORAGE_LIMIT));
}

function clearThumbsHistory() {
  try {
    localStorage.removeItem(THUMBS_STORAGE_KEY);
  } catch (error) {
    console.error(error);
  }
  renderThumbsStrip();
}

/** @returns {{title: string, artist: string, direction: "up"|"down", at: string}[]} */
function loadThumbsHistory() {
  try {
    const raw = localStorage.getItem(THUMBS_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter(isValidThumbEntry) : [];
  } catch (error) {
    console.error(error);
    return [];
  }
}

/** @param {unknown[]} entries */
function saveThumbsHistory(entries) {
  try {
    localStorage.setItem(THUMBS_STORAGE_KEY, JSON.stringify(entries));
  } catch (error) {
    console.error(error);
  }
}

/** @param {unknown} entry */
function isValidThumbEntry(entry) {
  return (
    entry !== null &&
    typeof entry === "object" &&
    typeof entry.title === "string" &&
    typeof entry.artist === "string" &&
    (entry.direction === "up" || entry.direction === "down") &&
    typeof entry.at === "string"
  );
}

/**
 * Renders the "your thumbs" strip from localStorage — never from the network. Hidden when the
 * feature itself is absent (thumbsPresent false) or the history is empty; every node is built
 * with textContent only, never innerHTML, the same discipline renderHistory follows for
 * server-sourced text.
 */
function renderThumbsStrip() {
  const strip = document.getElementById("thumbs-strip");
  const list = document.getElementById("thumbs-strip-list");
  if (!strip || !list) return;

  const history = loadThumbsHistory();
  list.textContent = "";

  if (!thumbsPresent || history.length === 0) {
    strip.hidden = true;
    return;
  }

  strip.hidden = false;
  for (const entry of history) {
    const row = document.createElement("li");
    row.className = "thumbs-strip__row";

    const glyph = document.createElement("span");
    glyph.className = "thumbs-strip__glyph";
    glyph.setAttribute("aria-hidden", "true");
    glyph.textContent = entry.direction === "down" ? "👎" : "👍";

    const label = document.createElement("span");
    label.className = "thumbs-strip__label";
    // T368 review LOW-2: `at` was stored and validated but never rendered — the existing safe
    // formatTimeOfDay (already used for history/up-next) turns it into a local clock reading, e.g.
    // "Title — Artist · 21:14" (Dean's copy rule: capital-first — Title/Artist are user data, not
    // a copy string, so this needs no capitalization of its own).
    const titleArtist = entry.artist ? `${entry.title} — ${entry.artist}` : entry.title;
    label.textContent = `${titleArtist} · ${formatTimeOfDay(entry.at)}`;

    row.appendChild(glyph);
    row.appendChild(label);
    list.appendChild(row);
  }
}

// ── Wiring ───────────────────────────────────────────────────────────────────

function init() {
  loadAbout();
  initArtworkFallback();
  initDjAvatarFallback();
  initPlayerRecovery();
  initMediaSession();
  pollNowPlaying();
  pollHistory();
  pollStats();
  initRequestForm();
  probeThumbsPresence();

  setInterval(pollNowPlaying, NOW_PLAYING_POLL_MS);
  setInterval(() => {
    pollHistory();
    pollStats();
  }, HISTORY_STATS_POLL_MS);
  setInterval(renderNowPlaying, CLOCK_TICK_MS);
  setInterval(probeThumbsPresence, THUMBS_PRESENCE_POLL_MS);
}

init();
