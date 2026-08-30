// @jest-environment jsdom
// STORY-374 / STORY-376 / STORY-379 — The Gardener page: findings by kind, Keep this one, and the
// file-action dry-run (SPEC F153.9-F153.10, F153.5, F154.5 · PLAN T378, T381)
//
// PLAN T378 (this file's own top section) BUILDS the Gardener page itself —
// app/(authed)/gardener/GardenerView.tsx + page.tsx (+ GardenerSection/GardenerRow/
// DuplicateGroupCard), plus the shared `_components/PurgeUnavailableAction` and the Gardener nav
// entry (gardener-nav.spec.tsx) — replacing this file's own former T378 `it.todo` placeholders with
// real specs below. Runner: Jest (jsdom) + @testing-library/react. GardenerView fetches its own
// data client-side once on mount (no SSR props, no polling) — these specs mock `global.fetch`
// dispatched by URL+method (the personas-page.spec.tsx/catalog-purge-unavailable.spec.tsx
// convention) and render `<GardenerView />` directly, wrapped in ConfirmDialogProvider + Toaster
// (Keep this one and Dismiss both need the confirm dialog; every verb toasts on failure).
//
// T378 review MED-5 — one assertion per `it`: the five-sections-in-order, Keep-this-one, and nav
// scenarios are each split across several `it`s rather than packed into one with several `expect`s.
//
// T381's own file-action dry-run/confirm scenarios (SPEC F154.5, STORY-379) stay PENDING at the
// bottom of this file — that surface does not exist yet (F154, a later task per the spec's own
// "the page mints no new mutation beyond dismiss (and F154, a later task)" line).

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { GardenerView } from "../app/(authed)/gardener/GardenerView";
import type { GardenerFindingDto, GardenerFindingsResponse } from "@/lib/gardener-api";
import type { StatusResponse } from "@/lib/broadcast-api";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function media(overrides: Partial<GardenerFindingDto["media"]> = {}): GardenerFindingDto["media"] {
  return {
    path: "/media/track.flac",
    title: "Track",
    artist: "Artist",
    durationMs: 200000,
    plays: 3,
    rating: 60,
    neverPlay: false,
    eligible: true,
    ...overrides,
  };
}

const DEAD_FINDING: GardenerFindingDto = {
  id: 101,
  mediaId: 1001,
  state: "open",
  evidence: { reason: "failed", since: "2026-08-01T00:00:00Z" },
  openedAt: "2026-08-01T00:00:00Z",
  resolvedAt: null,
  dismissedAt: null,
  media: media({ path: "/media/dead-track.flac", title: "Dead Track", artist: "Artist D" }),
};

const DUP_A: GardenerFindingDto = {
  id: 201,
  mediaId: 2001,
  state: "open",
  evidence: {
    group_key: "grp-1",
    title_variant: null,
    siblings: [
      { media_id: 2002, duration_ms: 200000 },
      { media_id: 2003, duration_ms: 201000 },
    ],
    versions: [],
  },
  openedAt: "2026-08-02T00:00:00Z",
  resolvedAt: null,
  dismissedAt: null,
  media: media({ path: "/media/dup-a.flac", title: "Song X", artist: "Artist Y", rating: 70 }),
};

const DUP_B: GardenerFindingDto = {
  ...DUP_A,
  id: 202,
  mediaId: 2002,
  media: media({ path: "/media/dup-b.flac", title: "Song X (Live)", artist: "Artist Y", rating: null }),
};

const DUP_C: GardenerFindingDto = {
  ...DUP_A,
  id: 203,
  mediaId: 2003,
  media: media({ path: "/media/dup-c.flac", title: "Song X (Demo)", artist: "Artist Y", rating: null }),
};

const STALE_FINDING: GardenerFindingDto = {
  id: 301,
  mediaId: 3001,
  state: "open",
  evidence: { fields: ["artist", "title"] },
  openedAt: "2026-08-03T00:00:00Z",
  resolvedAt: null,
  dismissedAt: null,
  media: media({ path: "/media/stale.flac", title: null, artist: null, rating: null }),
};

const SHELF_DUST_FINDING: GardenerFindingDto = {
  id: 401,
  mediaId: 4001,
  state: "open",
  evidence: { discovered_at: "2026-01-01T00:00:00Z", days_on_shelf: 240 },
  openedAt: "2026-01-01T00:00:00Z",
  resolvedAt: null,
  dismissedAt: null,
  media: media({ path: "/media/shelf.flac", title: "Shelf Track", artist: "Artist S", plays: 0, rating: null }),
};

