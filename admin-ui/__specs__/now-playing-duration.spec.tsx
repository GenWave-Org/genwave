// @jest-environment jsdom
// STORY-146 — Duration on the air surfaces (Epic X / SPEC F50.4–F50.5, closes gitea-#218) — UI half.
// The feeder half lives in Core.Tests/Specs/Story146_FeederStampsDuration.cs; the DTO wire half
// in Host.Tests/Specs/Story146_DurationOnAirDtos.cs.
//
// Runner: Jest (jsdom) + @testing-library/react. Mounts NowPlayingCard/PlayHistoryTable/RecentPlays
// directly with fixed props (no fetch mocking needed — all three are pure prop-driven components);
// fake timers pin `Date.now()` so the card's elapsed-seconds calculation is deterministic without
// advancing the clock.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { NowPlayingCard } from "../app/(authed)/_components/NowPlayingCard";
import { PlayHistoryTable } from "../app/(authed)/live/PlayHistoryTable";
import { RecentPlays } from "../app/(authed)/dashboard/RecentPlays";
import type { NowPlayingState, PlayHistoryEntry } from "../lib/broadcast-api";

const ISO_NOW = "2026-01-01T12:00:00.000Z";
const NOW_MS = new Date(ISO_NOW).getTime();

interface TrackStateOverrides {
  startedAt?: string;
  durationMs?: number | null;
}

function makeTrackState(overrides: TrackStateOverrides = {}): NowPlayingState {
  return {
    kind: "track",
    stationId: "1",
    mediaId: "m1",
    title: "Astral Plane",
    artist: "Valerie June",
    gainDb: -2.3,
    startedAt: overrides.startedAt ?? ISO_NOW,
    durationMs: overrides.durationMs,
  };
}

function makeHistoryEntry(overrides: Partial<PlayHistoryEntry> = {}): PlayHistoryEntry {
  return {
    mediaId: "m9",
    title: "Deep Blue",
    artist: "Arca",
    gainDb: -1.25,
    startedAt: "2026-01-01T10:04:00.000Z",
    ...overrides,
  };
}

beforeEach(() => {
  jest.useFakeTimers({ now: new Date(ISO_NOW) });
});

afterEach(() => {
  jest.useRealTimers();
});

describe("Feature: Duration on the air surfaces", () => {
  describe("Scenario: the now-playing card shows elapsed over total", () => {
    it("renders elapsed/total as m:ss / m:ss from startedAt and durationMs (F50.4)", () => {
      const startedAt = new Date(NOW_MS - 93_000).toISOString(); // 1:33 elapsed
      render(<NowPlayingCard state={makeTrackState({ startedAt, durationMs: 186_000 })} error={false} />); // 3:06 total

      expect(screen.getByText("01:33 / 03:06")).toBeInTheDocument();
    });

    it("renders a progress bar proportional to elapsed, clamped at total (F50.4)", () => {
      const startedAt = new Date(NOW_MS - 93_000).toISOString(); // 93 / 186 = 50%
      render(<NowPlayingCard state={makeTrackState({ startedAt, durationMs: 186_000 })} error={false} />);

      const bar = screen.getByRole("progressbar", { name: "Track progress" });
      expect(bar).toHaveAttribute("aria-valuenow", "50");
      expect((bar.firstElementChild as HTMLElement).style.width).toBe("50%");
    });

    it("never renders negative or over-100% progress when startedAt drifts (F50.4)", () => {
      // Clock skew: the feeder's startedAt reads slightly in the future — elapsed must clamp at
      // 0%, never go negative.
      const future = new Date(NOW_MS + 5_000).toISOString();
      const { unmount } = render(
        <NowPlayingCard state={makeTrackState({ startedAt: future, durationMs: 200_000 })} error={false} />
      );
      const barBefore = screen.getByRole("progressbar");
      expect(barBefore).toHaveAttribute("aria-valuenow", "0");
      expect((barBefore.firstElementChild as HTMLElement).style.width).toBe("0%");
      unmount();

      // Crossfade/skip drift: elapsed has run well past the track's own duration — clamp at
      // 100%, never overflow.
      const wayPast = new Date(NOW_MS - 500_000).toISOString();
      render(<NowPlayingCard state={makeTrackState({ startedAt: wayPast, durationMs: 200_000 })} error={false} />);
      const barAfter = screen.getByRole("progressbar");
      expect(barAfter).toHaveAttribute("aria-valuenow", "100");
      expect((barAfter.firstElementChild as HTMLElement).style.width).toBe("100%");
      expect(screen.getByText("03:20 / 03:20")).toBeInTheDocument();
    });
  });

  describe("Scenario: the lists carry durations", () => {
    it("shows plain duration on Live history rows where present (F50.5)", () => {
      const entries = [makeHistoryEntry({ durationMs: 180_000 })];
      render(
        <PlayHistoryTable
          entries={entries}
          error={false}
          timeZone="UTC"
          ratings={new Map()}
          onRatingChange={() => {}}
        />
      );

      expect(screen.getByText("03:00")).toBeInTheDocument();
    });

    it("shows plain duration on dashboard recent plays where present (F50.5)", () => {
      const entries = [makeHistoryEntry({ durationMs: 210_000 })];
      render(<RecentPlays entries={entries} error={false} timeZone="UTC" />);

      expect(screen.getByText("03:30")).toBeInTheDocument();
    });
  });

  describe("Scenario (sad path): durationless plays render honestly", () => {
    it("renders the card without elapsed/progress when durationMs is null — today's shape (F50.4)", () => {
      render(<NowPlayingCard state={makeTrackState({ durationMs: null })} error={false} />);

      expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
      expect(screen.getByText(/00:00 elapsed/)).toBeInTheDocument();
    });

    it("renders history rows without a duration cell value for tts:* and drain plays (F50.6)", () => {
      const entries = [
        makeHistoryEntry({ mediaId: "tts:seg", title: "Station ID", artist: "GenWave", durationMs: null }),
        makeHistoryEntry({ mediaId: "engine-1", title: "Please Stand By", artist: "Test Station" }),
      ];
      render(
        <PlayHistoryTable
          entries={entries}
          error={false}
          timeZone="UTC"
          ratings={new Map()}
          onRatingChange={() => {}}
        />
      );

      for (const title of ["Station ID", "Please Stand By"]) {
        const row = screen.getByText(title).closest("tr");
        expect(row).not.toBeNull();
        const cells = within(row as HTMLElement).getAllByRole("cell");
        expect(cells[4].textContent).toBe("");
      }
    });
  });
});
