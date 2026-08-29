// STORY-373 — I can install and tune Deep Cuts: the Shows page rotation rule editor (SPEC
// F152.5-F152.7 · PLAN T362)
//
// BDD specification — jest. PENDING until T362. T362 adds a rotation rule editor to the Shows page
// — plausibly ShowRotationRuleEditor.tsx beside app/(authed)/shows/ShowsClient.tsx, extending
// ShowRequestBody/ShowDto (app/(authed)/shows/types.ts) with an `envelope.rotation` field the PUT
// carries and the GET echoes — plus a pool chip reading GET /api/shows/{id}/rotation-pool
// ({ eligible, since }) and a last-airing line read from booth-log RotationRelax stamps.

import { describe, it } from "@jest/globals";

describe("Feature: Shows page rotation rule", () => {
  describe("Scenario: the editor saves the rule", () => {
    it.todo("saving the rule editor PUTs envelope.rotation on the show (pending T362, STORY-373 AC1)");
    it.todo("after saving, the editor reflects the value the GET echoes back (pending T362, STORY-373 AC1)");
  });

  describe("Scenario: the live pool size", () => {
    it.todo("the show's card shows the eligible pool size from GET /api/shows/{id}/rotation-pool (pending T362, STORY-373 AC2)");
  });

  describe("Scenario: the last airing's relax count", () => {
    it.todo('a show with booth-log picks stamped RotationRelax 0,0,1,2 shows "last airing: 4 picks, 2 relaxed" (pending T362, STORY-373 AC3)');
  });
});
