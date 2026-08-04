// STORY-274 — Previewing and installing a theme (SPEC F103.5, F103.6)
//
// Runner: Jest. Opening a theme's detail/review shows a LIVE composed mini-preview — the fetched
// manifest run through the same ThemeCssComposer into a SCOPED preview container (not :root), so a
// browser sees the real look before adopting it. Because v1 themes are colour-only over the
// already-loaded curated fonts, the preview loads NO new fonts (nothing to thrash on repeated
// opens). Confirming posts the manifest to POST /api/themes/{slug}/import; cancelling does nothing.
//
// Specs are it.todo pending T186 (wire). The live-browser preview/install round-trip is
// browser-verified at T186's acceptance. Un-pin against the rendered component as it lands.

import { describe, it } from "@jest/globals";

describe("Feature: previewing and installing a catalog theme", () => {
  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: the detail view previews the theme live", () => {
    it.todo("composes the fetched manifest via ThemeCssComposer into a scoped preview container (T186, AC1)");

    it.todo("requests no font beyond the already-loaded curated set while previewing (T186, AC2)");
  });

  describe("Scenario: confirming installs the theme", () => {
    it.todo("posts the manifest to the import endpoint and the theme becomes available (T186, AC3)");
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: cancelling installs nothing", () => {
    it.todo("makes no import request and stores no theme when the owner cancels (T186, AC4)");
  });
});
