/**
 * Injection point for clock formatting: `timeZone` defaults to the
 * browser's local zone (`Intl.DateTimeFormat` receives `undefined`) so
 * operators see wall-clock-correct times in production. Tests pin a fixed
 * `timeZone` (and optionally `locale`) for determinism instead of relying
 * on the host's TZ.
 *
 * Shared by the Dashboard (Q5) and Live (Q6) pages' history tables.
 */
export interface ClockFormatOptions {
  timeZone?: string;
  locale?: string;
}

/**
 * Formats an ISO timestamp as `HH:MM` (24-hour) in the given zone, or the
 * browser's local zone by default (SPEC F28.7 play-history "time" column).
 */
export function formatClockTime(iso: string, options: ClockFormatOptions = {}): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return "--:--";
  }
  return new Intl.DateTimeFormat(options.locale, {
    hour: "2-digit",
    minute: "2-digit",
    // hourCycle: "h23" (not hour12: false) — some ICU versions render
    // hour12: false as "24:00" at midnight instead of "00:00" (Q5 review
    // finding, folded into Q11). h23 pins the 0-23 cycle explicitly.
    hourCycle: "h23",
    timeZone: options.timeZone,
  }).format(date);
}

/**
 * Formats an ISO timestamp as `HH:MM · Mon D` for the "API up since" tile,
 * in the given zone or the browser's local zone by default. No zone label
 * is rendered — the reference frame is whichever zone the reader is in.
 */
export function formatUpSince(iso: string, options: ClockFormatOptions = {}): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return "unknown";
  }
  const time = formatClockTime(iso, options);
  const monthDay = new Intl.DateTimeFormat(options.locale, {
    month: "short",
    day: "numeric",
    timeZone: options.timeZone,
  }).format(date);
  return `${time} · ${monthDay}`;
}

/**
 * Formats an ISO timestamp as a bare calendar date, `Mon D, YYYY` — no time-of-day, no internal
 * separator of its own — in the given zone or the browser's local zone by default. Distinct from
 * `formatUpSince` (which renders `HH:MM · Mon D`, no year, for the "API up since" tile): a caller
 * that wants a single clean date FIELD — not a time-plus-month-day pair — needs a formatter that
 * doesn't fold its own ` · ` into the result. Used by the Personas provenance badge (SPEC
 * F90.7/F94.4, T105/T130) so "Hired · &lt;source&gt; · &lt;date&gt;" stays a genuine three-segment
 * string instead of five.
 */
export function formatDateStamp(iso: string, options: ClockFormatOptions = {}): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return "unknown";
  }
  return new Intl.DateTimeFormat(options.locale, {
    month: "short",
    day: "numeric",
    year: "numeric",
    timeZone: options.timeZone,
  }).format(date);
}

/**
 * Formats a duration in milliseconds as `M:SS` (or `H:MM:SS` past an hour,
 * hours omitted otherwise) — the single m:ss formatter for both the
 * now-playing card's elapsed/total readout and the history surfaces' plain
 * duration column (SPEC F50.4–F50.5), so the two never drift onto their own
 * formats.
 */
export function formatDuration(durationMs: number): string {
  const totalSeconds = Math.max(0, Math.floor(durationMs / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const mm = String(minutes).padStart(2, "0");
  const ss = String(seconds).padStart(2, "0");
  return hours > 0 ? `${hours}:${mm}:${ss}` : `${mm}:${ss}`;
}

/**
 * Formats a play-history row's optional duration (SPEC F50.5) — blank (not
 * an em-dash, unlike the Catalog table's convention for its own Duration
 * column) when absent: engine-initiated plays and `tts:*` patter entries
 * carry no duration at all (F50.2, F50.6), so there is nothing to show.
 */
export function formatDurationCell(durationMs: number | null | undefined): string {
  return durationMs != null ? formatDuration(durationMs) : "";
}

/**
 * Coarse "how long ago" phrase ("just now", "12m ago", "3h ago", "4d ago") for the Health page's
 * restart-recency readout (gh-#490): the reader needs to place an event in time at a glance, not
 * read a precise duration, so this rounds down to the single largest whole unit and never goes
 * finer than minutes. `null` or an unparseable timestamp reads "unknown" rather than fabricating
 * an age — same "never fabricated" discipline as the container stats' null measurements.
 */
export function formatRelativeAgo(iso: string | null): string {
  if (iso === null) return "unknown";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "unknown";

  const elapsedMs = Math.max(0, Date.now() - date.getTime());
  const minuteMs = 60_000;
  const hourMs = 60 * minuteMs;
  const dayMs = 24 * hourMs;

  if (elapsedMs < minuteMs) return "just now";
  if (elapsedMs < hourMs) return `${Math.floor(elapsedMs / minuteMs)}m ago`;
  if (elapsedMs < dayMs) return `${Math.floor(elapsedMs / hourMs)}h ago`;
  return `${Math.floor(elapsedMs / dayMs)}d ago`;
}

/**
 * Humanizes a measured elapsed time (gh-#141, formats tightened by gh-#210 — the LLM call
 * inspector's ELAPSED column): sub-second stays in raw milliseconds ("842ms" — the precision is
 * the information there), everything from one second up reads in seconds with one decimal
 * ("1.4s", "12.3s"), and the pathological minute-plus case reads "2m 03s". Distinct from
 * {@link formatDuration}'s "mm:ss" on purpose: that shape reads as a playback clock, which a
 * call latency is not.
 */
export function formatElapsedMs(elapsedMs: number): string {
  const ms = Math.max(0, elapsedMs);
  if (ms < 1000) return `${Math.round(ms)}ms`;
  // Round to tenths BEFORE branching — 59 950ms must read "1m 00s", never "60.0s".
  const tenths = Math.round(ms / 100);
  if (tenths < 600) return `${(tenths / 10).toFixed(1)}s`;
  // Round to whole seconds BEFORE splitting — 119 800ms must read "2m 00s", never "1m 60s".
  const totalSeconds = Math.round(ms / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}m ${String(seconds).padStart(2, "0")}s`;
}
