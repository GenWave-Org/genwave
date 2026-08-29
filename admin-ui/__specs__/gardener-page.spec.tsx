// STORY-374 / STORY-376 / STORY-379 — The Gardener page: findings by kind, Keep this one, and the
// file-action dry-run (SPEC F153.9-F153.10, F153.5, F154.5 · PLAN T378, T381)
//
// BDD specification — jest. PENDING until T378/T381. T378 builds the Gardener page itself —
// app/(authed)/gardener/GardenerClient.tsx + page.tsx, plus a Gardener entry beside
// app/(authed)/_components/nav-items.ts — rendering GET /api/gardener/findings grouped by kind,
// each row offering the STORY-374 AC9 verbs (eligibility, never-play, re-enrich, dismiss, and
// purge on dead-file rows only) plus the near-duplicate group's "Keep this one" (STORY-376 AC6,
// driving the same bulk-eligibility endpoint the catalog table already posts to). T381 adds the
// file-action controls on the same page: a dry-run step against
// POST /api/gardener/file-actions/dry-run (404 when Gardener:FileActions:Enabled is off, STORY-379
// AC1) that renders the returned plan (from → to) before anything executes, and a confirm step
// that posts the plan's plan_token to POST /api/gardener/file-actions/confirm (STORY-379 AC2/AC3).

import { describe, it } from "@jest/globals";

describe("Feature: Gardener page", () => {
  describe("Scenario: findings render grouped by kind", () => {
    it.todo("each finding kind from the response renders as its own section (pending T378, STORY-374 AC9)");
    it.todo("each row offers eligibility, never-play, and re-enrich controls (pending T378, STORY-374 AC9)");
    it.todo("a dead-file row additionally offers a purge control (pending T378, STORY-374 AC9)");
    it.todo("each row offers a dismiss control (pending T378, STORY-374 AC9)");
  });

  describe("Scenario: Keep this one applies bulk eligibility to the duplicate group's siblings", () => {
    it.todo(
      "clicking Keep this one on a duplicate-group row posts the sibling media ids to the bulk eligibility endpoint (pending T378, STORY-376 AC6)"
    );
    it.todo(
      "after Keep this one succeeds, the sibling rows drop out of the duplicate group's list (pending T378, STORY-376 AC6)"
    );
  });

  describe("Scenario: the file-action dry-run shows the plan before executing", () => {
    it.todo("choosing a file action on a row renders the returned plan's from and to paths (pending T381, STORY-379 AC2)");
    it.todo("confirming the plan posts its plan_token to the confirm endpoint (pending T381, STORY-379 AC3)");
  });

  describe("Scenario: file actions disabled (sad path)", () => {
    it.todo(
      "when the dry-run endpoint 404s, the page shows how to enable Gardener file actions (pending T381, STORY-379 AC1)"
    );
  });
});
