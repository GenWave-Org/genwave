import { formatWeekdayMonthDay, type ClockFormatOptions } from "@/lib/format-clock";
import type { CatchphraseOveruseStatus, DjRepeatStatus, RepeatsStatus } from "@/lib/broadcast-api";

/** SPEC F218.29, STORY-542 — the Repeats panel's wording, in plain words (no radio jargon, gh-#707). */

/** Shared with the booth log (lib/booth-log-copy.ts). The tile uses CUT_SHORT_WORDS; its stock-line sentence
 * pluralises, so it spells its own words, and STOCK_LINE_SWAP_WORDS copies its singular form for the feed. */
export const CUT_SHORT_WORDS = "cut short";
export const STOCK_LINE_SWAP_WORDS = "swapped for a stock line";

export const NO_REPEATS_LINE = "No repeats caught this week.";

function plural(count: number, singular: string, pluralForm: string): string {
  return count === 1 ? singular : pluralForm;
}

/**
 * One DJ's line, e.g. "Caught 3 repeats this week, 2 cut short. 1 line swapped for a stock line." Stock-line
 * swaps are their own sentence: they also count lines that failed the truth check, not only caught repeats.
 */
export function djRepeatLine(dj: DjRepeatStatus): string {
  const swapped =
    dj.fallbacks > 0
      ? `${dj.fallbacks} ${plural(dj.fallbacks, "line", "lines")} swapped for ${plural(dj.fallbacks, "a stock line", "stock lines")}`
      : "";
  if (dj.catches === 0) {
    return `${swapped} this week.`;
  }
  const trimmed = dj.trims > 0 ? `, ${dj.trims} ${CUT_SHORT_WORDS}` : "";
  const caught = `Caught ${dj.catches} ${plural(dj.catches, "repeat", "repeats")} this week${trimmed}.`;
  return swapped === "" ? caught : `${caught} ${swapped}.`;
}

function timesSaid(count: number): string {
  return count === 2 ? "twice" : `${count} times`;
}

/** e.g. `Flip said "banger, log it" twice in one shift on Sun Nov 8 (the limit is 1).` */
export function overuseLine(over: CatchphraseOveruseStatus, clock: ClockFormatOptions = {}): string {
  const said = `${over.name} said "${over.phrase}" ${timesSaid(over.occurrences)} in one shift`;
  const limit = `(the limit is ${over.maxPerShift}).`;
  // An older api sends no day: fall back to the sentence without one rather than naming "unknown".
  if (over.lastSaidAt === undefined || Number.isNaN(Date.parse(over.lastSaidAt))) {
    return `${said} ${limit}`;
  }
  return `${said} on ${formatWeekdayMonthDay(over.lastSaidAt, clock)} ${limit}`;
}

/** True when there is nothing worth showing: no DJ had a repeat caught or swapped, no phrase went over. */
export function isQuietWeek(repeats: RepeatsStatus): boolean {
  return repeats.djs.length === 0 && repeats.overused.length === 0;
}
