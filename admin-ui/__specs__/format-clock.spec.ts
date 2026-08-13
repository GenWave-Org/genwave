// Q5 review finding, folded into Q11 (STORY-093): formatClockTime must use
// `hourCycle: "h23"`, not `hour12: false` — some ICU versions render
// `hour12: false` as "24:00" at midnight instead of "00:00".
//
// Runner: Jest (node) — pure formatting logic, no DOM needed.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import {
  formatClockTime,
  formatDuration,
  formatDurationCell,
  formatElapsedMs,
  formatRelativeAgo,
} from "../lib/format-clock";

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

  // gh-#210 — the LLM call inspector's ELAPSED humanizer: raw milliseconds under a second,
  // one-decimal seconds under a minute, "Nm SSs" from a minute up. Never formatDuration's
  // "mm:ss" — that shape reads as a playback clock, which a call latency is not.
  describe("Scenario: measured elapsed times humanize by magnitude", () => {
    it("keeps a sub-second measurement in raw milliseconds", () => {
      expect(formatElapsedMs(842)).toBe("842ms");
    });

    it("renders zero as 0ms", () => {
      expect(formatElapsedMs(0)).toBe("0ms");
    });

    it("renders a second-plus measurement as one-decimal seconds", () => {
      expect(formatElapsedMs(1400)).toBe("1.4s");
    });

    it("keeps one-decimal seconds right up to the minute threshold", () => {
      expect(formatElapsedMs(59_940)).toBe("59.9s");
    });

    it("renders a minute-plus measurement as m ss with zero-padded seconds", () => {
      expect(formatElapsedMs(123_000)).toBe("2m 03s");
    });
  });

  describe("Scenario (sad path): elapsed rounding never fabricates an impossible reading", () => {
    it("clamps a negative measurement to 0ms rather than rendering nonsense", () => {
      expect(formatElapsedMs(-50)).toBe("0ms");
    });

    it("rounds 59 950ms up into the minute shape — 1m 00s, never 60.0s", () => {
      expect(formatElapsedMs(59_950)).toBe("1m 00s");
    });

    it("rounds 119 800ms to 2m 00s — never 1m 60s", () => {
      expect(formatElapsedMs(119_800)).toBe("2m 00s");
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

  // gh-#490 — the Health page's restart-recency readout: a coarse "how long ago" phrase, rounded
  // down to the single largest whole unit.
  describe("Scenario: relative-ago formatting rounds down to the largest whole unit", () => {
    const ISO_NOW = "2026-08-13T12:00:00.000Z";

    beforeEach(() => {
      jest.useFakeTimers({ now: new Date(ISO_NOW) });
    });

    afterEach(() => {
      jest.useRealTimers();
    });

    it("reads 'just now' inside the first minute", () => {
      expect(formatRelativeAgo("2026-08-13T11:59:30.000Z")).toBe("just now");
    });

    it("renders whole minutes under an hour", () => {
      expect(formatRelativeAgo("2026-08-13T11:48:00.000Z")).toBe("12m ago");
    });

    it("renders whole hours under a day", () => {
      expect(formatRelativeAgo("2026-08-13T09:00:00.000Z")).toBe("3h ago");
    });

    it("renders whole days at a day or more — the gh-#490 demo-box case (4 days)", () => {
      expect(formatRelativeAgo("2026-08-09T08:00:00.000Z")).toBe("4d ago");
    });

    it("reads 'unknown' for a null timestamp rather than fabricating an age", () => {
      expect(formatRelativeAgo(null)).toBe("unknown");
    });

    it("reads 'unknown' for an unparseable timestamp rather than fabricating an age", () => {
      expect(formatRelativeAgo("not-a-date")).toBe("unknown");
    });
  });
});
