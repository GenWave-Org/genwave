// @jest-environment jsdom
// STORY-374 / STORY-376 / STORY-379 — The Gardener page: findings by kind, Keep this one, and the
// file-action dry-run (SPEC F153.9-F153.10, F153.5, F154.5 · PLAN T378, T381)
// STORY-381/382/383 rider (SPEC F153.10 rider 2026-08-31 · PLAN T387 · gh-#654/#655/#657) —
// RECONCILED: the Gardener page is now server-rendered (`gardener/page.tsx`), one tab's own kind at
// a time. `GardenerView` (client LoadState/fetch-on-mount, the gh-#654 defect) and its own findings/
// status fetch mocks retire with it. `GardenerSection` is now the page's own "use client" boundary
// (mirrors `catalog/CatalogTable.tsx`) — every row verb still re-fetches on success, but via
// `router.refresh()` rather than a client-held re-fetch closure, so this file mocks `next/navigation`
// (the `persona-catalog-page.spec.tsx`/`catalog-selection-toolbar.spec.tsx` convention) instead of
// mocking `GET /api/gardener/findings`/`GET /api/status` — `GardenerSection` no longer fetches
// either itself, it only receives them as props. The five-sections-in-fixed-order and "Showing
// first N of M" scenarios retire outright (the tab strip owns kind order now — see
// gardener-tabs.spec.tsx — and the flat-paging caveat is gone per the rider); the dead-file Purge
// trigger's cross-tab visibility likewise moves to gardener-tabs.spec.tsx's own "purge lives on the
// dead-files tab" scenario, which is the page-level surface that claim is actually about. What
// stays here: verb wiring (eligibility/never-play/re-enrich/dismiss/Keep this one), the gh-#655
// purge label, the per-kind empty state, duplicate-group rendering, and T381's own file-action
// dry-run/confirm scenarios — all still true, just exercised through `GardenerSection` directly.
//
// Runner: Jest (jsdom) + @testing-library/react.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, beforeAll, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { GardenerDuplicateGroupDto, GardenerFindingDto, GardenerGroupDto, GardenerKind } from "@/lib/gardener-api";
import type { GardenerSection as GardenerSectionComponent } from "../app/(authed)/gardener/GardenerSection";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;
const mockedRefresh = jest.fn<() => void>();

// `GardenerSection` calls `useRouter()` unconditionally, so this module must be `import()`ed AFTER
// `jest.mock("next/navigation", ...)` has registered — a static top-level `import` here would bind
// the REAL `next/navigation` export before the mock factory above ever runs (this project's
// SWC-based jest transform does not hoist `jest.mock` past a static import), the same reason
// `persona-catalog-page.spec.tsx`'s own harness does this too.
let GardenerSection: typeof GardenerSectionComponent;

beforeAll(async () => {
  ({ GardenerSection } = await import("../app/(authed)/gardener/GardenerSection"));
});

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

function groupOf(
  kind: GardenerKind,
  findings: GardenerFindingDto[],
  duplicateGroups: GardenerDuplicateGroupDto[] = []
): GardenerGroupDto {
  return { kind, findings, duplicateGroups };
}

// ---------------------------------------------------------------------------
// Fetch mock — only the WRITE endpoints. GardenerSection is fully props-driven now (page.tsx does
// every GET server-side), so unlike the old GardenerView-era mock, there is no findings/status GET
// to intercept — an unexpected GET here would mean a verb regressed back to client-side fetching.
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

function renderSection(
  kind: GardenerKind,
  group: GardenerGroupDto,
  options: { openCount?: number | null; total?: number } = {}
): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      <GardenerSection
        kind={kind}
        group={group}
        openCount={options.openCount ?? null}
        total={options.total ?? group.findings.length}
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

// ---------------------------------------------------------------------------
// Feature: the Gardener page's own section (SPEC F153.10)
// ---------------------------------------------------------------------------

