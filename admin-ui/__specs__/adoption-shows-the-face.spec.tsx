// STORY-334 — Faces arrive with adoption: the trust-modal UI half (PLAN T297).
// Runner: Jest. Todo-scaffolded at /plan (2026-08-15); T297 turns these live.
// Backend halves live in tests/GenWave.Host.Tests/Specs/Story334_FacesArriveWithAdoption.cs.

import { describe, it } from "@jest/globals";

describe("Feature: informed adoption shows the face", () => {
  describe("Scenario: the modal renders everything the import carries", () => {
    it.todo("renders the entry's avatar image alongside the full card text");
    it.todo("issues zero write requests before the explicit confirm (the F90 trust posture)");
    it.todo("a faceless entry's modal renders exactly as before — no empty image slot");
  });
});
