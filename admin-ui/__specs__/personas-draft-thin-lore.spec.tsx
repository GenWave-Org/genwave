// @jest-environment jsdom
// PLAN T741 — Draft lore for thin DJs (SPEC F218.49, STORY-553)
// Runner: Jest (jsdom). Drives the real PersonasClient with a fetch mock.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";
import type { PersonaMemoryRow } from "../lib/persona-memory-api";

function persona(id: number, name: string): PersonaDto {
  return {
    id,
    name,
    backstory: "A DJ.",
    style: "Warm.",
    voice: "af_alloy",
    slug: name.toLowerCase().replace(/ /g, "-"),
    importedFrom: null,
    importedAt: null,
    soul: "",
    quirks: [],
    lore: [],
  };
}

const NOVA = persona(1, "Nova Q");
const REX = persona(2, "Radio Rex");

function loreRow(id: number, overrides: Partial<PersonaMemoryRow> = {}): PersonaMemoryRow {
  return {
    id,
    kind: "lore",
    source: "authored",
    text: `Lore ${id}.`,
    airedCount: 0,
    askedCount: 0,
    lastAiredAt: null,
    createdAt: "2026-11-01T10:00:00Z",
    approvedAt: "2026-11-01T10:00:00Z",
    forgottenAt: null,
    pending: false,
    forgotten: false,
    ...overrides,
  };
}

const THREE_LIVE = [loreRow(10), loreRow(11), loreRow(12)];
// One live note; a pending draft, a forgotten note and a non-lore row must not count.
const ONE_LIVE = [
  loreRow(20),
  loreRow(21, { pending: true, approvedAt: null, source: "drafted" }),
  loreRow(22, { forgotten: true }),
  loreRow(23, { kind: "life" }),
];

interface Mock {
  suggestCalls: string[];
}

interface SuggestFailure {
  status: number;
  detail: string;
}

interface MockOptions {
  /** DJs whose suggest call fails, with the problem the server answers. */
  suggestFails?: Record<number, SuggestFailure>;
  /** DJs whose memory list can't be read. */
  unreadable?: readonly number[];
}

function problem(status: number, detail: string): Response {
  return {
    ok: false,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue({ detail }),
    headers: new Headers({ "Content-Type": "application/problem+json" }),
  } as unknown as Response;
}

/** `memory` is live: a suggest call adds a drafted row and a PATCH approve updates the row, as the server would. */
function mockApi(memory: Record<number, PersonaMemoryRow[]>, options: MockOptions = {}): Mock {
  const mock: Mock = { suggestCalls: [] };
  global.fetch = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const url = String(input);
    const method = (init?.method ?? "GET").toUpperCase();
    const suggest = /^\/api\/personas\/(\d+)\/memory\/suggest$/.exec(url);
    if (method === "POST" && suggest !== null) {
      mock.suggestCalls.push(url);
      const id = Number(suggest[1]);
      const failure = options.suggestFails?.[id];
      if (failure !== undefined) return problem(failure.status, failure.detail);
      const drafted = loreRow(100 + id, { pending: true, approvedAt: null, source: "drafted", text: "Drafted line." });
      memory[id] = [...(memory[id] ?? []), drafted];
      return okJson([drafted]);
    }
    const patch = /^\/api\/personas\/(\d+)\/memory\/(\d+)$/.exec(url);
    if (method === "PATCH" && patch !== null) {
      const personaId = Number(patch[1]);
      const rows = memory[personaId] ?? [];
      const row = rows.find((r) => r.id === Number(patch[2]));
      if (row === undefined) return problem(404, "Not found.");
      const approved = { ...row, pending: false, approvedAt: "2026-11-02T10:00:00Z" };
      memory[personaId] = rows.map((r) => (r.id === row.id ? approved : r));
      return okJson(approved);
    }
    const list = /^\/api\/personas\/(\d+)\/memory$/.exec(url);
    if (method === "GET" && list !== null) {
      const id = Number(list[1]);
      if (options.unreadable?.includes(id)) return problem(500, "Could not read.");
      return okJson(memory[id] ?? []);
    }
    return okJson([]);
  }) as unknown as typeof fetch;
  return mock;
}

