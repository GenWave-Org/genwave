// @jest-environment jsdom
// STORY-479 — Live model and voice lists in the form (gh-#778 · SPEC F205.7g · PLAN T581)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T581). Each Given comment names the arrange the scenario needs.

import { describe, it } from "@jest/globals";

describe("Feature: Live model and voice lists in the form", () => {
  describe("Scenario: a stale list", () => {
    // Given: a choice DTO with choicesStale = true, value "phi4", choices [llama3, phi4]
    it.todo("AC9 — the select renders");
    it.todo("AC9 — the note \"This list may be out of date.\" renders");
  });

  describe("Scenario: a blank value", () => {
    // Given: a choice DTO with value "" and choices [llama3, phi4]
    it.todo("AC10 — the select shows the \"Choose…\" placeholder");
    it.todo("AC10 — no blank option is offered");
  });

  describe("Scenario: the settings control registry", () => {
    // Given: SettingsForm's registry and the settings folder listing
    it.todo("AC19 — Station:Voice has no registry entry");
    it.todo("AC19 — VoiceSettingControl.tsx does not exist");
  });

  // ---- sad path ----
  describe("Scenario: a list that never loaded", () => {
    // Given: a choice DTO with choicesFailed = true, value "phi4", choices [("phi4", "phi4 (not found)")]
    it.todo("AC17 — the select is disabled");
    it.todo("AC17 — the select holds \"phi4\"");
    it.todo("AC17 — the help text renders");
    it.todo("AC17 — the note \"Couldn't load the list.\" renders");
    it.todo("AC18 — no text input renders for the key");
  });
});
