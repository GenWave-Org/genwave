// @jest-environment jsdom
// SPEC F164, F165, STORY-397, PLAN T418 — voice-pack and jingle-pack install/uninstall verbs.
//
// Runner: Jest. Mirrors ad-pack-shelf-install.spec.tsx's own install-confirm idiom (200 POSTs with
// no body, toasts the installed pack) and wardrobe-uninstall-pack.spec.tsx's own uninstall idiom
// (useConfirm() → DELETE → 204 toasts + router.refresh(), 409 toasts the server's own explanation) —
// applied to BOTH new kinds, which (unlike Font/Avatar/Icon/AdPack) read their "Installed" state
// fresh off the server via `router.refresh()` rather than a locally-flipped `Set` (see
// `PersonaCatalogClient`'s own `installedVoicePackSlugs`/`installedJinglePackSlugs` remarks) — every
// success path here is pinned on `router.refresh()` having fired, not on a local chip flip. R4/R5's
// own ProblemDetails-field ruling: a voice-pack install failure toasts the problem's TITLE (its own
// short, actionable string); every other failure here (voice-pack uninstall, jingle-pack install AND
// uninstall) toasts the DETAIL instead. The preview-gate itself is NOT this file's concern (see
// voice-pack-shelf-preview-install.spec.tsx) — every voice-pack fixture below serves a preview asset
// that resolves immediately and fires `canplay` before Install is ever clicked.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within, fireEvent, waitFor, act } from "@testing-library/react";
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

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
  } as unknown as Response;
}

function make204(): Response {
  return { ok: true, status: 204, json: jest.fn<() => Promise<unknown>>().mockResolvedValue(undefined) } as unknown as Response;
}

function cardFor(name: string): HTMLElement {
  const grid = screen.getByRole("list", { name: "Community catalog entries" });
  const nameNode = within(grid).getByText(name);
  const card = nameNode.closest("button");
  if (card === null) throw new Error(`No <button> ancestor for "${name}"`);
  return card;
}

// ---------------------------------------------------------------------------
// Voice pack
// ---------------------------------------------------------------------------

