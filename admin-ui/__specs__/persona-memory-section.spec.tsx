// @jest-environment jsdom
// STORY-540 — I can see and shape a DJ's memory (gh-#293 gh-#920 · SPEC F218 · PLAN T707)
//
// BDD specification — Jest. AC1/AC2/AC5 are xUnit in tests/GenWave.Host.Tests/Specs/Story540_ICanSeeAndShapeADjsMemory.cs.
// Drives the real PersonasClient with a fetch mock dispatched by "METHOD url".

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonaMemorySection } from "../app/(authed)/personas/PersonaMemorySection";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";
import type { PersonaMemoryRow } from "../lib/persona-memory-api";

const REX: PersonaDto = {
  id: 1,
  name: "Radio Rex",
  backstory: "A grizzled late-night jock.",
  style: "Warm, gravelly, brief.",
  voice: "af_alloy",
  slug: "radio-rex",
  importedFrom: null,
  importedAt: null,
  soul: "",
  quirks: [],
  lore: [],
};

function memoryRow(overrides: Partial<PersonaMemoryRow> & Pick<PersonaMemoryRow, "id" | "kind" | "text">): PersonaMemoryRow {
  return {
    source: "authored",
    airedCount: 0,
    lastAiredAt: null,
    createdAt: "2026-11-01T10:00:00Z",
    approvedAt: "2026-11-01T10:00:00Z",
    forgottenAt: null,
    pending: false,
    forgotten: false,
    ...overrides,
  };
}

const LORE_AUTHORED = memoryRow({ id: 10, kind: "lore", text: "Grew up above a record shop." });
const LORE_PENDING = memoryRow({
  id: 11,
  kind: "lore",
  source: "drafted",
  text: "Once met a famous drummer.",
  approvedAt: null,
  pending: true,
});
const LORE_FORGOTTEN = memoryRow({
  id: 12,
  kind: "lore",
  text: "Hates Mondays.",
  forgottenAt: "2026-11-05T10:00:00Z",
  forgotten: true,
});
const EPISODE = memoryRow({ id: 20, kind: "episode", source: "accrued", text: "Talked about the rain." });
const LIFE = memoryRow({ id: 30, kind: "life", source: "extracted", text: "Has a cat called Biscuit.", airedCount: 2 });
const SCRIPT_TEXT = memoryRow({ id: 31, kind: "life", source: "extracted", text: "<b>bold</b> <img src=x onerror=alert(1)>" });

const ALL_ROWS = [LORE_AUTHORED, LORE_PENDING, LORE_FORGOTTEN, EPISODE, LIFE, SCRIPT_TEXT];

interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

function makeFetchMock(routes: Record<string, RouteResponseSpec>): jest.MockedFunction<typeof fetch> {
  const all: Record<string, RouteResponseSpec> = {
    "GET /api/voices": { status: 200, body: [] },
    "GET /api/personas/1/memory": { status: 200, body: ALL_ROWS },
    ...routes,
  };
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const spec = all[`${(init?.method ?? "GET").toUpperCase()} ${String(input)}`] ?? { status: 200, body: {} };
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

function callsTo(mockFetch: jest.MockedFunction<typeof fetch>, method: string, url: string): [RequestInfo | URL, RequestInit?][] {
  return mockFetch.mock.calls.filter(([input, init]) => String(input) === url && (init?.method ?? "GET") === method);
}

async function openMemory(lifeExtractionOff = false): Promise<HTMLElement> {
  render(
    <ConfirmDialogProvider>
      <PersonasClient initialPersonas={[REX]} lifeExtractionOff={lifeExtractionOff} />
      <Toaster />
    </ConfirmDialogProvider>
  );
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "Show memory for Radio Rex" }));
    await Promise.resolve();
  });
  return screen.findByRole("region", { name: "Memory for Radio Rex" });
}

async function click(button: HTMLElement): Promise<void> {
  await act(async () => {
    fireEvent.click(button);
    await Promise.resolve();
  });
}

async function openTab(region: HTMLElement, name: string): Promise<void> {
  await click(within(region).getByRole("button", { name }));
}

