// STORY-337 — Icon packs swap the chrome: the renderer + Wardrobe UI halves (PLAN T304).
// Runner: Jest. Todo-scaffolded at /plan (2026-08-15); T304 turns these live.
// Backend halves live in tests/GenWave.Host.Tests/Specs/Story337_IconPacksSwapTheChrome.cs.

import { describe, it } from "@jest/globals";

describe("Feature: the safe icon-pack renderer", () => {
  describe("Scenario: whitelisted primitives render into the house frame", () => {
    it.todo("renders a pack icon's primitives inside the 16×16 IconBase frame");
    it.todo("applies the pack-level strokeWidth/fill style block");
    it.todo("emits only none|currentColor — no literal color can reach the DOM");
  });

  describe("Scenario: per-name fallback keeps the chrome whole", () => {
    it.todo("renders the house icon for any name the active pack lacks");
    it.todo("renders the full house set when no pack is active (empty Station:IconPack)");
  });

  describe("Scenario: the Wardrobe Icons tab", () => {
    it.todo("lists installed packs with a specimen row rendered by the safe renderer");
    it.todo("the settings page shows an inline notice for a dangling Station:IconPack value");
  });
});
