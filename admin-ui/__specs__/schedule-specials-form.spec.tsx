// @jest-environment jsdom
// STORY-317 — Dated specials shadow the grid (F120.3) — form half (PLAN T259, makes real the T-shaped
// scaffold this file used to carry as it.todo entries).
//
// Drives the REAL SpecialsForm with @testing-library/react, a fetch mock dispatched by METHOD+URL
// (mirrors shows-page.spec.tsx's own harness) — useConfirm()/toast need their providers, so every
// render wraps in ConfirmDialogProvider and mounts Toaster (mirrors shows-page.spec.tsx/
// wardrobe-uninstall-pack.spec.tsx's own harness).
//
// PLAN T259's own honesty note: this suite proves the FORM (author/list/edit/delete against the real
// /api/schedule/specials wire shape) — it never touches GenWave.Orchestration.ScheduleResolver, which
// still does not consume this store in production until PLAN T260 (SpecialsController's own class
// remarks). Endpoint validation itself (30-minute steps, range, persona/show existence, the EXCLUDE
// overlap) is proven for real in GenWave.Host.Tests/Specs/Story317_SpecialsApi.cs; this file only
// proves the WIRE mapping — the request this component builds, and how a 201/409/400 response renders.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SpecialsForm } from "../app/(authed)/schedule/SpecialsForm";
import type { SpecialsFormProps } from "../app/(authed)/schedule/SpecialsForm";
import type {
  RosterPersonaDto,
  ScheduleShowOptionDto,
  ScheduleShowsStatus,
  ScheduleSpecialDto,
  ScheduleSpecialsStatus,
} from "../app/(authed)/schedule/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const NOVA: RosterPersonaDto = { id: 1, name: "Nova" };
const REX: RosterPersonaDto = { id: 2, name: "Radio Rex" };
const PERSONAS: RosterPersonaDto[] = [NOVA, REX];

const NIGHT_MOVES: ScheduleShowOptionDto = { id: 1, name: "Night Moves", tagline: "Late-night deep cuts" };
const LOADED_SHOWS: ScheduleShowsStatus = { kind: "loaded", shows: [NIGHT_MOVES] };

const HOLIDAY_SPECIAL: ScheduleSpecialDto = {
  id: 9,
  onDate: "2026-12-24",
  startMinute: 1140,
  endMinute: 1260,
  personaId: NOVA.id,
  genres: ["holiday"],
  energyMin: 0.2,
  energyMax: 0.7,
  showId: NIGHT_MOVES.id,
};
const LOADED_SPECIALS: ScheduleSpecialsStatus = { kind: "loaded", specials: [] };

// ---------------------------------------------------------------------------
// Fetch mock — dispatched by "METHOD url" (mirrors shows-page.spec.tsx's own makeDispatchFetchMock).
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

function findCall(
  mockFetch: jest.MockedFunction<typeof fetch>,
  method: string,
  url: string
): [string, RequestInit] | undefined {
  return mockFetch.mock.calls.find(
    ([callUrl, init]) => String(callUrl) === url && ((init as RequestInit | undefined)?.method ?? "GET") === method
  ) as [string, RequestInit] | undefined;
}

