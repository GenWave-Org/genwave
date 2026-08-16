// @jest-environment jsdom
// STORY-249 — Hire, not import (gh-#169, SPEC F94.4, PLAN T130)
//
// This file pins the hire-verb copy pass end to end, in one place: PersonaCatalogClient's shelf
// button and success copy, the shared PersonaCardReviewModal's confirm label on both the catalog
// and file-upload doors, and PersonasClient's ProvenanceBadge. Each of these components already
// has its own full behavioral coverage elsewhere (persona-catalog-page.spec.tsx,
// persona-export-import.spec.tsx, persona-card-review-modal.spec.tsx, personas-page.spec.tsx) —
// this file does not re-derive that coverage, it exists so a future wording regression on the
// hire verb fails here FIRST, in a file named for exactly that concern. The wire-contract-
// unchanged half (SPEC F94.4: endpoints/DTOs/provenance column values stay "import") is asserted
// directly via the URL a hire flow POSTs to, in the same test as the success copy.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeAll, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, waitFor, within, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonaImportPanel } from "../app/(authed)/personas/PersonaImportPanel";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";
import type { PersonaCatalogClient as PersonaCatalogClientComponent } from "../app/(authed)/persona-catalog/PersonaCatalogClient";
import type { CatalogEntryDetailDto, CatalogShelfEntryDto } from "../app/(authed)/persona-catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// See persona-catalog-page.spec.tsx's own remarks on why this must be a dynamic import AFTER
// jest.mock("next/navigation", ...) registers, not a static top-level one (this project's
// SWC-based jest transform does not hoist jest.mock past a static import).
let PersonaCatalogClient: typeof PersonaCatalogClientComponent;

