// @jest-environment jsdom
// SPEC F153.10, PLAN T378 — the Gardener nav entry, now inside the grouped Tools group
// (SPEC F203.1, PLAN T558).
//
// Runner: Jest. NAV_GROUPS is plain data (no rendering needed) — same posture the Sidebar itself
// reads it with.

import { describe, it, expect } from "@jest/globals";
import { NAV_GROUPS } from "../app/(authed)/_components/nav-items";

describe("Feature: the Gardener nav entry (SPEC F153.10)", () => {
  describe("Scenario: the Tools group", () => {
    const tools = NAV_GROUPS.find((group) => group.title === "Tools");

    it("places Gardener first in the Tools group", () => {
      expect(tools?.items[0]?.label).toBe("Gardener");
    });

    it("links Gardener to /gardener", () => {
      const gardener = tools?.items.find((item) => item.label === "Gardener");

      expect(gardener?.href).toBe("/gardener");
    });
  });
});
