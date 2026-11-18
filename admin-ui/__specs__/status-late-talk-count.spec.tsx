// PLAN T768 — "Talk skipped for lateness" on the Status page (SPEC F219.26, STORY-560 AC3)
// Runner: Jest (jsdom). Pending: activated by the task named above.

import { describe, it } from "@jest/globals";

describe("Feature: the late-talk count", () => {
  describe("Scenario: 2 this week", () => {
    it.todo("shows 2 for the last 7 days");
  });
  describe("Scenario: none this week", () => {
    it.todo("shows None");
  });
});