const VOICE_ENTRY: CatalogShelfEntryDto = {
  slug: "moonlit-narrators",
  kind: "voice-pack",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

const VOICE_DETAIL: CatalogEntryDetailDto = {
  card: JSON.stringify({
    packName: "Moonlit Narrators",
    engine: "kokoro",
    preview: "moonlit-preview.mp3",
    voices: [{ voiceId: "af_heart", file: "af_heart.pt" }],
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

const VOICE_ENTRY_URL = "/api/catalog/entries/moonlit-narrators";
const VOICE_ASSET_URL = "/api/catalog/entries/moonlit-narrators/assets/moonlit-preview.mp3";
const VOICE_INSTALL_URL = "/api/voice-packs/moonlit-narrators/install";
const VOICE_UNINSTALL_URL = "/api/voice-packs/moonlit-narrators";

function makeAssetResponse(): Response {
  return { ok: true, status: 200, blob: jest.fn<() => Promise<Blob>>().mockResolvedValue(new Blob(["fake-mp3"])) } as unknown as Response;
}

/** Opens the voice-pack detail panel and waits for the preview to prove playable — the SAME
 * `fireEvent.canPlay` step voice-pack-shelf-preview-install.spec.tsx pins on its own, done here only
 * to REACH the install/uninstall buttons, never re-asserted as this file's own fact. Already-installed
 * packs render "Re-install" beside "Uninstall" rather than a bare "Install" — the button this helper
 * waits for tracks that same installed/not-installed split. */
async function openMoonlitNarratorsWithPlayablePreview(installedVoicePackSlugs: string[] = []): Promise<void> {
  ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
  render(
    <ConfirmDialogProvider>
      <PersonaCatalogClient
        activeKind="voice-pack"
        initialIndex={{ entries: [VOICE_ENTRY], fetchedAt: "2026-09-07T00:00:00Z", unreachable: false }}
        installedVoicePackSlugs={installedVoicePackSlugs}
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
  fireEvent.click(cardFor("Moonlit Narrators"));
  const audio = await screen.findByLabelText("Voice pack preview");
  await act(async () => {
    fireEvent.canPlay(audio);
  });
  const installLabel = installedVoicePackSlugs.includes("moonlit-narrators") ? "Re-install" : "Install";
  await screen.findByRole("button", { name: installLabel });
}

describe("Feature: voice-pack install/uninstall verbs (SPEC F164, STORY-397, PLAN T418)", () => {
  let originalFetch: typeof fetch;
  let refreshMock: jest.Mock;

  beforeEach(() => {
    originalFetch = global.fetch;
    refreshMock = jest.fn();
    mockedUseRouter.mockReturnValue({ push: jest.fn(), refresh: refreshMock } as unknown as ReturnType<typeof useRouter>);

    let counter = 0;
    URL.createObjectURL = jest.fn(() => `blob:mock-${counter++}`) as unknown as typeof URL.createObjectURL;
    URL.revokeObjectURL = jest.fn() as unknown as typeof URL.revokeObjectURL;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: confirming install posts with no body and refreshes on 200", () => {
    it("POSTs once with no body, toasts the installed voice ids, and calls router.refresh()", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        if (url === VOICE_ENTRY_URL) return makeJsonResponse(200, VOICE_DETAIL);
        if (url === VOICE_ASSET_URL) return makeAssetResponse();
        if (url === VOICE_INSTALL_URL && (init?.method ?? "GET") === "POST") {
          return makeJsonResponse(200, { slug: "moonlit-narrators", packName: "Moonlit Narrators", voiceIds: ["af_heart"] });
        }
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openMoonlitNarratorsWithPlayablePreview();
      fireEvent.click(screen.getByRole("button", { name: "Install" }));
      const dialog = within(await screen.findByRole("dialog"));

      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });

      const installCalls = fetchMock.mock.calls.filter(([url]) => String(url) === VOICE_INSTALL_URL);
      expect(installCalls).toHaveLength(1);
      const [, init] = installCalls[0] as [string, RequestInit];
      expect(init.method).toBe("POST");
      expect(init.body).toBeUndefined();

      expect(await screen.findByText('"Moonlit Narrators" installed (af_heart).')).toBeInTheDocument();
      await waitFor(() => expect(refreshMock).toHaveBeenCalled());
    });
  });

  describe("Scenario: a 409 install conflict toasts the problem's TITLE (R4 ruling)", () => {
    it("toasts the title, not the detail, and never refreshes", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        if (url === VOICE_ENTRY_URL) return makeJsonResponse(200, VOICE_DETAIL);
        if (url === VOICE_ASSET_URL) return makeAssetResponse();
        if (url === VOICE_INSTALL_URL && (init?.method ?? "GET") === "POST") {
          return makeJsonResponse(409, {
            title: "Voice id already installed.",
            detail: 'Voice id "af_heart" is already installed by pack "other-pack".',
          });
        }
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openMoonlitNarratorsWithPlayablePreview();
      fireEvent.click(screen.getByRole("button", { name: "Install" }));
      const dialog = within(await screen.findByRole("dialog"));

      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });

      // Two renderings of the SAME title-first message: the toast, and the dialog's own inline alert.
      expect(await screen.findAllByText("Voice id already installed.")).toHaveLength(2);
      expect(screen.queryByText(/is already installed by pack/)).not.toBeInTheDocument();
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: a 400 not_supported_engine refusal leaves the shelf on Install (STORY-396 AC4)", () => {
    it("never refreshes, and closing the dialog shows the SAME Install button, never Installed/Uninstall", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        if (url === VOICE_ENTRY_URL) return makeJsonResponse(200, VOICE_DETAIL);
        if (url === VOICE_ASSET_URL) return makeAssetResponse();
        if (url === VOICE_INSTALL_URL && (init?.method ?? "GET") === "POST") {
          return makeJsonResponse(400, {
            title: "This station cannot install this voice pack.",
            type: "not_supported_engine",
            detail: 'Pack "moonlit-narrators" declares engine "kokoro", but this station\'s primary voice engine is "piper".',
          });
        }
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openMoonlitNarratorsWithPlayablePreview();
      fireEvent.click(screen.getByRole("button", { name: "Install" }));
      const dialog = within(await screen.findByRole("dialog"));

      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });

      // Same title-first ProblemDetails posture the 409 case above already proves (R4 ruling
      // applies to every VoicePackInstallModal failure regardless of status code) — never refreshes,
      // so PersonaCatalogClient's installedVoicePackSlugSet is never asked to change.
      expect(await screen.findAllByText("This station cannot install this voice pack.")).toHaveLength(2);
      expect(refreshMock).not.toHaveBeenCalled();

      // Then closing the dialog (AC4's own "no half-state") returns to the exact button the card
      // started with — "Install", never a "Re-install"/"Installed" chip/"Uninstall" button a
      // successful install would have produced.
      fireEvent.click(dialog.getByRole("button", { name: "Cancel" }));
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Install" })).toBeInTheDocument();
      expect(screen.queryByText("Installed")).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Uninstall" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: uninstall confirms, then DELETEs on confirm", () => {
    it("204 toasts success and calls router.refresh()", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        const method = init?.method ?? "GET";
        if (url === VOICE_ENTRY_URL) return makeJsonResponse(200, VOICE_DETAIL);
        if (url === VOICE_ASSET_URL) return makeAssetResponse();
        if (url === VOICE_UNINSTALL_URL && method === "DELETE") return make204();
        throw new Error(`unexpected fetch ${url} ${method}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openMoonlitNarratorsWithPlayablePreview(["moonlit-narrators"]);
      fireEvent.click(screen.getByRole("button", { name: "Uninstall" }));
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Uninstall" }));
        await Promise.resolve();
      });

      expect(await screen.findByText('"Moonlit Narrators" uninstalled.')).toBeInTheDocument();
      await waitFor(() => expect(refreshMock).toHaveBeenCalled());
    });

    it("409 toasts the DETAIL (R4 ruling — the referenced-by names) and never refreshes", async () => {
      const detail = 'Voice pack "moonlit-narrators" is still referenced by ad spot(s) "spot-12" and cannot be uninstalled.';
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        const method = init?.method ?? "GET";
        if (url === VOICE_ENTRY_URL) return makeJsonResponse(200, VOICE_DETAIL);
        if (url === VOICE_ASSET_URL) return makeAssetResponse();
        if (url === VOICE_UNINSTALL_URL && method === "DELETE") {
          return makeJsonResponse(409, { title: "Voice pack is referenced.", detail });
        }
        throw new Error(`unexpected fetch ${url} ${method}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openMoonlitNarratorsWithPlayablePreview(["moonlit-narrators"]);
      fireEvent.click(screen.getByRole("button", { name: "Uninstall" }));
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Uninstall" }));
        await Promise.resolve();
      });

      expect(await screen.findByText(detail)).toBeInTheDocument();
      expect(screen.queryByText("Voice pack is referenced.")).not.toBeInTheDocument();
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });
});

// ---------------------------------------------------------------------------
// Jingle pack
// ---------------------------------------------------------------------------

const JINGLE_ENTRY: CatalogShelfEntryDto = {
  slug: "sunday-drive-jingles",
  kind: "jingle-pack",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

const JINGLE_DETAIL: CatalogEntryDetailDto = {
  card: JSON.stringify({
    packName: "Sunday Drive Jingles",
    assets: [{ file: "station-id-chime.mp3", sha256: "b".repeat(64), role: "station_id", title: "Station Chime", license: "CC0" }],
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

const JINGLE_ENTRY_URL = "/api/catalog/entries/sunday-drive-jingles";
const JINGLE_INSTALL_URL = "/api/jingle-packs/sunday-drive-jingles/install";
const JINGLE_UNINSTALL_URL = "/api/jingle-packs/sunday-drive-jingles";

async function openSundayDriveDetail(installedJinglePackSlugs: string[] = []): Promise<void> {
  ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
  render(
    <ConfirmDialogProvider>
      <PersonaCatalogClient
        activeKind="jingle-pack"
        initialIndex={{ entries: [JINGLE_ENTRY], fetchedAt: "2026-09-07T00:00:00Z", unreachable: false }}
        installedJinglePackSlugs={installedJinglePackSlugs}
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
  fireEvent.click(cardFor("Sunday Drive Jingles"));
  const installLabel = installedJinglePackSlugs.includes("sunday-drive-jingles") ? "Re-install" : "Install";
  await screen.findByRole("button", { name: installLabel });
}

describe("Feature: jingle-pack install/uninstall verbs (SPEC F165, STORY-397, PLAN T418)", () => {
  let originalFetch: typeof fetch;
  let refreshMock: jest.Mock;

  beforeEach(() => {
    originalFetch = global.fetch;
    refreshMock = jest.fn();
    mockedUseRouter.mockReturnValue({ push: jest.fn(), refresh: refreshMock } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: confirming install posts with no body and refreshes on 200", () => {
    it("POSTs once with no body, toasts the installed asset count, and calls router.refresh()", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        if (url === JINGLE_ENTRY_URL) return makeJsonResponse(200, JINGLE_DETAIL);
        if (url === JINGLE_INSTALL_URL && (init?.method ?? "GET") === "POST") {
          return makeJsonResponse(200, {
            slug: "sunday-drive-jingles",
            packName: "Sunday Drive Jingles",
            assets: [{ file: "station-id-chime.mp3", role: "station_id", mediaId: 42 }],
          });
        }
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openSundayDriveDetail();
      fireEvent.click(screen.getByRole("button", { name: "Install" }));
      const dialog = within(await screen.findByRole("dialog"));

      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });

      const installCalls = fetchMock.mock.calls.filter(([url]) => String(url) === JINGLE_INSTALL_URL);
      expect(installCalls).toHaveLength(1);
      const [, init] = installCalls[0] as [string, RequestInit];
      expect(init.method).toBe("POST");
      expect(init.body).toBeUndefined();

      expect(await screen.findByText('"Sunday Drive Jingles" installed (1 asset).')).toBeInTheDocument();
      await waitFor(() => expect(refreshMock).toHaveBeenCalled());
    });
  });

  describe("Scenario: a 409 install conflict toasts the DETAIL (R5 ruling — both verbs)", () => {
    it("toasts the detail naming the referencing spots, and never refreshes", async () => {
      const detail = 'Jingle pack "sunday-drive-jingles" is still referenced by ad spot(s) "spot-7" and cannot be re-installed.';
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        if (url === JINGLE_ENTRY_URL) return makeJsonResponse(200, JINGLE_DETAIL);
        if (url === JINGLE_INSTALL_URL && (init?.method ?? "GET") === "POST") {
          return makeJsonResponse(409, { title: "Jingle pack is referenced.", type: "jingle_pack_in_use", detail });
        }
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openSundayDriveDetail();
      fireEvent.click(screen.getByRole("button", { name: "Install" }));
      const dialog = within(await screen.findByRole("dialog"));

      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });

      expect(await screen.findAllByText(detail)).toHaveLength(2);
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: uninstall confirms, then DELETEs on confirm", () => {
    it("204 toasts success and calls router.refresh()", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        const method = init?.method ?? "GET";
        if (url === JINGLE_ENTRY_URL) return makeJsonResponse(200, JINGLE_DETAIL);
        if (url === JINGLE_UNINSTALL_URL && method === "DELETE") return make204();
        throw new Error(`unexpected fetch ${url} ${method}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openSundayDriveDetail(["sunday-drive-jingles"]);
      fireEvent.click(screen.getByRole("button", { name: "Uninstall" }));
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Uninstall" }));
        await Promise.resolve();
      });

      expect(await screen.findByText('"Sunday Drive Jingles" uninstalled.')).toBeInTheDocument();
      await waitFor(() => expect(refreshMock).toHaveBeenCalled());
    });

    it("409 (jingle_pack_in_use) toasts the DETAIL and never refreshes — the SAME type as install's own refusal", async () => {
      const detail = 'Jingle pack "sunday-drive-jingles" is still referenced by ad spot(s) "spot-7" and cannot be uninstalled.';
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        const method = init?.method ?? "GET";
        if (url === JINGLE_ENTRY_URL) return makeJsonResponse(200, JINGLE_DETAIL);
        if (url === JINGLE_UNINSTALL_URL && method === "DELETE") {
          return makeJsonResponse(409, { title: "Jingle pack is referenced.", type: "jingle_pack_in_use", detail });
        }
        throw new Error(`unexpected fetch ${url} ${method}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openSundayDriveDetail(["sunday-drive-jingles"]);
      fireEvent.click(screen.getByRole("button", { name: "Uninstall" }));
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Uninstall" }));
        await Promise.resolve();
      });

      expect(await screen.findByText(detail)).toBeInTheDocument();
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: cancelling either verb issues no request", () => {
    it("install cancel: closes the dialog and posts nothing", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === JINGLE_ENTRY_URL) return makeJsonResponse(200, JINGLE_DETAIL);
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openSundayDriveDetail();
      fireEvent.click(screen.getByRole("button", { name: "Install" }));
      await screen.findByRole("dialog");
      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(fetchMock.mock.calls.some(([url]) => String(url) === JINGLE_INSTALL_URL)).toBe(false);
      expect(refreshMock).not.toHaveBeenCalled();
    });

    it("uninstall cancel: closes the dialog and issues no DELETE", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === JINGLE_ENTRY_URL) return makeJsonResponse(200, JINGLE_DETAIL);
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      await openSundayDriveDetail(["sunday-drive-jingles"]);
      fireEvent.click(screen.getByRole("button", { name: "Uninstall" }));
      const dialog = await screen.findByRole("dialog");
      fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(fetchMock.mock.calls.some(([url]) => String(url) === JINGLE_UNINSTALL_URL)).toBe(false);
      expect(refreshMock).not.toHaveBeenCalled();
    });
  });
});
