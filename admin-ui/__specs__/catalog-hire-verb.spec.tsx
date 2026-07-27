// @jest-environment jsdom
// STORY-249 — Hire, not import (gh-#169, SPEC F94.4, PLAN T130)
//
// BDD specification — Jest, pending (it.todo). Copy assertions ride the existing
// PersonaCatalogClient harness; the wire-contract-unchanged half is pinned server-side
// (endpoints/DTOs/provenance values keep "import") and needs no UI spec.

describe("Feature: Hire, not import", () => {
  describe("Scenario: the verb is Hire", () => {
    it.todo("the shelf action button says Hire");
    it.todo("the review modal confirm says Hire");
    it.todo("the success copy speaks hiring language");
    it.todo("the provenance badge reads Hired · <source> · <date>");
  });

  describe("Scenario: the contract is still import", () => {
    it.todo("the file-upload path still says Import");
    it.todo("the hire flow calls the unchanged import endpoint");
  });
});