function okJson(body: unknown): Response {
  return {
    ok: true,
    status: 200,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

async function renderPage(personas: PersonaDto[]): Promise<void> {
  render(
    <ConfirmDialogProvider>
      <PersonasClient initialPersonas={personas} lifeExtractionOff={false} />
      <Toaster />
    </ConfirmDialogProvider>
  );
  await act(async () => {
    await Promise.resolve();
  });
}

async function pressDraft(): Promise<void> {
  const button = await screen.findByRole("button", { name: "Draft lore for thin DJs" });
  await waitFor(() => expect(button).toBeEnabled());
  await act(async () => {
    fireEvent.click(button);
  });
}

const DOWN: SuggestFailure = { status: 502, detail: "The model is down." };

describe("STORY-553 — lore for thin DJs", () => {
  let originalFetch: typeof fetch;
  beforeEach(() => {
    originalFetch = global.fetch;
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it("AC1 — calls suggest once, for the DJ with 1 live lore note", async () => {
    const mock = mockApi({ 1: ONE_LIVE, 2: THREE_LIVE });
    await renderPage([NOVA, REX]);
    await pressDraft();

    await waitFor(() => expect(mock.suggestCalls).toEqual(["/api/personas/1/memory/suggest"]));
  });

  it("AC2 — the toast names how many DJs got drafts", async () => {
    mockApi({ 1: ONE_LIVE, 2: [] });
    await renderPage([NOVA, REX]);
    await pressDraft();

    expect(await screen.findByText(/Drafted lore for 2 DJs\./)).toBeInTheDocument();
  });

  it("AC2 — an open Memory section shows the new draft, waiting for approval", async () => {
    mockApi({ 1: ONE_LIVE, 2: THREE_LIVE });
    await renderPage([NOVA, REX]);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Show memory for Nova Q" }));
    });
    await screen.findByRole("region", { name: "Memory for Nova Q" });
    await pressDraft();

    const row = await screen.findByTestId("memory-row-101");
    expect(row).toHaveTextContent("Drafted line.");
    expect(row).toHaveTextContent("Waiting for your OK");
  });

  it("AC2 — only the drafted DJ's open section is re-read", async () => {
    const mock = mockApi({ 1: ONE_LIVE, 2: THREE_LIVE });
    await renderPage([NOVA, REX]);
    for (const name of ["Nova Q", "Radio Rex"]) {
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: `Show memory for ${name}` }));
      });
    }
    await screen.findByRole("region", { name: "Memory for Radio Rex" });
    const reads = (id: number): number =>
      (global.fetch as jest.Mock<typeof fetch>).mock.calls.filter(
        ([url, init]) => String(url) === `/api/personas/${id}/memory` && (init?.method ?? "GET") === "GET"
      ).length;
    const rexBefore = reads(2);
    const novaBefore = reads(1);
    await pressDraft();
    await screen.findByTestId("memory-row-101");
    expect(mock.suggestCalls).toEqual(["/api/personas/1/memory/suggest"]);
    // Each DJ is read twice by the button (before and after the run). Only Nova's section also remounts and reads.
    expect(reads(2) - rexBefore).toBe(2);
    expect(reads(1) - novaBefore).toBe(3);
  });

  it("AC3 — disabled and reads Every DJ has lore when nobody is thin", async () => {
    const mock = mockApi({ 1: THREE_LIVE, 2: THREE_LIVE });
    await renderPage([NOVA, REX]);

    const button = await screen.findByRole("button", { name: "Every DJ has lore" });
    expect(button).toBeDisabled();
    expect(mock.suggestCalls).toEqual([]);
  });

  it("reads the counts again on press: a DJ who stopped being thin is skipped", async () => {
    const memory = { 1: [...ONE_LIVE], 2: [] as PersonaMemoryRow[] };
    const mock = mockApi(memory);
    await renderPage([NOVA, REX]);
    const button = await screen.findByRole("button", { name: "Draft lore for thin DJs" });
    await waitFor(() => expect(button).toBeEnabled());
    memory[1] = THREE_LIVE;
    await act(async () => {
      fireEvent.click(button);
    });

    await waitFor(() => expect(mock.suggestCalls).toEqual(["/api/personas/2/memory/suggest"]));
  });

  it("approving a draft in a Memory section updates the thin list", async () => {
    const pendingThird = loreRow(32, { pending: true, approvedAt: null, source: "drafted" });
    const mock = mockApi({ 1: [loreRow(30), loreRow(31), pendingThird], 2: [] });
    await renderPage([NOVA, REX]);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Show memory for Nova Q" }));
    });
    const row = await screen.findByTestId("memory-row-32");
    await act(async () => {
      fireEvent.click(within(row).getByRole("button", { name: "Approve" }));
    });
    await waitFor(() => expect(within(row).queryByText("Waiting for your OK")).not.toBeInTheDocument());
    await pressDraft();

    await waitFor(() => expect(mock.suggestCalls).toEqual(["/api/personas/2/memory/suggest"]));
  });

  it("when a DJ's lore can't be read, the button stays usable and leaves that DJ out", async () => {
    const mock = mockApi({ 1: [], 2: [] }, { unreadable: [2] });
    await renderPage([NOVA, REX]);
    await pressDraft();

    await waitFor(() => expect(mock.suggestCalls).toEqual(["/api/personas/1/memory/suggest"]));
  });

  it("when no DJ's lore can be read, it says so and is disabled", async () => {
    mockApi({}, { unreadable: [1, 2] });
    await renderPage([NOVA, REX]);

    expect(await screen.findByRole("button", { name: "Could not read lore" })).toBeDisabled();
  });

  it("AC4 — a failed call does not stop the next DJ and the toast names the failure and why", async () => {
    const mock = mockApi({ 1: [], 2: [] }, { suggestFails: { 1: DOWN } });
    await renderPage([NOVA, REX]);
    await pressDraft();

    expect(
      await screen.findByText('Drafted lore for 1 DJ. Drafting failed for "Nova Q": The model is down.')
    ).toBeInTheDocument();
    expect(mock.suggestCalls).toEqual(["/api/personas/1/memory/suggest", "/api/personas/2/memory/suggest"]);
  });

  it("failures with the same reason show it once", async () => {
    mockApi({ 1: [], 2: [] }, { suggestFails: { 1: DOWN, 2: DOWN } });
    await renderPage([NOVA, REX]);
    await pressDraft();

    expect(
      await screen.findByText('No lore drafted. Drafting failed for "Nova Q", "Radio Rex": The model is down.')
    ).toBeInTheDocument();
  });

  it("failures with different reasons list each one", async () => {
    mockApi({ 1: [], 2: [] }, { suggestFails: { 1: DOWN, 2: { status: 502, detail: "The model gave no usable lore." } } });
    await renderPage([NOVA, REX]);
    await pressDraft();

    expect(
      await screen.findByText(
        'No lore drafted. Drafting failed for "Nova Q" (The model is down), "Radio Rex" (The model gave no usable lore).'
      )
    ).toBeInTheDocument();
  });

  it("a 409 (no language model) stops the run and says so", async () => {
    const mock = mockApi({ 1: [], 2: [] }, { suggestFails: { 1: { status: 409, detail: "No language model is set up." } } });
    await renderPage([NOVA, REX]);
    await pressDraft();

    expect(
      await screen.findByText(
        'No lore drafted. Drafting stopped at "Nova Q": No language model is set up. The other DJs were not tried.'
      )
    ).toBeInTheDocument();
    expect(mock.suggestCalls).toEqual(["/api/personas/1/memory/suggest"]);
  });

  it("a 409 after an earlier failure lists the earlier one, then the stop", async () => {
    const third = persona(3, "Third Tom");
    const mock = mockApi(
      { 1: [], 2: [], 3: [] },
      { suggestFails: { 1: DOWN, 2: { status: 409, detail: "No language model is set up." } } }
    );
    await renderPage([NOVA, REX, third]);
    await pressDraft();

    expect(
      await screen.findByText(
        'No lore drafted. Drafting failed for "Nova Q": The model is down. Drafting stopped at "Radio Rex": No language model is set up. The other DJs were not tried.'
      )
    ).toBeInTheDocument();
    expect(mock.suggestCalls).toEqual(["/api/personas/1/memory/suggest", "/api/personas/2/memory/suggest"]);
  });

  it("when no count can be read on press, the toast says so instead of claiming nobody is thin", async () => {
    const memory: Record<number, PersonaMemoryRow[]> = { 1: [], 2: [] };
    const options: MockOptions = { unreadable: [] };
    mockApi(memory, options);
    await renderPage([NOVA, REX]);
    const button = await screen.findByRole("button", { name: "Draft lore for thin DJs" });
    await waitFor(() => expect(button).toBeEnabled());
    options.unreadable = [1, 2];
    await act(async () => {
      fireEvent.click(button);
    });

    expect(await screen.findByText("Could not read lore.")).toBeInTheDocument();
    expect(screen.queryByText(/No DJ has fewer than 3/)).not.toBeInTheDocument();
  });

  it("a Memory change during a run does not leave the button stuck disabled", async () => {
    const pendingThird = loreRow(32, { pending: true, approvedAt: null, source: "drafted" });
    const memory: Record<number, PersonaMemoryRow[]> = { 1: [loreRow(30), loreRow(31), pendingThird], 2: [] };
    mockApi(memory);
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const inner = global.fetch;
    global.fetch = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
      if (String(input) === "/api/personas/2/memory/suggest") await gate;
      return inner(input, init);
    }) as unknown as typeof fetch;

    await renderPage([NOVA, REX]);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Show memory for Nova Q" }));
    });
    const row = await screen.findByTestId("memory-row-32");
    await pressDraft();
    await act(async () => {
      fireEvent.click(within(row).getByRole("button", { name: "Approve" }));
    });
    await waitFor(() => expect(within(row).queryByText("Waiting for your OK")).not.toBeInTheDocument());
    await act(async () => {
      release();
    });

    // Rex still has only a pending draft, so he is thin: the button must come back enabled.
    const button = await screen.findByRole("button", { name: "Draft lore for thin DJs" });
    await waitFor(() => expect(button).toBeEnabled());
  });
});
