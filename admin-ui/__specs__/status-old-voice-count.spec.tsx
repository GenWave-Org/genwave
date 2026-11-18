// PLAN T765 — "N items still in the old voice" on the Status page (SPEC F219.23, STORY-559 AC5)
// Runner: Jest (jsdom). Pending: activated by the task named above.

import { describe, it } from "@jest/globals";

describe("Feature: the old-voice count", () => {
  describe("Scenario: 4 stale items", () => {
    it.todo("shows 4 items still in the old voice");
  });
  describe("Scenario: none stale", () => {
    it.todo("hides the line");
  });
});
