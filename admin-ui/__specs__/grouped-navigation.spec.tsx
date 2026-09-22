// @jest-environment jsdom
// STORY-471 — Grouped navigation (gh-#779 gh-#707 · SPEC F203 · PLAN T558 T559 T560)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T558–T560). Each Given comment names the arrange the scenario needs.

import { describe, it } from "@jest/globals";

describe("Feature: Grouped navigation", () => {
  describe("Scenario: the nav model", () => {
    // Given: NAV_GROUPS, NAV_TOP, NAV_BOTTOM, NAV_FOOTER from nav-items.ts
    it.todo("AC1 — NAV_GROUPS is Station, Media, Tools, Status with the ratified items in order");
    it.todo("AC2 — NAV_TOP is Dashboard, NAV_BOTTOM is Settings, NAV_FOOTER is About then Sign out");
    it.todo("AC3 — no module exports or imports NAV_ITEMS");
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
    it.todo("AC9 — Media shows Catalog and Announcements only");
  });

  describe("Scenario: an empty group", () => {
    // Given: every Tools item hidden
    it.todo("AC10 — no Tools heading renders");
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