/** Groups deliberately NOT in `GARDENER_KIND_ORDER` order — proves the page renders its own fixed
 * section order rather than whatever order the response happened to list groups in. `unreachable`
 * carries no findings at all (the empty-kind Scenario). Only ONE `stale_metadata` row ships even
 * though `status.gardener.open.staleMetadata` below says 5 (the "Showing first N of M" Scenario). */
const FINDINGS_FIXTURE: GardenerFindingsResponse = {
  groups: [
    { kind: "shelf_dust", findings: [SHELF_DUST_FINDING], duplicateGroups: [] },
    {
      kind: "near_duplicate",
      findings: [DUP_A, DUP_B, DUP_C],
      duplicateGroups: [{ groupKey: "grp-1", members: [DUP_A, DUP_B, DUP_C] }],
    },
    { kind: "unreachable", findings: [], duplicateGroups: [] },
    { kind: "dead_file", findings: [DEAD_FINDING], duplicateGroups: [] },
    { kind: "stale_metadata", findings: [STALE_FINDING], duplicateGroups: [] },
  ],
};

function statusFixture(): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    voice: { engine: "kokoro", degraded: false, reason: null, checkedAt: null },
    gardener: {
      open: { deadFile: 1, nearDuplicate: 3, staleMetadata: 5, unreachable: 0, shelfDust: 1 },
      total: 10,
    },
  };
}

// ---------------------------------------------------------------------------
// Fetch mock
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

