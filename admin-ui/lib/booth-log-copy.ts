/**
 * Display-only wording for booth-log rows. The stored summary (BoothLogWriter.Summarize(RepeatCaught) in
 * src/GenWave.MediaLibrary/Station/BoothLogWriter.cs; change this parser with it) and the
 * db views that match on it are never touched; this only rewrites what the feed shows.
 */

import { CUT_SHORT_WORDS, STOCK_LINE_SWAP_WORDS } from "@/lib/repeats-copy";

export const REPEAT_CAUGHT_KIND = "repeat-caught";

/** Stored outcome word -> plain words, matching the dashboard Repeats tile (lib/repeats-copy.ts). */
const OUTCOME_LABELS: Readonly<Record<string, string>> = {
  trimmed: CUT_SHORT_WORDS,
  template: STOCK_LINE_SWAP_WORDS,
};

// Stored shape: `Repeat caught: "<phrase>" — <outcome>`. The phrase may itself contain " — ", so
// anchor on the prefix and take the LAST " — " as the outcome separator.
const REPEAT_CAUGHT_SUMMARY = /^Repeat caught: ([\s\S]*) — (trimmed|re-asked|template|dropped)$/;

/**
 * Plain-words summary for a repeat-caught row. With a DJ name it reads `Flip wrote "x" again — cut
 * short`; without one it keeps the stored lead-in. A summary that is not the expected shape is
 * returned unchanged.
 */
export function repeatCaughtSummary(summary: string, djName: string | null): string {
  const match = REPEAT_CAUGHT_SUMMARY.exec(summary);
  if (match === null) return summary;
  const phrase = match[1] ?? "";
  const outcomeWord = match[2] ?? "";
  const outcome = OUTCOME_LABELS[outcomeWord] ?? outcomeWord;
  const lead = djName === null ? "Repeat caught:" : `${djName} wrote`;
  const tail = djName === null ? "" : " again";
  return `${lead} ${phrase}${tail} — ${outcome}`;
}
