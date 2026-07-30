// @jest-environment jsdom
// gh-#256 — editing a catalog-hired DJ: soul/quirks/lore visible, and the editor cannot silently
// wipe card fields it doesn't render.
//
// A catalog hire stores the persona's narrative in its F71.1 card (`soul`, with the "Style:" line
// embedded, plus `quirks[]`/`lore[]`) and deliberately blanks the legacy backstory/style columns
// (PersonaImportRepository). The editor used to read ONLY the legacy columns — a hired DJ opened
// with a blank Backstory, no Style anywhere, and quirks/lore invisible; worse, saving rebuilt the
// whole card from those blank fields. These specs drive the REAL PersonasClient (same harness as
// personas-page.spec.tsx: fetch mock by METHOD+URL, ConfirmDialogProvider + Toaster) and pin the
// fixed contract: the card persona's soul is shown and edited verbatim, quirks/lore render
// read-only, the Style column parses the soul's embedded "Style:" line, and the PATCH body carries
// `soul` without ever copying it into the legacy backstory/style fields. An authored persona keeps
// the classic Backstory/Style form and sends NO `soul` at all.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactNode } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const LENA_SOUL =
  "Late Night Lena has broadcast from a converted lighthouse since 1987, " +
  "spinning slow-burn soul records for insomniacs and lighthouse keepers alike.\n" +
  "Style: hushed, confiding, never hurried.";

/** The catalog-hire shape: narrative in the card, legacy columns blank on purpose. */
const LENA: PersonaDto = {
  id: 4,
  name: "Late Night Lena",
  backstory: "",
  style: "",
  voice: "af_heart",
  slug: "late-night-lena",
  importedFrom: "late-night-lena",
  importedAt: "2026-07-21T09:05:00Z",
  soul: LENA_SOUL,
  quirks: ["Always mentions the weather at sea", "Collects broken transistor radios"],
  lore: ["Once kept the signal alive through a three-day storm on a car battery"],
};

/** An authored-in-place persona: narrative in the legacy columns, card soul derived from them. */
const REX: PersonaDto = {
  id: 1,
  name: "Radio Rex",
  backstory: "A grizzled late-night jock.",
  style: "Warm, gravelly, brief.",
  voice: "af_alloy",
  slug: "radio-rex",
  importedFrom: null,
  importedAt: null,
  soul: "Backstory: A grizzled late-night jock.\nStyle: Warm, gravelly, brief.",
  quirks: [],
  lore: [],
};

// ---------------------------------------------------------------------------
// Fetch mock — METHOD+URL dispatch, same idiom as personas-page.spec.tsx.
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

function makeFetchMock(routes: Record<string, RouteResponseSpec>): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = (init?.method ?? "GET").toUpperCase();
    const key = `${method} ${String(input)}`;
    const spec = routes[key];
    if (spec === undefined) {
      // The VoiceControl's mount fetch — answered generically so every scenario doesn't have to
      // re-declare it.
      if (key === "GET /api/voices") {
        return {
          ok: true,
          status: 200,
          json: jest.fn<() => Promise<unknown>>().mockResolvedValue(["af_alloy", "af_heart"]),
          headers: new Headers(),
        } as unknown as Response;
      }
      throw new Error(`Unexpected fetch in this suite: ${key}`);
    }
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

function lastBodyFor(
  mockFetch: jest.MockedFunction<typeof fetch>,
  method: string,
  url: string
): Record<string, unknown> {
  const call = [...mockFetch.mock.calls]
    .reverse()
    .find(([input, init]) => String(input) === url && (init?.method ?? "GET").toUpperCase() === method);
  if (call === undefined) throw new Error(`${method} ${url} was never called`);
  return JSON.parse(String(call[1]?.body)) as Record<string, unknown>;
}

function renderClient(personas: PersonaDto[]): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      <PersonasClient initialPersonas={personas} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function clickEdit(name: string): void {
  const row = screen.getByText(name).closest("tr");
  if (row === null) throw new Error(`no roster row for ${name}`);
  fireEvent.click(within(row).getByRole("button", { name: /edit/i }));
}

