// @jest-environment jsdom
// STORY-482 — Crosstalk shows as checkboxes (gh-#778 · SPEC F205.7g · PLAN T586)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T586). Each Given comment names the arrange the scenario needs.

import { describe, it } from "@jest/globals";

describe("Feature: Crosstalk shows as checkboxes", () => {
  describe("Scenario: three shows, one enabled", () => {
    // Given: a multi-choice DTO, choices morning-drive/late-late/jazz-hour, value ["late-late"]
    it.todo("AC4 — three checkboxes render");
    it.todo("AC4 — only \"Late Late\" is checked");
  });

  describe("Scenario: enabling a second show", () => {
    // Given: the same, then "Morning Drive" checked and the form saved (fetch captured)
    it.todo("AC5 — the PUT carries Crosstalk:Shows = [\"morning-drive\",\"late-late\"]");
  });

  // ---- sad path ----
  describe("Scenario: the show list failed to load", () => {
    // Given: a multi-choice DTO with choicesFailed = true, value ["late-late"]
    it.todo("AC8 — the checkboxes are disabled");
    it.todo("AC8 — no text input renders for the key");
  });
});
