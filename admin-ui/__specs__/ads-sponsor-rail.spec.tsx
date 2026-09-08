// @jest-environment jsdom
// SPEC F171.8, STORY-413, PLAN T447 — the Ads page groups by sponsor.
//
// Runner: Jest. it.todo skeleton (house rule since Epic S) — T447 implements against the
// /ads page's SponsorRail + the scoped right pane and un-pins each entry.

import { describe, it } from "@jest/globals";

describe("Feature: The Ads page groups by sponsor", () => {
  describe("Scenario: the sponsor rail lists every sponsor", () => {
    it.todo("renders one rail row per sponsor with its name — AC1");
    it.todo("shows the briefs count on each row — AC1");
    it.todo("shows the spots-total count on each row — AC1");
    it.todo("shows a Paused badge on the paused sponsor only — AC1");
  });

  describe("Scenario: selecting a sponsor scopes the right pane", () => {
    it.todo("shows only the selected sponsor's briefs — AC2");
    it.todo("shows only the selected sponsor's spots — AC2");
    it.todo("opens New spot with the selected sponsor preselected — AC2");
  });

  describe("Scenario: no jargon", () => {
    it.todo("the rendered page contains neither 'brand' nor 'advertiser' — AC3");
  });
});
