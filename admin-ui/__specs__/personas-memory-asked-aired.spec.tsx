// @jest-environment jsdom
// PLAN T740 — Memory tab rows say asked and aired (SPEC F218.48, STORY-552 AC2, AC3)
// Runner: Jest (jsdom). Drives the real PersonasClient with a fetch mock.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
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

function mockMemory(rows: PersonaMemoryRow[]): void {
  const routes: Record<string, unknown> = {
    "GET /api/voices": [],
    "GET /api/personas/1/memory": rows,
  };
  global.fetch = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const body = routes[`${(init?.method ?? "GET").toUpperCase()} ${String(input)}`] ?? {};
    return {
      ok: true,
      status: 200,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
      headers: new Headers(),
    } as unknown as Response;
  }) as unknown as typeof fetch;
}

async function openMemory(): Promise<HTMLElement> {
  render(
    <ConfirmDialogProvider>
      <PersonasClient initialPersonas={[REX]} lifeExtractionOff={false} />
      <Toaster />
    </ConfirmDialogProvider>
  );
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "Show memory for Radio Rex" }));
    await Promise.resolve();
  });
  return screen.findByRole("region", { name: "Memory for Radio Rex" });
}

describe("STORY-552 — asked and aired on the Memory tab", () => {
  let originalFetch: typeof fetch;
  beforeEach(() => {
    originalFetch = global.fetch;
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it("AC2 — reads Asked 4 times · Aired once", async () => {
    mockMemory([memoryRow({ id: 1, kind: "lore", text: "Asked line.", askedCount: 4, airedCount: 1 })]);
    const region = await openMemory();

    expect(within(within(region).getByTestId("memory-row-1")).getByText("Asked 4 times · Aired once")).toBeInTheDocument();
  });

  it("AC3 — reads Not asked yet for a memory with no offers", async () => {
    mockMemory([memoryRow({ id: 2, kind: "lore", text: "Fresh line." })]);
    const region = await openMemory();

    expect(within(within(region).getByTestId("memory-row-2")).getByText("Not asked yet")).toBeInTheDocument();
  });

  it("a memory asked but never used reads Asked 2 times · Not aired yet", async () => {
    mockMemory([memoryRow({ id: 3, kind: "lore", text: "Passed over.", askedCount: 2 })]);
    const region = await openMemory();

    expect(within(within(region).getByTestId("memory-row-3")).getByText("Asked 2 times · Not aired yet")).toBeInTheDocument();
  });
});
