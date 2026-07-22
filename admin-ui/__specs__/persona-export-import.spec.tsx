// @jest-environment jsdom
// STORY-208/209 — Card export/import from the console (SPEC F79.1, F79.4–F79.6, PLAN T68)
//
// Runner: Jest (jsdom) + @testing-library/react. `PersonaExportLink` is a pure anchor, tested by
// its rendered href. `PersonaImportPanel` owns the whole file → preview → confirm → import
// narrative and is driven directly (it needs no ConfirmDialogProvider/Toaster context beyond the
// Toaster mount for its own toast calls) with a mocked global fetch, mirroring voice-dropdown.spec
// .tsx's sequenced-response style since this flow's calls happen in a fixed order (never fan-out).

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { Toaster } from "@/components/ui/toast";
import { PersonaExportLink } from "../app/(authed)/personas/PersonaExportLink";
import { PersonaImportPanel } from "../app/(authed)/personas/PersonaImportPanel";
import type { PersonaDto } from "../app/(authed)/personas/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function cardJson(overrides: Record<string, unknown> = {}): string {
  return JSON.stringify({
    schemaVersion: 1,
    name: "Radio Rex",
    tagline: "Late-night lore",
    soul: "Backstory: A grizzled jock.",
    quirks: ["hums between tracks", "loves a cold open"],
    voice: { engine: "", voiceId: "af_alloy", pace: 1.0, language: "en" },
    energyDisposition: 0,
    lore: ["Once played a 40-minute Zeppelin side."],
    corrections: [],
    taste: [
      { predicate: {}, context: {}, weight: 0.4 },
      { predicate: {}, context: {}, weight: -0.2 },
      { predicate: {}, context: {}, weight: 0.1 },
    ],
    ...overrides,
  });
}

function makeFile(name: string, content: string, type = "application/json"): File {
  return new File([content], name, { type });
}

/** jsdom's `HTMLInputElement.files` has no public setter; RTL's documented pattern (also used by
 * every file-upload test in the wild) is to shadow it with an own property before firing change.
 * The panel reads the file via `FileReader` (async, jsdom-only event loop tick — `Blob.text()`
 * isn't implemented in this project's jsdom version), so callers must await the preview/oversized
 * state settling before asserting on it; `waitForPicked` below does that. */
function selectFile(input: HTMLInputElement, file: File): void {
  Object.defineProperty(input, "files", { value: [file], configurable: true });
  fireEvent.change(input);
}

/** Awaits the panel's post-FileReader state: either the preview region (ordinary/malformed pick)
 * or the oversized notice (size-capped pick), whichever this file produces. */
async function waitForPicked(): Promise<void> {
  await waitFor(() => {
    const settled =
      screen.queryByRole("region", { name: "Persona card preview" }) ??
      screen.queryByText(/over the 256 KB limit/i);
    expect(settled).not.toBeNull();
  });
}

interface MockResponseSpec {
  status: number;
  body?: unknown;
}

