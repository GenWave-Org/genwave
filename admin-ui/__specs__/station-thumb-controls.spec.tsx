// STORY-370 — I can thumb from the booth: the station-thumb pair beside persona taste (SPEC
// F150.1, F150.8 · PLAN T369)
//
// BDD specification — jest. PENDING until T369. T369 adds a new station-thumb control — plausibly
// app/(authed)/_components/StationThumbControls.tsx, sized next to
// app/(authed)/_components/PersonaTasteThumbs.tsx rather than folded into it — posting to
// POST /api/booth-log/{id}/station-thumb (T367's wire). It renders on the Live now-playing card
// (app/(authed)/live/LiveView.tsx / _components/NowPlayingCard.tsx) beside the existing
// PersonaTasteThumbs pair, and on booth-log track-started rows (app/(authed)/booth-log/
// BoothLogFeed.tsx) beside the same. The two pairs must never be visually confusable (mirrors
// PersonaTasteThumbs' own distinctness proof against RatingControls in
// persona-taste-thumbs.spec.tsx) — distinct glyphs, distinct labels, no shared affordance class.

import { describe, it } from "@jest/globals";

describe("Feature: Station-thumb controls", () => {
  describe("Scenario: the Live now-playing card shows both thumb pairs", () => {
    it.todo("the now-playing card renders a station-thumb pair with its own glyph and label (pending T369, STORY-370 AC4)");
    it.todo(
      "the now-playing card renders a persona-taste pair alongside it, both pairs present at once (pending T369, STORY-370 AC4)"
    );
  });

  describe("Scenario: a booth-log track row shows both thumb pairs", () => {
    it.todo(
      "a booth-log track-started row renders a station-thumb pair beside its persona-taste pair (pending T369, STORY-370 AC4)"
    );
  });

  describe("Scenario: the two pairs never blur together", () => {
    it.todo("the station-thumb pair's glyphs and labels are distinct from the persona-taste pair's (pending T369, STORY-370 AC4)");
    it.todo("the station-thumb pair shares no affordance class with the persona-taste pair (pending T369, STORY-370 AC4)");
  });
});
