// @jest-environment jsdom
// SPEC F174.1, F174.7, STORY-421 AC1–AC3, STORY-427 AC3/AC4, PLAN T448 — the five-step SpotWizard.
//
// Runner: Jest. it.todo skeleton (house rule since Epic S) — T448 implements against the wizard
// wired to the real routes (T441 write, T442 preview, T445 approve, T446 music browse) and
// un-pins each entry. STORY-421 AC4 (the 120 s pin) is a wire proof (T451), not a jest fact.

import { describe, it } from "@jest/globals";

describe("Feature: New spot is a five-step wizard", () => {
  describe("Scenario: the five step names are exact", () => {
    it.todo("renders the step labels Sponsor, Angle & length, Script, Hear, Approve in that order — AC1");
  });

  describe("Scenario: every step names its purpose in a sentence", () => {
    it.todo("each step carries exactly one purpose sentence — AC2");
  });

  describe("Scenario: the Sponsor step picks from the list or creates inline", () => {
    it.todo("typing an unknown name offers Create — AC3");
    it.todo("choosing Create posts to /api/sponsors and advances with the new sponsor preselected — AC3");
  });

  describe("Scenario: the Hear step's music choice is the installed music", () => {
    it.todo("the dropdown's default option has a null value — STORY-427 AC3");
    it.todo("the default option's label reads 'Let the station pick' — STORY-427 AC3");
    it.todo("there is no numeric text input for a media id — STORY-427 AC4");
  });
});
