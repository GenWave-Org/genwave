// @jest-environment jsdom
// STORY-217 — The booth log tells me why each track was picked (SPEC F86.3, F86.5 — UI half, T75)
//
// Runner: Jest + jsdom + @testing-library/react. Booth-log track rows whose API entry
// carries `pick` render one chip per fired rule (summary + signed weight, e.g.
// "artist: MacLeod +0.6") and an exploration badge when isExploration — chips XOR badge,
// never both (F86.5). Rows without pick data render byte-identically to today. The chip
// row is the shared PickChips component T76's Live card reuses.
//
// Specs are it.todo pending until T75 ships (house pattern: safe-scope-empty-badge /
// catalog-rating-toolbar). Un-pin against the rendered BoothLogPage rows + the extracted
// PickChips component once its props are final in review.

import { describe, it } from "@jest/globals";

describe("Feature: booth-log why-this-pick chips", () => {
  describe("Scenario: a stamped rule-driven pick row", () => {
    it.todo("renders one chip per fired rule");
    it.todo("shows each chip's summary with its signed weight");
    it.todo("renders no exploration badge");
  });

  describe("Scenario: a stamped exploration pick row", () => {
    it.todo("renders the exploration badge");
    it.todo("renders zero rule chips");
  });

  describe("Scenario: rows without pick data", () => {
    it.todo("renders an unstamped row without chips, badge, or layout change");
    it.todo("keeps the existing taste thumbs unaffected beside the new chips");
  });
});
