// STORY-392 — I manage the Ads library from one page (page half: AC1–AC5 · F162.1 · pending T404)
// Pending: it.todo only until T404 builds the page; the API half lives in
// tests/GenWave.Host.Tests/Specs/Story392_AdsApi.cs.

import { describe, it } from "@jest/globals";

describe("Feature: The Ads page", () => {
  describe("Scenario: spots list by state", () => {
    it.todo("renders only the active state's spots at ?tab=ready");
    it.todo("badges every state tab with its count");
    it.todo("pages on the shared pager with the 25-default size picker");
  });

  describe("Scenario: the editor round-trips", () => {
    it.todo("saves a valid draft and re-opens it with every field intact");
    it.todo("offers voices from GET /api/voices and beds via the BedPicker");
  });

  describe("Scenario: verbs drive the state machine", () => {
    it.todo("approve/retry/retire move the row and refresh from server truth");
  });

  describe("Scenario: ready spots preview", () => {
    it.todo("plays the rendered artifact in the browser");
  });

  describe("Scenario: briefs are manageable", () => {
    it.todo("lists pack and owner briefs with enable/disable toggles");
    it.todo("adds an owner brief through the form");
  });

  describe("Scenario: rejecting invalid input", () => {
    it.todo("surfaces the validator's 400 rule id on the offending field");
  });
});
