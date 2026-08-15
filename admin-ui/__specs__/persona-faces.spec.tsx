// STORY-333 — The worn face: Personas-page UI halves (PLAN T296).
// Runner: Jest. Todo-scaffolded at /plan (2026-08-15); T296 turns these live.
// Backend halves live in tests/GenWave.Host.Tests/Specs/Story333_TheWornFace.cs.

import { describe, it } from "@jest/globals";

describe("Feature: Personas wear faces in the console", () => {
  describe("Scenario: the face renders where the persona does", () => {
    it.todo("shows the worn face on the persona card and detail");
    it.todo("shows the neutral Wireless placeholder for a faceless persona, never a broken image");
  });

  describe("Scenario: suggestions offer, never write", () => {
    it.todo("highlights a pack item whose suggestedPersona matches a persona slug");
    it.todo("bulk apply sits behind ONE confirm listing the exact item→persona mapping");
    it.todo("closing the confirm issues zero writes");
  });

  describe("Scenario: upload and remove controls", () => {
    it.todo("the upload control PUTs the chosen file to the persona avatar endpoint");
    it.todo("remove issues the DELETE and the placeholder returns");
  });
});
