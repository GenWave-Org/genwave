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

/** @type {{kind: "standby"} | {kind: "track"|"patter", title?: string, artist?: string, startedAt: Date, durationMs: number|null, dj?: string|null, upNext?: {startsAt: string, dj: string|null}|null, artworkUrl?: string|null}} */
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
    nowPlaying =
      payload.state === "onAir"
        ? {
            kind: payload.kind,
            title: payload.title,
            artist: payload.artist,
            startedAt: new Date(payload.startedAt),
            durationMs: payload.durationMs ?? null,
            // dj/upNext/artworkUrl (SPEC F93.1–F93.3) — optional-chained/defaulted since an
            // OutputCache entry from before PLAN T125 shipped may still be in rotation and
            // simply lack these properties.
            dj: payload.dj ?? null,
            upNext: payload.upNext ?? null,
            artworkUrl: payload.artworkUrl ?? null,
          }
        : { kind: "standby" };
    // A changed artwork URL means a new track (or a transition to/from standby) — the previous
    // failure, if any, no longer applies, so give the new URL a fresh attempt.
    const nextArtworkUrl = nowPlaying.kind === "standby" ? null : nowPlaying.artworkUrl ?? null;
    if (nextArtworkUrl !== previousArtworkUrl) failedArtworkUrl = null;
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
  const dj = document.getElementById("now-playing-dj");
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
    renderDjLine(dj, null);
    meta.hidden = true;
    renderUpNextLine(upNext, null);
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

  renderDjLine(dj, nowPlaying.dj ?? null);
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
}

/** @param {string|null} dj — the on-air persona's display name, or null for a music-only segment. */
function renderDjLine(element, dj) {
  element.hidden = !dj;
  element.textContent = dj ? `with ${dj}` : "";
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
//   - live pause semantics (gh-#298): a user pause also detaches the src (honest stop — kills
//     Chrome's paused-stream background download and its banked buffer); the next play
//     reattaches fresh through recoverPlayer, rejoining the live head instead of the bank.

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

/** Tears down the src and reattaches the stream URL with a cache-buster param — the fresh URL is
 * what breaks Chrome's Range-resume behavior. An AbortError from play() (user paused, or a newer
 * load superseded this one) is deliberately swallowed; a NotAllowedError means the browser revoked
 * autoplay credit, so the player disarms and waits for a real press of play. */
function recoverPlayer(player) {
  player.src = `${streamUrl}${streamUrl.includes("?") ? "&" : "?"}reconnect=${Date.now()}`;
  player.load();
  player.play().catch((error) => {
    if (error.name === "NotAllowedError") playIntended = false;
  });
}

function initPlayerRecovery() {
  const player = document.getElementById("player");
  player.addEventListener("play", () => {
    playIntended = true;
    // Rejoin live (gh-#298): play with no source attached — the honest-stop pause below
    // detached it — reattaches through the same cache-busted path recovery uses, so resume
    // always lands on the live head, never a paused buffer. recoverPlayer's own play() makes
    // this handler re-enter, but by then the src attribute is set, so the guard ends the loop.
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
    // banked buffer; the play handler above rejoins live with a fresh cache-busted attach.
    // load() with no src fires no error/ended, so nothing here re-arms recovery.
    player.removeAttribute("src");
    player.load();
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

// ── Wiring ───────────────────────────────────────────────────────────────────

function init() {
  loadAbout();
  initArtworkFallback();
  initPlayerRecovery();
  initMediaSession();
  pollNowPlaying();
  pollHistory();
  pollStats();
  initRequestForm();

  setInterval(pollNowPlaying, NOW_PLAYING_POLL_MS);
  setInterval(() => {
    pollHistory();
    pollStats();
  }, HISTORY_STATS_POLL_MS);
  setInterval(renderNowPlaying, CLOCK_TICK_MS);
}

init();
