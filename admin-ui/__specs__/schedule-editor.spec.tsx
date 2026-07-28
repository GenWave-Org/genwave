// @jest-environment jsdom
// STORY-248 — Paint the week (SPEC F94.3, PLAN T129 — the "or bust" deliverable)
//
// BDD specification — Jest, pending (it.todo). The paint model's state logic (cell math,
// segment merging, 30-minute snapping, week-document serialization) is component/unit
// testable; pointer-drag feel and the 12-DJ-week-in-minutes bar are T129 browser
// acceptance (T92 precedent).

describe("Feature: Paint the week", () => {
  describe("Scenario: painting creates segments", () => {
    it.todo("dragging across cells with a DJ selected produces one segment block on 30-minute boundaries");
    it.todo("extending a drag grows the same segment, not a second one");
    it.todo("the music-only brush produces a persona-less segment");
  });

  describe("Scenario: blocks open the envelope panel", () => {
    it.todo("clicking a block opens the side panel with its genre/energy overrides");
    it.todo("blank envelope fields serialize as station-default (nulls)");
  });

  describe("Scenario: save is the whole week", () => {
    it.todo("save issues one PUT /api/schedule carrying the entire week document");
    it.todo("the grid re-renders from the PUT response");
  });

  describe("Scenario: rejections land on cells", () => {
    it.todo("per-cell 400 errors highlight the offending blocks in place with the error text");
    it.todo("a rejected save never silently drops the edit");
  });
});
