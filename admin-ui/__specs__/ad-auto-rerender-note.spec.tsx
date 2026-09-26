// @jest-environment jsdom
// gh-#865 — the ad page says when a spot's take was re-rendered automatically, and on which release.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(() => ({ refresh: jest.fn() })),
}));

import { describe, it, expect, jest, beforeAll, afterEach } from "@jest/globals";
import { cleanup, render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import type { AdSpotDto, AdState } from "@/lib/ads-api";
import { formatDateStamp } from "@/lib/format-clock";
import type { AdSpotRow as AdSpotRowComponent } from "../app/(authed)/ads/AdSpotRow";

let AdSpotRow: typeof AdSpotRowComponent;

beforeAll(async () => {
  ({ AdSpotRow } = await import("../app/(authed)/ads/AdSpotRow"));
});

afterEach(cleanup);

const AT = "2026-09-26T14:05:00Z";

function adSpot(overrides: Partial<AdSpotDto> = {}): AdSpotDto {
  return {
    id: 18,
    sponsorId: 1,
    sponsorName: "Acme",
    sponsor: { id: 1, name: "Acme", paused: false },
    title: "Acme spring sale",
    brief: null,
    script: "ANNOUNCER: Hello.",
    source: "llm",
    packSlug: null,
    spotSeconds: 30,
    voicePlan: null,
    bedMediaId: null,
    state: "ready",
    failReason: null,
    mediaId: 2596,
    createdAt: AT,
    stateChangedAt: AT,
    renderedAt: AT,
    retiredAt: null,
    version: "1",
    job: null,
    preview: null,
    renderWithinMinutes: null,
    ...overrides,
  };
}

function renderRow(spot: AdSpotDto, timeZone?: string): HTMLElement {
  render(
    <ConfirmDialogProvider>
      <AdSpotRow spot={spot} onChanged={() => {}} onEdit={() => {}} timeZone={timeZone} />
    </ConfirmDialogProvider>
  );
  return screen.getByText(spot.title).closest("div.py-3") as HTMLElement;
}

describe("Feature: an automatic re-render is visible on the ad page", () => {
  describe("Scenario: a ready spot re-rendered by a release", () => {
    it("says it was re-rendered automatically, naming the release and the date", () => {
      const row = renderRow(adSpot({ autoRerender: { onVersion: "v5.13.1", at: AT } }));

      expect(
        within(row).getByText(`Re-rendered automatically on v5.13.1 · ${formatDateStamp(AT)}`)
      ).toBeInTheDocument();
    });

    it("dates the note in the zone it is given, not the browser's", () => {
      // 23:30 UTC on the 26th is already the 27th in Auckland.
      const at = "2026-09-26T23:30:00Z";
      const row = renderRow(adSpot({ autoRerender: { onVersion: "v5.13.1", at } }), "Pacific/Auckland");

      expect(
        within(row).getByText(
          `Re-rendered automatically on v5.13.1 · ${formatDateStamp(at, { timeZone: "Pacific/Auckland" })}`
        )
      ).toBeInTheDocument();
      expect(formatDateStamp(at, { timeZone: "Pacific/Auckland" })).not.toBe(
        formatDateStamp(at, { timeZone: "UTC" })
      );
    });
  });

  describe("Scenario: no note without an automatic re-render", () => {
    it.each([
      { name: "null on the wire", spot: adSpot({ autoRerender: null }) },
      { name: "absent (older api)", spot: adSpot() },
    ])("shows nothing when autoRerender is $name", ({ spot }) => {
      const row = renderRow(spot);

      expect(within(row).queryByText(/Re-rendered automatically/)).not.toBeInTheDocument();
    });

    it.each<AdState>(["retired", "failed"])("shows nothing on a %s spot", (state) => {
      const row = renderRow(
        adSpot({
          state,
          mediaId: null,
          failReason: state === "failed" ? "render: boom" : null,
          autoRerender: { onVersion: "v5.13.1", at: AT },
        })
      );

      expect(within(row).queryByText(/Re-rendered automatically/)).not.toBeInTheDocument();
    });
  });
});
