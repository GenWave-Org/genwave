// STORY-382 — I page through a big kind at my own pace · STORY-383 AC4 — a whole cluster renders
// together (SPEC F153.9/F153.10 riders 2026-08-31 · PLAN T387 · gh-#657)
//
// BDD specification — Jest, pending (it.todo) until T387. The pager is the catalog page's own
// idiom: "page N of M" from the kind-scoped response's `total`, Previous/Next plain anchors,
// size picker 25/50/100/250 living in ?limit= only. /build-loop turns each todo into a real
// spec with one assertion; the production arc is T387's browser smoke.

import { describe, it } from "@jest/globals";

describe("Feature: Gardener pagination", () => {
  describe("Scenario: the default page", () => {
    it.todo("renders 25 rows for a 60-row kind with no paging params");
    it.todo('reads "page 1 of 3"');
    it.todo("renders a Next anchor to page 2");
  });

  describe("Scenario: a deep page from the URL", () => {
    it.todo("renders rows 51-60 at ?page=3 of a 60-row kind");
    it.todo("renders a Previous anchor to page 2");
    it.todo("renders no live Next anchor on the last page");
  });

  describe("Scenario: the size picker", () => {
    it.todo("offers exactly 25, 50, 100, and 250");
    it.todo("writes limit=100 to the URL when 100 is picked");
    it.todo("resets to page 1 when the size changes");
  });

  describe("Scenario: tab switch keeps size, resets page", () => {
    it.todo("keeps limit=100 in the target tab's URL");
    it.todo("resets the target tab to page 1");
  });

  describe("Scenario: the total comes from the response", () => {
    it.todo('derives "page N of M" from the kind-scoped response total, not /api/status');
  });

  describe("Scenario: a whole cluster renders together", () => {
    it.todo("renders all members of a 4-member duplicate group in one card on one page");
  });

  // ── Sad path ──────────────────────────────────────────────────────────
  describe("Scenario: out-of-set sizes read as 25", () => {
    it.todo("treats ?limit=999 as 25");
    it.todo("shows 25 in the picker for an out-of-set ?limit=");
  });

  describe("Scenario: a page beyond the end recovers", () => {
    it.todo("renders the empty state at ?page=3 of a 2-page kind");
    it.todo("keeps the pager live so Previous reaches page 2");
  });
});
