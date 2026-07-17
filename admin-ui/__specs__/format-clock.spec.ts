// Q5 review finding, folded into Q11 (STORY-093): formatClockTime must use
// `hourCycle: "h23"`, not `hour12: false` — some ICU versions render
// `hour12: false` as "24:00" at midnight instead of "00:00".
//
// Runner: Jest (node) — pure formatting logic, no DOM needed.

import { describe, it, expect } from "@jest/globals";
import { formatClockTime, formatDuration, formatDurationCell } from "../lib/format-clock";

describe("Feature: Clock formatting", () => {
  describe("Scenario: 24-hour formatting never renders the midnight-as-24:00 artifact", () => {
    it("renders midnight as 00:00, not 24:00", () => {
      expect(formatClockTime("2026-01-01T00:00:00Z", { timeZone: "UTC" })).toBe("00:00");
    });

    it("renders a normal daytime hour unaffected", () => {
      expect(formatClockTime("2026-01-01T13:45:00Z", { timeZone: "UTC" })).toBe("13:45");
    });

    it("renders the hour before midnight as 23:00, confirming the 0-23 cycle", () => {
      expect(formatClockTime("2026-01-01T23:00:00Z", { timeZone: "UTC" })).toBe("23:00");
    });
  });

  // SPEC F50.4–F50.5 — the shared m:ss formatter for the now-playing card's elapsed/total readout
  // and the history surfaces' plain duration column.
  describe("Scenario: duration formatting renders zero-padded m:ss", () => {
    it("formats a sub-hour duration as MM:SS", () => {
      expect(formatDuration(222_000)).toBe("03:42");
    });

    it("formats an hour-plus duration as H:MM:SS", () => {
      expect(formatDuration(3_723_000)).toBe("1:02:03");
    });

    it("formats zero milliseconds as 00:00", () => {
      expect(formatDuration(0)).toBe("00:00");
    });
  });

  describe("Scenario (sad path): a play-history row's duration cell is blank when absent", () => {
    it("formats a present duration through formatDuration", () => {
      expect(formatDurationCell(180_000)).toBe("03:00");
    });

    it("renders blank (not an em-dash) for null", () => {
      expect(formatDurationCell(null)).toBe("");
    });

    it("renders blank (not an em-dash) for undefined", () => {
      expect(formatDurationCell(undefined)).toBe("");
    });
  });
});
