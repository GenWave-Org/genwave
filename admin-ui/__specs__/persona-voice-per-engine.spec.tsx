// PLAN T754 — a voice picker per engine on the DJ page (SPEC F219.10, STORY-556 AC5, AC6)
// Runner: Jest (jsdom). Pending: activated by the task named above.

import { describe, it } from "@jest/globals";

describe("Feature: a voice per engine on the DJ page", () => {
  describe("Scenario: two engines", () => {
    it.todo("shows one picker per engine");
    it.todo("puts the active engine's picker first");
    it.todo("marks the active engine");
    it.todo("saves the map with both voices");
  });
  describe("Scenario: Hear it", () => {
    it.todo("requests a sample in that picker's voice and engine");
  });
  describe("Scenario: an engine with no voice chosen", () => {
    it.todo("says this DJ stays quiet on that engine");
  });
});
