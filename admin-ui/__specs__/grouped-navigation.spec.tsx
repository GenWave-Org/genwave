// @jest-environment jsdom
// STORY-471 — Grouped navigation (gh-#779 gh-#707 · SPEC F203 · PLAN T558 T559 T560)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T558–T560). Each Given comment names the arrange the scenario needs.

import { describe, it, expect } from "@jest/globals";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import {
  NAV_BOTTOM,
  NAV_FOOTER,
  NAV_GROUPS,
  NAV_TOP,
  visibleNavGroups,
  type NavGroup,
} from "../app/(authed)/_components/nav-items";

const ROOT = path.resolve(__dirname, "..");

/** Recursively lists files under `dir` with one of `exts`, skipping build/dep dirs (house pattern,
 * see app-shell.spec.tsx's own `collectFiles`). */
function collectFiles(dir: string, exts: string[], out: string[] = []): string[] {
  const SKIP = new Set(["node_modules", ".next"]);
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (SKIP.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      collectFiles(full, exts, out);
    } else if (exts.some((ext) => entry.name.endsWith(ext))) {
      out.push(full);
    }
  }
  return out;
}

describe("Feature: Grouped navigation", () => {
  describe("Scenario: the nav model", () => {
    // Given: NAV_GROUPS, NAV_TOP, NAV_BOTTOM, NAV_FOOTER from nav-items.ts
    it("AC1 — NAV_GROUPS is Station, Media, Tools, Status with the ratified items in order", () => {
      const shape = NAV_GROUPS.map((group) => ({
        title: group.title,
        items: group.items.map((item) => item.label),
      }));

      expect(shape).toEqual([
        { title: "Station", items: ["Station sounds", "Ads", "Personas", "Schedule", "Shows"] },
        { title: "Media", items: ["Catalog", "Announcements", "Community Catalog"] },
        { title: "Tools", items: ["Gardener", "Theme Editor"] },
        { title: "Status", items: ["Booth log", "Health"] },
      ]);
    });

    it("AC2 — NAV_TOP is Dashboard, NAV_BOTTOM is Settings, NAV_FOOTER is About then Sign out", () => {
      const shape = {
        top: NAV_TOP.map((item) => item.label),
        bottom: NAV_BOTTOM.map((item) => item.label),
        footer: NAV_FOOTER.map((entry) => entry.label),
      };

      expect(shape).toEqual({ top: ["Dashboard"], bottom: ["Settings"], footer: ["About", "Sign out"] });
    });

    it("AC3 — the retired flat nav array is imported and exported nowhere", () => {
      // Built from parts, not the literal identifier — this spec proves the retired token is gone
      // from the codebase, so it must not itself reintroduce a source hit of that same token.
      const retiredExportName = ["NAV", "ITEMS"].join("_");

      const files = [
        ...collectFiles(path.join(ROOT, "app"), [".ts", ".tsx"]),
        ...collectFiles(path.join(ROOT, "__specs__"), [".ts", ".tsx"]),
      ];
      const navItemsPath = path.join(ROOT, "app", "(authed)", "_components", "nav-items.ts");
      const offenders = files.filter((f) => readFileSync(f, "utf-8").includes(retiredExportName));

      // Single assertion that still proves the scan wasn't vacuous: it must have covered
      // nav-items.ts, and found zero offenders.
      expect({ scannedModel: files.includes(navItemsPath), offenders }).toEqual({
        scannedModel: true,
        offenders: [],
      });
    });
  });

  describe("Scenario: the sidebar at /ads", () => {
    // Given: Sidebar rendered with usePathname → "/ads", no stored toggles
    it.todo("AC4 — exactly the Station group is open");
  });

  describe("Scenario: the sidebar at /health", () => {
    // Given: usePathname → "/health"
    it.todo("AC5 — exactly the Status group is open");
  });

  describe("Scenario: a remembered toggle", () => {
    // Given: Media toggled open at /ads, then re-rendered at /ads
    it.todo("AC6 — Media is open alongside Station");
  });

  describe("Scenario: the drawer", () => {
    // Given: MobileNav rendered at /ads
    it.todo("AC8 — lists the same groups and items in the same order as the Sidebar");
  });

  describe("Scenario: the catalog gate", () => {
    // Given: catalog unavailable
    it("AC9 — Media shows Catalog and Announcements only", () => {
      const media = visibleNavGroups(false).find((group) => group.title === "Media");

      expect(media?.items.map((item) => item.label)).toEqual(["Catalog", "Announcements"]);
    });
  });

  describe("Scenario: an empty group", () => {
    // Given: every Tools item hidden — the house Tools group carries no gate, so this drives
    // visibleNavGroups with its own test group (per nav-items.ts's own remarks on that parameter).
    it("AC10 — no Tools heading renders", () => {
      const groups: NavGroup[] = [
        { title: "Tools", items: [{ href: "/gardener", label: "Gardener", iconName: "restore", requiresCatalog: true }] },
      ];

      expect(visibleNavGroups(false, groups)).toEqual([]);
    });
  });

  describe("Scenario: the retired routes", () => {
    // Given: app/(authed)/live/page.tsx and app/(authed)/wardrobe/page.tsx
    it.todo("AC11 — /live redirects to /");
    it.todo("AC12 — /wardrobe redirects to /persona-catalog");
    it.todo("AC13 — no import of PickChips, RatingControls, StationThumbs, PersonaTasteThumbs, PlayHistoryTable or the Wardrobe tabs remains");
  });

  describe("Scenario: station sounds", () => {
    // Given: the imaging page and its nav entry
    it.todo("AC14 — label and h1 read \"Station sounds\"");
    it.todo("AC14 — the one-line explainer is present");
  });

  // ---- sad path ----
  describe("Scenario: storage that throws", () => {
    // Given: localStorage access throws
    it.todo("AC7 — renders with the route rule only");
  });

});
