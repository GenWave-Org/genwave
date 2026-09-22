// @jest-environment jsdom
// STORY-479 — Live choice lists in the form (gh-#778 · SPEC F205.7 · PLAN T580)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T580). Each Given comment names the arrange the scenario needs.

import { describe, it } from "@jest/globals";

describe("Feature: Live choice lists in the form", () => {
  describe("Scenario: a stale list", () => {
    // Given: a DTO with choicesStale = true
    it.todo("AC6 — the select renders with a \"list may be out of date\" note");
  });

  // ---- sad path ----
  describe("Scenario: a failed probe", () => {
    // Given: a DTO with probeFailed = true, choices []
    it.todo("AC8 — a disabled select with the help text renders");
    it.todo("AC8 — no text input renders");
  });

});
