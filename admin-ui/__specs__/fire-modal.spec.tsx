// @jest-environment jsdom
// STORY-247 — Two-stage firing with a parachute (SPEC F94.2, F91.9, PLAN T128)
//
// Fire replaces the generic-confirm "Delete" button PLAN T127 shipped for the roster — and only on
// a BENCH row (fire from a scheduled segment is unpainting, T129's own editor; F91.9's FK guard
// already 409s a scheduled delete server-side, so a Scheduled row gets no delete/fire affordance at
// all, not merely a button that always fails). Drives `PersonasClient` through the same
// dispatch-by-URL+METHOD fetch-mock harness `roster-page.spec.tsx` and `personas-page.spec.tsx`
// both already use — this file owns the Fire/`FireModal` slice of that shared component's
// behavior, the same way `roster-page.spec.tsx` owns the Scheduled/Bench split and the On The Air
// badge.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonasClientProps } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const REX: PersonaDto = {
  id: 1,
  name: "Radio Rex",
  backstory: "A grizzled late-night jock who has seen every format come and go.",
  style: "Warm, gravelly, brief.",
  voice: "af_alloy",
  slug: "radio-rex",
  importedFrom: null,
  importedAt: null,
  soul: "",
  quirks: [],
  lore: [],
};

const NOVA: PersonaDto = {
  id: 2,
  name: "Nova",
  backstory: "An upbeat morning host.",
  style: "Bright and quick.",
  voice: "",
  slug: "nova",
  importedFrom: null,
  importedAt: null,
  soul: "",
  quirks: [],
  lore: [],
};

// ---------------------------------------------------------------------------
// Fetch mock — dispatched by "METHOD url" (mirrors roster-page.spec.tsx's own harness).
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

function routeKey(method: string, url: string): string {
  return `${method.toUpperCase()} ${url}`;
}

const DEFAULT_ROUTES: Record<string, RouteResponseSpec> = {
  "GET /api/voices": { status: 200, body: [] },
};

