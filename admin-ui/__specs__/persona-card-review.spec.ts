// STORY-235 — informed catalog import (SPEC F90.5, F90.6, PLAN T103), review finding #9: direct
// unit coverage for the pure formatters `PersonaCardReviewModal` calls, previously only exercised
// indirectly through a full component render.
//
// Runner: Jest (node) — pure functions, no DOM needed.

import { describe, it, expect } from "@jest/globals";
import {
  DAY_LABELS,
  describeTasteContext,
  describeTastePredicate,
  formatWeight,
  parsePersonaCardReview,
} from "../app/(authed)/_components/persona-card-review";

describe("Feature: Taste predicate/context readable-form formatters", () => {
  describe("Scenario: describeTastePredicate names every set constraint, not just the most specific", () => {
    it("falls back to 'any track' when no predicate field is set", () => {
      expect(describeTastePredicate({ artist: null, genre: null, tag: null })).toBe("any track");
    });

    it("names artist alone", () => {
      expect(describeTastePredicate({ artist: "Radiohead", genre: null, tag: null })).toBe("artist: Radiohead");
    });

    it("joins every set field, artist first", () => {
      expect(describeTastePredicate({ artist: "Radiohead", genre: "Rock", tag: "moody" })).toBe(
        "artist: Radiohead, genre: Rock, tag: moody"
      );
    });
  });

  describe("Scenario: describeTasteContext reads the day/hour gate in plain words", () => {
    it("falls back to 'any time' when ungated", () => {
      expect(describeTasteContext({ daysOfWeek: [], startHour: null, endHour: null })).toBe("any time");
    });

    it("names the days alone when hours are unbounded", () => {
      expect(describeTasteContext({ daysOfWeek: [0, 3], startHour: null, endHour: null })).toBe("Sun, Wed");
    });

    it("names the hour range alone when every day matches", () => {
      expect(describeTasteContext({ daysOfWeek: [], startHour: 6, endHour: 12 })).toBe("06:00–12:00");
    });

    it("joins days and hour range when both are gated", () => {
      expect(describeTasteContext({ daysOfWeek: [0], startHour: 6, endHour: 12 })).toBe("Sun · 06:00–12:00");
    });

    it("falls back to '?' for a day index outside DAY_LABELS' 0-6 range", () => {
      expect(describeTasteContext({ daysOfWeek: [7], startHour: null, endHour: null })).toBe("?");
      expect(DAY_LABELS).toHaveLength(7);
    });
  });

  describe("Scenario: formatWeight is always signed and two decimals within SPEC F82.1's [-1, 1]", () => {
    it("signs a positive weight", () => {
      expect(formatWeight(0.4)).toBe("+0.40");
    });

    it("signs a negative weight", () => {
      expect(formatWeight(-0.6)).toBe("-0.60");
    });

    it("signs zero as positive", () => {
      expect(formatWeight(0)).toBe("+0.00");
    });

    it("rounds to two decimal places", () => {
      expect(formatWeight(0.123456)).toBe("+0.12");
    });

    it("does not flag the boundary values — F82.1's range is inclusive of ±1", () => {
      expect(formatWeight(1)).toBe("+1.00");
      expect(formatWeight(-1)).toBe("-1.00");
    });
  });

  describe("Scenario: an out-of-range weight (hostile/malformed card) shows the TRUE value, honestly flagged (review follow-up #2)", () => {
    it("renders the real value with an out-of-range marker rather than clamping it to something the card never said", () => {
      expect(formatWeight(5000)).toBe("+5000.00 (out of range)");
      expect(formatWeight(-5000)).toBe("-5000.00 (out of range)");
    });

    it("switches to exponential notation only once the magnitude is too large to read as plain digits — the value itself is still never altered", () => {
      expect(formatWeight(1e21)).toBe("+1.00e+21 (out of range)");
      expect(formatWeight(-1e21)).toBe("-1.00e+21 (out of range)");
    });
  });
});

describe("Feature: parsePersonaCardReview's otherFields projection", () => {
  describe("Scenario: unknown top-level keys survive the parse (review finding #6)", () => {
    it("collects a key this projection doesn't name into otherFields, value intact", () => {
      const review = parsePersonaCardReview(JSON.stringify({ name: "Radio Rex", futureFeature: "x" }));
      expect(review?.otherFields).toEqual({ futureFeature: "x" });
    });

    it("keeps a top-level __proto__ key as a real, displayable own property (review follow-up #1)", () => {
      // A raw JSON STRING, deliberately not a JS object literal — `{ __proto__: "pwned" }` written
      // as source is spec-special-cased (it sets the resulting object's OWN prototype rather than
      // creating an own property named "__proto__"), which would test the wrong thing entirely.
      // `JSON.parse` has no such special case (it uses `CreateDataProperty`), so this is exactly
      // the shape a hostile/malformed card's raw bytes would actually carry over the wire.
      const review = parsePersonaCardReview('{"name":"Radio Rex","__proto__":"pwned"}');
      expect(Object.prototype.hasOwnProperty.call(review?.otherFields ?? {}, "__proto__")).toBe(true);
      expect(review?.otherFields["__proto__"]).toBe("pwned");
    });

    it("treats schemaVersion as an 'other' field too — it is never rendered by name", () => {
      const review = parsePersonaCardReview(JSON.stringify({ name: "Radio Rex", schemaVersion: 1 }));
      expect(review?.otherFields).toEqual({ schemaVersion: 1 });
    });

    it("is empty when the card carries only the fields this projection already reads", () => {
      const review = parsePersonaCardReview(
        JSON.stringify({
          name: "Radio Rex",
          tagline: "",
          soul: "",
          quirks: [],
          voice: {},
          energyDisposition: 0,
          corrections: [],
          lore: [],
          taste: [],
        })
      );
      expect(review?.otherFields).toEqual({});
    });
  });
});
