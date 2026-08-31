// @jest-environment jsdom
// STORY-373 — I can install and tune Deep Cuts: the Shows page rotation rule editor (SPEC
// F152.5-F152.7 · PLAN T362)
//
// BDD specification — jest. Drives ShowRotationRuleEditor via @testing-library/react with a fetch
// mock dispatched by URL+METHOD (mirrors shows-page.spec.tsx's own makeDispatchFetchMock) — the
// component issues PUT /api/shows/{id} (the rule save) and GET /api/shows/{id}/rotation-pool +
// GET /api/shows/{id}/last-airing (usePoll's own combined status read) on mount. AC4/AC5 (the
// catalog import path) and AC8 (the catalog lint) are not this project's concern — see
// Story373_InstallAndTuneDeepCuts.cs's own header.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { Toaster } from "@/components/ui/toast";
import { ShowRotationRuleEditor } from "../app/(authed)/shows/ShowRotationRuleEditor";
import type { ShowRotationRuleEditorProps } from "../app/(authed)/shows/ShowRotationRuleEditor";

// ---------------------------------------------------------------------------
// Fetch mock — dispatched by "METHOD url" (mirrors shows-page.spec.tsx's own harness). Every test
// below explicitly maps BOTH background poll routes (rotation-pool/last-airing) even when a given
// scenario doesn't care about one of them, so the default `{}` body never leaks an
// undefined-shaped value into an assertion that isn't looking for it.
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

function routeKey(method: string, url: string): string {
  return `${method.toUpperCase()} ${url}`;
}

function makeDispatchFetchMock(routes: Record<string, RouteResponseSpec>): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = init?.method ?? "GET";
    const url = String(input);
    const spec = routes[routeKey(method, url)] ?? { status: 200, body: {} };
    // spec.body === undefined (the property OMITTED) means "no opinion, default to {}"; an
    // EXPLICIT null (SPEC F152.5's own last-airing "never aired yet" shape) must survive verbatim
    // — `spec.body ?? {}` would silently coalesce that null to {}, which is exactly the bug this
    // guard exists to avoid (found the hard way: it masked the "no last airing" scenario below).
    const body = spec.body === undefined ? {} : spec.body;
    return {
      ok: spec.status >= 200 && spec.status < 300,
      status: spec.status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
      headers: new Headers(),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
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

const QUIET_STATUS_ROUTES: Record<string, RouteResponseSpec> = {
  "GET /api/shows/1/rotation-pool": { status: 200, body: { eligible: null, since: null } },
  "GET /api/shows/1/last-airing": { status: 200, body: { airedCount: null, relaxed: null } },
};

/** Renders the editor and flushes usePoll's own immediate mount-time fetch (the "fires the fetcher
 * immediately on mount" contract, lib/use-poll.ts's own remarks) inside an `act` boundary — without
 * this, that resolution lands between the test's own act scopes and React logs an
 * update-not-wrapped-in-act warning even though every assertion below still passes. */
async function renderEditor(overrides: Partial<ShowRotationRuleEditorProps> = {}): Promise<ReturnType<typeof render>> {
  const props: ShowRotationRuleEditorProps = {
    showId: 1,
    initialRotation: null,
    pollIntervalMs: 60_000,
    ...overrides,
  };
  const result = render(
    <>
      <ShowRotationRuleEditor {...props} />
      <Toaster />
    </>
  );
  await act(async () => {
    await Promise.resolve();
  });
  return result;
}

// ---------------------------------------------------------------------------
// Feature: Shows page rotation rule
// ---------------------------------------------------------------------------

describe("Feature: Shows page rotation rule", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the editor saves the rule", () => {
    it("saving the rule editor PUTs envelope.rotation on the show (pending T362, STORY-373 AC1)", async () => {
      const mockFetch = makeDispatchFetchMock({
        ...QUIET_STATUS_ROUTES,
        "PUT /api/shows/1": { status: 200, body: { rotation: { maxPlays: 1, notAiredWithinDays: null } } },
      });
      await renderEditor();

      fireEvent.change(screen.getByLabelText("Max plays"), { target: { value: "1" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save rule" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(findCall(mockFetch, "PUT", "/api/shows/1")).toBeDefined();
      });

      const [, init] = findCall(mockFetch, "PUT", "/api/shows/1") as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body).toEqual({ rotation: { maxPlays: 1, notAiredWithinDays: null } });
    });

    it("after saving, the editor reflects the value the GET echoes back (pending T362, STORY-373 AC1)", async () => {
      makeDispatchFetchMock(QUIET_STATUS_ROUTES);

      // The rule the page's own SSR-fetched GET /api/shows already echoed back (SPEC F152.5's own
      // "absent = leave unchanged, the GET echoes it" contract) — the editor's form fields must
      // show exactly this value on render, not just whatever was last typed into them.
      await renderEditor({ initialRotation: { maxPlays: 3, notAiredWithinDays: 90 } });

      expect((screen.getByLabelText("Max plays") as HTMLInputElement).value).toBe("3");
      expect((screen.getByLabelText("Not aired within (days)") as HTMLInputElement).value).toBe("90");
      // And the rule already existing offers a way to clear it.
      expect(screen.getByRole("button", { name: "Clear rule" })).toBeInTheDocument();
    });
  });

  describe("Scenario: the live pool size", () => {
    it("the show's card shows the eligible pool size from GET /api/shows/{id}/rotation-pool (pending T362, STORY-373 AC2)", async () => {
      makeDispatchFetchMock({
        "GET /api/shows/1/rotation-pool": { status: 200, body: { eligible: 1234, since: "2026-08-01T00:00:00Z" } },
        "GET /api/shows/1/last-airing": { status: 200, body: { airedCount: null, relaxed: null } },
      });

      await renderEditor();

      await waitFor(() => {
        expect(screen.getByText("1,234 tracks eligible right now")).toBeInTheDocument();
      });
    });

    it('shows "eligibility unknown" when the catalog cannot answer (pending T362, STORY-373 AC2)', async () => {
      makeDispatchFetchMock(QUIET_STATUS_ROUTES);

      await renderEditor();

      await waitFor(() => {
        expect(screen.getByText("eligibility unknown")).toBeInTheDocument();
      });
    });
  });

  describe("Scenario: the last airing's relax count", () => {
    it('a show with booth-log picks stamped RotationRelax 0,0,1,2 shows "last airing: 4 picks, 2 relaxed" (pending T362, STORY-373 AC3)', async () => {
      makeDispatchFetchMock({
        "GET /api/shows/1/rotation-pool": { status: 200, body: { eligible: null, since: null } },
        "GET /api/shows/1/last-airing": { status: 200, body: { airedCount: 4, relaxed: 2 } },
      });

      await renderEditor();

      await waitFor(() => {
        expect(
          screen.getByText((_, node) => node?.textContent === "last airing: 4 picks, 2 relaxed")
        ).toBeInTheDocument();
      });
    });

    it("renders no last-airing line at all when the show has never aired (pending T362, STORY-373 AC3)", async () => {
      makeDispatchFetchMock(QUIET_STATUS_ROUTES);

      await renderEditor();

      await waitFor(() => {
        expect(screen.getByText("eligibility unknown")).toBeInTheDocument();
      });
      expect(screen.queryByText(/last airing/)).not.toBeInTheDocument();
    });
  });
});
