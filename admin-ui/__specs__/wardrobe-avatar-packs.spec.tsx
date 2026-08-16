// @jest-environment jsdom
// STORY-332 — Avatar packs into the library: the Wardrobe/shelf UI halves (PLAN T294).
// Runner: Jest. Backend halves (install/uninstall/list) live in
// tests/GenWave.Host.Tests/Specs/Story332_AvatarPacksIntoTheLibrary.cs.
//
// Two surfaces: `AvatarWardrobeClient` (the Avatars tab's own listing off `GET /api/avatar-packs` —
// mirrors font-pack-wardrobe.spec.tsx's own shape) and `PersonaCatalogClient`'s avatar-kind routing
// (the shelf's transient face-grid preview — mirrors font-pack-shelf-specimen.spec.tsx's own shape,
// simplified: `AvatarItemFace` loads through a plain `<img>`, not `SpecimenBlock`'s own fetch/Blob/
// FontFace machinery, so no jsdom Font Loading API stand-ins are needed here — see that component's
// own remarks for why). `timeZone="UTC"` pins the provenance chip's date half, the house idiom
// (T105/T187). Both `AvatarWardrobeClient` (via `AvatarUninstallPackButton`) and `PersonaCatalogClient`
// call `useRouter()` unconditionally, so both are dynamic-imported AFTER the `next/navigation` mock is
// in place — the wardrobe-tabs.spec.tsx/font-pack-wardrobe.spec.tsx established idiom.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach } from "@jest/globals";
import { render, screen, within, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import type { CatalogEntryDetailDto, CatalogShelfEntryDto } from "../app/(authed)/persona-catalog/types";
import type { PersonaCatalogClient as PersonaCatalogClientType } from "../app/(authed)/persona-catalog/PersonaCatalogClient";
import { WardrobeTabs } from "../app/(authed)/wardrobe/WardrobeTabs";
import type { AvatarWardrobeClient as AvatarWardrobeClientType } from "../app/(authed)/wardrobe/AvatarWardrobeClient";
import type { AvatarPackSummaryDto } from "../app/(authed)/wardrobe/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

let PersonaCatalogClient: typeof PersonaCatalogClientType;
let AvatarWardrobeClient: typeof AvatarWardrobeClientType;

beforeEach(async () => {
  mockedUseRouter.mockReturnValue(
    { push: jest.fn(), refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>
  );
  ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
  ({ AvatarWardrobeClient } = await import("../app/(authed)/wardrobe/AvatarWardrobeClient"));
});

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const WARM_GRINS_PACK: AvatarPackSummaryDto = {
  slug: "warm-grins",
  name: "Warm Grins",
  items: [
    { name: "Classic", suggestedPersona: "flip" },
    { name: "Retro", suggestedPersona: null },
  ],
  importedFrom: "warm-grins",
  importedAt: "2026-08-15T12:00:00Z",
};

const AVATAR_ENTRY: CatalogShelfEntryDto = {
  slug: "warm-grins",
  kind: "avatar",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

const AVATAR_DETAIL: CatalogEntryDetailDto = {
  card: JSON.stringify({
    packName: "Warm Grins",
    items: [{ name: "Classic", file: "classic.png", suggestedPersona: "flip" }],
  }),
  meta: "{}",
  fetchedAt: "2026-08-15T00:00:00Z",
  unreachable: false,
  audience: "everyone",
  bestFor: [],
  author: null,
  description: "A curated avatar pack for the wardrobe specs.",
  samplePatter: [],
  fontFamily: null,
  fontByteTotal: null,
  fontSpecimenFile: null,
  fontLicense: null,
  fontVersion: null,
  fontSubset: null,
  suggestedPersona: null,
  avatarItems: [{ name: "Classic", file: "classic.png", suggestedPersona: "flip" }],
  personaAvatarFile: null,
  // Deliberately DIFFERENT from `prettifySlug("warm-grins")` ("Warm Grins") — proves
  // AvatarDetailPanel's heading reads this WIRE field (PLAN T304 rider 4), not a coincidental
  // slug-derived match (see the "reads the manifest's own packName" fact below).
  packName: "Warm Grins & Co.",
  iconCount: null,
};

const ENTRY_URL = "/api/catalog/entries/warm-grins";

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    text: jest.fn<() => Promise<string>>().mockResolvedValue(JSON.stringify(body)),
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

describe("Feature: Avatar packs in the Wardrobe", () => {
  describe("Scenario: the Avatars tab lists installed packs", () => {
    it("shows every installed pack with its item grid", () => {
      render(
        <ConfirmDialogProvider>
          <AvatarWardrobeClient packs={[WARM_GRINS_PACK]} timeZone="UTC" />
        </ConfirmDialogProvider>
      );

      const list = screen.getByRole("list", { name: "Installed avatar packs" });
      const card = within(list).getByText("Warm Grins").closest("li");
      if (card === null) throw new Error("No <li> ancestor for the pack card");

      expect(within(card).getByText("Classic")).toBeInTheDocument();
      expect(within(card).getByText("Retro")).toBeInTheDocument();
      expect(within(card).getByText("Suggested: Flip")).toBeInTheDocument();
      expect(within(card).getByText("Installed · warm-grins · Aug 15, 2026")).toBeInTheDocument();
      expect(within(card).getByRole("button", { name: "Uninstall Warm Grins" })).toBeInTheDocument();
    });

    it("shows the Avatars tab even when no pack is installed (empty state, never a hidden tab)", () => {
      render(<WardrobeTabs activeTab="avatars" />);

      const nav = screen.getByRole("navigation", { name: "Wardrobe sections" });
      expect(within(nav).getByRole("link", { name: "Avatars" })).toHaveAttribute("href", "/wardrobe?tab=avatars");

      render(<AvatarWardrobeClient packs={[]} timeZone="UTC" catalogEnabled />);

      expect(screen.getByText("No avatar packs installed")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Browse the Community Catalog" })).toHaveAttribute(
        "href",
        "/persona-catalog"
      );
    });
  });

  describe("Scenario: shelf detail previews stay transient", () => {
    it("renders pack faces from the proxied hash-verified preview route before install", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === ENTRY_URL) return makeJsonResponse(200, AVATAR_DETAIL);
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      render(
        <PersonaCatalogClient
          activeKind="avatar"
          initialIndex={{ entries: [AVATAR_ENTRY], fetchedAt: "2026-08-15T00:00:00Z", unreachable: false }}
        />
      );
      fireEvent.click(cardFor("Warm Grins"));

      const image = await screen.findByAltText("Classic");
      expect(image).toHaveAttribute("src", "/api/catalog/entries/warm-grins/assets/classic.png");
    });

    it("the detail panel's own heading reads the manifest's own packName off the wire (PLAN T304 rider 4)", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === ENTRY_URL) return makeJsonResponse(200, AVATAR_DETAIL);
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      render(
        <PersonaCatalogClient
          activeKind="avatar"
          initialIndex={{ entries: [AVATAR_ENTRY], fetchedAt: "2026-08-15T00:00:00Z", unreachable: false }}
        />
      );
      // The SHELF card still reads prettifySlug(slug) ("Warm Grins") — only the shelf INDEX row has
      // no packName field; the click below opens the DETAIL panel, which does.
      fireEvent.click(cardFor("Warm Grins"));

      expect(await screen.findByRole("heading", { name: "Warm Grins & Co." })).toBeInTheDocument();
    });

    it("issues no install/write request from merely opening the detail", async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === ENTRY_URL) return makeJsonResponse(200, AVATAR_DETAIL);
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      render(
        <PersonaCatalogClient
          activeKind="avatar"
          initialIndex={{ entries: [AVATAR_ENTRY], fetchedAt: "2026-08-15T00:00:00Z", unreachable: false }}
        />
      );
      fireEvent.click(cardFor("Warm Grins"));
      await screen.findByRole("button", { name: "Install" });

      // Opening the detail only ever issues the ONE GET the entry fetch itself is (never a POST to
      // /install, never a DELETE) — the shelf's own "browsing costs nothing" contract, applied to the
      // avatar kind.
      const methods = fetchMock.mock.calls.map(([, init]) => (init as RequestInit | undefined)?.method ?? "GET");
      expect(methods.every((method) => method === "GET")).toBe(true);
      expect(fetchMock.mock.calls.some(([url]) => String(url).includes("/install"))).toBe(false);
    });

    it("renders the 'No face' tile — never an <img>, never an asset request — for an item with no verified file (R4)", async () => {
      // Given a manifest item whose `file` the index's own hash-verified `assets[]` never actually
      // declared (`CatalogAvatarItemDto`'s own remarks — `AvatarItemFace` degrades this to `null`),
      const detailWithUndeclaredFile: CatalogEntryDetailDto = {
        ...AVATAR_DETAIL,
        avatarItems: [
          { name: "Classic", file: "classic.png", suggestedPersona: "flip" },
          { name: "Retro", file: null, suggestedPersona: null },
        ],
      };
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === ENTRY_URL) return makeJsonResponse(200, detailWithUndeclaredFile);
        // The net: any OTHER fetch — including one this component should never issue for a
        // `file: null` item — fails the Fact outright rather than silently resolving.
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      render(
        <PersonaCatalogClient
          activeKind="avatar"
          initialIndex={{ entries: [AVATAR_ENTRY], fetchedAt: "2026-08-15T00:00:00Z", unreachable: false }}
        />
      );
      fireEvent.click(cardFor("Warm Grins"));

      // When the detail's face grid renders,
      await screen.findByAltText("Classic");

      // Then the undeclared-file item shows the degraded "No face" tile, with no <img> (and so no
      // asset request) of its own — and the ONLY fetch this whole flow ever issued is the entry
      // detail GET itself (the throw-on-unexpected mock above is the enforcement: a stray asset
      // request against the unverified name would already have failed this Fact).
      expect(screen.getByText("No face")).toBeInTheDocument();
      expect(screen.queryByAltText("Retro")).not.toBeInTheDocument();
      expect(fetchMock.mock.calls.map(([url]) => String(url))).toEqual([ENTRY_URL]);
    });
  });

  describe("Scenario: the widened kind union renders an icon-kind entry as still-hidden (rider 1)", () => {
    it("renders no card for an icon-kind entry — T304 owns its own tab", () => {
      const ICON_ENTRY: CatalogShelfEntryDto = {
        slug: "sunburst",
        kind: "icon",
        audience: "everyone",
        bestFor: [],
        preview: null,
        fontFamily: null,
        fontByteTotal: null,
      };

      render(
        <PersonaCatalogClient
          activeKind="persona"
          initialIndex={{ entries: [ICON_ENTRY], fetchedAt: "2026-08-15T00:00:00Z", unreachable: false }}
        />
      );

      // The persona tab is active and the (only) entry is icon-kind — the per-tab empty state
      // renders, never a misrouted persona card for an entry this client doesn't recognise.
      expect(screen.getByText("No personas on the shelf")).toBeInTheDocument();
    });
  });

  describe("Scenario: a pack's own display strings are bounded (rider 2)", () => {
    it("clamps an overlong manifest pack name rather than stretching the Wardrobe card", () => {
      const longName = "A".repeat(200);
      render(
        <ConfirmDialogProvider>
          <AvatarWardrobeClient packs={[{ ...WARM_GRINS_PACK, name: longName }]} timeZone="UTC" />
        </ConfirmDialogProvider>
      );

      expect(screen.queryByText(longName)).not.toBeInTheDocument();
      expect(screen.getByText(`${"A".repeat(80)}…`)).toBeInTheDocument();
    });
  });
});
