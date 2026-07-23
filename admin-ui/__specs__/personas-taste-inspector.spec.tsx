// @jest-environment jsdom
// STORY-219 — I can inspect what my persona's taste is and what it has learned (SPEC F86.7 — UI half, T78)
//
// Runner: Jest + jsdom + @testing-library/react, driving the real PersonasClient (mirrors
// personas-page.spec.tsx's harness: a fetch mock dispatched by "METHOD url", wrapped in
// ConfirmDialogProvider + Toaster). Each persona row on the Personas page gains an expandable
// read-only "Taste" section fed by GET /api/personas/{id}/taste, fetched lazily — only once the
// row's disclosure is opened, never on page load — through PersonaTasteSection.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonasClientProps } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";
import type { PersonaTasteResponse } from "../lib/persona-taste-inspector-api";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const REX: PersonaDto = {
  id: 1,
  name: "Radio Rex",
  backstory: "A grizzled late-night jock who has seen every format come and go.",
  style: "Warm, gravelly, brief.",
  voice: "af_alloy",
};

const NOVA: PersonaDto = {
  id: 2,
  name: "Nova",
  backstory: "An upbeat morning host.",
  style: "Bright and quick.",
  voice: "",
};

const RULE_ALL_SOURCES: PersonaTasteResponse = {
  authored: [
    {
      predicateSummary: "Pink Floyd",
      daysOfWeek: [0],
      startHour: 6,
      endHour: 12,
      weight: 0.800000011920929,
      updatedAt: "2026-07-01T09:00:00Z",
    },
  ],
  operator: [
    {
      predicateSummary: "Vaporwave",
      daysOfWeek: [],
      startHour: null,
      endHour: null,
      weight: -0.6,
      updatedAt: "2026-07-10T14:30:00Z",
    },
  ],
  accrued: [
    {
      predicateSummary: "The Waveforms",
      daysOfWeek: [],
      startHour: null,
      endHour: null,
      weight: 0.3,
      updatedAt: "2026-07-20T03:15:00Z",
    },
  ],
  accruedCount: 12,
  accruedCap: 40,
};

const NO_TASTE: PersonaTasteResponse = {
  authored: [],
  operator: [],
  accrued: [],
  accruedCount: 0,
  accruedCap: 40,
};

// ---------------------------------------------------------------------------
// Fetch mock — dispatched by "METHOD url" (mirrors personas-page.spec.tsx's own harness).
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

