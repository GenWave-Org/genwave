// @jest-environment jsdom
// STORY-219 — I can inspect what my persona's taste is and what it has learned (SPEC F86.7 — UI half, T78)
//
// Runner: Jest + jsdom + @testing-library/react. Each persona row on the Personas page gains
// an expandable read-only "Taste" section fed by GET /api/personas/{id}/taste: rules grouped
// authored / operator / accrued, each with a signed weight bar in [−1, +1], plus the accrued
// cap meter (count / 50 from the response — the UI hardcodes no cap). A persona with no
// taste states that plainly. No mutation control exists anywhere in the section.
//
// Specs are it.todo pending until T78 ships (depends on T77's endpoint).
// Un-pin against the PersonasClient row expand once the section's markup lands.

import { describe, it } from "@jest/globals";

describe("Feature: persona taste inspector", () => {
  describe("Scenario: expanding a persona with rules from all three sources", () => {
    it.todo("groups rules under authored, operator, and accrued headings");
    it.todo("renders a signed weight bar per rule spanning −1 to +1");
    it.todo("shows each rule's predicate summary");
    it.todo("renders the accrued cap meter from the response's count and cap");
  });

  describe("Scenario: expanding a persona with no taste", () => {
    it.todo("states plainly that the persona has no taste yet");
  });

  describe("Scenario: read-only guarantees", () => {
    it.todo("renders no edit, delete, or add control anywhere in the section");
  });
});
