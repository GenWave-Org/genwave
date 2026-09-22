// @jest-environment jsdom
// STORY-472 — Install and Uninstall on the catalog entry (SPEC F204 · PLAN T564)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T564). Each Given comment names the arrange the scenario needs.

import { describe, it } from "@jest/globals";

describe("Feature: Install and Uninstall on the catalog entry", () => {
  describe("Scenario: a font not installed", () => {
    // Given: installed set without the slug
    it.todo("AC1 — the toggle reads \"Install\"");
  });

  describe("Scenario: a font installed", () => {
    // Given: installed set with the slug
    it.todo("AC2 — the toggle reads \"Uninstall\"");
  });

  describe("Scenario: one installed row per kind", () => {
    // Given: font, icon, avatar, ad pack, jingle pack, voice pack
    it.todo("AC3 — each shows \"Uninstall\"");
  });

  describe("Scenario: the server component", () => {
    // Given: persona-catalog/page.tsx with fetch mocked
    it.todo("AC4 — fetches /api/fonts, /api/icons, /api/avatars, /api/ad-packs, /api/jingle-packs, /api/voice-packs");
  });

  describe("Scenario: a confirmed uninstall of an icon", () => {
    // Given: DELETE mocked 204, ConfirmDialogProvider + Toaster
    it.todo("AC5 — DELETE /api/icons/{slug} is called once");
    it.todo("AC6 — the row reads \"Install\" without a reload");
    it.todo("AC7 — a toast names the pack");
  });

  describe("Scenario: a theme row", () => {
    // Given: a theme catalog entry
    it.todo("AC10 — shows Install only");
    it.todo("AC10 — shows \"Imported themes are managed on the Theme Editor\"");
  });

  describe("Scenario: the source tree", () => {
    // Given: admin-ui scanned
    it.todo("AC13 — referenced-themes.ts no longer exists");
  });

  // ---- sad path ----
  describe("Scenario: a font a theme references", () => {
    // Given: DELETE mocked 409 with extensions.referencedBy ["Night Owl"]
    it.todo("AC11 — the row still reads \"Uninstall\"");
    it.todo("AC12 — the toast contains \"Night Owl\"");
  });

});
