// @jest-environment jsdom
// STORY-312 — The Shows page (F119.1, F119.3) — implements T244's scaffold (the file used to carry
// 7 it.todo entries; every Scenario/it below is one of them made real).
//
// Drives ShowsClient via @testing-library/react with a fetch mock dispatched BY URL+METHOD (mirrors
// personas-page.spec.tsx's own harness) — no VoiceControl-shaped mount fetch here (ShowsClient issues
// no request on mount, unlike PersonasClient's VoiceControl), so no DEFAULT_ROUTES map is needed.
// useConfirm()/toast need their providers, so every render wraps in ConfirmDialogProvider and mounts
// Toaster (mirrors wardrobe-uninstall-pack.spec.tsx's harness).
//
// Browser acceptance (T92 precedent) covers the real-server round trip and full-page visual sweep;
// this file is the component-level BDD spec.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { ShowsClient } from "../app/(authed)/shows/ShowsClient";
import type { ShowsClientProps } from "../app/(authed)/shows/ShowsClient";
import type { ShowDto } from "../app/(authed)/shows/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const NIGHT_MOVES: ShowDto = {
  id: 1,
  name: "Night Moves",
  slug: "night-moves",
  tagline: "Late-night deep cuts",
  flavor: "moody, sparse, low crowd noise",
  importedFrom: null,
  importedAt: null,
};

const SUNDAY_STATIC: ShowDto = {
  id: 2,
  name: "Sunday Static",
  slug: "sunday-static",
  tagline: "Slow Sunday wind-down",
  flavor: null,
  importedFrom: null,
  importedAt: null,
};

const RETRO_NIGHTS: ShowDto = {
  id: 3,
  name: "Retro Nights",
  slug: "retro-nights",
  tagline: "Old tagline",
  flavor: null,
  importedFrom: "midnight-drive-catalog-entry",
  importedAt: "2026-07-21T09:05:00Z",
};

// ---------------------------------------------------------------------------
// Fetch mock — dispatched by "METHOD url", relative wire calls only (matches how ShowsClient
// issues same-origin fetch()). Mirrors personas-page.spec.tsx's own makeDispatchFetchMock.
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

