// STORY-353 — A red LLM tile names its cause (SPEC F139.2, the admin-ui half · PLAN T334)
//
// BDD specification — Jest (jsdom). PENDING via it.todo until T334 builds the surface;
// the backend half rides tests/GenWave.Host.Tests/Specs/Story353_LlmCauseTaxonomy.cs.

import { describe, it } from "@jest/globals";

describe("Feature: a red LLM tile names its cause", () => {
  describe("Scenario: the tile explains a red verdict", () => {
    it.todo("names the dominant recent cause when the LLM verdict is red (T334)");
    it.todo("names the model alongside the cause (T334)");
  });

  describe("Scenario: quiet states stay quiet", () => {
    it.todo("shows no cause line when the LLM verdict is green (T334)");
  });
});
