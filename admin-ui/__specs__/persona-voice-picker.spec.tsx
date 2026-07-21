// @jest-environment jsdom
// STORY-210 — Voice is picked from a list, not typed from memory (SPEC F79.4, F79.5)
//
// Runner: Jest (jsdom) + @testing-library/react. Authored pending (house rule since
// Epic S) as it.todo entries — PLAN T68 implements against the persona form and the
// import-warning state. The voices endpoint already exists (hardening-sweep dropdown
// on settings); this reuses its fetch seam, never a second voice-listing path.

import { describe, it } from "@jest/globals";

describe("Feature: Persona voice picker", () => {
  describe("Scenario: live voices listed", () => {
    // Arrange: voices fetch seam resolves the engine's real list.
    it.todo("offers the engine's voice list when the field opens");
    it.todo("submits the selected voiceId unchanged");
  });

  describe("Scenario: import warning leads here", () => {
    // Arrange: a persona imported with an unresolved voice (F79.4 warning state).
    it.todo("names the unresolved voice in the warning");
    it.todo("links the warning to the voice picker");
  });

  describe("Scenario: engine down (sad path)", () => {
    // Arrange: voices fetch seam rejects (engine unreachable).
    it.todo("degrades to a free-text voice field");
    it.todo("keeps the form submittable");
  });
});
