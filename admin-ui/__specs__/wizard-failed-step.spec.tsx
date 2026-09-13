// @jest-environment jsdom
// STORY-435 — A failed job says which step failed (UI half: AC6–AC8 · gh-#724 · PLAN T465)
// The API half (AC1–AC5, `job.failedKind`) lives in tests/GenWave.Host.Tests/Specs/Story435_FailedJobKind.cs.
//
// Runner: Jest (jsdom) + @testing-library/react — `ScriptStep` and `HearStep` render directly (the
// `spot-wizard.spec.tsx` posture); `HearStep`'s own music picker fetches `/api/media`, mocked through
// `./fetch-route-harness`. RED at plan time: `JobRunner` renders `job.error` on BOTH steps whenever it
// is present, so a failed preview shows up on the script step (AC8) and a failed write on the hear step.
//
// `jest.mock("next/navigation", ...)` sits above every `import` — the wizard steps do not call
// `useRouter()` themselves, but the shared harness convention keeps this file safe to extend.

jest.mock("next/navigation", () => ({
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { cleanup, render, screen, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { AdJobKind, AdSpotDto, AdSpotJobDto } from "@/lib/ads-api";
import type { SponsorRefDto } from "@/lib/sponsors-api";
import { HearStep } from "../app/(authed)/ads/steps/HearStep";
import { ScriptStep } from "../app/(authed)/ads/steps/ScriptStep";
import { installFetchMock } from "./fetch-route-harness";

afterEach(() => {
  cleanup();
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SPONSOR_ACME: SponsorRefDto = { id: 1, name: "Acme", paused: false };
const ERROR_TEXT = "tts_timeout";

/** A settled failure: `kind: null` (nothing running — the in-flight invariant) plus the kind that
 * failed. `failedKind` joins `AdSpotJobDto` at PLAN T464; the assertion keeps this file type-clean
 * until then. */
function failedJob(failedKind: AdJobKind): AdSpotJobDto {
  return {
    kind: null,
    startedAt: null,
    waitingForStation: false,
    error: ERROR_TEXT,
    failedKind,
  } as AdSpotJobDto;
}

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
    ...overrides,
  };
}

function renderScriptStep(spot: AdSpotDto): void {
  render(
    <ScriptStep
      spot={spot}
      onSpotUpdated={jest.fn()}
      onError={jest.fn()}
      onNext={jest.fn()}
      onWrite={jest.fn()}
      onCancelJob={jest.fn()}
      onCancel={jest.fn()}
      onBack={jest.fn()}
    />
  );
}

async function renderHearStep(spot: AdSpotDto): Promise<void> {
  installFetchMock([
    { method: "GET", match: (u) => u.pathname === "/api/media", respond: () => ({ status: 200, body: [] }) },
  ]);
  await act(async () => {
    render(
      <HearStep
        spot={spot}
        onSpotUpdated={jest.fn()}
        onError={jest.fn()}
        onNext={jest.fn()}
        onPreview={jest.fn()}
        onCancelJob={jest.fn()}
        onCancel={jest.fn()}
        onBack={jest.fn()}
      />
    );
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------

describe("Feature: A failed job says which step failed", () => {
  describe("Scenario: a failed write shows on the script step", () => {
    beforeEach(() => {
      renderScriptStep(adSpot({ job: failedJob("write") }));
    });

    it("renders the error text inside the script step — AC6", () => {
      expect(screen.getByRole("alert")).toHaveTextContent(ERROR_TEXT);
    });

    it("re-enables 'Write it for me' so the operator can try again", () => {
      expect(screen.getByRole("button", { name: "Write it for me" })).toBeEnabled();
    });
  });

  describe("Scenario: a failed preview shows on the hear step", () => {
    beforeEach(async () => {
      await renderHearStep(adSpot({ job: failedJob("preview") }));
    });

    it("renders the error text inside the hear step — AC7", () => {
      expect(screen.getByRole("alert")).toHaveTextContent(ERROR_TEXT);
    });
  });

  // ---------------------------------------------------------------------------
  // Sad path (segregated) — the other step's failure stays off this step
  // ---------------------------------------------------------------------------

  describe("Scenario: a failed preview is not shown on the script step", () => {
    beforeEach(() => {
      renderScriptStep(adSpot({ job: failedJob("preview") }));
    });

    it("shows no error text — AC8", () => {
      expect(screen.queryByText(ERROR_TEXT)).not.toBeInTheDocument();
    });
  });

  describe("Scenario: a failed write is not shown on the hear step", () => {
    beforeEach(async () => {
      await renderHearStep(adSpot({ job: failedJob("write") }));
    });

    it("shows no error text", () => {
      expect(screen.queryByText(ERROR_TEXT)).not.toBeInTheDocument();
    });
  });
});