function makeDispatchFetchMock(routes: Record<string, RouteResponseSpec>): jest.MockedFunction<typeof fetch> {
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

function renderClient(overrides: Partial<PersonasClientProps> = {}): ReturnType<typeof render> {
  const props: PersonasClientProps = {
    initialPersonas: [REX, NOVA],
    initialActiveId: 0,
    ...overrides,
  };
  return render(
    <ConfirmDialogProvider>
      <PersonasClient {...props} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function rowFor(name: string): HTMLElement {
  const nameNode = screen.getByTestId(`persona-name-${name}`);
  const row = nameNode.closest("tr");
  if (row === null) throw new Error(`No <tr> ancestor for "${name}"`);
  return row;
}

/** Expands Radio Rex's Taste disclosure and returns its region once mounted. */
async function expandTaste(name: string): Promise<HTMLElement> {
  const row = rowFor(name);
  await act(async () => {
    fireEvent.click(within(row).getByRole("button", { name: `Show taste for ${name}` }));
    await Promise.resolve();
  });
  return screen.findByRole("region", { name: `Taste for ${name}` });
}

/** Every call this mock recorded against a `GET /api/personas/{id}/taste` — the lazy-fetch
 * contract under test in "Scenario: lazy fetch" below. */
function tasteFetchCalls(mockFetch: jest.MockedFunction<typeof fetch>): unknown[] {
  return mockFetch.mock.calls.filter(([input]) => /\/api\/personas\/\d+\/taste$/.test(String(input)));
}

// ---------------------------------------------------------------------------
// Feature: persona taste inspector
// ---------------------------------------------------------------------------

describe("Feature: persona taste inspector", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: expanding a persona with rules from all three sources", () => {
    it("groups rules under authored, operator, and accrued headings", async () => {
      makeDispatchFetchMock({ "GET /api/personas/1/taste": { status: 200, body: RULE_ALL_SOURCES } });
      renderClient();

      const section = await expandTaste("Radio Rex");

      await waitFor(() => {
        expect(within(section).getByRole("heading", { name: "Authored" })).toBeInTheDocument();
      });
      expect(within(section).getByRole("heading", { name: "Operator" })).toBeInTheDocument();
      expect(within(section).getByRole("heading", { name: "Accrued" })).toBeInTheDocument();
    });

    it("renders a signed weight bar per rule spanning −1 to +1", async () => {
      makeDispatchFetchMock({ "GET /api/personas/1/taste": { status: 200, body: RULE_ALL_SOURCES } });
      renderClient();

      const section = await expandTaste("Radio Rex");

      const bar = await within(section).findByRole("progressbar", { name: "Weight +0.8" });
      expect([bar.getAttribute("aria-valuemin"), bar.getAttribute("aria-valuemax")]).toEqual(["-1", "1"]);
    });

    it("shows each rule's predicate summary", async () => {
      makeDispatchFetchMock({ "GET /api/personas/1/taste": { status: 200, body: RULE_ALL_SOURCES } });
      renderClient();

      const section = await expandTaste("Radio Rex");

      await waitFor(() => {
        expect(within(section).getByText("The Waveforms")).toBeInTheDocument();
      });
    });

    it("renders the accrued cap meter from the response's count and cap", async () => {
      makeDispatchFetchMock({ "GET /api/personas/1/taste": { status: 200, body: RULE_ALL_SOURCES } });
      renderClient();

      const section = await expandTaste("Radio Rex");

      const meter = await within(section).findByRole("progressbar", {
        name: "Accrued taste rules against the cap",
      });
      expect([meter.getAttribute("aria-valuenow"), meter.getAttribute("aria-valuemax")]).toEqual(["12", "40"]);
    });
  });

  describe("Scenario: expanding a persona with no taste", () => {
    it("states plainly that the persona has no taste yet", async () => {
      makeDispatchFetchMock({ "GET /api/personas/2/taste": { status: 200, body: NO_TASTE } });
      renderClient();

      const section = await expandTaste("Nova");

      await waitFor(() => {
        expect(within(section).getByText(/no taste yet/i)).toBeInTheDocument();
      });
    });
  });

  describe("Scenario: lazy fetch", () => {
    it("does not fetch the taste route for any row before it is expanded", () => {
      const mockFetch = makeDispatchFetchMock({
        "GET /api/personas/1/taste": { status: 200, body: RULE_ALL_SOURCES },
        "GET /api/personas/2/taste": { status: 200, body: NO_TASTE },
      });
      renderClient();

      expect(tasteFetchCalls(mockFetch)).toHaveLength(0);
    });

    it("fetches the taste route exactly once after expanding the row", async () => {
      const mockFetch = makeDispatchFetchMock({
        "GET /api/personas/1/taste": { status: 200, body: RULE_ALL_SOURCES },
      });
      renderClient();

      await expandTaste("Radio Rex");

      await waitFor(() => {
        expect(tasteFetchCalls(mockFetch)).toHaveLength(1);
      });
    });
  });

  describe("Scenario: read-only guarantees", () => {
    it("renders no edit, delete, or add control anywhere in the section", async () => {
      makeDispatchFetchMock({ "GET /api/personas/1/taste": { status: 200, body: RULE_ALL_SOURCES } });
      renderClient();

      const section = await expandTaste("Radio Rex");
      await waitFor(() => {
        expect(within(section).getByRole("heading", { name: "Authored" })).toBeInTheDocument();
      });

      expect(within(section).queryAllByRole("button")).toHaveLength(0);
    });
  });
});
