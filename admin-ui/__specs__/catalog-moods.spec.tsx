// @jest-environment jsdom
// STORY-220 — The catalog shows and filters by mood (SPEC F86.8 — UI half, T80)
//
// Runner: Jest + jsdom + @testing-library/react. A Moods column joins the catalog's
// existing column-visibility toggle; a mood filter offers the fixed MoodVocabulary
// terms (imported constant — the control issues NO facet/discovery fetch) and drives
// the repeatable ?mood-exact= query param. Untagged rows show an empty moods cell.
//
// Specs are it.todo pending until T80 ships (depends on T79's browse exposure).
// Un-pin against the Catalog table + filter bar once the column/control land.

import { describe, it } from "@jest/globals";

describe("Feature: catalog moods column and filter", () => {
  describe("Scenario: the moods column", () => {
    it.todo("appears in the column-visibility toggle");
    it.todo("renders each tagged row's mood tags when enabled");
    it.todo("renders an empty cell for untagged rows");
  });

  describe("Scenario: the mood filter", () => {
    it.todo("offers exactly the MoodVocabulary terms as choices");
    it.todo("issues no facet or discovery request for its choices");
    it.todo("applies selections as repeatable mood-exact query params");
    it.todo("composes with an active exact filter instead of replacing it");
  });
});
