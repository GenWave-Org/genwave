// @jest-environment jsdom
// SPEC F164, F103.5, STORY-397 AC4, PLAN T418 — the voice-pack detail panel's honest-preview gate.
//
// Runner: Jest. Mirrors font-pack-shelf-specimen.spec.tsx's own "real <audio>/<FontFace>, minimal
// jsdom stand-ins for URL.createObjectURL/revokeObjectURL" idiom one kind over. The one departure
// this kind's own contract forces: the Install button is NOT RENDERED — not merely disabled — until
// the `<audio>` element itself fires `canplay`, proving the browser's own decoder looked at the
// bytes and found real audio, not merely that the fetch returned 200. Four facts: no button while
// loading, the button appears once `canplay` fires, a fetch rejection degrades to the exact pinned
// failure copy with the button still absent, and the pack's license badge reads "Voices: Synthetic
// voices" rather than "License: …" (T418 review round 2 finding F1-r2 — a voice pack carries no
// license field, so labelling it "License:" would tell a screen reader something false). Install/
// uninstall VERB coverage (200/409 toast paths) lives in voice-jingle-pack-shelf-install.spec.tsx
// instead — this file is the preview gate alone.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within, fireEvent, act } from "@testing-library/react";
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

const VOICE_PACK_ENTRY: CatalogShelfEntryDto = {
  slug: "moonlit-narrators",
  kind: "voice-pack",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

const VOICE_PACK_DETAIL: CatalogEntryDetailDto = {
  card: JSON.stringify({
    packName: "Moonlit Narrators",
    engine: "kokoro",
    preview: "moonlit-preview.mp3",
    voices: [
      { voiceId: "af_heart", file: "af_heart.pt" },
      { voiceId: "am_fenrir", file: "am_fenrir.pt" },
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

const ENTRY_URL = "/api/catalog/entries/moonlit-narrators";
const ASSET_URL = "/api/catalog/entries/moonlit-narrators/assets/moonlit-preview.mp3";

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

function makeAssetResponse(status: number): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    blob: jest.fn<() => Promise<Blob>>().mockResolvedValue(new Blob(["fake-mp3-bytes"])),
  } as unknown as Response;
}

function cardFor(name: string): HTMLElement {
  const grid = screen.getByRole("list", { name: "Community catalog entries" });
  const nameNode = within(grid).getByText(name);
  const card = nameNode.closest("button");
  if (card === null) throw new Error(`No <button> ancestor for "${name}"`);
  return card;
}

describe("Feature: the voice-pack detail panel's honest-preview gate (F103.5, STORY-397 AC4)", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedUseRouter.mockReturnValue({ push: jest.fn(), refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);

    // jsdom implements neither `URL.createObjectURL`/`revokeObjectURL` (probe-verified, same as
    // font-pack-shelf-specimen.spec.tsx's own remarks) — minimal in-memory stand-ins.
    let counter = 0;
    URL.createObjectURL = jest.fn(() => `blob:mock-${counter++}`) as unknown as typeof URL.createObjectURL;
    URL.revokeObjectURL = jest.fn() as unknown as typeof URL.revokeObjectURL;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  async function openMoonlitNarratorsDetail(fetchMock: jest.MockedFunction<typeof fetch>): Promise<void> {
    ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
    global.fetch = fetchMock;
    render(
      <ConfirmDialogProvider>
        <PersonaCatalogClient
          activeKind="voice-pack"
          initialIndex={{ entries: [VOICE_PACK_ENTRY], fetchedAt: "2026-09-07T00:00:00Z", unreachable: false }}
        />
        <Toaster />
      </ConfirmDialogProvider>
    );
    fireEvent.click(cardFor("Moonlit Narrators"));
    await screen.findByLabelText("Voice pack preview");
  }

  describe("Scenario: the Install button is withheld until the preview proves playable", () => {
    it("renders no Install button while the preview clip is still loading", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === ENTRY_URL) return makeJsonResponse(200, VOICE_PACK_DETAIL);
        if (url === ASSET_URL) return makeAssetResponse(200);
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;

      await openMoonlitNarratorsDetail(fetchMock);

      expect(screen.getByText("Loading the preview…")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Install" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Re-install" })).not.toBeInTheDocument();
    });

    it("shows the Install button once the <audio> element itself fires canplay", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === ENTRY_URL) return makeJsonResponse(200, VOICE_PACK_DETAIL);
        if (url === ASSET_URL) return makeAssetResponse(200);
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;

      await openMoonlitNarratorsDetail(fetchMock);
      expect(screen.queryByRole("button", { name: "Install" })).not.toBeInTheDocument();

      const audio = screen.getByLabelText("Voice pack preview");
      await act(async () => {
        fireEvent.canPlay(audio);
      });

      expect(screen.getByRole("button", { name: "Install" })).toBeInTheDocument();
      expect(screen.queryByText("Loading the preview…")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: a preview that never loads refuses install, honestly (F103.5)", () => {
    it("shows the pinned failure copy and never renders Install when the fetch itself is rejected", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === ENTRY_URL) return makeJsonResponse(200, VOICE_PACK_DETAIL);
        if (url === ASSET_URL) throw new Error("network down");
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;

      global.fetch = fetchMock;
      ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
      render(
        <ConfirmDialogProvider>
          <PersonaCatalogClient
            activeKind="voice-pack"
            initialIndex={{ entries: [VOICE_PACK_ENTRY], fetchedAt: "2026-09-07T00:00:00Z", unreachable: false }}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );
      fireEvent.click(cardFor("Moonlit Narrators"));

      expect(await screen.findByRole("alert")).toHaveTextContent(
        "The preview could not be loaded, so this pack cannot be installed from here."
      );
      expect(screen.queryByRole("button", { name: "Install" })).not.toBeInTheDocument();
      expect(screen.queryByLabelText("Voice pack preview")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the license badge, not a licence, says so (T418 review round 2 finding F1-r2)", () => {
    it("labels the badge 'Voices: Synthetic voices', not 'License: …' (a voice pack carries no license field)", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === ENTRY_URL) return makeJsonResponse(200, VOICE_PACK_DETAIL);
        if (url === ASSET_URL) return makeAssetResponse(200);
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;

      await openMoonlitNarratorsDetail(fetchMock);

      expect(screen.getByLabelText("Voices: Synthetic voices")).toBeInTheDocument();
    });
  });
});
