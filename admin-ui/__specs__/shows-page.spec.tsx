// STORY-312 — The Shows page (F119.1, F119.3) — PENDING scaffold (T244, planned 2026-08-10)
//
// it.todo until /build-loop implements the page; UI-flow ACs beyond these are browser
// acceptance per the T92 precedent.

describe("Feature: The Shows page", () => {
  describe("Scenario: authoring in place", () => {
    it.todo("renders the show list with the provenance line on imported shows");
    it.todo("creates a show with name/tagline/flavor under budget maxlengths (60/120/400)");
    it.todo("edits an authored show and round-trips every field");
    it.todo("supports several shows referencing the same persona's blocks (one DJ, many shows)");
  });

  describe("Scenario: guarded delete UX", () => {
    it.todo("surfaces the 409 refusal naming the referencing schedule blocks");
    it.todo("deletes an unreferenced show after confirm");
  });

  describe("Scenario: coverage stays neutral", () => {
    it.todo("shows no nudge, badge, or warning anywhere for unnamed blocks (F119.3)");
  });
});
