// @jest-environment jsdom
// SPEC F165, gh-#707, STORY-397, PLAN T418 — the jingle-pack detail panel's read-only asset table.
//
// Runner: Jest. Mirrors ad-pack-shelf-install.spec.tsx's own "shelf card is meta-only, detail panel
// pays the one per-entry fetch" idiom — no preview gate at all here (a jingle pack declares no
// preview clip, unlike its voice-pack sibling's F103.5 contract; see
// voice-pack-shelf-preview-install.spec.tsx for that kind's own gate). This file pins the Title ·
// Kind · License · Credit table: a CC-BY asset's creator links to its sourceUrl, a CC0 asset shows
// "—" for Credit, every role renders its plain name (gh-#707 — never the raw `bed`/`sting`/
// `station_id` wire token), and the license badge aggregates the pack's own distinct licenses.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { PersonaCatalogClient as PersonaCatalogClientType } from "../app/(authed)/persona-catalog/PersonaCatalogClient";
import type { CatalogEntryDetailDto, CatalogShelfEntryDto } from "../app/(authed)/persona-catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

let PersonaCatalogClient: typeof PersonaCatalogClientType;

const JINGLE_PACK_ENTRY: CatalogShelfEntryDto = {
  slug: "sunday-drive-jingles",
  kind: "jingle-pack",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

// Two assets, two licenses, one of each: a CC-BY `bed` (background music) carrying attribution, and
// a CC0 `station_id` carrying none — proves the table's Credit column tells the two states apart
// rather than always rendering the same thing (SPEC F165.2's own "attribution rides ONLY a CC-BY
// asset" rule) and that role labels never leak their raw wire token (gh-#707).
const JINGLE_PACK_DETAIL: CatalogEntryDetailDto = {
  card: JSON.stringify({
    packName: "Sunday Drive Jingles",
    assets: [
      {
        file: "sunday-drive-bed.mp3",
        sha256: "a".repeat(64),
        role: "bed",
        title: "Sunday Drive",
        license: "CC-BY",
        attribution: { creator: "Field & Frame Audio", sourceUrl: "https://example.com/field-and-frame", license: "CC-BY" },
      },
      {
        file: "station-id-chime.mp3",
        sha256: "b".repeat(64),
        role: "station_id",
        title: "Station Chime",
        license: "CC0",
      },
    ],
  }),
  meta: "{}",
  fetchedAt: "2026-09-07T00:00:00Z",
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
  packName: null,
  iconCount: null,
  adPackBriefs: null,
};

const ENTRY_URL = "/api/catalog/entries/sunday-drive-jingles";

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

function cardFor(name: string): HTMLElement {
  const grid = screen.getByRole("list", { name: "Community catalog entries" });
  const nameNode = within(grid).getByText(name);
  const card = nameNode.closest("button");
  if (card === null) throw new Error(`No <button> ancestor for "${name}"`);
  return card;
}

describe("Feature: the jingle-pack detail panel's read-only asset table (gh-#707, SPEC F165)", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedUseRouter.mockReturnValue({ push: jest.fn(), refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  async function openSundayDriveDetail(): Promise<void> {
    ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
    global.fetch = jest.fn<typeof fetch>().mockImplementation(async (input) => {
      const url = String(input);
      if (url === ENTRY_URL) return makeJsonResponse(200, JINGLE_PACK_DETAIL);
      throw new Error(`unexpected fetch ${url}`);
    }) as unknown as typeof fetch;

    render(
      <ConfirmDialogProvider>
        <PersonaCatalogClient
          activeKind="jingle-pack"
          initialIndex={{ entries: [JINGLE_PACK_ENTRY], fetchedAt: "2026-09-07T00:00:00Z", unreachable: false }}
        />
        <Toaster />
      </ConfirmDialogProvider>
    );
    fireEvent.click(cardFor("Sunday Drive Jingles"));
    await screen.findByRole("table");
  }

  describe("Scenario: plain role names, never the raw wire token (gh-#707)", () => {
    it("renders 'Background music' and 'Station ID', never 'bed' or 'station_id'", async () => {
      await openSundayDriveDetail();

      expect(screen.getByText("Background music")).toBeInTheDocument();
      expect(screen.getByText("Station ID")).toBeInTheDocument();
      expect(screen.queryByText("bed")).not.toBeInTheDocument();
      expect(screen.queryByText("station_id")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: Credit tells CC-BY attribution and CC0 silence apart", () => {
    it("links the CC-BY asset's creator to its sourceUrl, safely", async () => {
      await openSundayDriveDetail();

      const link = screen.getByRole("link", { name: "Field & Frame Audio" });
      expect(link).toHaveAttribute("href", "https://example.com/field-and-frame");
      expect(link).toHaveAttribute("target", "_blank");
      expect(link).toHaveAttribute("rel", "noopener noreferrer");
    });

    it("shows '—' for the CC0 asset's Credit cell, no link and no fabricated name", async () => {
      await openSundayDriveDetail();

      const table = screen.getByRole("table");
      const chimeRow = within(table).getByText("Station Chime").closest("tr");
      if (chimeRow === null) throw new Error("No <tr> ancestor for the Station Chime row");
      expect(within(chimeRow).getByText("—")).toBeInTheDocument();
      expect(within(chimeRow).queryByRole("link")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the license badge aggregates the pack's own distinct licenses", () => {
    it("shows 'CC0 · CC-BY', deterministically ordered regardless of asset order", async () => {
      await openSundayDriveDetail();

      expect(screen.getByText("CC0 · CC-BY")).toBeInTheDocument();
    });

    // T418 review round 1 finding F3: a jingle pack's badge reads a REAL licence, so its aria-label
    // says "License: …" — contrast the voice-pack sibling's "Voices: …" prefix pinned in
    // voice-pack-shelf-preview-install.spec.tsx (that kind's "Synthetic voices" string is not a
    // licence at all).
    it("labels the badge 'License: CC0 · CC-BY' for assistive tech", async () => {
      await openSundayDriveDetail();

      expect(screen.getByLabelText("License: CC0 · CC-BY")).toBeInTheDocument();
    });
  });

  describe("Scenario: the table lists every asset's Title and License plainly", () => {
    it("shows each asset's own title and license string in its row", async () => {
      await openSundayDriveDetail();

      expect(screen.getByText("Sunday Drive")).toBeInTheDocument();
      expect(screen.getByText("Station Chime")).toBeInTheDocument();

      const table = screen.getByRole("table");
      const bedRow = within(table).getByText("Sunday Drive").closest("tr");
      if (bedRow === null) throw new Error("No <tr> ancestor for the Sunday Drive row");
      expect(within(bedRow).getByText("CC-BY")).toBeInTheDocument();
    });
  });
});