function makeDispatchFetchMock(routes: Record<string, RouteResponseSpec> = {}): jest.MockedFunction<typeof fetch> {
  const allRoutes = { ...DEFAULT_ROUTES, ...routes };
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = init?.method ?? "GET";
    const url = String(input);
    const spec = allRoutes[routeKey(method, url)] ?? { status: 200, body: {} };
    return {
      ok: spec.status >= 200 && spec.status < 300,
      status: spec.status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
      headers: new Headers(),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function renderRoster(overrides: Partial<PersonasClientProps> = {}): ReturnType<typeof render> {
  const props: PersonasClientProps = {
    initialPersonas: [REX, NOVA],
    scheduledPersonaIds: [],
    onAirPersonaName: null,
    ...overrides,
  };
  return render(
    <ConfirmDialogProvider>
      <PersonasClient {...props} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function findCall(
  mockFetch: jest.MockedFunction<typeof fetch>,
  method: string,
  url: string
): [string, RequestInit] | undefined {
  return mockFetch.mock.calls.find(
    ([callUrl, init]) => String(callUrl) === url && ((init as RequestInit | undefined)?.method ?? "GET") === method
  ) as [string, RequestInit] | undefined;
}

/** Opens the Fire modal for a bench persona and returns it, scoped via `within` like every other
 * dialog helper in this codebase (`confirm-dialog`/`PersonaCardReviewModal` specs alike) — only one
 * Fire modal is ever open at once (`PersonasClient`'s own `firingPersona` is a single value), so
 * an unqualified `getByRole("dialog")` is unambiguous, matching the house idiom. */
function openFireModal(name: string): HTMLElement {
  fireEvent.click(screen.getByRole("button", { name: `Fire ${name}` }));
  return screen.getByRole("dialog");
}

// ---------------------------------------------------------------------------
// Feature: Two-stage firing with a parachute
// ---------------------------------------------------------------------------

describe("Feature: Two-stage firing with a parachute", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: Fire is bench-only", () => {
    it("renders no delete/fire control on a Scheduled row", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [REX.id] });

      expect(screen.queryByRole("button", { name: "Fire Radio Rex" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Delete Radio Rex" })).not.toBeInTheDocument();
    });

    it("renders Fire on a Bench row", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [] });

      expect(screen.getByRole("button", { name: "Fire Radio Rex" })).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Fire Nova" })).toBeInTheDocument();
    });
  });

  describe("Scenario: the export-first gate (SPEC F94.2, STORY-247 AC2)", () => {
    it("disables Delete until export is clicked or skip is acknowledged", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [] });

      const modal = openFireModal("Radio Rex");
      expect(within(modal).getByRole("button", { name: "Delete" })).toBeDisabled();
    });

    it("enables Delete once the Export action is clicked", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [] });

      const modal = openFireModal("Radio Rex");
      fireEvent.click(within(modal).getByRole("link", { name: "Export Radio Rex" }));

      expect(within(modal).getByRole("button", { name: "Delete" })).toBeEnabled();
    });

    it("enables Delete once the skip-export box is checked, with no export click at all", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [] });

      const modal = openFireModal("Radio Rex");
      fireEvent.click(within(modal).getByRole("checkbox"));

      expect(within(modal).getByRole("button", { name: "Delete" })).toBeEnabled();
    });

    it("leaves Delete disabled after a click that lands in the export row's own dead space, not the Export link itself (F1)", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [] });

      const modal = openFireModal("Radio Rex");
      const exportLink = within(modal).getByRole("link", { name: "Export Radio Rex" });
      // The regression this pins: a bubbling `onClick` on the row's own wrapping `<div>` (removed
      // by the F1 fix) used to fire `setHasExported(true)` for ANY click landing in that div's dead
      // space beside the ~90px Export button, not just an actual click/keyboard-Enter on the anchor
      // itself. Clicking the div directly — not a sibling element elsewhere in the modal — is what
      // would catch that regression coming back.
      fireEvent.click(exportLink.closest("div") as HTMLElement);

      expect(within(modal).getByRole("button", { name: "Delete" })).toBeDisabled();
    });

    it("leaves Delete disabled after a click elsewhere in the modal body (the description text)", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [] });

      const modal = openFireModal("Radio Rex");
      fireEvent.click(within(modal).getByText(/This deletes Radio Rex permanently/));

      expect(within(modal).getByRole("button", { name: "Delete" })).toBeDisabled();
    });

    it("re-gates Delete on reopen — an export click doesn't survive a Cancel + reopen", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [] });

      const firstOpen = openFireModal("Radio Rex");
      fireEvent.click(within(firstOpen).getByRole("link", { name: "Export Radio Rex" }));
      expect(within(firstOpen).getByRole("button", { name: "Delete" })).toBeEnabled();
      fireEvent.click(within(firstOpen).getByRole("button", { name: "Cancel" }));
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

      const reopened = openFireModal("Radio Rex");
      expect(within(reopened).getByRole("button", { name: "Delete" })).toBeDisabled();
    });
  });

  describe("Scenario: cancel is a no-op (SPEC F94.2, STORY-247 AC4)", () => {
    it("issues zero requests and leaves the persona in place", () => {
      const mockFetch = makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [] });

      const modal = openFireModal("Radio Rex");
      fireEvent.click(within(modal).getByRole("button", { name: "Cancel" }));

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(screen.getByTestId("persona-name-Radio Rex")).toBeInTheDocument();
      expect(findCall(mockFetch, "DELETE", "/api/personas/1")).toBeUndefined();
    });
  });

  describe("Scenario: firing a benched DJ (the happy path)", () => {
    it("issues DELETE and removes the row once export-gated Delete is clicked", async () => {
      const mockFetch = makeDispatchFetchMock({ "DELETE /api/personas/1": { status: 204 } });
      renderRoster({ scheduledPersonaIds: [] });

      const modal = openFireModal("Radio Rex");
      fireEvent.click(within(modal).getByRole("checkbox"));

      await act(async () => {
        fireEvent.click(within(modal).getByRole("button", { name: "Delete" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.queryByTestId("persona-name-Radio Rex")).not.toBeInTheDocument();
      });
      expect(findCall(mockFetch, "DELETE", "/api/personas/1")).toBeDefined();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH — the RACE case
  // -------------------------------------------------------------------------

  describe("Scenario: racing a schedule edit (SPEC F91.9, the RACE case)", () => {
    it("closes the modal and toasts the server's slot-naming detail on a 409", async () => {
      makeDispatchFetchMock({
        "DELETE /api/personas/1": {
          status: 409,
          body: {
            title: "Persona is scheduled.",
            detail: "Persona 1 is still scheduled and cannot be deleted: Mon 09:00–12:00.",
          },
        },
      });
      renderRoster({ scheduledPersonaIds: [] });

      const modal = openFireModal("Radio Rex");
      fireEvent.click(within(modal).getByRole("checkbox"));

      await act(async () => {
        fireEvent.click(within(modal).getByRole("button", { name: "Delete" }));
        await Promise.resolve();
      });

      // The modal is gone — nothing left open to compete with the toast for the operator's
      // attention (T128's own addition: PersonaCardReviewModal's own error path keeps ITS dialog
      // open instead, but a 409 here is a stale fact about the SCHEDULE, not this dialog).
      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      expect(
        screen.getByText("Persona 1 is still scheduled and cannot be deleted: Mon 09:00–12:00.")
      ).toBeInTheDocument();

      // The store rejected the delete — the row survives.
      expect(screen.getByTestId("persona-name-Radio Rex")).toBeInTheDocument();
    });
  });
});
