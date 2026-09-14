// @jest-environment jsdom
// STORY-433 — Approve tells me when the station will render it (UI half: AC4–AC7 · gh-#745 · PLAN T458)
// The API half (AC1–AC3, `renderWithinMinutes` on the DTO) lives in tests/GenWave.Host.Tests/Specs/Story433_RenderWindow.cs.
//
// Runner: Jest (jsdom) + @testing-library/react — `AdsSection` renders directly with the real
// `AdSpotRow` inside it (the `ads-page.spec.tsx` "verbs drive the state machine" posture) and
// `ApproveStep` renders directly (the `spot-wizard.spec.tsx` posture); `global.fetch` is mocked by
// method+pathname through `./fetch-route-harness`. RED at plan time: both toasts read "Spot approved."
// today and nothing reads `renderWithinMinutes` off the approve response.
//
// `jest.mock("next/navigation", ...)` sits above every `import` — this project's SWC jest transform
// does not hoist `jest.mock` past a static import, and `AdsSection` calls `useRouter()` itself.

jest.mock("next/navigation", () => ({
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeAll, beforeEach, afterEach } from "@jest/globals";
import { cleanup, render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { AdSpotDto } from "@/lib/ads-api";
import type { SponsorRefDto } from "@/lib/sponsors-api";
import type { AdsSection as AdsSectionComponent } from "../app/(authed)/ads/AdsSection";
import { ApproveStep } from "../app/(authed)/ads/steps/ApproveStep";
import { installFetchMock } from "./fetch-route-harness";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;
const mockedRefresh = jest.fn<() => void>();

let AdsSection: typeof AdsSectionComponent;

beforeAll(async () => {
  ({ AdsSection } = await import("../app/(authed)/ads/AdsSection"));
});

beforeEach(() => {
  mockedRefresh.mockClear();
  mockedUseRouter.mockReturnValue({ refresh: mockedRefresh } as unknown as ReturnType<typeof useRouter>);
});

afterEach(() => {
  cleanup();
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SPONSOR_ACME: SponsorRefDto = { id: 1, name: "Acme", paused: false };

function adSpot(overrides: Partial<AdSpotDto> = {}): AdSpotDto {
  return {
    id: 1,
    sponsorId: 1,
    sponsorName: "Acme",
    sponsor: SPONSOR_ACME,
    title: "Acme Spot",
    brief: "A brief",
    script: "ANNOUNCER: Hello there.",
    source: "owner",
    packSlug: null,
    spotSeconds: 30,
    voicePlan: null,
    bedMediaId: null,
    state: "draft",
    failReason: null,
    mediaId: null,
    createdAt: "2026-09-01T00:00:00Z",
    stateChangedAt: "2026-09-01T00:00:00Z",
    renderedAt: null,
    retiredAt: null,
    version: "100",
    job: null,
    preview: null,
    renderWithinMinutes: null,
    ...overrides,
  };
}

/** The approve response body — `renderWithinMinutes` rides as a plain JSON field (PLAN T457 adds it to
 * the DTO type; until then this literal is what the wire carries, typed loosely on purpose). */
function approvedBody(spot: AdSpotDto, renderWithinMinutes: number | null): Record<string, unknown> {
  return { ...spot, state: "approved", version: "101", renderWithinMinutes };
}

function mockApprove(spot: AdSpotDto, renderWithinMinutes: number | null): void {
  installFetchMock([
    {
      method: "POST",
      match: (u) => u.pathname === `/api/ads/${spot.id}/approve`,
      respond: () => ({ status: 200, body: approvedBody(spot, renderWithinMinutes) }),
    },
  ]);
}

async function clickRowApprove(spot: AdSpotDto): Promise<void> {
  render(
    <ConfirmDialogProvider>
      <AdsSection tab="draft" items={[spot]} total={1} sponsorId={null} sponsors={[SPONSOR_ACME]} />
      <Toaster />
    </ConfirmDialogProvider>
  );
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "Approve" }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------

describe("Feature: Approve tells me when the station will render it", () => {
  describe("Scenario: the row toast names the window", () => {
    const spot = adSpot({ id: 1 });

    beforeEach(async () => {
      mockApprove(spot, 10);
      await clickRowApprove(spot);
    });

    it("toasts 'Approved. The station will render it within 10 minutes.' — AC4", async () => {
      expect(await screen.findByText("Approved. The station will render it within 10 minutes.")).toBeInTheDocument();
    });

    it("no longer toasts the bare 'Spot approved.'", async () => {
      await screen.findByText(/Approved\./);
      expect(screen.queryByText("Spot approved.")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the wizard's approve-without-a-preview names the window", () => {
    const spot = adSpot({ id: 2, preview: null });

    beforeEach(async () => {
      mockApprove(spot, 10);
      render(
        <>
          <ApproveStep
            spot={spot}
            onApproved={jest.fn()}
            onSpotUpdated={jest.fn()}
            onError={jest.fn()}
            onCancel={jest.fn()}
            onBack={jest.fn()}
          />
          <Toaster />
        </>
      );
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Approve without a preview" }));
        await Promise.resolve();
      });
    });

    it("toasts 'Approved. The station will render it within 10 minutes.' — AC5", async () => {
      expect(await screen.findByText("Approved. The station will render it within 10 minutes.")).toBeInTheDocument();
    });
  });

  describe("Scenario: one minute is singular", () => {
    const spot = adSpot({ id: 3 });

    beforeEach(async () => {
      mockApprove(spot, 1);
      await clickRowApprove(spot);
    });

    it("toasts '... within 1 minute.' — AC6", async () => {
      expect(await screen.findByText("Approved. The station will render it within 1 minute.")).toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  // Sad path (segregated)
  // ---------------------------------------------------------------------------

  describe("Scenario: no window in the response, plain confirmation", () => {
    const spot = adSpot({ id: 4 });

    beforeEach(async () => {
      mockApprove(spot, null);
      await clickRowApprove(spot);
    });

    it("toasts exactly 'Approved.' — AC7", async () => {
      expect(await screen.findByText("Approved.")).toBeInTheDocument();
    });
  });
});
