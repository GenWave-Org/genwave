import type { CatchphraseOveruseStatus, DjRepeatStatus, RepeatsStatus } from "@/lib/broadcast-api";

/** SPEC F218.29, STORY-542 — the Repeats panel's wording, in plain words (no radio jargon, gh-#707). */

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
  const trimmed = dj.trims > 0 ? `, ${dj.trims} cut short` : "";
  const caught = `Caught ${dj.catches} ${plural(dj.catches, "repeat", "repeats")} this week${trimmed}.`;
  return swapped === "" ? caught : `${caught} ${swapped}.`;
}

function timesSaid(count: number): string {
  return count === 2 ? "twice" : `${count} times`;
}

/** e.g. `Flip said "banger, log it" twice in one shift (the limit is 1).` */
export function overuseLine(over: CatchphraseOveruseStatus): string {
  return `${over.name} said "${over.phrase}" ${timesSaid(over.occurrences)} in one shift (the limit is ${over.maxPerShift}).`;
}

/** True when there is nothing worth showing: no DJ had a repeat caught or swapped, no phrase went over. */
export function isQuietWeek(repeats: RepeatsStatus): boolean {
  return repeats.djs.length === 0 && repeats.overused.length === 0;
}