function toResponse(spec: RouteResponseSpec): Response {
  return {
    ok: spec.status >= 200 && spec.status < 300,
    status: spec.status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

function makeFetchMock(): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = init?.method ?? "GET";
    const url = String(input);

    if (method === "GET" && url.includes("/api/gardener/findings")) {
      return toResponse({ status: 200, body: FINDINGS_FIXTURE });
    }
    if (method === "GET" && url.includes("/api/status")) {
      return toResponse({ status: 200, body: statusFixture() });
    }
    if (method === "POST" && url === "/api/media/eligibility") {
      return toResponse({ status: 200, body: { affected: 2 } });
    }
    if (method === "POST" && /\/api\/gardener\/findings\/\d+\/dismiss/.test(url)) {
      return toResponse({ status: 204 });
    }
    if (method === "PUT" && /\/api\/media\/\d+\/never-play/.test(url)) {
      return toResponse({ status: 200, body: { neverPlay: true } });
    }
    if (method === "POST" && /\/api\/media\/\d+\/reenrich/.test(url)) {
      return toResponse({ status: 202 });
    }
    throw new Error(`unexpected fetch call: ${method} ${url}`);
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function requestBody(mockFetch: jest.MockedFunction<typeof fetch>, callIndex: number): unknown {
  const call = mockFetch.mock.calls[callIndex] as unknown as [string, RequestInit];
  return JSON.parse(String(call[1].body));
}

function findCallIndex(
  mockFetch: jest.MockedFunction<typeof fetch>,
  method: string,
  urlPredicate: (url: string) => boolean
): number {
  return mockFetch.mock.calls.findIndex(
    ([url, init]) => urlPredicate(String(url)) && ((init as RequestInit | undefined)?.method ?? "GET") === method
  );
}

function renderPage(): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      <GardenerView />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function sectionHeadings(): string[] {
  return screen.getAllByRole("heading", { level: 2 }).map((heading) => heading.textContent ?? "");
}

// ---------------------------------------------------------------------------
// Feature: the Gardener page
// ---------------------------------------------------------------------------

describe("Feature: the Gardener page (SPEC F153.10)", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: five sections in the fixed order (dead_file, near_duplicate, stale_metadata, unreachable, shelf_dust)", () => {
    it("renders exactly five sections", async () => {
      makeFetchMock();
      renderPage();

      await waitFor(() => expect(sectionHeadings().length).toBe(5));
    });

    it("renders Dead files first", async () => {
      makeFetchMock();
      renderPage();

      await waitFor(() => expect(sectionHeadings().length).toBe(5));
      expect(sectionHeadings()[0]).toContain("Dead files");
    });

    it("renders Near duplicates second", async () => {
      makeFetchMock();
      renderPage();

      await waitFor(() => expect(sectionHeadings().length).toBe(5));
      expect(sectionHeadings()[1]).toContain("Near duplicates");
    });

    it("renders Stale metadata third", async () => {
      makeFetchMock();
      renderPage();

      await waitFor(() => expect(sectionHeadings().length).toBe(5));
      expect(sectionHeadings()[2]).toContain("Stale metadata");
    });

    it("renders Unreachable fourth", async () => {
      makeFetchMock();
      renderPage();

      await waitFor(() => expect(sectionHeadings().length).toBe(5));
      expect(sectionHeadings()[3]).toContain("Unreachable");
    });

    it("renders Shelf dust fifth", async () => {
      makeFetchMock();
      renderPage();

      await waitFor(() => expect(sectionHeadings().length).toBe(5));
      expect(sectionHeadings()[4]).toContain("Shelf dust");
    });
  });

  describe("Scenario: an empty kind", () => {
    it("shows a one-line, kind-named empty state for the kind with no findings", async () => {
      makeFetchMock();
      renderPage();

      const section = await screen.findByRole("region", { name: "Unreachable" });
      expect(within(section).getByText("Nothing unreachable.")).toBeInTheDocument();
    });
  });

  describe("Scenario: the flat-paging caveat", () => {
    it('shows "Showing first N of M" when the page has fewer rows for a kind than the status total', async () => {
      makeFetchMock();
      renderPage();

      const section = await screen.findByRole("region", { name: "Stale metadata" });
      expect(within(section).getByText("Showing first 1 of 5")).toBeInTheDocument();
    });
  });

  describe("Scenario: the eligibility control (T378 review BLOCK-2/BLOCK-1)", () => {
    it("posts eligible:false with only this row's own id when toggled off", async () => {
      const mockFetch = makeFetchMock();
      renderPage();

      const section = await screen.findByRole("region", { name: "Dead files" });
      const checkbox = within(section).getByRole("checkbox", { name: "Eligible" });

      await act(async () => {
        fireEvent.click(checkbox);
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(findCallIndex(mockFetch, "POST", (url) => url === "/api/media/eligibility")).toBeGreaterThan(-1);
      });
      const callIndex = findCallIndex(mockFetch, "POST", (url) => url === "/api/media/eligibility");
      expect(requestBody(mockFetch, callIndex)).toEqual({ eligible: false, filter: { mediaIds: [1001] } });
    });
  });

  describe("Scenario: the never-play control (reused from catalog/NeverPlayControl.tsx)", () => {
    it("PUTs /api/media/{id}/never-play with neverPlay:true", async () => {
      const mockFetch = makeFetchMock();
      renderPage();

      const section = await screen.findByRole("region", { name: "Dead files" });

      await act(async () => {
        fireEvent.click(within(section).getByRole("button", { name: "Never play" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(findCallIndex(mockFetch, "PUT", (url) => url === "/api/media/1001/never-play")).toBeGreaterThan(-1);
      });
      const callIndex = findCallIndex(mockFetch, "PUT", (url) => url === "/api/media/1001/never-play");
      expect(requestBody(mockFetch, callIndex)).toEqual({ neverPlay: true });
    });
  });

  describe("Scenario: re-enrich", () => {
    it("posts to /api/media/{id}/reenrich", async () => {
      const mockFetch = makeFetchMock();
      renderPage();

      const section = await screen.findByRole("region", { name: "Dead files" });

      await act(async () => {
        fireEvent.click(within(section).getByRole("button", { name: "Re-enrich" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledWith("/api/media/1001/reenrich", expect.objectContaining({ method: "POST" }));
      });
    });
  });

  describe("Scenario: the dead-file section's Purge unavailable trigger", () => {
    it("renders the purge trigger in Dead files and no other section", async () => {
      makeFetchMock();
      renderPage();
      await waitFor(() => expect(sectionHeadings().length).toBe(5));

      const sectionsWithPurgeTrigger = [
        "Dead files",
        "Near duplicates",
        "Stale metadata",
        "Unreachable",
        "Shelf dust",
      ].filter((label) => {
        const section = screen.getByRole("region", { name: label });
        return within(section).queryByRole("button", { name: "Purge unavailable…" }) !== null;
      });

      expect(sectionsWithPurgeTrigger).toEqual(["Dead files"]);
    });
  });

  describe("Scenario: Keep this one on a duplicate group of three (STORY-376 AC6)", () => {
    async function openKeepThisOneDialog(): Promise<HTMLElement> {
      const section = await screen.findByRole("region", { name: "Near duplicates" });
      const rowA = within(section).getByText("Song X").closest("div.py-3") as HTMLElement;

      await act(async () => {
        fireEvent.click(within(rowA).getByRole("button", { name: "Keep this one" }));
        await Promise.resolve();
      });

      return screen.findByRole("dialog");
    }

    it("shows a confirm dialog naming the sibling count", async () => {
      makeFetchMock();
      renderPage();

      const dialog = await openKeepThisOneDialog();
      expect(within(dialog).getByText("Mark 2 siblings ineligible?")).toBeInTheDocument();
    });

    it("does not post before the dialog is confirmed", async () => {
      const mockFetch = makeFetchMock();
      renderPage();

      await openKeepThisOneDialog();
      expect(findCallIndex(mockFetch, "POST", (url) => url === "/api/media/eligibility")).toBe(-1);
    });

    it("posts eligible:false with the OTHER members' ids after confirming", async () => {
      const mockFetch = makeFetchMock();
      renderPage();

      const dialog = await openKeepThisOneDialog();
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Keep this one" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(findCallIndex(mockFetch, "POST", (url) => url === "/api/media/eligibility")).toBeGreaterThan(-1);
      });
      const callIndex = findCallIndex(mockFetch, "POST", (url) => url === "/api/media/eligibility");
      expect(requestBody(mockFetch, callIndex)).toEqual({ eligible: false, filter: { mediaIds: [2002, 2003] } });
    });

    it("re-fetches the findings after success", async () => {
      const mockFetch = makeFetchMock();
      renderPage();

      const dialog = await openKeepThisOneDialog();
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Keep this one" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        const findingsCalls = mockFetch.mock.calls.filter(([url]) => String(url).includes("/api/gardener/findings"));
        expect(findingsCalls.length).toBeGreaterThanOrEqual(2);
      });
    });
  });

  describe("Scenario: dismiss confirms first (SMOKE-1, SPEC F153.2 — dismissed is never reopened)", () => {
    async function openDismissDialog(): Promise<HTMLElement> {
      const section = await screen.findByRole("region", { name: "Dead files" });

      await act(async () => {
        fireEvent.click(within(section).getByRole("button", { name: "Dismiss" }));
        await Promise.resolve();
      });

      return screen.findByRole("dialog");
    }

    it("shows a confirm dialog explaining dismiss is forever", async () => {
      makeFetchMock();
      renderPage();

      const dialog = await openDismissDialog();
      expect(within(dialog).getByText("The gardener will not raise this again for this track.")).toBeInTheDocument();
    });

    it("posts to /api/gardener/findings/{id}/dismiss only after confirming", async () => {
      const mockFetch = makeFetchMock();
      renderPage();

      const dialog = await openDismissDialog();
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Dismiss" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledWith(
          "/api/gardener/findings/101/dismiss",
          expect.objectContaining({ method: "POST" })
        );
      });
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: Gardener page — file actions (SPEC F154.1-F154.5, STORY-379, PLAN T381) — the "Fix…"
// button (excluded for dead_file — see GardenerRow's own remarks) opens FileActionDialog, which
// posts to the two endpoints below. Fixtures mirror the real GardenerFileActionsController wire
// shape (files/GenWave.Host/Api/GardenerFileActionsController.cs, PLAN T381).
// ---------------------------------------------------------------------------

describe("Feature: Gardener page file actions (SPEC F154, STORY-379, PLAN T381)", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  function makeFileActionFetchMock(routes: {
    dryRun?: RouteResponseSpec;
    confirm?: RouteResponseSpec;
  }): jest.MockedFunction<typeof fetch> {
    const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
      const method = init?.method ?? "GET";
      const url = String(input);

      if (method === "GET" && url.includes("/api/gardener/findings")) {
        return toResponse({ status: 200, body: FINDINGS_FIXTURE });
      }
      if (method === "GET" && url.includes("/api/status")) {
        return toResponse({ status: 200, body: statusFixture() });
      }
      if (method === "POST" && url === "/api/gardener/file-actions/dry-run") {
        return toResponse(routes.dryRun ?? { status: 200, body: {} });
      }
      if (method === "POST" && url === "/api/gardener/file-actions/confirm") {
        return toResponse(routes.confirm ?? { status: 200, body: {} });
      }
      throw new Error(`unexpected fetch call: ${method} ${url}`);
    });
    global.fetch = fn as unknown as typeof fetch;
    return fn;
  }

  /** Opens the Fix dialog off the (single-row) Stale metadata section — never near_duplicate, whose
   * rows render through DuplicateGroupCard instead of straight into the section's own row list. */
  async function openFixDialog(): Promise<HTMLElement> {
    const section = await screen.findByRole("region", { name: "Stale metadata" });
    fireEvent.click(within(section).getByRole("button", { name: "Fix…" }));
    return screen.findByRole("dialog", { name: "Fix this file" });
  }

  describe("Scenario: dead_file rows never offer Fix (T381 review N5 — the file is gone)", () => {
    it("the Dead files section renders no Fix button", async () => {
      makeFileActionFetchMock({});
      renderPage();

      const section = await screen.findByRole("region", { name: "Dead files" });

      expect(within(section).queryByRole("button", { name: "Fix…" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the file-action dry-run shows the plan before executing", () => {
    it("choosing a file action on a row renders the returned plan's from and to paths (STORY-379 AC2)", async () => {
      makeFileActionFetchMock({
        dryRun: {
          status: 200,
          body: {
            from: "/media/stale.flac",
            to: "/media/Artist - Title.flac",
            tagDiff: [],
            planToken: "tok-1",
            expiresAt: "2026-01-01T00:10:00Z",
          },
        },
      });
      renderPage();

      const dialog = await openFixDialog();
      fireEvent.click(within(dialog).getByRole("button", { name: "Dry run" }));

      expect(await within(dialog).findByTitle("/media/stale.flac")).toBeInTheDocument();
      expect(await within(dialog).findByTitle("/media/Artist - Title.flac")).toBeInTheDocument();
    });

    it("confirming the plan posts its plan_token to the confirm endpoint (STORY-379 AC3)", async () => {
      const mockFetch = makeFileActionFetchMock({
        dryRun: {
          status: 200,
          body: {
            from: "/media/stale.flac",
            to: "/media/stale.flac",
            tagDiff: [],
            planToken: "tok-confirm-1",
            expiresAt: "2026-01-01T00:10:00Z",
          },
        },
        confirm: { status: 200, body: { outcome: "done", to: "/media/stale.flac" } },
      });
      renderPage();

      const dialog = await openFixDialog();
      fireEvent.click(within(dialog).getByRole("button", { name: "Dry run" }));
      fireEvent.click(await within(dialog).findByRole("button", { name: "Confirm" }));

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledWith(
          "/api/gardener/file-actions/confirm",
          expect.objectContaining({ method: "POST", body: JSON.stringify({ planToken: "tok-confirm-1" }) })
        );
      });
    });
  });

  describe("Scenario: file actions disabled (sad path)", () => {
    it("when the dry-run endpoint 404s, the page shows how to enable Gardener file actions (STORY-379 AC1)", async () => {
      makeFileActionFetchMock({
        dryRun: {
          status: 404,
          body: { detail: "Gardener:FileActions:Enabled is false — set it to true to use this endpoint." },
        },
      });
      renderPage();

      const dialog = await openFixDialog();
      fireEvent.click(within(dialog).getByRole("button", { name: "Dry run" }));

      expect(await within(dialog).findByText(/Gardener__FileActions__Enabled=true/)).toBeInTheDocument();
    });
  });

  describe("Scenario: a refusal shows the rule's own message (sad path)", () => {
    // T381 review N3: Detail is the capitalised operator sentence ALONE (no snake_case rule
    // prefix — Dean's copy rule); the machine token travels on its own `rule` extension member
    // instead (GardenerFileActionsController.RefusalProblem's own remarks) — this fixture pins
    // the ACTUAL wire shape, not the old string-concatenated one.
    it("a refusal from dry-run shows the rule's message as the dialog's error line", async () => {
      makeFileActionFetchMock({
        dryRun: {
          status: 400,
          body: {
            detail: "The catalog and the file's own tags already agree — there is nothing to retag.",
            rule: "nothing_to_retag",
          },
        },
      });
      renderPage();

      const dialog = await openFixDialog();
      fireEvent.click(within(dialog).getByRole("button", { name: "Dry run" }));

      expect(
        await within(dialog).findByText(
          "The catalog and the file's own tags already agree — there is nothing to retag."
        )
      ).toBeInTheDocument();
    });

    it("the refusal message never carries the snake_case rule token", async () => {
      makeFileActionFetchMock({
        dryRun: {
          status: 400,
          body: {
            detail: "The catalog and the file's own tags already agree — there is nothing to retag.",
            rule: "nothing_to_retag",
          },
        },
      });
      renderPage();

      const dialog = await openFixDialog();
      fireEvent.click(within(dialog).getByRole("button", { name: "Dry run" }));
      await within(dialog).findByText(
        "The catalog and the file's own tags already agree — there is nothing to retag."
      );

      expect(dialog.textContent ?? "").not.toContain("nothing_to_retag:");
    });
  });
});
