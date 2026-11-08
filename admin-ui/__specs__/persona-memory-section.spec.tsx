// @jest-environment jsdom
// STORY-540 — I can see and shape a DJ's memory (gh-#293 gh-#920 · SPEC F218 · PLAN T707)
//
// BDD specification — Jest. AC1/AC2/AC5 are xUnit in tests/GenWave.Host.Tests/Specs/Story540_ICanSeeAndShapeADjsMemory.cs.
// Every specification is it.todo at plan time (2026-11-08); T707 turns them into `it`.

import { describe, it } from "@jest/globals";

describe("Feature: I can see and shape a DJ's memory", () => {
  // entry point: the Personas page (app/(authed)/personas/) rendered with a mocked api client
  describe("AC3 — the section renders", () => {
    // Given the Personas page for a DJ with memories
    // When  it loads
    it.todo("pending: T707 — a Memory section shows three tabs Lore, Lately, Life with their rows");
    it.todo("pending: T707 — pending rows are flagged");
    it.todo('pending: T707 — forgotten rows are hidden until "show forgotten"');
  });

  describe("AC4 — buttons fire", () => {
    // Given the Life tab
    // When  Forget is pressed on a row
    it.todo("pending: T707 — PATCH forgotten is sent for that row");
  });
});
