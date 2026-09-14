// @jest-environment jsdom
// SPEC F174.1, F174.7, STORY-421 AC1–AC3, STORY-427 AC3/AC4, PLAN T448 — the five-step SpotWizard.
//
// Runner: Jest (jsdom) + @testing-library/react. `SpotWizard` renders directly (the `AdSpotEditor`
// precedent in `ads-page.spec.tsx`: a feature's own dialog component gets its own direct RTL
// render, no server `page.tsx` wiring needed for a client-only modal); two of the wizard's own step
// components (`HearStep`, `ApproveStep`) are likewise rendered directly for the tests that only
// need one step's own markup, rather than walking the whole five-step flow to reach them.
//
// `jest.mock("next/navigation", ...)` sits above every `import` (the `ads-page.spec.tsx` header's
// own documented reason: this project's SWC jest transform does not hoist `jest.mock` past a
// static import) — `SpotWizard` calls `useRouter()` itself, on approve.

jest.mock("next/navigation", () => ({
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeAll, beforeEach, afterEach } from "@jest/globals";
import { cleanup, render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import type { AdSpotDto, AdSpotJobDto } from "@/lib/ads-api";
import type { SponsorRefDto } from "@/lib/sponsors-api";
import type { AdsSection as AdsSectionComponent } from "../app/(authed)/ads/AdsSection";
import type { SpotWizard as SpotWizardComponent } from "../app/(authed)/ads/SpotWizard";
import { AngleLengthStep } from "../app/(authed)/ads/steps/AngleLengthStep";
import { ApproveStep } from "../app/(authed)/ads/steps/ApproveStep";
import { HearStep } from "../app/(authed)/ads/steps/HearStep";
import { ScriptStep } from "../app/(authed)/ads/steps/ScriptStep";
import { SponsorStep } from "../app/(authed)/ads/steps/SponsorStep";
import { WIZARD_STEPS } from "../app/(authed)/ads/steps/wizard-steps";
import { installFetchMock, type RouteHandler } from "./fetch-route-harness";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;
const mockedRefresh = jest.fn<() => void>();

// `SpotWizard` calls `useRouter()` at render time, so — like every module under test in
// `ads-page.spec.tsx` — it must be `import()`ed AFTER the mock above is registered; a static
// top-level import would bind the REAL `next/navigation` export first (see the file header).
// `AdsSection` rides the same rule (PLAN T448's own "New spot…" opens this wizard, not
// `AdSpotEditor`'s own free-text create path) — it calls `useRouter()` itself too.
let SpotWizard: typeof SpotWizardComponent;
let AdsSection: typeof AdsSectionComponent;

beforeAll(async () => {
  ({ SpotWizard } = await import("../app/(authed)/ads/SpotWizard"));
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

// ---------------------------------------------------------------------------
// Fetch mock — `./fetch-route-harness`'s route table (the `ads-page.spec.tsx` precedent, hoisted
// PLAN T448 so this file's own smaller surface — sponsors/briefs/ads only, no verb/preview-stream/
// voices routes here — shares the one implementation instead of a second copy).
// ---------------------------------------------------------------------------

function requestBody(mockFetch: jest.MockedFunction<typeof fetch>, callIndex: number): unknown {
  const call = mockFetch.mock.calls[callIndex] as unknown as [string, RequestInit];
  return JSON.parse(String(call[1].body));
}

// ---------------------------------------------------------------------------

describe("Feature: New spot is a five-step wizard", () => {
  describe("Scenario: the five step names are exact", () => {
    it("renders the step labels Sponsor, Angle & length, Script, Hear, Approve in that order — AC1", () => {
      installFetchMock([]);
      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={null} onClose={jest.fn()} />);

      const nav = screen.getByRole("list", { name: "Steps" });
      const labels = within(nav)
        .getAllByRole("listitem")
        .map((item) => item.textContent);

      expect(labels).toEqual(["1. Sponsor", "2. Angle & length", "3. Script", "4. Hear", "5. Approve"]);
    });
  });

  describe("Scenario: the Sponsor step picks from the list or creates inline", () => {
    it("typing an unknown name offers Create — AC3", () => {
      installFetchMock([]);
      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={null} onClose={jest.fn()} />);

      expect(screen.queryByRole("button", { name: "Create" })).not.toBeInTheDocument();

      fireEvent.change(screen.getByLabelText("New sponsor"), { target: { value: "Riverside Diner" } });

      expect(screen.getByRole("button", { name: "Create" })).toBeInTheDocument();
    });

    it("choosing Create posts to /api/sponsors and advances with the new sponsor preselected — AC3", async () => {
      const mockFetch = installFetchMock([
        {
          method: "POST",
          match: (u) => u.pathname === "/api/sponsors",
          respond: () => ({ status: 201, body: { id: 9, name: "Riverside Diner", paused: false } }),
        },
        {
          method: "GET",
          match: (u) => u.pathname === "/api/ad-briefs",
          respond: () => ({ status: 200, body: [] }),
        },
      ]);

      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={null} onClose={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("New sponsor"), { target: { value: "Riverside Diner" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Create" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(screen.getByLabelText("Sponsor")).toHaveValue("9"));
      expect(requestBody(mockFetch, 0)).toEqual({ name: "Riverside Diner" });

      // "Next" was disabled with nothing chosen — the new sponsor being preselected is what lets
      // this actually move the wizard forward, the "advances" half of this fact.
      fireEvent.click(screen.getByRole("button", { name: "Next" }));
      expect(await screen.findByText(WIZARD_STEPS[1]?.purpose ?? "")).toBeInTheDocument();
    });
  });

  describe("Scenario: the Hear step's music choice is the installed music", () => {
    // Each render is wrapped in `act(async ...)` with a microtask flush — the step's own mount
    // effect awaits `listBackgroundMusic()` (a resolved mock fetch is still a real microtask), so
    // its `setMusicOptions` lands before any assertion runs, not after (an un-awaited render would
    // still pass here since the default option renders before that effect resolves, but would log
    // a spurious "not wrapped in act" warning on every one of these three).
    it("the dropdown's default option has a null value — STORY-427 AC3", async () => {
      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/media", respond: () => ({ status: 200, body: [] }) },
      ]);

      await act(async () => {
        render(
          <HearStep
            spot={adSpot()}
            onSpotUpdated={jest.fn()}
            onError={jest.fn()}
            onNext={jest.fn()}
            onPreview={jest.fn()}
            onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
        );
        await Promise.resolve();
      });

      const select = screen.getByLabelText("Background music");
      const defaultOption = within(select).getByRole("option", { name: "Let the station pick" }) as HTMLOptionElement;
      expect(defaultOption.value).toBe("");
    });

    it("the default option's label reads 'Let the station pick' — STORY-427 AC3", async () => {
      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/media", respond: () => ({ status: 200, body: [] }) },
      ]);

      await act(async () => {
        render(
          <HearStep
            spot={adSpot()}
            onSpotUpdated={jest.fn()}
            onError={jest.fn()}
            onNext={jest.fn()}
            onPreview={jest.fn()}
            onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
        );
        await Promise.resolve();
      });

      const select = screen.getByLabelText("Background music") as HTMLSelectElement;
      expect(select.options[0]?.textContent).toBe("Let the station pick");
    });

    it("there is no numeric text input for a media id — STORY-427 AC4", async () => {
      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        {
          method: "GET",
          match: (u) => u.pathname === "/api/media",
          respond: () => ({ status: 200, body: [{ mediaId: "42", title: "Jingle Bed", pack: "House Pack" }] }),
        },
      ]);

      await act(async () => {
        render(
          <HearStep
            spot={adSpot()}
            onSpotUpdated={jest.fn()}
            onError={jest.fn()}
            onNext={jest.fn()}
            onPreview={jest.fn()}
            onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
        );
        await Promise.resolve();
      });

      expect(document.querySelectorAll('input[type="number"]')).toHaveLength(0);
      expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: a stale preview blocks approving as heard", () => {
    // The task's own rule (SPEC F174.4, STORY-427): a preview that no longer matches the spot's
    // current script/background music/voice-plan must not be approvable as "heard" — this is what
    // the wizard actually gates on, distinct from the AC3/AC4 dropdown facts above.
    it("disables 'Approve as heard' while the preview is stale, enables it once fresh", () => {
      const { rerender } = render(
        <ApproveStep
          spot={adSpot({ preview: { at: "2026-09-01T00:00:00Z", key: "abc", stale: true } })}
          onApproved={jest.fn()}
          onSpotUpdated={jest.fn()}
          onError={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
      );
      expect(screen.getByRole("button", { name: "Approve as heard" })).toBeDisabled();

      rerender(
        <ApproveStep
          spot={adSpot({ preview: { at: "2026-09-01T00:00:00Z", key: "abc", stale: false } })}
          onApproved={jest.fn()}
          onSpotUpdated={jest.fn()}
          onError={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
      );
      expect(screen.getByRole("button", { name: "Approve as heard" })).toBeEnabled();
    });

    it("re-fetches the row and surfaces the server's own sentence when a 409 preview_stale races the click", async () => {
      const refreshed = adSpot({
        id: 77,
        preview: { at: "2026-09-01T00:05:00Z", key: "def", stale: true },
        version: "101",
      });
      const onSpotUpdated = jest.fn<(spot: AdSpotDto) => void>();
      const onError = jest.fn<(detail: string) => void>();
      installFetchMock([
        {
          method: "POST",
          match: (u) => u.pathname === "/api/ads/77/approve",
          respond: () => ({
            status: 409,
            body: { type: "preview_stale", detail: "The preview no longer matches this spot — render it again." },
          }),
        },
        {
          method: "GET",
          match: (u) => u.pathname === "/api/ads/77",
          respond: () => ({ status: 200, body: refreshed }),
        },
      ]);

      render(
        <ApproveStep
          spot={adSpot({ id: 77, preview: { at: "2026-09-01T00:00:00Z", key: "abc", stale: false }, version: "100" })}
          onApproved={jest.fn()}
          onSpotUpdated={onSpotUpdated}
          onError={onError} onCancel={jest.fn()} onBack={jest.fn()} />
      );

      fireEvent.click(screen.getByRole("button", { name: "Approve as heard" }));

      await waitFor(() => expect(onSpotUpdated).toHaveBeenCalledWith(refreshed));
      expect(onError).toHaveBeenCalledWith("The preview no longer matches this spot — render it again.");
    });
  });

  describe("Scenario: 'Approve without a preview' only offers a path when nothing has been rendered yet — PLAN T448 ruling", () => {
    it("hides the without-preview button while a stale preview exists, alongside the hint and a disabled Approve as heard", () => {
      render(
        <ApproveStep
          spot={adSpot({ preview: { at: "2026-09-01T00:00:00Z", key: "abc", stale: true } })}
          onApproved={jest.fn()}
          onSpotUpdated={jest.fn()}
          onError={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
      );

      expect(
        screen.getByText("The preview is out of date. Render it again to approve what you heard.")
      ).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Approve as heard" })).toBeDisabled();
      expect(screen.queryByRole("button", { name: "Approve without a preview" })).not.toBeInTheDocument();
    });

    it("hides both the hint and the without-preview button once the preview is fresh", () => {
      render(
        <ApproveStep
          spot={adSpot({ preview: { at: "2026-09-01T00:00:00Z", key: "abc", stale: false } })}
          onApproved={jest.fn()}
          onSpotUpdated={jest.fn()}
          onError={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
      );

      expect(
        screen.queryByText("The preview is out of date. Render it again to approve what you heard.")
      ).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Approve without a preview" })).not.toBeInTheDocument();
    });

    it("offers the without-preview button and no hint when no preview has ever been rendered", () => {
      render(<ApproveStep spot={adSpot({ preview: null })} onApproved={jest.fn()} onSpotUpdated={jest.fn()} onError={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />);

      expect(screen.getByRole("button", { name: "Approve without a preview" })).toBeInTheDocument();
      expect(
        screen.queryByText("The preview is out of date. Render it again to approve what you heard.")
      ).not.toBeInTheDocument();
    });
  });

  describe("Scenario: New spot opens the wizard, not the editor", () => {
    it("opens the SpotWizard from 'New spot…', never AdSpotEditor's own free-text create form — PLAN T448 ruling", () => {
      render(<AdsSection tab="draft" items={[]} total={0} sponsorId={null} sponsors={[SPONSOR_ACME]} />);

      fireEvent.click(screen.getByRole("button", { name: "New spot…" }));

      expect(screen.getByRole("dialog", { name: "New spot" })).toBeInTheDocument();
      expect(screen.getByRole("list", { name: "Steps" })).toBeInTheDocument();
      expect(screen.queryByLabelText("Title")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the Sponsor step refuses to advance with nothing chosen", () => {
    it("refuses to submit with no sponsor chosen, in plain wording, without ever calling the api", () => {
      const onError = jest.fn<(detail: string) => void>();
      const onNext = jest.fn();
      render(
        <SponsorStep
          sponsors={[SPONSOR_ACME]}
          value={null}
          onChange={jest.fn()}
          onSponsorCreated={jest.fn()}
          onError={onError}
          onNext={onNext} onCancel={jest.fn()} />
      );

      fireEvent.click(screen.getByRole("button", { name: "Next" }));

      expect(onError).toHaveBeenCalledWith("Choose a sponsor first.");
      expect(onNext).not.toHaveBeenCalled();
    });

    it("clears the refusal once a sponsor is chosen and the wizard moves on", async () => {
      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) },
      ]);

      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={null} onClose={jest.fn()} />);

      fireEvent.click(screen.getByRole("button", { name: "Next" }));
      const dialog = screen.getByRole("dialog", { name: "New spot" });
      expect(within(dialog).getByRole("alert")).toHaveTextContent("Choose a sponsor first.");

      fireEvent.change(screen.getByLabelText("Sponsor"), { target: { value: String(SPONSOR_ACME.id) } });
      fireEvent.click(screen.getByRole("button", { name: "Next" }));

      expect(await screen.findByText(WIZARD_STEPS[1]?.purpose ?? "")).toBeInTheDocument();
      expect(within(dialog).queryByRole("alert")).not.toBeInTheDocument();
      expect(screen.queryByText("Choose a sponsor first.")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: every step names its purpose in a sentence", () => {
    it("each step carries exactly one purpose sentence — AC2", async () => {
      // Every step's own purpose, checked against the exact shape STORY-421 AC2 names: one
      // capitalized sentence, a single trailing period, nothing abbreviated or multi-sentence.
      expect(WIZARD_STEPS).toHaveLength(5);
      for (const step of WIZARD_STEPS) {
        expect(step.purpose).toMatch(/^[A-Z][^.]*\.$/);
      }

      const createdSpot = adSpot({ id: 61, script: null, bedMediaId: null });
      const scriptedSpot = adSpot({ id: 61, script: "ANNOUNCER: Walk.", bedMediaId: null });

      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) },
        { method: "POST", match: (u) => u.pathname === "/api/ads", respond: () => ({ status: 201, body: createdSpot }) },
        { method: "PATCH", match: (u) => u.pathname === "/api/ads/61", respond: () => ({ status: 200, body: scriptedSpot }) },
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/media", respond: () => ({ status: 200, body: [] }) },
      ]);

      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={SPONSOR_ACME.id} onClose={jest.fn()} />);

      // Every step renders exactly one `[data-purpose]` node — the wizard's own single shared
      // paragraph, never one per step stacking up as the wizard advances — and `StepHeader`'s own
      // `aria-current="step"` tracks the same step.
      function assertOnStep(index: number): void {
        expect(document.querySelectorAll("[data-purpose]")).toHaveLength(1);
        expect(screen.getByText(WIZARD_STEPS[index]?.purpose ?? "")).toBeInTheDocument();
        const nav = screen.getByRole("list", { name: "Steps" });
        const current = within(nav)
          .getAllByRole("listitem")
          .find((li) => li.getAttribute("aria-current") === "step");
        expect(current?.textContent).toBe(`${index + 1}. ${WIZARD_STEPS[index]?.label ?? ""}`);
      }

      assertOnStep(0);

      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Sponsor -> Angle
      expect(await screen.findByText(WIZARD_STEPS[1]?.purpose ?? "")).toBeInTheDocument();
      assertOnStep(1);

      fireEvent.change(screen.getByLabelText("Angle"), { target: { value: "A walkthrough angle." } });
      fireEvent.click(screen.getByLabelText("Keep this angle for later")); // skip the extra brief POST
      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Angle -> Script (POST /api/ads)
      expect(await screen.findByText(WIZARD_STEPS[2]?.purpose ?? "")).toBeInTheDocument();
      assertOnStep(2);

      fireEvent.change(screen.getByLabelText("Script"), { target: { value: "ANNOUNCER: Walk." } });
      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Script -> Hear (PATCH)
      expect(await screen.findByText(WIZARD_STEPS[3]?.purpose ?? "")).toBeInTheDocument();
      assertOnStep(3);

      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Hear -> Approve
      expect(await screen.findByText(WIZARD_STEPS[4]?.purpose ?? "")).toBeInTheDocument();
      assertOnStep(4);
    });
  });

  describe("Scenario: a committed background-music title can't be cleared back to the default", () => {
    it("choosing the disabled default option while a title is committed fires no request", async () => {
      const onSpotUpdated = jest.fn<(spot: AdSpotDto) => void>();
      const mockFetch = installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        {
          method: "GET",
          match: (u) => u.pathname === "/api/media",
          respond: () => ({ status: 200, body: [{ mediaId: "9", title: "Morning Chime", pack: null }] }),
        },
      ]);

      await act(async () => {
        render(
          <HearStep
            spot={adSpot({ bedMediaId: 9 })}
            onSpotUpdated={onSpotUpdated}
            onError={jest.fn()}
            onNext={jest.fn()}
            onPreview={jest.fn()}
            onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
        );
        await Promise.resolve();
      });

      const select = screen.getByLabelText("Background music") as HTMLSelectElement;
      fireEvent.change(select, { target: { value: "" } });

      expect(onSpotUpdated).not.toHaveBeenCalled();
      const patchCalls = mockFetch.mock.calls.filter((call) => (call[1] as RequestInit | undefined)?.method === "PATCH");
      expect(patchCalls).toHaveLength(0);
    });

    it("the default option is disabled once a title is committed", async () => {
      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/media", respond: () => ({ status: 200, body: [] }) },
      ]);

      await act(async () => {
        render(
          <HearStep
            spot={adSpot({ bedMediaId: 9 })}
            onSpotUpdated={jest.fn()}
            onError={jest.fn()}
            onNext={jest.fn()}
            onPreview={jest.fn()}
            onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
        );
        await Promise.resolve();
      });

      const select = screen.getByLabelText("Background music") as HTMLSelectElement;
      const defaultOption = within(select).getByRole("option", { name: "Let the station pick" }) as HTMLOptionElement;
      expect(defaultOption.disabled).toBe(true);
    });

    it("reads the committed title back into the select and states the one-way rule verbatim", async () => {
      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        {
          method: "GET",
          match: (u) => u.pathname === "/api/media",
          respond: () => ({ status: 200, body: [{ mediaId: "77", title: "Morning Chime", pack: null }] }),
        },
      ]);

      await act(async () => {
        render(
          <HearStep
            spot={adSpot({ bedMediaId: 77 })}
            onSpotUpdated={jest.fn()}
            onError={jest.fn()}
            onNext={jest.fn()}
            onPreview={jest.fn()}
            onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
        );
        await Promise.resolve();
      });

      expect(screen.getByLabelText("Background music")).toHaveValue("77");
      expect(
        screen.getByText("Once you pick a title, the music can only be changed to another title.")
      ).toBeInTheDocument();
    });
  });

  describe("Scenario: the Angle & length step's own defaults (F174.1, F174.2)", () => {
    it("defaults the spot length to 30 seconds", async () => {
      installFetchMock([{ method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) }]);

      await act(async () => {
        render(<AngleLengthStep sponsor={SPONSOR_ACME} onSpotCommitted={jest.fn()} onError={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />);
        await Promise.resolve();
      });

      const radiogroup = screen.getByRole("radiogroup", { name: "Length" });
      const thirty = within(radiogroup).getByRole("radio", { name: "30s" }) as HTMLInputElement;
      expect(thirty.checked).toBe(true);
    });

    it("keeps 'Keep this angle for later' checked by default, and saves the typed angle as a brief on Next when left checked", async () => {
      installFetchMock([{ method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) }]);

      const onSpotCommitted = jest.fn<(spot: AdSpotDto) => void>();
      await act(async () => {
        render(<AngleLengthStep sponsor={SPONSOR_ACME} onSpotCommitted={onSpotCommitted} onError={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />);
        await Promise.resolve();
      });

      const checkbox = screen.getByLabelText("Keep this angle for later") as HTMLInputElement;
      expect(checkbox.checked).toBe(true);

      const mockFetch = installFetchMock([
        { method: "POST", match: (u) => u.pathname === "/api/ads", respond: () => ({ status: 201, body: adSpot({ id: 70, script: null }) }) },
        { method: "POST", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 201, body: { id: 1 } }) },
      ]);

      fireEvent.change(screen.getByLabelText("Angle"), { target: { value: "A kept angle." } });
      fireEvent.click(screen.getByRole("button", { name: "Next" }));

      await waitFor(() => expect(onSpotCommitted).toHaveBeenCalled());

      const briefPost = mockFetch.mock.calls.find((call) => {
        const method = (call[1] as RequestInit | undefined)?.method;
        return method === "POST" && new URL(String(call[0]), "http://localhost").pathname === "/api/ad-briefs";
      });
      expect(briefPost).toBeDefined();
    });

    it("the created spot is titled 'Acme spot' — PLAN T451 ruling (SPEC F171.7)", async () => {
      const angleText = "Fresh roast before six. Warm pastries all morning.";
      const mockFetch = installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) },
        {
          method: "POST",
          match: (u) => u.pathname === "/api/ads",
          respond: () => ({ status: 201, body: adSpot({ id: 71, script: null, title: "Acme spot", brief: angleText }) }),
        },
      ]);

      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={SPONSOR_ACME.id} onClose={jest.fn()} />);

      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Sponsor -> Angle
      await screen.findByText(WIZARD_STEPS[1]?.purpose ?? "");

      fireEvent.change(screen.getByLabelText("Angle"), { target: { value: angleText } });
      fireEvent.click(screen.getByLabelText("Keep this angle for later")); // skip the extra brief POST — leaves POST /api/ads the one mutation to inspect
      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Angle -> Script (POST /api/ads)

      await screen.findByText(WIZARD_STEPS[2]?.purpose ?? "");

      const adsPostIndex = mockFetch.mock.calls.findIndex((call) => {
        const method = (call[1] as RequestInit | undefined)?.method;
        return method === "POST" && new URL(String(call[0]), "http://localhost").pathname === "/api/ads";
      });
      expect(requestBody(mockFetch, adsPostIndex)).toMatchObject({ title: "Acme spot", brief: angleText });
    });
  });

  describe("Scenario: the Hear step's rendered preview carries a cache-busting key", () => {
    it("the <audio> element's src carries the preview's own key as a query param", async () => {
      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/media", respond: () => ({ status: 200, body: [] }) },
      ]);

      await act(async () => {
        render(
          <HearStep
            spot={adSpot({ preview: { at: "2026-09-01T00:00:00Z", key: "xyz789", stale: false } })}
            onSpotUpdated={jest.fn()}
            onError={jest.fn()}
            onNext={jest.fn()}
            onPreview={jest.fn()}
            onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
        );
        await Promise.resolve();
      });

      const audio = document.querySelector("audio");
      expect(audio?.getAttribute("src")).toContain("?key=xyz789");
    });
  });

  describe("Scenario: the Sponsor step's own picker never offers a bare 'No sponsor' choice", () => {
    it("leaves the placeholder option disabled — PLAN T449 ruling", () => {
      render(
        <SponsorStep
          sponsors={[SPONSOR_ACME]}
          value={null}
          onChange={jest.fn()}
          onSponsorCreated={jest.fn()}
          onError={jest.fn()}
          onNext={jest.fn()} onCancel={jest.fn()} />
      );

      const select = screen.getByLabelText("Sponsor") as HTMLSelectElement;
      const placeholder = within(select).getByRole("option", { name: "Choose a sponsor…" }) as HTMLOptionElement;
      expect(placeholder.disabled).toBe(true);
    });

    it("names the consequence of choosing a paused sponsor", () => {
      const pausedSponsor: SponsorRefDto = { id: 5, name: "Old Mill Diner", paused: true };
      render(
        <SponsorStep
          sponsors={[pausedSponsor]}
          value={5}
          onChange={jest.fn()}
          onSponsorCreated={jest.fn()}
          onError={jest.fn()}
          onNext={jest.fn()} onCancel={jest.fn()} />
      );

      expect(
        screen.getByText("This sponsor is paused — the station will refuse a new spot for it until it is unpaused.")
      ).toBeInTheDocument();
    });
  });

  describe("Scenario: no radio jargon anywhere in the wizard's own copy (gh-#707)", () => {
    const JARGON_PATTERNS = [/brand/i, /advertiser/i, /\bbed\b/i];

    // Text alone would miss copy that only ever shows up in an `aria-label`/`placeholder`/`title`
    // attribute (an accessible name or a hint with no visible sibling text) — jargon leaking into
    // an attribute is just as real a leak as jargon in visible copy, so both are scanned together,
    // against the wizard SHELL itself (`currentStep.purpose` renders there, not inside any one
    // step component) rather than each step rendered in isolation.
    function assertNoJargon(): void {
      const attributeText = Array.from(document.querySelectorAll("[aria-label],[placeholder],[title]"))
        .flatMap((el) => [el.getAttribute("aria-label"), el.getAttribute("placeholder"), el.getAttribute("title")])
        .filter((value): value is string => value !== null)
        .join(" ");
      const text = `${document.body.textContent ?? ""} ${attributeText}`;
      for (const pattern of JARGON_PATTERNS) {
        expect(text).not.toMatch(pattern);
      }
    }

    it("keeps every step's own rendered copy free of 'brand', 'advertiser', and 'bed' as a bare word", async () => {
      const createdSpot = adSpot({ id: 63, script: null, bedMediaId: null });
      const scriptedSpot = adSpot({ id: 63, script: "ANNOUNCER: Walk.", bedMediaId: null });

      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) },
        { method: "POST", match: (u) => u.pathname === "/api/ads", respond: () => ({ status: 201, body: createdSpot }) },
        { method: "PATCH", match: (u) => u.pathname === "/api/ads/63", respond: () => ({ status: 200, body: scriptedSpot }) },
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/media", respond: () => ({ status: 200, body: [] }) },
      ]);

      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={SPONSOR_ACME.id} onClose={jest.fn()} />);
      assertNoJargon(); // Sponsor

      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Sponsor -> Angle
      await screen.findByText(WIZARD_STEPS[1]?.purpose ?? "");
      assertNoJargon(); // Angle & length

      fireEvent.change(screen.getByLabelText("Angle"), { target: { value: "A walkthrough angle." } });
      fireEvent.click(screen.getByLabelText("Keep this angle for later")); // skip the extra brief POST
      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Angle -> Script (POST /api/ads)
      await screen.findByText(WIZARD_STEPS[2]?.purpose ?? "");
      assertNoJargon(); // Script

      fireEvent.change(screen.getByLabelText("Script"), { target: { value: "ANNOUNCER: Walk." } });
      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Script -> Hear (PATCH)
      await screen.findByText(WIZARD_STEPS[3]?.purpose ?? "");
      assertNoJargon(); // Hear

      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Hear -> Approve
      await screen.findByText(WIZARD_STEPS[4]?.purpose ?? "");
      assertNoJargon(); // Approve
    });
  });

  describe("Scenario: a failed job leaves the wizard fully usable, not wedged (SPEC F174.3, F174.4; PLAN T448)", () => {
    const FAILED_JOB: AdSpotJobDto = { kind: null, failedKind: "write", startedAt: null, waitingForStation: false, error: "Model timed out" };
    const FAILED_PREVIEW_JOB: AdSpotJobDto = { ...FAILED_JOB, failedKind: "preview" };

    it("keeps the action button, Next, and the script textarea enabled with no progress copy, on both Script and Hear", async () => {
      render(
        <ScriptStep
          spot={adSpot({ job: FAILED_JOB })}
          onSpotUpdated={jest.fn()}
          onError={jest.fn()}
          onNext={jest.fn()}
          onWrite={jest.fn()}
          onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
      );

      expect(screen.getByRole("button", { name: "Write it for me" })).toBeEnabled();
      expect(screen.getByRole("button", { name: "Next" })).toBeEnabled();
      expect(screen.getByLabelText("Script")).toBeEnabled();
      expect(screen.queryByText("Writing…")).not.toBeInTheDocument();
      expect(screen.getByText("Model timed out")).toBeInTheDocument();
      cleanup();

      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/media", respond: () => ({ status: 200, body: [] }) },
      ]);
      await act(async () => {
        render(
          <HearStep
            spot={adSpot({ job: FAILED_PREVIEW_JOB })}
            onSpotUpdated={jest.fn()}
            onError={jest.fn()}
            onNext={jest.fn()}
            onPreview={jest.fn()}
            onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
        );
        await Promise.resolve();
      });

      expect(screen.getByRole("button", { name: "Render preview" })).toBeEnabled();
      expect(screen.getByRole("button", { name: "Next" })).toBeEnabled();
      expect(screen.queryByText("Rendering…")).not.toBeInTheDocument();
      expect(screen.getByText("Model timed out")).toBeInTheDocument();
    });
  });

  describe("Scenario: the write/preview job poll (SPEC F174.3, F174.4; PLAN T441, T442, T448)", () => {
    const ACTIVE_WRITE_JOB: AdSpotJobDto = { kind: "write", failedKind: null, startedAt: "2026-09-01T00:00:05Z", waitingForStation: false, error: null };

    beforeEach(() => {
      jest.useFakeTimers({ now: new Date("2026-09-01T00:00:00Z") });
    });

    afterEach(() => {
      jest.useRealTimers();
    });

    async function flush(): Promise<void> {
      await act(async () => {
        await jest.advanceTimersByTimeAsync(0);
      });
    }

    async function advance(ms: number): Promise<void> {
      await act(async () => {
        await jest.advanceTimersByTimeAsync(ms);
      });
    }

    async function reachScriptStepWithActiveWriteJob(extraHandlers: RouteHandler[] = []): Promise<jest.MockedFunction<typeof fetch>> {
      const createdSpot = adSpot({ id: 55, script: null, job: null });
      const activeJobSpot = adSpot({ id: 55, script: null, job: ACTIVE_WRITE_JOB });

      const mockFetch = installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) },
        { method: "POST", match: (u) => u.pathname === "/api/ads", respond: () => ({ status: 201, body: createdSpot }) },
        { method: "POST", match: (u) => u.pathname === "/api/ads/55/write", respond: () => ({ status: 202, body: activeJobSpot }) },
        ...extraHandlers,
      ]);

      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={SPONSOR_ACME.id} onClose={jest.fn()} />);

      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Sponsor -> Angle
      await flush();

      fireEvent.change(screen.getByLabelText("Angle"), { target: { value: "A pollable angle." } });
      fireEvent.click(screen.getByLabelText("Keep this angle for later")); // skip the extra brief POST
      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Angle -> Script (POST /api/ads)
      await flush();

      fireEvent.click(screen.getByRole("button", { name: "Write it for me" }));
      await flush();

      return mockFetch;
    }

    function pollCallCount(mockFetch: jest.MockedFunction<typeof fetch>): number {
      return mockFetch.mock.calls.filter((call) => {
        const method = (call[1] as RequestInit | undefined)?.method ?? "GET";
        const url = new URL(String(call[0]), "http://localhost");
        return method === "GET" && url.pathname === "/api/ads/55";
      }).length;
    }

    it("polls GET /api/ads/{id} at the fixed 2s interval while a job is running", async () => {
      const mockFetch = await reachScriptStepWithActiveWriteJob([
        {
          method: "GET",
          match: (u) => u.pathname === "/api/ads/55",
          respond: () => ({ status: 200, body: adSpot({ id: 55, script: null, job: ACTIVE_WRITE_JOB }) }),
        },
      ]);

      expect(pollCallCount(mockFetch)).toBe(0);

      await advance(2000);
      expect(pollCallCount(mockFetch)).toBe(1);

      await advance(2000);
      expect(pollCallCount(mockFetch)).toBe(2);

      await advance(2000);
      expect(pollCallCount(mockFetch)).toBe(3);
    });

    it("stops polling once the last job's own kind clears to null, even though the failed job's error stays visible", async () => {
      const mockFetch = await reachScriptStepWithActiveWriteJob([
        {
          method: "GET",
          match: (u) => u.pathname === "/api/ads/55",
          respond: () => ({
            status: 200,
            body: adSpot({ id: 55, script: null, job: { kind: null, failedKind: "write", startedAt: null, waitingForStation: false, error: "The write job failed." } }),
          }),
        },
      ]);

      await advance(2000);
      expect(pollCallCount(mockFetch)).toBe(1);
      expect(screen.getByText("The write job failed.")).toBeInTheDocument();

      await advance(2000);
      expect(pollCallCount(mockFetch)).toBe(1);
    });

    it("Stop fires exactly one DELETE /api/ads/{id}/job request and re-fetches the row", async () => {
      const mockFetch = await reachScriptStepWithActiveWriteJob([
        { method: "DELETE", match: (u) => u.pathname === "/api/ads/55/job", respond: () => ({ status: 204 }) },
        {
          method: "GET",
          match: (u) => u.pathname === "/api/ads/55",
          respond: () => ({ status: 200, body: adSpot({ id: 55, script: null, job: null }) }),
        },
      ]);

      fireEvent.click(screen.getByRole("button", { name: "Stop" }));
      await flush();

      const deleteCalls = mockFetch.mock.calls.filter((call) => ((call[1] as RequestInit | undefined)?.method ?? "GET") === "DELETE");
      expect(deleteCalls).toHaveLength(1);
      expect(new URL(String(deleteCalls[0]?.[0]), "http://localhost").pathname).toBe("/api/ads/55/job");

      const getCalls = mockFetch.mock.calls.filter((call) => {
        const method = (call[1] as RequestInit | undefined)?.method ?? "GET";
        const url = new URL(String(call[0]), "http://localhost");
        return method === "GET" && url.pathname === "/api/ads/55";
      });
      expect(getCalls).toHaveLength(1);
      expect(screen.queryByRole("button", { name: "Stop" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: every step offers Cancel, and every step after the first offers Back (Dean, 2026-09-11)", () => {
    const IN_FLIGHT_WRITE: AdSpotJobDto = { kind: "write", failedKind: null, startedAt: "2026-09-11T14:06:06Z", waitingForStation: false, error: null };

    it("the Sponsor step has Cancel but no Back, and Cancel closes the wizard", () => {
      installFetchMock([]);
      const onClose = jest.fn();
      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={null} onClose={onClose} />);

      expect(screen.queryByRole("button", { name: "Back" })).not.toBeInTheDocument();
      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
      expect(onClose).toHaveBeenCalledTimes(1);
    });

    it("Back from Angle & length returns to Sponsor with the sponsor still chosen", async () => {
      installFetchMock([{ method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) }]);
      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={SPONSOR_ACME.id} onClose={jest.fn()} />);

      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Sponsor -> Angle
      await screen.findByText(WIZARD_STEPS[1]?.purpose ?? "");

      fireEvent.click(screen.getByRole("button", { name: "Back" })); // Angle -> Sponsor
      expect(await screen.findByText(WIZARD_STEPS[0]?.purpose ?? "")).toBeInTheDocument();
      expect(screen.getByLabelText("Sponsor")).toHaveValue(String(SPONSOR_ACME.id));
    });

    it("Script, Hear, and Approve each wire Back to the wizard", async () => {
      const onBack = jest.fn();
      render(
        <ScriptStep spot={adSpot()} onSpotUpdated={jest.fn()} onError={jest.fn()} onNext={jest.fn()} onWrite={jest.fn()} onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={onBack} />
      );
      fireEvent.click(screen.getByRole("button", { name: "Back" }));
      expect(onBack).toHaveBeenCalledTimes(1);
      cleanup();

      installFetchMock([{ method: "GET", match: (u) => u.pathname === "/api/media", respond: () => ({ status: 200, body: [] }) }]);
      await act(async () => {
        render(
          <HearStep spot={adSpot()} onSpotUpdated={jest.fn()} onError={jest.fn()} onNext={jest.fn()} onPreview={jest.fn()} onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={onBack} />
        );
        await Promise.resolve();
      });
      fireEvent.click(screen.getByRole("button", { name: "Back" }));
      expect(onBack).toHaveBeenCalledTimes(2);
      cleanup();

      render(<ApproveStep spot={adSpot()} onApproved={jest.fn()} onSpotUpdated={jest.fn()} onError={jest.fn()} onCancel={jest.fn()} onBack={onBack} />);
      fireEvent.click(screen.getByRole("button", { name: "Back" }));
      expect(onBack).toHaveBeenCalledTimes(3);
    });

    it("Back and Cancel are disabled while a job is in flight, and the job's own button reads Stop, never Cancel", () => {
      render(
        <ScriptStep spot={adSpot({ job: IN_FLIGHT_WRITE })} onSpotUpdated={jest.fn()} onError={jest.fn()} onNext={jest.fn()} onWrite={jest.fn()} onCancelJob={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
      );

      expect(screen.getByRole("button", { name: "Back" })).toBeDisabled();
      expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
      expect(screen.getByRole("button", { name: "Stop" })).toBeEnabled();
      expect(screen.getAllByRole("button", { name: "Cancel" })).toHaveLength(1);
    });
  });

  describe("Scenario: Back to Angle & length edits the spot the wizard already created, never a second one", () => {
    it("reads the row's own angle and length back, and Next with nothing changed sends no request", async () => {
      const mockFetch = installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) },
      ]);
      const existing = adSpot({ id: 61, brief: "Fresh bread every morning", spotSeconds: 60 });
      const onSpotCommitted = jest.fn();

      await act(async () => {
        render(
          <AngleLengthStep sponsor={SPONSOR_ACME} existingSpot={existing} onSpotCommitted={onSpotCommitted} onError={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
        );
        await Promise.resolve();
      });

      expect(screen.getByLabelText("Angle")).toHaveValue("Fresh bread every morning");
      expect(screen.getByLabelText("60s")).toBeChecked();

      fireEvent.click(screen.getByRole("button", { name: "Next" }));
      expect(onSpotCommitted).toHaveBeenCalledWith(existing);
      expect(mockFetch.mock.calls.filter((call) => ((call[1] as RequestInit | undefined)?.method ?? "GET") !== "GET")).toHaveLength(0);
    });

    it("a changed length is a PATCH on the existing row carrying only the change — never a POST /api/ads", async () => {
      const existing = adSpot({ id: 61, brief: "Fresh bread every morning", spotSeconds: 30 });
      const mockFetch = installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) },
        { method: "PATCH", match: (u) => u.pathname === "/api/ads/61", respond: () => ({ status: 200, body: { ...existing, spotSeconds: 60, version: "101" } }) },
      ]);
      const onSpotCommitted = jest.fn();

      await act(async () => {
        render(
          <AngleLengthStep sponsor={SPONSOR_ACME} existingSpot={existing} onSpotCommitted={onSpotCommitted} onError={jest.fn()} onCancel={jest.fn()} onBack={jest.fn()} />
        );
        await Promise.resolve();
      });

      fireEvent.click(screen.getByLabelText("60s"));
      fireEvent.click(screen.getByLabelText("Keep this angle for later")); // unchanged angle — no brief POST to muddy the count
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Next" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(onSpotCommitted).toHaveBeenCalledTimes(1));
      const mutations = mockFetch.mock.calls.filter((call) => ((call[1] as RequestInit | undefined)?.method ?? "GET") !== "GET");
      expect(mutations).toHaveLength(1);
      expect(new URL(String(mutations[0]?.[0]), "http://localhost").pathname).toBe("/api/ads/61");
      expect((mutations[0]?.[1] as RequestInit).method).toBe("PATCH");
      expect(JSON.parse(String((mutations[0]?.[1] as RequestInit).body))).toMatchObject({ spotSeconds: 60, brief: null, script: null });
      expect(onSpotCommitted.mock.calls[0]?.[0]).toMatchObject({ spotSeconds: 60, version: "101" });
    });

    it("the whole trip Sponsor -> Angle -> Script -> Back -> Next creates exactly one spot", async () => {
      const angleText = "Fresh bread every morning";
      const created = adSpot({ id: 61, script: null, brief: angleText });
      const mockFetch = installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/ad-briefs", respond: () => ({ status: 200, body: [] }) },
        { method: "POST", match: (u) => u.pathname === "/api/ads", respond: () => ({ status: 201, body: created }) },
      ]);
      render(<SpotWizard sponsors={[SPONSOR_ACME]} initialSponsorId={SPONSOR_ACME.id} onClose={jest.fn()} />);

      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Sponsor -> Angle
      await screen.findByText(WIZARD_STEPS[1]?.purpose ?? "");
      fireEvent.change(screen.getByLabelText("Angle"), { target: { value: angleText } });
      fireEvent.click(screen.getByLabelText("Keep this angle for later"));
      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Angle -> Script (the one POST)
      await screen.findByText(WIZARD_STEPS[2]?.purpose ?? "");

      fireEvent.click(screen.getByRole("button", { name: "Back" })); // Script -> Angle, revisiting the row
      await screen.findByText(WIZARD_STEPS[1]?.purpose ?? "");
      expect(await screen.findByLabelText("Angle")).toHaveValue(angleText);

      fireEvent.click(screen.getByRole("button", { name: "Next" })); // Angle -> Script again, nothing changed
      await screen.findByText(WIZARD_STEPS[2]?.purpose ?? "");

      const adsPosts = mockFetch.mock.calls.filter((call) => {
        const method = (call[1] as RequestInit | undefined)?.method;
        return method === "POST" && new URL(String(call[0]), "http://localhost").pathname === "/api/ads";
      });
      expect(adsPosts).toHaveLength(1);
    });
  });

  describe("Scenario: the Hear step's music picker reaches beds in the installed pack's own library (gh-#718)", () => {
    it("names each installed pack's library with library-id= and offers those beds alongside the station-scoped browse", async () => {
      const mockFetch = installFetchMock([
        {
          method: "GET",
          match: (u) => u.pathname === "/api/jingle-packs",
          respond: () => ({ status: 200, body: [{ slug: "gw-first-beds", packName: "First Beds", libraryId: 3 }, { slug: "twice", packName: "Same Library", libraryId: 3 }] }),
        },
        {
          method: "GET",
          match: (u) => u.pathname === "/api/media" && u.searchParams.get("library-id") === "3",
          respond: () => ({ status: 200, body: [{ mediaId: "2569", title: "Swinging Sweet", pack: "First Beds" }] }),
        },
        {
          method: "GET",
          match: (u) => u.pathname === "/api/media" && u.searchParams.get("library-id") === null,
          respond: () => ({ status: 200, body: [{ mediaId: "42", title: "In-Scope Bed", pack: null }] }),
        },
      ]);

      await act(async () => {
        render(
          <HearStep spot={adSpot()} onSpotUpdated={jest.fn()} onError={jest.fn()} onNext={jest.fn()} onPreview={jest.fn()} onCancelJob={jest.fn()} onBack={jest.fn()} onCancel={jest.fn()} />
        );
        await Promise.resolve();
      });

      const select = await screen.findByLabelText("Background music");
      await waitFor(() => expect(within(select).getByRole("option", { name: "Swinging Sweet — First Beds" })).toBeInTheDocument());
      expect(within(select).getByRole("option", { name: "In-Scope Bed" })).toBeInTheDocument();

      const mediaBrowses = mockFetch.mock.calls
        .map((call) => new URL(String(call[0]), "http://localhost"))
        .filter((u) => u.pathname === "/api/media");
      expect(mediaBrowses.map((u) => u.searchParams.get("library-id")).sort()).toEqual([null, "3"].sort());
      expect(mediaBrowses).toHaveLength(2); // one named browse per DISTINCT library, not per pack
      for (const u of mediaBrowses) {
        expect(u.searchParams.get("imagingKind")).toBe("jingle");
        expect(u.searchParams.get("jingleRole")).toBe("bed");
      }
    });

    it("falls back to the station-scoped browse alone when no pack is installed", async () => {
      const mockFetch = installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/jingle-packs", respond: () => ({ status: 200, body: [] }) },
        { method: "GET", match: (u) => u.pathname === "/api/media", respond: () => ({ status: 200, body: [] }) },
      ]);

      await act(async () => {
        render(
          <HearStep spot={adSpot()} onSpotUpdated={jest.fn()} onError={jest.fn()} onNext={jest.fn()} onPreview={jest.fn()} onCancelJob={jest.fn()} onBack={jest.fn()} onCancel={jest.fn()} />
        );
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch.mock.calls.filter((call) => String(call[0]).startsWith("/api/media"))).toHaveLength(1));
      expect(String(mockFetch.mock.calls.find((call) => String(call[0]).startsWith("/api/media"))?.[0])).not.toContain("library-id");
    });
  });
});
