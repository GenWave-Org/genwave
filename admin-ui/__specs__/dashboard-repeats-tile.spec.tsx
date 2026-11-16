// @jest-environment jsdom
// PLAN T708 — The Repeats panel on the Status page (SPEC F218.29, STORY-542 AC3)
//
// Runner: Jest (jsdom) + @testing-library/react. StatusTiles is prop-driven; the api half is
// GenWave.Host.Tests/Specs/Story542_TheWeekAfterInNumbers.cs.

import { describe, it, expect, afterEach } from "@jest/globals";
import { render, screen, cleanup, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { RepeatsStatus, StatusResponse } from "@/lib/broadcast-api";
import { djRepeatLine, overuseLine } from "@/lib/repeats-copy";

function makeStatus(repeats: StatusResponse["repeats"]): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    voice: { engine: "kokoro", degraded: false, reason: null, checkedAt: null },
    repeats,
  };
}

const flip = { personaId: 7, name: "Flip", catches: 3, trims: 2, fallbacks: 1 };

function repeatsTile(): HTMLElement {
  return screen.getByRole("group", { name: "Repeats" });
}

afterEach(cleanup);

describe("Feature: The Repeats panel on the Status page", () => {
  describe("Scenario: the views have rows", () => {
    const repeats: RepeatsStatus = {
      djs: [flip],
      overused: [{ personaId: 7, name: "Flip", phrase: "banger, log it", maxPerShift: 1, occurrences: 2, lastSaidAt: "2026-11-09T03:00:00Z" }],
    };

    it("shows each DJ's numbers in plain words", () => {
      render(<StatusTiles status={makeStatus(repeats)} error={false} />);

      const tile = repeatsTile();
      expect(within(tile).getByText("Flip")).toBeInTheDocument();
      expect(
        within(tile).getByText("Caught 3 repeats this week, 2 cut short. 1 line swapped for a stock line.")
      ).toBeInTheDocument();
    });

    it("names a phrase said more than its limit in one shift", () => {
      render(<StatusTiles status={makeStatus(repeats)} error={false} timeZone="America/Edmonton" />);

      expect(
        within(repeatsTile()).getByText('Flip said "banger, log it" twice in one shift on Sun Nov 8 (the limit is 1).')
      ).toBeInTheDocument();
    });
  });

  describe("Scenario: nothing to report", () => {
    it("says so in plain words", () => {
      render(<StatusTiles status={makeStatus({ djs: [], overused: [] })} error={false} />);

      expect(within(repeatsTile()).getByText("No repeats caught this week.")).toBeInTheDocument();
    });
  });

  describe("Scenario: the numbers could not be read", () => {
    it("reads as unavailable when the api sends null", () => {
      render(<StatusTiles status={makeStatus(null)} error={false} />);

      expect(within(repeatsTile()).getByText("Unavailable")).toBeInTheDocument();
    });

    it("reads as unavailable on an older api that sends nothing", () => {
      render(<StatusTiles status={makeStatus(undefined)} error={false} />);

      expect(within(repeatsTile()).getByText("Unavailable")).toBeInTheDocument();
    });
  });

  describe("Scenario: the wording", () => {
    it("singular and plural", () => {
      expect(djRepeatLine({ ...flip, catches: 1, trims: 1, fallbacks: 0 })).toBe("Caught 1 repeat this week, 1 cut short.");
      expect(djRepeatLine({ ...flip, catches: 2, trims: 0, fallbacks: 2 })).toBe(
        "Caught 2 repeats this week. 2 lines swapped for stock lines."
      );
      expect(djRepeatLine({ ...flip, catches: 2, trims: 0, fallbacks: 0 })).toBe("Caught 2 repeats this week.");
    });

    it("a swapped line with no catch still reads as a sentence", () => {
      expect(djRepeatLine({ ...flip, catches: 0, trims: 0, fallbacks: 1 })).toBe(
        "1 line swapped for a stock line this week."
      );
    });

    it("counts above two use a number", () => {
      expect(
        overuseLine(
          { personaId: 1, name: "Sal", phrase: "oh boy", maxPerShift: 1, occurrences: 4, lastSaidAt: "2026-11-09T03:00:00Z" },
          { timeZone: "America/Edmonton" }
        )
      ).toBe('Sal said "oh boy" 4 times in one shift on Sun Nov 8 (the limit is 1).');
    });

    it("leaves the day out when the api sent none or an invalid one", () => {
      const base = { personaId: 1, name: "Flip", phrase: "banger, log it", maxPerShift: 1, occurrences: 2 };
      const expected = 'Flip said "banger, log it" twice in one shift (the limit is 1).';
      expect(overuseLine(base)).toBe(expected);
      expect(overuseLine({ ...base, lastSaidAt: "not a date" })).toBe(expected);
    });
  });
});