describe("Feature: the Gardener section (SPEC F153.10 rider 2026-08-31)", () => {
  beforeEach(() => {
    mockedRefresh.mockClear();
    mockedUseRouter.mockReturnValue({ refresh: mockedRefresh } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe("Scenario: an empty kind", () => {
    it("shows a one-line, kind-named empty state for the kind with no findings", () => {
      renderSection("unreachable", groupOf("unreachable", []));

      const section = screen.getByRole("region", { name: "Unreachable" });
      expect(within(section).getByText("Nothing unreachable.")).toBeInTheDocument();
    });
  });

  describe("Scenario: the eligibility control (T378 review BLOCK-2/BLOCK-1)", () => {
    it("posts eligible:false with only this row's own id when toggled off", async () => {
      const mockFetch = makeFetchMock();
      renderSection("dead_file", groupOf("dead_file", [DEAD_FINDING]), { total: 1 });

      const section = screen.getByRole("region", { name: "Dead files" });
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

    it("calls router.refresh after a successful toggle", async () => {
      makeFetchMock();
      renderSection("dead_file", groupOf("dead_file", [DEAD_FINDING]), { total: 1 });

      const section = screen.getByRole("region", { name: "Dead files" });
      const checkbox = within(section).getByRole("checkbox", { name: "Eligible" });

      await act(async () => {
        fireEvent.click(checkbox);
        await Promise.resolve();
      });

      await waitFor(() => expect(mockedRefresh).toHaveBeenCalled());
    });
  });

  describe("Scenario: the never-play control (reused from catalog/NeverPlayControl.tsx)", () => {
    it("PUTs /api/media/{id}/never-play with neverPlay:true", async () => {
      const mockFetch = makeFetchMock();
      renderSection("dead_file", groupOf("dead_file", [DEAD_FINDING]), { total: 1 });

      const section = screen.getByRole("region", { name: "Dead files" });

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
      renderSection("dead_file", groupOf("dead_file", [DEAD_FINDING]), { total: 1 });

      const section = screen.getByRole("region", { name: "Dead files" });

      await act(async () => {
        fireEvent.click(within(section).getByRole("button", { name: "Re-enrich" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledWith("/api/media/1001/reenrich", expect.objectContaining({ method: "POST" }));
      });
    });
  });

  describe("Scenario: the dead-file Purge trigger (gh-#655 verb-object label)", () => {
    it('reads "Purge dead tracks…", never a status reading', () => {
      renderSection("dead_file", groupOf("dead_file", [DEAD_FINDING]), { total: 1 });

      const section = screen.getByRole("region", { name: "Dead files" });
      expect(within(section).getByRole("button", { name: "Purge dead tracks…" })).toBeInTheDocument();
    });

    it("renders no purge trigger once the kind's own total reaches zero", () => {
      renderSection("dead_file", groupOf("dead_file", []), { total: 0 });

      const section = screen.getByRole("region", { name: "Dead files" });
      expect(within(section).queryByRole("button", { name: /purge/i })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: Keep this one on a duplicate group of three (STORY-376 AC6, STORY-383 AC4)", () => {
    // near_duplicate's own total is GROUP-scoped (STORY-382 AC6/AC8: 1 group here, even though
    // the group holds 3 rows) — the header's ROW-scoped fallback openCount (3, from status) is
    // supplied too, deliberately different, per this task's own "both true at once" rider.
    function renderDuplicateGroup(): ReturnType<typeof render> {
      return renderSection(
        "near_duplicate",
        groupOf("near_duplicate", [DUP_A, DUP_B, DUP_C], [{ groupKey: "grp-1", members: [DUP_A, DUP_B, DUP_C] }]),
        { openCount: 3, total: 1 }
      );
    }

    async function openKeepThisOneDialog(): Promise<HTMLElement> {
      const section = screen.getByRole("region", { name: "Near duplicates" });
      const rowA = within(section).getByText("Song X").closest("div.py-3") as HTMLElement;

      await act(async () => {
        fireEvent.click(within(rowA).getByRole("button", { name: "Keep this one" }));
        await Promise.resolve();
      });

      return screen.findByRole("dialog");
    }

    it("renders all three members of the group in one card", () => {
      renderDuplicateGroup();

      const section = screen.getByRole("region", { name: "Near duplicates" });
      expect(within(section).getAllByText("Song X", { exact: false })).toHaveLength(3);
    });

    it("shows a confirm dialog naming the sibling count", async () => {
      makeFetchMock();
      renderDuplicateGroup();

      const dialog = await openKeepThisOneDialog();
      expect(within(dialog).getByText("Mark 2 siblings ineligible?")).toBeInTheDocument();
    });

    it("does not post before the dialog is confirmed", async () => {
      const mockFetch = makeFetchMock();
      renderDuplicateGroup();

      await openKeepThisOneDialog();
      expect(findCallIndex(mockFetch, "POST", (url) => url === "/api/media/eligibility")).toBe(-1);
    });

    it("posts eligible:false with the OTHER members' ids after confirming", async () => {
      const mockFetch = makeFetchMock();
      renderDuplicateGroup();

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

    it("calls router.refresh after success", async () => {
      makeFetchMock();
      renderDuplicateGroup();

      const dialog = await openKeepThisOneDialog();
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Keep this one" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockedRefresh).toHaveBeenCalled());
    });
  });

  describe("Scenario: dismiss confirms first (SMOKE-1, SPEC F153.2 — dismissed is never reopened)", () => {
    async function openDismissDialog(): Promise<HTMLElement> {
      const section = screen.getByRole("region", { name: "Dead files" });

      await act(async () => {
        fireEvent.click(within(section).getByRole("button", { name: "Dismiss" }));
        await Promise.resolve();
      });

      return screen.findByRole("dialog");
    }

    it("shows a confirm dialog explaining dismiss is forever", async () => {
      makeFetchMock();
      renderSection("dead_file", groupOf("dead_file", [DEAD_FINDING]), { total: 1 });

      const dialog = await openDismissDialog();
      expect(within(dialog).getByText("The gardener will not raise this again for this track.")).toBeInTheDocument();
    });

    it("posts to /api/gardener/findings/{id}/dismiss only after confirming", async () => {
      const mockFetch = makeFetchMock();
      renderSection("dead_file", groupOf("dead_file", [DEAD_FINDING]), { total: 1 });

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
// shape (src/GenWave.Host/Api/GardenerFileActionsController.cs, PLAN T381). Unaffected by T387's
// own rider — still exercised through GardenerSection directly.
// ---------------------------------------------------------------------------

describe("Feature: Gardener page file actions (SPEC F154, STORY-379, PLAN T381)", () => {
  beforeEach(() => {
    mockedRefresh.mockClear();
    mockedUseRouter.mockReturnValue({ refresh: mockedRefresh } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  function makeFileActionFetchMock(routes: { dryRun?: RouteResponseSpec; confirm?: RouteResponseSpec }): jest.MockedFunction<typeof fetch> {
    const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
      const method = init?.method ?? "GET";
      const url = String(input);

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
    const section = screen.getByRole("region", { name: "Stale metadata" });
    fireEvent.click(within(section).getByRole("button", { name: "Fix…" }));
    return screen.findByRole("dialog", { name: "Fix this file" });
  }

  describe("Scenario: dead_file rows never offer Fix (T381 review N5 — the file is gone)", () => {
    it("the Dead files section renders no Fix button", () => {
      makeFileActionFetchMock({});
      renderSection("dead_file", groupOf("dead_file", [DEAD_FINDING]), { total: 1 });

      const section = screen.getByRole("region", { name: "Dead files" });

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
      renderSection("stale_metadata", groupOf("stale_metadata", [STALE_FINDING]), { total: 1 });

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
      renderSection("stale_metadata", groupOf("stale_metadata", [STALE_FINDING]), { total: 1 });

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
      renderSection("stale_metadata", groupOf("stale_metadata", [STALE_FINDING]), { total: 1 });

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
      renderSection("stale_metadata", groupOf("stale_metadata", [STALE_FINDING]), { total: 1 });

      const dialog = await openFixDialog();
      fireEvent.click(within(dialog).getByRole("button", { name: "Dry run" }));

      expect(
        await within(dialog).findByText("The catalog and the file's own tags already agree — there is nothing to retag.")
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
      renderSection("stale_metadata", groupOf("stale_metadata", [STALE_FINDING]), { total: 1 });

      const dialog = await openFixDialog();
      fireEvent.click(within(dialog).getByRole("button", { name: "Dry run" }));
      await within(dialog).findByText("The catalog and the file's own tags already agree — there is nothing to retag.");

      expect(dialog.textContent ?? "").not.toContain("nothing_to_retag:");
    });
  });
});
