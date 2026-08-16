// STORY-332 — Avatar packs into the library: the Wardrobe/shelf UI halves (PLAN T294).
// Runner: Jest. Todo-scaffolded at /plan (2026-08-15); T294 turns these live.
// Backend halves live in tests/GenWave.Host.Tests/Specs/Story332_AvatarPacksIntoTheLibrary.cs.

import { describe, it } from "@jest/globals";

describe("Feature: Avatar packs in the Wardrobe", () => {
  describe("Scenario: the Avatars tab lists installed packs", () => {
    it.todo("shows every installed pack with its item grid");
    it.todo("shows the Avatars tab even when no pack is installed (empty state, never a hidden tab)");
  });

  describe("Scenario: shelf detail previews stay transient", () => {
    it.todo("renders pack faces from the proxied hash-verified preview route before install");
    it.todo("issues no install/write request from merely opening the detail");
  });
});