beforeAll(async () => {
  ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
});

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const LENA_ENTRY: CatalogShelfEntryDto = {
  slug: "late-night-lena",
  kind: "persona",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

const LENA_CARD_JSON = JSON.stringify({
  schemaVersion: 1,
  name: "Late Night Lena",
  tagline: "Warm 2am company",
  soul: "A late-night voice who never rushes a segue.",
  quirks: [],
  voice: { engine: "kokoro", voiceId: "af_lena", pace: 1.0, language: "en" },
  energyDisposition: -0.2,
  lore: [],
  corrections: [],
  taste: [],
});

const LENA_DETAIL: CatalogEntryDetailDto = {
  card: LENA_CARD_JSON,
  meta: "{}",
  fetchedAt: "2026-07-27T00:00:00Z",
  unreachable: false,
  audience: "everyone",
  bestFor: [],
  author: null,
  description: null,
  samplePatter: [],
  fontFamily: null,
  fontByteTotal: null,
  fontSpecimenFile: null,
  fontLicense: null,
  fontVersion: null,
  fontSubset: null,
  suggestedPersona: null,
  avatarItems: null,
  personaAvatarFile: null,
};

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

function cardFor(name: string): HTMLElement {
  const grid = screen.getByRole("list", { name: "Community catalog entries" });
  const nameNode = within(grid).getByText(name);
  const card = nameNode.closest("button");
  if (card === null) throw new Error(`No <button> ancestor for "${name}"`);
  return card;
}

/** Opens Lena's detail panel and clicks the shelf action button, landing on the open review
 * modal — the shared arrange step every "Scenario: the verb is Hire" test below builds on. */
async function openLenaCatalogReview(): Promise<void> {
  global.fetch = jest
    .fn<typeof fetch>()
    .mockResolvedValue(makeJsonResponse(200, LENA_DETAIL)) as unknown as typeof fetch;

  render(
    <>
      <PersonaCatalogClient initialIndex={{ entries: [LENA_ENTRY], fetchedAt: "2026-07-27T00:00:00Z", unreachable: false }} />
      <Toaster />
    </>
  );
  fireEvent.click(cardFor("Late Night Lena"));
  const hireButton = await screen.findByRole("button", { name: "Hire" });
  fireEvent.click(hireButton);
  await screen.findByRole("dialog");
}

/** Carries `openLenaCatalogReview` through to a completed hire — the arrange step the two
 * wire-contract/success-copy tests below share, since both observe the outcome of the SAME
 * confirm click, just via different assertions. */
async function completeLenaHire(): Promise<jest.MockedFunction<typeof fetch>> {
  await openLenaCatalogReview();

  const postFetch = jest
    .fn<typeof fetch>()
    .mockResolvedValue(makeJsonResponse(201, { name: "Late Night Lena", warnings: [] })) as unknown as jest.MockedFunction<
    typeof fetch
  >;
  global.fetch = postFetch;

  const dialog = within(screen.getByRole("dialog"));
  await act(async () => {
    fireEvent.click(dialog.getByRole("button", { name: "Confirm hire" }));
    await Promise.resolve();
  });

  return postFetch;
}

// ---------------------------------------------------------------------------
// Feature: Hire, not import
// ---------------------------------------------------------------------------

describe("Feature: Hire, not import", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedUseRouter.mockReturnValue({ push: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the verb is Hire", () => {
    it("the shelf action button says Hire", async () => {
      global.fetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeJsonResponse(200, LENA_DETAIL)) as unknown as typeof fetch;

      render(
        <PersonaCatalogClient initialIndex={{ entries: [LENA_ENTRY], fetchedAt: "2026-07-27T00:00:00Z", unreachable: false }} />
      );
      fireEvent.click(cardFor("Late Night Lena"));

      expect(await screen.findByRole("button", { name: "Hire" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Import" })).not.toBeInTheDocument();
    });

    it("the review modal confirm says Hire", async () => {
      await openLenaCatalogReview();

      const dialog = within(screen.getByRole("dialog"));
      expect(dialog.getByRole("button", { name: "Confirm hire" })).toBeInTheDocument();
      expect(dialog.queryByRole("button", { name: "Confirm import" })).not.toBeInTheDocument();
    });

    it("the success copy speaks hiring language", async () => {
      await completeLenaHire();

      expect(await screen.findByText('"Late Night Lena" hired.')).toBeInTheDocument();
    });

    it("the provenance badge reads Hired · <source> · <date>", () => {
      const hiredFromCatalog: PersonaDto = {
        id: 1,
        name: "Late Night Lena",
        backstory: "",
        style: "",
        voice: "",
        slug: "late-night-lena",
        importedFrom: "late-night-lena",
        importedAt: "2026-07-21T09:05:00Z",
        soul: "",
        quirks: [],
        lore: [],
      };
      global.fetch = jest.fn<typeof fetch>().mockResolvedValue(makeJsonResponse(200, [])) as unknown as typeof fetch;

      render(
        <ConfirmDialogProvider>
          <PersonasClient initialPersonas={[hiredFromCatalog]} timeZone="UTC" />
          <Toaster />
        </ConfirmDialogProvider>
      );

      expect(screen.getByText("Hired · late-night-lena · Jul 21, 2026")).toBeInTheDocument();
    });
  });

  describe("Scenario: the contract is still import", () => {
    it("the file-upload path still says Import", async () => {
      render(
        <>
          <PersonaImportPanel onImported={jest.fn()} />
          <Toaster />
        </>
      );
      const input = screen.getByLabelText("Persona card (.json)") as HTMLInputElement;
      const file = new File([LENA_CARD_JSON], "late-night-lena.persona.json", { type: "application/json" });
      Object.defineProperty(input, "files", { value: [file], configurable: true });
      fireEvent.change(input);

      const dialog = within(await screen.findByRole("dialog"));
      expect(dialog.getByRole("button", { name: "Confirm import" })).toBeInTheDocument();
      expect(dialog.queryByRole("button", { name: "Confirm hire" })).not.toBeInTheDocument();
    });

    it("the hire flow calls the unchanged import endpoint", async () => {
      const postFetch = await completeLenaHire();

      await waitFor(() => expect(postFetch).toHaveBeenCalledTimes(1));
      const [url] = postFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/personas/late-night-lena/import?catalogSlug=late-night-lena");
    });
  });
});
