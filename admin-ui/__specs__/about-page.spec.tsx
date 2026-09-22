// @jest-environment jsdom
// STORY-474 — About page (gh-#16 · SPEC F207 · PLAN T561 T559)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T561). Each Given comment names the arrange the scenario needs.

import { describe, it } from "@jest/globals";

describe("Feature: About page", () => {
  describe("Scenario: the page with a response", () => {
    // Given: /about rendered with version, stationName, libraryCount, uptimeSeconds, attributions
    it.todo("AC4 — shows the version");
    it.todo("AC4 — shows the station name");
    it.todo("AC4 — shows the library count");
    it.todo("AC4 — shows the uptime");
    it.todo("AC4 — shows every attribution name");
  });

  describe("Scenario: the sidebar footer", () => {
    // Given: Sidebar rendered
    it.todo("AC5 — About sits in the footer beside Sign out");
  });

});