describe("Feature: I can see and shape a DJ's memory", () => {
  let originalFetch: typeof fetch;
  beforeEach(() => {
    originalFetch = global.fetch;
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  describe("AC3 — the section renders", () => {
    it("a Memory section shows three tabs Lore, Lately, Life with their rows", async () => {
      makeFetchMock({});
      const region = await openMemory();

      const kinds = within(region).getByRole("group", { name: "Memory kind" });
      expect(within(kinds).getAllByRole("button").map((t) => t.textContent)).toEqual(["Lore", "Lately", "Life"]);
      expect(within(kinds).getByRole("button", { name: "Lore" })).toHaveAttribute("aria-pressed", "true");
      expect(within(kinds).getByRole("button", { name: "Lately" })).toHaveAttribute("aria-pressed", "false");
      expect(within(region).getByText("Grew up above a record shop.")).toBeInTheDocument();
      expect(within(region).queryByText("Talked about the rain.")).not.toBeInTheDocument();

      await openTab(region, "Lately");
      expect(within(kinds).getByRole("button", { name: "Lately" })).toHaveAttribute("aria-pressed", "true");
      expect(within(kinds).getByRole("button", { name: "Lore" })).toHaveAttribute("aria-pressed", "false");
      expect(within(region).getByText("Talked about the rain.")).toBeInTheDocument();

      await openTab(region, "Life");
      expect(within(region).getByText("Has a cat called Biscuit.")).toBeInTheDocument();
    });

    it("a row's status says how often it was offered to the model, not aired (T721)", async () => {
      const never = memoryRow({ id: 40, kind: "lore", text: "Never offered line." });
      const once = memoryRow({ id: 41, kind: "lore", text: "Once line.", airedCount: 1 });
      const many = memoryRow({ id: 42, kind: "lore", text: "Many line.", airedCount: 3, lastAiredAt: "2026-11-10T10:00:00Z" });
      makeFetchMock({ "GET /api/personas/1/memory": { status: 200, body: [never, once, many] } });
      const region = await openMemory();

      expect(within(within(region).getByTestId("memory-row-40")).getByText("Not offered yet")).toBeInTheDocument();
      expect(within(within(region).getByTestId("memory-row-41")).getByText("Offered once")).toBeInTheDocument();
      expect(within(within(region).getByTestId("memory-row-42")).getByText(/^Offered 3 times, last /)).toBeInTheDocument();
      expect(within(region).queryByText(/aired/i)).not.toBeInTheDocument();
    });

    it("Lately explains itself and shows episode text starting with a capital", async () => {
      const hosted = memoryRow({ id: 21, kind: "episode", source: "accrued", text: "hosted a shift on Sunday" });
      makeFetchMock({ "GET /api/personas/1/memory": { status: 200, body: [hosted, LORE_AUTHORED] } });
      const region = await openMemory();

      await openTab(region, "Lately");
      expect(
        within(region).getByText("What this DJ did on the station lately. They can refer back to it.")
      ).toBeInTheDocument();
      expect(within(within(region).getByTestId("memory-row-21")).getByText("Hosted a shift on Sunday")).toBeInTheDocument();

      await click(within(within(region).getByTestId("memory-row-21")).getByRole("button", { name: "Edit" }));
      expect(within(region).getByRole("textbox", { name: "Memory text" })).toHaveValue("hosted a shift on Sunday");
    });

    it("each open section's Add label points at its own textbox", async () => {
      makeFetchMock({ "GET /api/personas/2/memory": { status: 200, body: [] } });
      render(
        <ConfirmDialogProvider>
          <PersonaMemorySection personaId={1} personaName="Radio Rex" />
          <PersonaMemorySection personaId={2} personaName="Other DJ" />
        </ConfirmDialogProvider>
      );
      const first = await screen.findByRole("region", { name: "Memory for Radio Rex" });
      const second = await screen.findByRole("region", { name: "Memory for Other DJ" });
      const firstBox = await within(first).findByLabelText("Add a line of lore");
      const secondBox = await within(second).findByLabelText("Add a line of lore");
      expect(firstBox).not.toBe(secondBox);
      expect(first.contains(firstBox)).toBe(true);
      expect(second.contains(secondBox)).toBe(true);
      expect(firstBox.id).not.toBe(secondBox.id);
    });

    it("lists the memory lazily: only once the section is opened", async () => {
      const mockFetch = makeFetchMock({});
      render(
        <ConfirmDialogProvider>
          <PersonasClient initialPersonas={[REX]} />
        </ConfirmDialogProvider>
      );
      expect(callsTo(mockFetch, "GET", "/api/personas/1/memory")).toHaveLength(0);
    });

    it("pending rows are flagged", async () => {
      makeFetchMock({});
      const region = await openMemory();

      const pending = within(region).getByTestId("memory-row-11");
      expect(within(pending).getByText("Waiting for your OK")).toBeInTheDocument();
      expect(within(pending).getByText("Suggested")).toBeInTheDocument();
      expect(within(within(region).getByTestId("memory-row-10")).queryByText("Waiting for your OK")).not.toBeInTheDocument();
    });

    it('forgotten rows are hidden until "show forgotten"', async () => {
      makeFetchMock({});
      const region = await openMemory();
      expect(within(region).queryByText("Hates Mondays.")).not.toBeInTheDocument();

      await click(within(region).getByRole("checkbox", { name: /Show forgotten/ }));
      expect(within(region).getByText("Hates Mondays.")).toBeInTheDocument();
      expect(within(within(region).getByTestId("memory-row-12")).getByRole("button", { name: "Remember again" })).toBeInTheDocument();
    });

    it("renders memory text as text, never as markup", async () => {
      makeFetchMock({});
      const region = await openMemory();
      await openTab(region, "Life");

      expect(within(region).getByText("<b>bold</b> <img src=x onerror=alert(1)>")).toBeInTheDocument();
      expect(region.querySelector("img")).toBeNull();
      expect(region.querySelector("b")).toBeNull();
    });

    it("offers Suggest lore and Add on the Lore tab only", async () => {
      makeFetchMock({});
      const region = await openMemory();
      expect(within(region).getByRole("button", { name: "Suggest lore" })).toBeInTheDocument();
      expect(within(region).getByRole("button", { name: "Add" })).toBeInTheDocument();

      await openTab(region, "Life");
      expect(within(region).queryByRole("button", { name: "Suggest lore" })).not.toBeInTheDocument();
      expect(within(region).queryByRole("button", { name: "Add" })).not.toBeInTheDocument();
    });

    it("states an empty tab in plain words", async () => {
      makeFetchMock({ "GET /api/personas/1/memory": { status: 200, body: [] } });
      const region = await openMemory();
      expect(within(region).getByText("No lore yet. Add a line, or ask for suggestions.")).toBeInTheDocument();
    });
  });

  describe("T720 — empty states", () => {
    const SWITCHED_OFF = "Picking up life details is switched off.";
    const NORMAL_EMPTY = "Nothing here yet. This fills up as the DJ talks on air.";

    it("Life empty with life extraction off says it is switched off", async () => {
      makeFetchMock({ "GET /api/personas/1/memory": { status: 200, body: [] } });
      const region = await openMemory(true);
      await openTab(region, "Life");
      expect(within(region).getByText(SWITCHED_OFF)).toBeInTheDocument();
    });

    it("Life empty with life extraction on shows the normal empty text", async () => {
      makeFetchMock({ "GET /api/personas/1/memory": { status: 200, body: [] } });
      const region = await openMemory(false);
      await openTab(region, "Life");
      expect(within(region).queryByText(SWITCHED_OFF)).not.toBeInTheDocument();
      expect(within(region).getByText(NORMAL_EMPTY)).toBeInTheDocument();
    });

    it("Lately empty says it fills as the DJ hosts shifts and hands over", async () => {
      makeFetchMock({ "GET /api/personas/1/memory": { status: 200, body: [] } });
      const region = await openMemory();
      await openTab(region, "Lately");
      expect(
        within(region).getByText("Nothing here yet. This fills up as the DJ hosts shifts and hands over to other DJs.")
      ).toBeInTheDocument();
    });
  });

  describe("AC4 — buttons fire", () => {
    it("PATCH forgotten is sent for that row", async () => {
      const forgotten = { ...LIFE, forgotten: true, forgottenAt: "2026-11-15T10:00:00Z" };
      const mockFetch = makeFetchMock({ "PATCH /api/personas/1/memory/30": { status: 200, body: forgotten } });
      const region = await openMemory();
      await openTab(region, "Life");

      await click(within(within(region).getByTestId("memory-row-30")).getByRole("button", { name: "Forget" }));

      const [call] = callsTo(mockFetch, "PATCH", "/api/personas/1/memory/30");
      expect(JSON.parse(String(call?.[1]?.body))).toEqual({ forgotten: true });
      expect(within(region).queryByText("Has a cat called Biscuit.")).not.toBeInTheDocument();
    });

    it("Remember again sends forgotten false and shows the row", async () => {
      const back = { ...LORE_FORGOTTEN, forgotten: false, forgottenAt: null };
      const mockFetch = makeFetchMock({ "PATCH /api/personas/1/memory/12": { status: 200, body: back } });
      const region = await openMemory();
      await click(within(region).getByRole("checkbox", { name: /Show forgotten/ }));

      await click(within(within(region).getByTestId("memory-row-12")).getByRole("button", { name: "Remember again" }));

      const [call] = callsTo(mockFetch, "PATCH", "/api/personas/1/memory/12");
      expect(JSON.parse(String(call?.[1]?.body))).toEqual({ forgotten: false });
      expect(within(within(region).getByTestId("memory-row-12")).getByRole("button", { name: "Forget" })).toBeInTheDocument();
    });

    it("Approve sends approved true and clears the pending flag", async () => {
      const approved = { ...LORE_PENDING, pending: false, approvedAt: "2026-11-15T10:00:00Z" };
      const mockFetch = makeFetchMock({ "PATCH /api/personas/1/memory/11": { status: 200, body: approved } });
      const region = await openMemory();

      await click(within(within(region).getByTestId("memory-row-11")).getByRole("button", { name: "Approve" }));

      const [call] = callsTo(mockFetch, "PATCH", "/api/personas/1/memory/11");
      expect(JSON.parse(String(call?.[1]?.body))).toEqual({ approved: true });
      expect(within(within(region).getByTestId("memory-row-11")).queryByText("Waiting for your OK")).not.toBeInTheDocument();
    });

    it("Edit sends the new text and shows the saved row", async () => {
      const edited = { ...LORE_AUTHORED, text: "Grew up above a bakery." };
      const mockFetch = makeFetchMock({ "PATCH /api/personas/1/memory/10": { status: 200, body: edited } });
      const region = await openMemory();

      await click(within(within(region).getByTestId("memory-row-10")).getByRole("button", { name: "Edit" }));
      fireEvent.change(within(region).getByRole("textbox", { name: "Memory text" }), {
        target: { value: "  Grew up above a bakery.  " },
      });
      await click(within(region).getByRole("button", { name: "Save" }));

      const [call] = callsTo(mockFetch, "PATCH", "/api/personas/1/memory/10");
      expect(JSON.parse(String(call?.[1]?.body))).toEqual({ text: "Grew up above a bakery." });
      expect(within(region).getByText("Grew up above a bakery.")).toBeInTheDocument();
    });

    it("Delete asks first, then removes the row", async () => {
      const mockFetch = makeFetchMock({ "DELETE /api/personas/1/memory/10": { status: 204 } });
      const region = await openMemory();

      await click(within(within(region).getByTestId("memory-row-10")).getByRole("button", { name: "Delete" }));
      expect(callsTo(mockFetch, "DELETE", "/api/personas/1/memory/10")).toHaveLength(0);

      const dialog = await screen.findByRole("dialog");
      await click(within(dialog).getByRole("button", { name: "Delete" }));

      expect(callsTo(mockFetch, "DELETE", "/api/personas/1/memory/10")).toHaveLength(1);
      expect(within(region).queryByText("Grew up above a record shop.")).not.toBeInTheDocument();
    });

    it("Add posts the text and lists the new row", async () => {
      const added = memoryRow({ id: 40, kind: "lore", text: "Loves jazz." });
      const mockFetch = makeFetchMock({ "POST /api/personas/1/memory": { status: 201, body: added } });
      const region = await openMemory();

      fireEvent.change(within(region).getByLabelText("Add a line of lore"), { target: { value: "Loves jazz." } });
      await click(within(region).getByRole("button", { name: "Add" }));

      const [call] = callsTo(mockFetch, "POST", "/api/personas/1/memory");
      expect(JSON.parse(String(call?.[1]?.body))).toEqual({ text: "Loves jazz." });
      expect(within(region).getByText("Loves jazz.")).toBeInTheDocument();
    });

    it("Suggest lore lists the drafted rows as waiting for approval", async () => {
      const drafted = memoryRow({
        id: 50,
        kind: "lore",
        source: "drafted",
        text: "Collects old microphones.",
        approvedAt: null,
        pending: true,
      });
      const mockFetch = makeFetchMock({ "POST /api/personas/1/memory/suggest": { status: 201, body: [drafted] } });
      const region = await openMemory();

      await click(within(region).getByRole("button", { name: "Suggest lore" }));

      expect(callsTo(mockFetch, "POST", "/api/personas/1/memory/suggest")).toHaveLength(1);
      expect(await screen.findByText("1 line suggested. Approve the ones you like.")).toBeInTheDocument();
      expect(within(within(region).getByTestId("memory-row-50")).getByText("Waiting for your OK")).toBeInTheDocument();
    });

    it("shows the server's plain-words reason when Suggest lore is refused", async () => {
      makeFetchMock({
        "POST /api/personas/1/memory/suggest": {
          status: 409,
          body: { title: "Language model not set up.", detail: "No language model is set up." },
        },
      });
      const region = await openMemory();

      await click(within(region).getByRole("button", { name: "Suggest lore" }));

      expect(await screen.findByText("No language model is set up.")).toBeInTheDocument();
    });
  });
});
