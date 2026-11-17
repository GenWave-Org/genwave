/** SPEC F218.47, STORY-551 — the "Callbacks per DJ-hour" number's wording, in plain words (no radio jargon, gh-#707). */

export const CALLBACKS_LABEL = "Callbacks per DJ-hour";
export const NO_CALLBACKS_YET = "None yet";

/** The line under the number, e.g. "5 aired of 8 asked · last 7 days"; null with no offers (the tile says "None yet"). */
export function callbacksCountsLine(asks: number, used: number): string | null {
  return asks === 0 ? null : `${used} aired of ${asks} asked · last 7 days`;
}

/** The station-wide rate to two places ("0.75"), or "None yet" when the api sends no number (no offers). */
export function callbacksPerDjHourText(perDjHour: number | null): string {
  return perDjHour === null ? NO_CALLBACKS_YET : perDjHour.toFixed(2);
}
