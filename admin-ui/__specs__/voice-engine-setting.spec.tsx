// PLAN T750 — "Voice engine" on the Settings page (SPEC F219.2, STORY-555 AC4)
// Runner: Jest (jsdom). Pending: activated by the task named above.

import { describe, it } from "@jest/globals";

describe("Feature: the voice engine setting", () => {
  describe("Scenario: two engines in the registry", () => {
    it.todo("lists both engines");
    it.todo("marks the active engine");
    it.todo("saves the chosen engine");
  });
  describe("Scenario: the save is refused", () => {
    it.todo("shows the error and keeps the old choice");
  });
});