function makeSequencedFetchMock(specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  let callIndex = 0;
  const fn = jest.fn<typeof fetch>().mockImplementation(async () => {
    const spec = specs[callIndex] ?? specs[specs.length - 1];
    callIndex += 1;
    return {
      ok: spec !== undefined && spec.status >= 200 && spec.status < 300,
      status: spec?.status ?? 500,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec?.body ?? {}),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function renderPanel(onImported: () => void = jest.fn()): ReturnType<typeof render> {
  return render(
    <>
      <PersonaImportPanel onImported={onImported} />
      <Toaster />
    </>
  );
}

// ---------------------------------------------------------------------------
// Feature: Export a persona card
// ---------------------------------------------------------------------------

describe("Feature: Export a persona card", () => {
  describe("Scenario: the export link targets this persona's slug", () => {
    it("links to GET /api/personas/{slug}/export, slugified from the name", () => {
      const rex: PersonaDto = { id: 1, name: "Radio Rex!", backstory: "", style: "", voice: "" };
      render(<PersonaExportLink persona={rex} />);

      const link = screen.getByRole("link", { name: "Export Radio Rex!" });
      expect(link).toHaveAttribute("href", "/api/personas/radio-rex/export");
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: Import a persona card
// ---------------------------------------------------------------------------

describe("Feature: Import a persona card", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: happy path — preview then confirm", () => {
    it("previews name/tagline/voice/quirk+lore+taste counts before anything is uploaded", async () => {
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;

      selectFile(input, makeFile("radio-rex.persona.json", cardJson()));

      await waitForPicked();

      const preview = within(screen.getByRole("region", { name: "Persona card preview" }));
      expect(preview.getByText("Radio Rex")).toBeInTheDocument();
      expect(preview.getByText("Late-night lore")).toBeInTheDocument();
      expect(preview.getByText("af_alloy")).toBeInTheDocument();
      expect(preview.getByText("2")).toBeInTheDocument(); // Quirks
      expect(preview.getByText("1")).toBeInTheDocument(); // Lore
      expect(preview.getByText("3")).toBeInTheDocument(); // Taste rules
    });

    it("POSTs the file's original bytes verbatim to the card's own slug", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 201, body: { id: 1, slug: "radio-rex", name: "Radio Rex", warnings: [] } },
      ]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      const raw = cardJson();
      selectFile(input, makeFile("some-file-name.json", raw));
      await waitForPicked();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/personas/radio-rex/import");
      expect(init.method).toBe("POST");
      expect(init.body).toBe(raw);
    });

    it("shows created/updated plus any warnings once the import succeeds", async () => {
      makeSequencedFetchMock([
        {
          status: 200,
          body: { id: 1, slug: "radio-rex", name: "Radio Rex", warnings: ['Voice "af_ghost" is not available.'] },
        },
      ]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      selectFile(input, makeFile("radio-rex.persona.json", cardJson()));
      await waitForPicked();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText(/"Radio Rex" updated\./)).toBeInTheDocument();
      });
      expect(screen.getByText('Voice "af_ghost" is not available.')).toBeInTheDocument();
    });

    it("calls onImported so the parent can refresh its list", async () => {
      makeSequencedFetchMock([
        { status: 201, body: { id: 1, slug: "radio-rex", name: "Radio Rex", warnings: [] } },
      ]);
      const onImported = jest.fn();
      renderPanel(onImported);
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      selectFile(input, makeFile("radio-rex.persona.json", cardJson()));
      await waitForPicked();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(onImported).toHaveBeenCalledTimes(1));
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: oversized payload is blocked before upload (sad path)", () => {
    it("shows an honest too-large message and never calls fetch", async () => {
      const mockFetch = makeSequencedFetchMock([]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;

      selectFile(input, makeFile("huge.persona.json", "a".repeat(300 * 1024)));

      await waitForPicked();

      expect(screen.getByText(/over the 256 KB limit/i)).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Confirm import" })).not.toBeInTheDocument();
      expect(mockFetch).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: server rejects the import (sad path)", () => {
    it("surfaces the newer-major message naming both versions (F79.2)", async () => {
      makeSequencedFetchMock([
        {
          status: 400,
          body: { detail: "Card schema version 7 is newer than this station's supported version 1." },
        },
      ]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      selectFile(input, makeFile("radio-rex.persona.json", cardJson({ schemaVersion: 7 })));
      await waitForPicked();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        const preview = within(screen.getByRole("region", { name: "Persona card preview" }));
        expect(preview.getByRole("alert")).toHaveTextContent(
          "Card schema version 7 is newer than this station's supported version 1."
        );
      });
    });

    it("surfaces a 409 name conflict", async () => {
      makeSequencedFetchMock([
        { status: 409, body: { detail: 'A persona named "Radio Rex" already exists.' } },
      ]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      selectFile(input, makeFile("radio-rex.persona.json", cardJson()));
      await waitForPicked();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        const preview = within(screen.getByRole("region", { name: "Persona card preview" }));
        expect(preview.getByRole("alert")).toHaveTextContent('A persona named "Radio Rex" already exists.');
      });
    });

    it("still lets a malformed file attempt import (server is the real validator)", async () => {
      makeSequencedFetchMock([
        { status: 400, body: { detail: "'x' is an invalid start of a value." } },
      ]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      selectFile(input, makeFile("broken.persona.json", "not valid json"));
      await waitForPicked();

      expect(screen.getByText(/couldn.t preview/i)).toBeInTheDocument();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        const preview = within(screen.getByRole("region", { name: "Persona card preview" }));
        expect(preview.getByRole("alert")).toHaveTextContent("'x' is an invalid start of a value.");
      });
    });
  });
});
