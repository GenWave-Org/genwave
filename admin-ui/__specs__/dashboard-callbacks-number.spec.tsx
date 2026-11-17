// @jest-environment jsdom
// PLAN T738 — "Callbacks per DJ-hour" on the Status page (SPEC F218.47, STORY-551 AC2, AC3)
// Runner: Jest (jsdom). The api half is GenWave.Host.Tests/Specs/Story551_TheCallbackCount.cs.

import { describe, it, expect, afterEach } from "@jest/globals";
import { render, screen, cleanup, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { StatusResponse } from "@/lib/broadcast-api";

function makeStatus(callbacks: StatusResponse["callbacks"]): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    voice: { engine: "kokoro", degraded: false, reason: null, checkedAt: null },
    callbacks,
  };
}

function callbacksTile(): HTMLElement {
  return screen.getByRole("group", { name: "Callbacks per DJ-hour" });
}

afterEach(cleanup);

describe("STORY-551 — the callback count on the Status page", () => {
  it("AC2 — shows Callbacks per DJ-hour as 0.75", () => {
    render(<StatusTiles status={makeStatus({ perDjHour: 0.75 })} error={false} />);

    expect(within(callbacksTile()).getByText("0.75")).toBeInTheDocument();
  });

  it("AC3 — reads None yet with no offers", () => {
    render(<StatusTiles status={makeStatus({ perDjHour: null })} error={false} />);

    expect(within(callbacksTile()).getByText("None yet")).toBeInTheDocument();
  });

  it("reads as unavailable when the api could not read the numbers", () => {
    render(<StatusTiles status={makeStatus(null)} error={false} />);

    expect(within(callbacksTile()).getByText("Unavailable")).toBeInTheDocument();
  });
});
