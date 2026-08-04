// STORY-273 — The shelf lists themes beside personas (SPEC F103.3, F103.4)
//
// Runner: Jest. The community-catalog shelf gains a second kind: theme entries are listed on the
// SAME shelf as personas, routed by `kind`, and previewed cheaply — a theme card renders colour
// chips from the entry's `meta` preview swatches with NO manifest fetch and NO CSS composition, so
// a wild card-to-card browse costs nothing beyond the one index read.
//
// Specs are it.todo pending T185 (the wire task builds against golden.theme.json + a fake index;
// the live-browser shelf is browser-verified at T185's acceptance). Un-pin against the rendered
// component as it lands, per the house pattern (see theme-selection.spec.tsx).

import { describe, it } from "@jest/globals";

describe("Feature: the catalog shelf lists themes beside personas", () => {
  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: both kinds appear on one shelf", () => {
    it.todo("lists a theme entry and a persona entry, each routed by its kind (T185, AC1)");
  });

  describe("Scenario: a theme card previews cheaply from meta", () => {
    it.todo("renders colour swatch chips from the entry's meta preview swatches (T185, AC2)");

    it.todo("fetches no theme manifest and composes no CSS while rendering shelf cards (T185, AC3)");
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: the shelf survives the catalog being disabled", () => {
    it.todo("shows the not-available state, not an error, when Community:CatalogIndexUrl is empty (T185, AC4)");
  });
});
