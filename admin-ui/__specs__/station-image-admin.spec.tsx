// STORY-339 — The station's own image: the authed admin tab-icon half (PLAN T307).
// Runner: Jest. Todo-scaffolded at /plan (2026-08-15); T307 turns these live.
// Backend halves live in tests/GenWave.Host.Tests/Specs/Story339_TheStationsOwnImage.cs.

import { describe, it } from "@jest/globals";

describe("Feature: the admin console wears the station image", () => {
  describe("Scenario: the authed layout swaps the tab icon", () => {
    it.todo("sets the favicon link to the station image once the session payload reports one");
    it.todo("keeps the shipped icon when no station image is set");
  });
});
