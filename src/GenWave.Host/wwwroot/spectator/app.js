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

// The station's own art (also the artwork endpoint's no-oracle fallback, SPEC F88.3) — the
// "loading" state for a track's real cover art and the terminal state for everything else
// (DJ break, a track with no embedded art, standby).
const STATION_ICON_PATH = "/spectator/favicon.ico";

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

function renderRequestVisibility(enabled) {
  document.getElementById("request-section").hidden = !enabled;
}

async function submitRequest(wish) {
  const response = await fetch("/spectator/api/requests", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ wish }),
  });
  return response.ok;
}

function initRequestForm() {
  const form = document.getElementById("request-form");
  const input = document.getElementById("request-wish");
  const message = document.getElementById("request-message");

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    const wish = input.value.trim();
    if (!wish) return;

    let accepted = false;
    try {
      accepted = await submitRequest(wish);
    } catch (error) {
      console.error(error);
    }

    message.textContent = accepted ? REQUEST_THANK_YOU : REQUEST_BUSY;
    if (accepted) input.value = "";
  });
}

// ── Wiring ───────────────────────────────────────────────────────────────────

function init() {
  loadAbout();
  initArtworkFallback();
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
