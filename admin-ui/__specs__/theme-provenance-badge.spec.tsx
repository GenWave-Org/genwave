// STORY-275 — Imported-theme provenance (SPEC F103.11)
//
// Runner: Jest. An imported theme is labelled with where it came from ("Imported · <source> ·
// <date>", the F90.7/db-25 persona-provenance pattern) so an owner can tell an installed theme
// from a shipped default and re-find its source; a shipped default shows nothing.
//
// Specs are it.todo pending T187. Un-pin against the rendered component as it lands.

import { describe, it } from "@jest/globals";

describe("Feature: imported-theme provenance badge", () => {
  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: an imported theme shows its provenance", () => {
    it.todo('shows "Imported · <source> · <date>" for a theme imported from the catalog (T187, AC1)');
  });

  describe("Scenario: a shipped default shows none", () => {
    it.todo("renders no provenance label for an embedded default theme (T187, AC2)");
  });
});
