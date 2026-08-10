// STORY-317 — Dated specials shadow the grid (F120.3) — form half — PENDING scaffold
// (T259, planned 2026-08-10). 🪂 DROPPABLE SLICE: dropping PR 5 removes these todos with it.
// Deliberately a dated-list form, NOT a second paint grid.

describe("Feature: The specials form", () => {
  describe("Scenario: authoring a dated special", () => {
    it.todo("creates a special with date, span, persona, show, and envelope");
    it.todo("lists upcoming specials by date with edit/delete");
  });

  describe("Scenario: rejections surface honestly", () => {
    it.todo("an overlapping span on the same date surfaces the EXCLUDE rejection in place");
  });
});