function renderClient(overrides: Partial<ShowsClientProps> = {}): ReturnType<typeof render> {
  const props: ShowsClientProps = { initialShows: [NIGHT_MOVES, SUNDAY_STATIC], ...overrides };
  return render(
    <ConfirmDialogProvider>
      <ShowsClient {...props} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/** Finds a show's own `<li>` card via its dedicated name testid (mirrors personas-page.spec.tsx's
 * `rowFor` — a raw `getByText(name)` on the card would fail once a provenance chip sits beside it). */
function cardFor(name: string): HTMLElement {
  const nameNode = screen.getByTestId(`show-name-${name}`);
  const card = nameNode.closest("li");
  if (card === null) throw new Error(`No <li> ancestor for "${name}"`);
  return card;
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

async function confirmInDialog(label: string): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name: label }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: The Shows page
// ---------------------------------------------------------------------------

describe("Feature: The Shows page", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: authoring in place", () => {
    it("renders the show list with the provenance line on imported shows", () => {
      makeDispatchFetchMock({});
      renderClient({ initialShows: [NIGHT_MOVES, RETRO_NIGHTS], timeZone: "UTC" });

      // An authored-in-place show carries no provenance line at all.
      expect(within(cardFor("Night Moves")).queryByText(/^Imported/)).not.toBeInTheDocument();

      // An imported show's line reads the literal three-field pattern (SPEC F90.7's own shape,
      // F119.1's own wording): "Imported · <source> · <date>", source rendered verbatim.
      expect(
        within(cardFor("Retro Nights")).getByText("Imported · midnight-drive-catalog-entry · Jul 21, 2026")
      ).toBeInTheDocument();
    });

    it("creates a show with name/tagline/flavor under budget maxlengths (60/120/400)", async () => {
      const created: ShowDto = {
        id: 4,
        name: "Morning Static",
        slug: "morning-static",
        tagline: "First light, low volume",
        flavor: "bright, brief, upbeat",
        importedFrom: null,
        importedAt: null,
      };
      const mockFetch = makeDispatchFetchMock({ "POST /api/shows": { status: 201, body: created } });
      renderClient({ initialShows: [] });

      const nameField = screen.getByLabelText("Name") as HTMLInputElement;
      const taglineField = screen.getByLabelText("Tagline") as HTMLInputElement;
      const flavorField = screen.getByLabelText("Flavor") as HTMLTextAreaElement;

      // The budgets are enforced at the DOM level, not just checked by this spec (SPEC F115.1 — the
      // UI maxlength exists specifically to stop an over-budget round trip before the wire).
      expect(nameField.maxLength).toBe(60);
      expect(taglineField.maxLength).toBe(120);
      expect(flavorField.maxLength).toBe(400);

      fireEvent.change(nameField, { target: { value: "Morning Static" } });
      fireEvent.change(taglineField, { target: { value: "First light, low volume" } });
      fireEvent.change(flavorField, { target: { value: "bright, brief, upbeat" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Create show" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByTestId("show-name-Morning Static")).toBeInTheDocument();
      });

      const call = findCall(mockFetch, "POST", "/api/shows");
      expect(call).toBeDefined();
      const [, init] = call as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body).toEqual({
        name: "Morning Static",
        tagline: "First light, low volume",
        flavor: "bright, brief, upbeat",
      });
    });

    it("edits an authored show and round-trips every field", async () => {
      const updated: ShowDto = {
        ...NIGHT_MOVES,
        name: "Night Moves Revisited",
        slug: "night-moves-revisited",
        tagline: "Revisited",
        flavor: "moodier, sparser",
      };
      const mockFetch = makeDispatchFetchMock({
        "PATCH /api/shows/night-moves": { status: 200, body: updated },
      });
      renderClient({ initialShows: [NIGHT_MOVES, SUNDAY_STATIC] });

      fireEvent.click(within(cardFor("Night Moves")).getByRole("button", { name: "Edit Night Moves" }));

      // The form pre-fills from the row being edited.
      expect((screen.getByLabelText("Name") as HTMLInputElement).value).toBe("Night Moves");
      expect((screen.getByLabelText("Tagline") as HTMLInputElement).value).toBe("Late-night deep cuts");
      expect((screen.getByLabelText("Flavor") as HTMLTextAreaElement).value).toBe(
        "moody, sparse, low crowd noise"
      );

      fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Night Moves Revisited" } });
      fireEvent.change(screen.getByLabelText("Tagline"), { target: { value: "Revisited" } });
      fireEvent.change(screen.getByLabelText("Flavor"), { target: { value: "moodier, sparser" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save changes" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByTestId("show-name-Night Moves Revisited")).toBeInTheDocument();
      });

      const card = cardFor("Night Moves Revisited");
      expect(within(card).getByText("Revisited")).toBeInTheDocument();
      expect(within(card).getByText("moodier, sparser")).toBeInTheDocument();

      // The PATCH targeted the show's slug AS IT WAS when Edit was clicked — a rename mid-edit must
      // never move the address the request is sent to (see ShowsClient's own FormMode remarks).
      expect(findCall(mockFetch, "PATCH", "/api/shows/night-moves")).toBeDefined();
    });

    it("supports several shows referencing the same persona's blocks (one DJ, many shows)", () => {
      // A show carries no persona reference of its own (that's the schedule block's job, PLAN
      // T243) — this page's own structural claim is simply that it renders more than one show at
      // once with no objection to two rows existing side by side.
      makeDispatchFetchMock({});
      renderClient({ initialShows: [NIGHT_MOVES, SUNDAY_STATIC] });

      expect(screen.getByTestId("show-name-Night Moves")).toBeInTheDocument();
      expect(screen.getByTestId("show-name-Sunday Static")).toBeInTheDocument();
      expect(screen.getByRole("list", { name: "Show list" }).children).toHaveLength(2);
    });
  });

  describe("Scenario: guarded delete UX", () => {
    it("surfaces the 409 refusal naming the referencing schedule blocks", async () => {
      const detail = '"night-moves" is still scheduled and cannot be deleted: Mon 09:00–12:00.';
      makeDispatchFetchMock({ "DELETE /api/shows/night-moves": { status: 409, body: { detail } } });
      renderClient({ initialShows: [NIGHT_MOVES, SUNDAY_STATIC] });

      fireEvent.click(within(cardFor("Night Moves")).getByRole("button", { name: "Delete Night Moves" }));
      await confirmInDialog("Delete");

      expect(await screen.findByText(detail)).toBeInTheDocument();
      // Nothing was removed — the refused row is still on the page.
      expect(screen.getByTestId("show-name-Night Moves")).toBeInTheDocument();
    });

    it("deletes an unreferenced show after confirm", async () => {
      const mockFetch = makeDispatchFetchMock({ "DELETE /api/shows/sunday-static": { status: 204 } });
      renderClient({ initialShows: [NIGHT_MOVES, SUNDAY_STATIC] });

      fireEvent.click(within(cardFor("Sunday Static")).getByRole("button", { name: "Delete Sunday Static" }));
      await confirmInDialog("Delete");

      await waitFor(() => {
        expect(screen.queryByTestId("show-name-Sunday Static")).not.toBeInTheDocument();
      });
      expect(await screen.findByText('"Sunday Static" deleted.')).toBeInTheDocument();
      expect(findCall(mockFetch, "DELETE", "/api/shows/sunday-static")).toBeDefined();
    });
  });

  describe("Scenario: coverage stays neutral", () => {
    it("shows no nudge, badge, or warning anywhere for unnamed blocks (F119.3)", () => {
      makeDispatchFetchMock({});
      renderClient({ initialShows: [NIGHT_MOVES, SUNDAY_STATIC] });

      // No coverage-flavored copy anywhere on the page, and no role="alert"/"status" element at
      // all — F119.3 rules this out structurally, not just as a missing string.
      expect(screen.queryByText(/coverage/i)).not.toBeInTheDocument();
      expect(screen.queryByText(/uncovered/i)).not.toBeInTheDocument();
      expect(screen.queryByText(/unnamed block/i)).not.toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
      expect(screen.queryByRole("status")).not.toBeInTheDocument();
    });
  });
});
