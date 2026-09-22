// @jest-environment jsdom
// STORY-473 — Toasts mount on the built image (gh-#765 · SPEC F206 · PLAN T568 T569)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T568–T569). Each Given comment names the arrange the scenario needs.

import { describe, it } from "@jest/globals";

describe("Feature: Toasts mount on the built image", () => {
  describe("Scenario: the built bundle", () => {
    // Given: admin-ui/.next scanned after `next build`
    it.todo("AC2 — exactly one copy of the sonner module is present");
  });

  describe("Scenario: a jsdom mount", () => {
    // Given: <Toaster/> rendered, toast("saved") called
    it.todo("AC4 — \"saved\" is in the document");
  });

  describe("Scenario: the production image (Playwright, recorded in T568/T569)", () => {
    // Given: a settings save on the dev station
    it.todo("AC1 — the repro report records [data-sonner-toaster] presence and the console");
    it.todo("AC3 — after the fix the toast node is found on the rebuilt image");
  });

});