function renderForm(overrides: Partial<SpecialsFormProps> = {}): ReturnType<typeof render> {
  const props: SpecialsFormProps = {
    personas: PERSONAS,
    shows: LOADED_SHOWS,
    specials: LOADED_SPECIALS,
    ...overrides,
  };
  return render(
    <ConfirmDialogProvider>
      <SpecialsForm {...props} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

async function openSection(): Promise<void> {
  fireEvent.click(screen.getByRole("button", { name: "Show" }));
}

async function confirmInDialog(label: string): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name: label }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: The specials form
// ---------------------------------------------------------------------------

describe("Feature: The specials form", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: authoring a dated special", () => {
    it("creates a special with date, span, persona, show, and envelope", async () => {
      const created: ScheduleSpecialDto = {
        id: 10,
        onDate: "2026-12-24",
        startMinute: 1140,
        endMinute: 1260,
        personaId: NOVA.id,
        genres: ["holiday", "jazz"],
        energyMin: 0.2,
        energyMax: 0.7,
        showId: NIGHT_MOVES.id,
      };
      const mockFetch = makeDispatchFetchMock({ "POST /api/schedule/specials": { status: 201, body: created } });
      renderForm();
      await openSection();

      fireEvent.change(screen.getByLabelText("Date"), { target: { value: "2026-12-24" } });
      fireEvent.change(screen.getByLabelText("Start"), { target: { value: "1140" } });
      fireEvent.change(screen.getByLabelText("End"), { target: { value: "1260" } });
      fireEvent.change(screen.getByLabelText("Persona"), { target: { value: String(NOVA.id) } });
      fireEvent.change(screen.getByLabelText("Show"), { target: { value: String(NIGHT_MOVES.id) } });
      fireEvent.change(screen.getByLabelText("Genres (comma-separated, blank = station default)"), {
        target: { value: "holiday, jazz" },
      });
      fireEvent.change(screen.getByLabelText("Energy min"), { target: { value: "0.2" } });
      fireEvent.change(screen.getByLabelText("Energy max"), { target: { value: "0.7" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Create special" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Special created.")).toBeInTheDocument();
      });

      const call = findCall(mockFetch, "POST", "/api/schedule/specials");
      expect(call).toBeDefined();
      const [, init] = call as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body).toEqual({
        onDate: "2026-12-24",
        startMinute: 1140,
        endMinute: 1260,
        personaId: NOVA.id,
        showId: NIGHT_MOVES.id,
        genres: ["holiday", "jazz"],
        energyMin: 0.2,
        energyMax: 0.7,
      });

      // The new row appears in the list, in place, without a page reload.
      expect(screen.getByText("2026-12-24")).toBeInTheDocument();
    });

    it("lists upcoming specials by date with edit/delete", async () => {
      const mockFetch = makeDispatchFetchMock({
        "DELETE /api/schedule/specials/9": { status: 204 },
      });
      renderForm({ specials: { kind: "loaded", specials: [HOLIDAY_SPECIAL] } });
      await openSection();

      // The list names the date, span, persona, and show — scoped to the list itself, since
      // "Nova"/"Night Moves" are ALSO option text inside the form's own persona/show selects above it.
      const list = screen.getByRole("list", { name: "Special list" });
      expect(within(list).getByText("2026-12-24")).toBeInTheDocument();
      expect(within(list).getByText("19:00–21:00")).toBeInTheDocument();
      expect(within(list).getByText(/Nova/)).toBeInTheDocument();
      expect(within(list).getByText(/Night Moves/)).toBeInTheDocument();

      // Edit pre-fills the form from the row.
      fireEvent.click(screen.getByRole("button", { name: "Edit special 2026-12-24" }));
      expect((screen.getByLabelText("Date") as HTMLInputElement).value).toBe("2026-12-24");
      expect((screen.getByLabelText("Start") as HTMLSelectElement).value).toBe("1140");
      expect((screen.getByLabelText("Persona") as HTMLSelectElement).value).toBe(String(NOVA.id));
      expect(screen.getByRole("button", { name: "Save changes" })).toBeInTheDocument();

      // Cancel leaves the row untouched and restores the create form.
      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
      expect(screen.getByRole("button", { name: "Create special" })).toBeInTheDocument();

      // Delete removes it after confirmation.
      fireEvent.click(screen.getByRole("button", { name: "Delete special 2026-12-24" }));
      await confirmInDialog("Delete");

      await waitFor(() => {
        expect(screen.queryByText("2026-12-24")).not.toBeInTheDocument();
      });
      expect(findCall(mockFetch, "DELETE", "/api/schedule/specials/9")).toBeDefined();
    });

    it("an edit deletes the original row then posts the edited one", async () => {
      const edited: ScheduleSpecialDto = { ...HOLIDAY_SPECIAL, startMinute: 1200, endMinute: 1320 };
      const mockFetch = makeDispatchFetchMock({
        "DELETE /api/schedule/specials/9": { status: 204 },
        "POST /api/schedule/specials": { status: 201, body: edited },
      });
      renderForm({ specials: { kind: "loaded", specials: [HOLIDAY_SPECIAL] } });
      await openSection();

      fireEvent.click(screen.getByRole("button", { name: "Edit special 2026-12-24" }));
      fireEvent.change(screen.getByLabelText("Start"), { target: { value: "1200" } });
      fireEvent.change(screen.getByLabelText("End"), { target: { value: "1320" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save changes" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("20:00–22:00")).toBeInTheDocument();
      });
      expect(findCall(mockFetch, "DELETE", "/api/schedule/specials/9")).toBeDefined();
      expect(findCall(mockFetch, "POST", "/api/schedule/specials")).toBeDefined();
    });
  });

  describe("Scenario: rejections surface honestly", () => {
    it("an overlapping span on the same date surfaces the EXCLUDE rejection in place", async () => {
      const detail = "Another special already covers this time range on 2026-12-24.";
      makeDispatchFetchMock({
        "POST /api/schedule/specials": { status: 409, body: { detail } },
      });
      renderForm();
      await openSection();

      fireEvent.change(screen.getByLabelText("Date"), { target: { value: "2026-12-24" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Create special" }));
        await Promise.resolve();
      });

      // The server's own overlap wording — naming the exact date — surfaces verbatim, never a
      // generic "something went wrong" fallback.
      expect(await screen.findByText(detail)).toBeInTheDocument();
      // Nothing was added to the list — the rejection changed nothing locally.
      expect(screen.queryByText("2026-12-24")).not.toBeInTheDocument();
    });

    it("a validation rejection (e.g. an unknown persona/show, or a past date) surfaces its own detail", async () => {
      const detail = "onDate 2026-01-01 is in the past; the earliest allowed date is today (2026-08-15).";
      makeDispatchFetchMock({
        "POST /api/schedule/specials": { status: 400, body: { detail } },
      });
      renderForm();
      await openSection();

      fireEvent.change(screen.getByLabelText("Date"), { target: { value: "2026-01-01" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Create special" }));
        await Promise.resolve();
      });

      expect(await screen.findByText(detail)).toBeInTheDocument();
    });

    it("disables submit and names the reason when end is not after start", async () => {
      renderForm();
      await openSection();

      fireEvent.change(screen.getByLabelText("Date"), { target: { value: "2026-12-24" } });
      fireEvent.change(screen.getByLabelText("Start"), { target: { value: "1200" } });
      fireEvent.change(screen.getByLabelText("End"), { target: { value: "1140" } });

      expect(screen.getByText("End must be after start.")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Create special" })).toBeDisabled();
    });
  });
});
