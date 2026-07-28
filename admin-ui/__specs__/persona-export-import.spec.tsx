// @jest-environment jsdom
// STORY-208/209 — Card export/import from the console (SPEC F79.1, F79.4–F79.6, PLAN T68)
// STORY-236 — File-upload import retrofit: same review modal, no loophole (SPEC F90.6, PLAN T104)
//
// Runner: Jest (jsdom) + @testing-library/react. `PersonaExportLink` is a pure anchor, tested by
// its rendered href. `PersonaImportPanel` owns file selection → the shared `PersonaCardReviewModal`
// (T103's catalog-door component, reused here verbatim) → import; its own section-rendering/error
// states are pinned once in `persona-card-review-modal.spec.tsx` and are NOT re-asserted here — this
// file only pins the panel-specific wiring: which file text reaches the modal, that no fetch fires
// before Confirm, cancel's no-op, and the no-catalogSlug seam — mirroring
// `persona-catalog-page.spec.tsx`'s own "wiring only" posture for the catalog door.

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
 * isn't implemented in this project's jsdom version), so callers must await the modal (or the
 * oversized/unreadable notice) settling before asserting on it. */
function selectFile(input: HTMLInputElement, file: File): void {
  Object.defineProperty(input, "files", { value: [file], configurable: true });
  fireEvent.change(input);
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
  describe("Scenario: the export link targets this persona's SERVER slug (PLAN T128 review fix)", () => {
    it("links to GET /api/personas/{slug}/export using persona.slug verbatim, not a name-derived one", () => {
      // Real dev data shape: an imported persona's stored slug can diverge from a fresh slugify of
      // its current name (the import route's slug and the card's own `name` field are independent
      // — see PersonaDto.slug's own remarks). This pins that the href uses the slug the server
      // actually stored, never `personaSlug(persona.name)` — the bug that 404'd this exact link
      // inside the Fire modal's export-first parachute.
      const novaQ: PersonaDto = {
        id: 1,
        name: "Nova Q",
        backstory: "",
        style: "",
        voice: "",
        slug: "persona-2",
        importedFrom: null,
        importedAt: null,
      };
      render(<PersonaExportLink persona={novaQ} />);

      const link = screen.getByRole("link", { name: "Export Nova Q" });
      expect(link).toHaveAttribute("href", "/api/personas/persona-2/export");
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

  describe("Scenario: file selection opens the same review modal the catalog door uses (STORY-236, PLAN T104)", () => {
    it("renders the file's full text inside PersonaCardReviewModal, with no separate preview pane", async () => {
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;

      selectFile(input, makeFile("radio-rex.persona.json", cardJson()));

      const dialog = await screen.findByRole("dialog");
      const scoped = within(dialog);
      expect(scoped.getByText("Radio Rex")).toBeInTheDocument();
      expect(scoped.getByText("Late-night lore")).toBeInTheDocument();
      expect(scoped.getByText("Once played a 40-minute Zeppelin side.")).toBeInTheDocument();
      expect(screen.queryByRole("region", { name: "Persona card preview" })).not.toBeInTheDocument();
    });

    it("issues zero fetch calls before Confirm is clicked", async () => {
      const mockFetch = makeSequencedFetchMock([]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;

      selectFile(input, makeFile("radio-rex.persona.json", cardJson()));
      await screen.findByRole("dialog");

      expect(mockFetch).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: confirm posts the file's original bytes, no catalogSlug (the T104 seam)", () => {
    it("POSTs the file's raw text verbatim to /api/personas/{slug}/import, without a catalogSlug param", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 201, body: { name: "Radio Rex", warnings: [] } },
      ]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      const raw = cardJson();
      selectFile(input, makeFile("some-file-name.json", raw));
      const dialog = await screen.findByRole("dialog");

      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/personas/radio-rex/import");
      expect(init.method).toBe("POST");
      expect(init.body).toBe(raw);
    });

    it("shows created/updated plus any warnings once the import succeeds, closes the modal, and refreshes the list", async () => {
      makeSequencedFetchMock([
        {
          status: 200,
          body: { name: "Radio Rex", warnings: ['Voice "af_ghost" is not available.'] },
        },
      ]);
      const onImported = jest.fn();
      renderPanel(onImported);
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      selectFile(input, makeFile("radio-rex.persona.json", cardJson()));
      const dialog = await screen.findByRole("dialog");

      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
      // Scoped to the panel's own success block, not the toast — sonner renders the SAME message
      // text a second time in its own live region, which `screen.getByText` (unscoped) would find
      // ambiguous.
      const doneBlock = screen.getByRole("button", { name: "Import another" }).closest("div");
      expect(doneBlock).not.toBeNull();
      const scopedDone = within(doneBlock as HTMLElement);
      expect(scopedDone.getByText(/"Radio Rex" updated\./)).toBeInTheDocument();
      expect(scopedDone.getByText('Voice "af_ghost" is not available.')).toBeInTheDocument();
      expect(onImported).toHaveBeenCalledTimes(1);
    });
  });

  describe("Scenario: cancel abandons the attempt — no import request, file selection cleared (STORY-236 AC1)", () => {
    it("closes the modal, issues no fetch, and clears the file input", async () => {
      const mockFetch = makeSequencedFetchMock([]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      selectFile(input, makeFile("radio-rex.persona.json", cardJson()));
      const dialog = await screen.findByRole("dialog");

      fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
      expect(mockFetch).not.toHaveBeenCalled();
      expect(input.value).toBe("");
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: oversized payload is blocked before the modal ever opens (sad path)", () => {
    it("shows an honest too-large message and never calls fetch or opens the review modal", async () => {
      const mockFetch = makeSequencedFetchMock([]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;

      selectFile(input, makeFile("huge.persona.json", "a".repeat(300 * 1024)));

      await waitFor(() => {
        expect(screen.getByText(/over the 256 KB limit/i)).toBeInTheDocument();
      });
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(mockFetch).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: a malformed file still opens the modal — ITS error state blocks Confirm, never this panel's (sad path)", () => {
    it("opens the modal in its own 'couldn't be read' state with Confirm disabled, and never attempts import", async () => {
      const mockFetch = makeSequencedFetchMock([]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      selectFile(input, makeFile("broken.persona.json", "not valid json"));

      const dialog = await screen.findByRole("dialog");
      const scoped = within(dialog);
      expect(scoped.getByRole("alert")).toHaveTextContent(/couldn.t be read/i);
      expect(scoped.getByRole("button", { name: "Confirm import" })).toBeDisabled();

      fireEvent.click(scoped.getByRole("button", { name: "Confirm import" }));
      expect(mockFetch).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: server rejects the import (sad path)", () => {
    it("surfaces the newer-major message naming both versions (F79.2), inside the modal", async () => {
      makeSequencedFetchMock([
        {
          status: 400,
          body: { detail: "Card schema version 7 is newer than this station's supported version 1." },
        },
      ]);
      renderPanel();
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      selectFile(input, makeFile("radio-rex.persona.json", cardJson({ schemaVersion: 7 })));
      const dialog = await screen.findByRole("dialog");

      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(within(dialog).getByRole("alert")).toHaveTextContent(
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
      const dialog = await screen.findByRole("dialog");

      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(within(dialog).getByRole("alert")).toHaveTextContent(
          'A persona named "Radio Rex" already exists.'
        );
      });
    });
  });
});
