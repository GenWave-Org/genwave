// @jest-environment jsdom
// PLAN T738 — "Memories per DJ-hour" on the Status page (SPEC F218.47, STORY-551 AC2, AC3)
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
  return screen.getByRole("group", { name: "Memories per DJ-hour" });
}

afterEach(cleanup);

describe("STORY-551 — the callback count on the Status page", () => {
  it("AC2 — shows Memories per DJ-hour as 0.75", () => {
    render(<StatusTiles status={makeStatus({ perDjHour: 0.75, asks: 8, used: 5 })} error={false} />);

    expect(within(callbacksTile()).getByText("0.75")).toBeInTheDocument();
  });

  it("T743 — adds 5 aired of 8 asked · last 7 days under the number", () => {
    render(<StatusTiles status={makeStatus({ perDjHour: 0.75, asks: 8, used: 5 })} error={false} />);

    expect(within(callbacksTile()).getByText("5 aired of 8 asked · last 7 days")).toBeInTheDocument();
  });

  it("T743 — hides the line with no offers", () => {
    render(<StatusTiles status={makeStatus({ perDjHour: null, asks: 0, used: 0 })} error={false} />);

    expect(within(callbacksTile()).queryByText(/asked/)).not.toBeInTheDocument();
  });

  it("AC3 — reads None yet with no offers", () => {
    render(<StatusTiles status={makeStatus({ perDjHour: null, asks: 0, used: 0 })} error={false} />);

    expect(within(callbacksTile()).getByText("None yet")).toBeInTheDocument();
  });

  it("reads as unavailable when the api could not read the numbers", () => {
    render(<StatusTiles status={makeStatus(null)} error={false} />);

    expect(within(callbacksTile()).getByText("Unavailable")).toBeInTheDocument();
  });
});
