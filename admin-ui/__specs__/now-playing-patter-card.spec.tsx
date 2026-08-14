// @jest-environment jsdom
// gh-#187 — admin-ui: Live rendered a patter like a track: a tts:* break's Title is literally the
// station name (TtsSegmentSource), and with no kind on the Live payload the card showed
// "GenWave Demo" as if it were a song. The card now gives kind: "patter" its own treatment —
// DJ-break source chip + persona name (the wire `artist`) as the headline — while kind: "track"
// renders byte-identically to before.
//
// Runner: Jest + jsdom + @testing-library/react, NowPlayingCard rendered directly (it is a pure
// presentational component; the kind mapping itself lives in lib/broadcast-api.ts).

import { describe, it, expect } from "@jest/globals";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { NowPlayingCard } from "../app/(authed)/_components/NowPlayingCard";
import type { NowPlayingState } from "../lib/broadcast-api";

const ISO_STARTED = "2026-07-28T20:54:45.000Z";

function patterState(): Extract<NowPlayingState, { kind: "patter" }> {
  return {
    kind: "patter",
    stationId: "1",
    mediaId: "tts:abc123",
    title: "GenWave Demo", // the station name — exactly what must NOT render as a track title
    artist: "The Archivist",
    gainDb: -2.5,
    startedAt: ISO_STARTED,
    durationMs: 8219,
  };
}

function trackState(): NowPlayingState {
  return {
    kind: "track",
    stationId: "1",
    mediaId: "42",
    title: "Uncle Dave",
    artist: "Boom Boom Beckett",
    gainDb: -1.25,
    startedAt: ISO_STARTED,
    durationMs: 248581,
  };
}

describe("NowPlayingCard patter treatment (gh-#187)", () => {
  it("renders a DJ-break chip and the persona name instead of the station-name title", () => {
    render(<NowPlayingCard state={patterState()} error={false} />);

    expect(screen.getByText("DJ break")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "The Archivist" })).toBeInTheDocument();
    // The station name must not masquerade as a track title anywhere on the card.
    expect(screen.queryByText("GenWave Demo")).not.toBeInTheDocument();
  });

  it("still shows the on-air pill and the measured-duration progress readout for a break", () => {
    render(<NowPlayingCard state={patterState()} error={false} />);

    expect(screen.getByText("On air")).toBeInTheDocument();
    // durationMs present → the real progress bar renders, same as a track (SPEC F50.4).
    expect(screen.getByRole("progressbar", { name: "Track progress" })).toBeInTheDocument();
  });

  it("falls back to a neutral voice label when a break carries no persona name", () => {
    render(<NowPlayingCard state={{ ...patterState(), artist: undefined }} error={false} />);

    expect(screen.getByRole("heading", { level: 2, name: "Station voice" })).toBeInTheDocument();
  });

  it("keeps the track treatment unchanged — title headline, artist line, no DJ-break chip", () => {
    render(<NowPlayingCard state={trackState()} error={false} />);

    expect(screen.getByRole("heading", { level: 2, name: "Uncle Dave" })).toBeInTheDocument();
    expect(screen.getByText("Boom Boom Beckett")).toBeInTheDocument();
    expect(screen.queryByText("DJ break")).not.toBeInTheDocument();
  });
});
