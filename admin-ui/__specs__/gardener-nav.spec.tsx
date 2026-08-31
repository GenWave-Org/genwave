// @jest-environment jsdom
// SPEC F153.10, PLAN T378 — the Gardener nav entry.
//
// Runner: Jest. NAV_ITEMS/visibleNavItems are plain data (no rendering needed) — same posture the
// Sidebar itself reads them with.

import { describe, it, expect } from "@jest/globals";
import { visibleNavItems } from "../app/(authed)/_components/nav-items";

describe("Feature: the Gardener nav entry (SPEC F153.10)", () => {
  describe("Scenario: the sidebar/mobile nav item list", () => {
    it("places Gardener right after Catalog", () => {
      const items = visibleNavItems(false);
      const catalogIndex = items.findIndex((item) => item.label === "Catalog");
      const gardenerIndex = items.findIndex((item) => item.label === "Gardener");

      expect(gardenerIndex).toBe(catalogIndex + 1);
    });

    it("links Gardener to /gardener", () => {
      const items = visibleNavItems(false);
      const gardener = items.find((item) => item.label === "Gardener");

      expect(gardener?.href).toBe("/gardener");
    });
  });
});
