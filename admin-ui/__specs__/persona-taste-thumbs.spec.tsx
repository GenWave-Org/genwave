// @jest-environment jsdom
// STORY-215 — The persona learns only from me, and can't spiral (UI half — SPEC F84.1, F84.6, F84.7)
//
// Runner: Jest (jsdom) + @testing-library/react. Authored pending (house rule since
// Epic S) as it.todo entries — PLAN T71 implements against the now-playing surface and
// booth-log rows. The taste thumb is a DIFFERENT control from the F33 catalog vote
// (curation vs character) and must never be visually confusable with it (F84.7).

import { describe, it } from "@jest/globals";

describe("Feature: Persona taste thumbs", () => {
  describe("Scenario: thumbing the now-playing track", () => {
    // Arrange: now-playing with an active persona; thumb endpoints faked at the fetch seam.
    it.todo("shows taste thumbs attributed to the active persona");
    it.todo("posts one thumb per tap to the taste endpoint");
    it.todo("reflects the recorded direction after the round trip");
  });

  describe("Scenario: thumbing a booth-log row", () => {
    // Arrange: booth-log rows — one stamped with persona A, one unstamped (F84.6).
    it.todo("offers thumbs on a persona-stamped track row");
    it.todo("labels the thumb with the stamped persona, not the now-active one");
  });

  describe("Scenario: guardrails in the UI (sad path)", () => {
    it.todo("offers no taste thumb on an unstamped row");
    it.todo("disables the tapped direction after recording (idempotency affordance)");
    it.todo("renders the taste thumb visually distinct from the catalog vote control");
  });
});
