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
