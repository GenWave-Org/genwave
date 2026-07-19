// GenWave spectator page — vanilla JS, no build step (SPEC F63.3–F63.5).
//
// Every network call targets the public read-only surface at /spectator/api/* — never
// /api/* (the admin plane). Three independent cadences:
//   - now-playing: polled every 5s, plus a 1s-tick clock so the progress bar/elapsed
//     readout advances between polls without hammering the server.
//   - play-history + stats: polled every 30s (their SpectatorCacheControl/OutputCache
//     policies match this cadence server-side).
//   - about: fetched once — station identity, license, and the live stream URL rarely
//     change, and a poll would gain nothing.
// A failed poll is swallowed and retried on the next tick; the page never swaps to an
// error state — it just keeps showing the last known-good render (or the initial
// "Loading…" placeholder if nothing has resolved yet).

const NOW_PLAYING_POLL_MS = 5000;
const HISTORY_STATS_POLL_MS = 30000;
const CLOCK_TICK_MS = 1000;
// The API serves up to 20 entries (SPEC F62.6); the pane shows only the freshest few and
// older rows simply fall off as new tracks air (operator request, 2026-07-19).
const MAX_HISTORY_ROWS = 6;

/** @type {{kind: "standby"} | {kind: "track"|"patter", title?: string, artist?: string, startedAt: Date, durationMs: number|null}} */
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
    nowPlaying =
      payload.state === "onAir"
        ? {
            kind: payload.kind,
            title: payload.title,
            artist: payload.artist,
            startedAt: new Date(payload.startedAt),
            durationMs: payload.durationMs ?? null,
          }
        : { kind: "standby" };
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
  const kicker = document.getElementById("now-playing-kicker");
  const title = document.getElementById("now-playing-title");
  const artist = document.getElementById("now-playing-artist");
  const meta = document.getElementById("now-playing-meta");
  const progress = document.getElementById("progress");
  const fill = document.getElementById("progress-fill");
  const clock = document.getElementById("now-playing-clock");

  if (nowPlaying.kind === "standby") {
    dot.classList.remove("now-playing__dot--live");
    kicker.textContent = "Stand by";
    title.textContent = stationName;
    artist.textContent = "";
    meta.hidden = true;
    return;
  }

  dot.classList.add("now-playing__dot--live");

  if (nowPlaying.kind === "patter") {
    kicker.textContent = "DJ break";
    title.textContent = "";
    artist.textContent = "";
  } else {
    kicker.textContent = "On air";
    title.textContent = nowPlaying.title || "Untitled";
    artist.textContent = nowPlaying.artist || "";
  }

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

// ── Wiring ───────────────────────────────────────────────────────────────────

function init() {
  loadAbout();
  pollNowPlaying();
  pollHistory();
  pollStats();

  setInterval(pollNowPlaying, NOW_PLAYING_POLL_MS);
  setInterval(() => {
    pollHistory();
    pollStats();
  }, HISTORY_STATS_POLL_MS);
  setInterval(renderNowPlaying, CLOCK_TICK_MS);
}

init();