async function submitEditForm(): Promise<void> {
  fireEvent.click(screen.getByRole("button", { name: "Save changes" }));
  await waitFor(() => {
    expect(screen.queryByRole("button", { name: "Saving…" })).not.toBeInTheDocument();
  });
}

describe("Feature: the persona editor speaks the catalog card schema (gh-#256)", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: a hired DJ's card is visible", () => {
    it("shows the soul text (not a blank Backstory) when editing a catalog-hired persona", () => {
      makeFetchMock({});
      renderClient([LENA]);

      clickEdit(LENA.name);

      const soulField = screen.getByLabelText(/Soul/);
      expect(soulField).toHaveValue(LENA_SOUL);
      // The legacy two-field form is not what a card persona edits.
      expect(screen.queryByLabelText("Backstory")).not.toBeInTheDocument();
    });

    it("shows the card's quirks and lore read-only in the edit form", () => {
      makeFetchMock({});
      renderClient([LENA]);

      clickEdit(LENA.name);

      expect(screen.getByText("Always mentions the weather at sea")).toBeInTheDocument();
      expect(screen.getByText("Collects broken transistor radios")).toBeInTheDocument();
      expect(
        screen.getByText("Once kept the signal alive through a three-day storm on a car battery")
      ).toBeInTheDocument();
    });

    it("the roster's Style column parses the soul's embedded Style: line instead of 'No style set'", () => {
      makeFetchMock({});
      renderClient([LENA]);

      expect(screen.getByText("hushed, confiding, never hurried.")).toBeInTheDocument();
      expect(screen.queryByText("No style set")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: saving cannot silently wipe card fields", () => {
    it("an untouched save round-trips the soul verbatim and never fabricates backstory/style", async () => {
      const mockFetch = makeFetchMock({
        [`PATCH /api/personas/${LENA.id}`]: { status: 200, body: LENA },
      });
      renderClient([LENA]);

      clickEdit(LENA.name);
      await submitEditForm();

      const body = lastBodyFor(mockFetch, "PATCH", `/api/personas/${LENA.id}`);
      expect(body["soul"]).toBe(LENA_SOUL);
      expect(body["backstory"]).toBe("");
      expect(body["style"]).toBe("");
    });

    it("an edited soul is submitted verbatim", async () => {
      const editedSoul = `${LENA_SOUL}\nNow broadcasting from a houseboat.`;
      const mockFetch = makeFetchMock({
        [`PATCH /api/personas/${LENA.id}`]: { status: 200, body: { ...LENA, soul: editedSoul } },
      });
      renderClient([LENA]);

      clickEdit(LENA.name);
      fireEvent.change(screen.getByLabelText(/Soul/), { target: { value: editedSoul } });
      await submitEditForm();

      const body = lastBodyFor(mockFetch, "PATCH", `/api/personas/${LENA.id}`);
      expect(body["soul"]).toBe(editedSoul);
    });
  });

  describe("Scenario: authored personas keep the legacy form", () => {
    it("shows Backstory/Style fields and sends no soul field at all", async () => {
      const mockFetch = makeFetchMock({
        [`PATCH /api/personas/${REX.id}`]: { status: 200, body: REX },
      });
      renderClient([REX]);

      clickEdit(REX.name);
      expect(screen.getByLabelText("Backstory")).toHaveValue(REX.backstory);
      expect(screen.getByLabelText("Style")).toHaveValue(REX.style);
      expect(screen.queryByLabelText(/Soul/)).not.toBeInTheDocument();
      await submitEditForm();

      const body = lastBodyFor(mockFetch, "PATCH", `/api/personas/${REX.id}`);
      expect(body["soul"]).toBeUndefined();
      expect(body["backstory"]).toBe(REX.backstory);
      expect(body["style"]).toBe(REX.style);
    });
  });
});
