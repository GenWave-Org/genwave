// PLAN T758 — the Voices page (SPEC F219.12–F219.14, STORY-557 AC6)
// Runner: Jest (jsdom). Pending: activated by the task named above.

import { describe, it } from "@jest/globals";

describe("Feature: the Voices page", () => {
  describe("Scenario: clips exist", () => {
    it.todo("lists each clip with its label");
    it.todo("shows the credit on a starter clip");
    it.todo("uploads a file with a label");
    it.todo("removes a deleted clip from the list");
  });
  describe("Scenario: refusals", () => {
    it.todo("shows the reason when an upload is refused");
    it.todo("names the DJs when a clip in use can't be deleted");
  });
});
