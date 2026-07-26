// @jest-environment jsdom
// STORY-233 — Browsing the shelf (SPEC F90.4a, F90.6; PLAN T102)
//
// Runner: Jest (jsdom). RTL drives PersonaCatalogClient directly (mirrors the
// PersonasClient/SafeContentClient harness in personas-page.spec.tsx). The server component
// (page.tsx) is exercised separately via the catalog-pages.spec.ts tree-walker house pattern,
// with a mocked global.fetch and next/headers.cookies().

jest.mock("next/headers", () => ({
  cookies: jest.fn().mockResolvedValue({ toString: () => "session=test-cookie" }),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, waitFor, within, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactNode } from "react";
import { PersonaCatalogClient } from "../app/(authed)/persona-catalog/PersonaCatalogClient";
import type {
  CatalogEntryDetailDto,
  CatalogIndexResponseDto,
  CatalogShelfEntryDto,
} from "../app/(authed)/persona-catalog/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const EVERYONE_ENTRY: CatalogShelfEntryDto = {
  slug: "late-night-lena",
  audience: "everyone",
  bestFor: ["late-night", "chill"],
};

const MATURE_ENTRY: CatalogShelfEntryDto = {
  slug: "gritty-gary",
  audience: "mature",
  bestFor: [],
};

const LENA_DETAIL: CatalogEntryDetailDto = {
  card: "{}",
  meta: "{}",
  fetchedAt: "2026-07-26T00:00:00Z",
  unreachable: false,
  audience: "everyone",
  bestFor: ["late-night", "chill"],
  author: "Test Author",
  description: "A warm late-night voice.",
  samplePatter: ["Line one.", "Line two."],
};

const GARY_DETAIL: CatalogEntryDetailDto = {
  card: "{}",
  meta: "{}",
  fetchedAt: "2026-07-26T00:00:00Z",
  unreachable: false,
  audience: "mature",
  bestFor: [],
  author: "Gary Author",
  description: "Gritty Gary's bio.",
  samplePatter: ["Gary's line."],
};

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

/** Finds the shelf card `<button>` for a given entry's displayed (prettified) name — scoped to
 * the entries grid so it never ambiguously matches the detail panel's own heading, which can
 * carry the same text once an entry is selected. */
function cardFor(name: string): HTMLElement {
  const grid = screen.getByRole("list", { name: "Persona catalog entries" });
  const nameNode = within(grid).getByText(name);
  const card = nameNode.closest("button");
  if (card === null) throw new Error(`No <button> ancestor for "${name}"`);
  return card;
}

/** A promise plus its own resolver, exposed separately — lets a test hold a `fetch()` call open
 * across a second interaction (T102 review, HIGH: the loadDetail race) rather than resolving
 * immediately like every other mock in this file. No `!` — `resolveFn` is guaranteed assigned by
 * the time the `Promise` executor returns (it runs synchronously), but the wrapper still guards it
 * dynamically instead of asserting that past the compiler. */
function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolveFn: ((value: T) => void) | undefined;
  const promise = new Promise<T>((res) => {
    resolveFn = res;
  });
  return {
    promise,
    resolve: (value: T) => {
      if (resolveFn === undefined) throw new Error("deferred: resolve called before its executor ran");
      resolveFn(value);
    },
  };
}

// ---------------------------------------------------------------------------
// Feature: Browsing the shelf
// ---------------------------------------------------------------------------

describe("Feature: Browsing the shelf", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the shelf renders (AC1)", () => {
    it("shows the 18+ badge exactly on the mature entry, never the everyone one", () => {
      render(
        <PersonaCatalogClient
          initialIndex={{ entries: [EVERYONE_ENTRY, MATURE_ENTRY], fetchedAt: "2026-07-26T00:00:00Z", unreachable: false }}
        />
      );

      expect(within(cardFor("Late Night Lena")).queryByLabelText("Mature content")).toBeNull();
      expect(within(cardFor("Gritty Gary")).getByLabelText("Mature content")).toBeInTheDocument();
    });

    it("shows bestFor chips when present, and none when the entry has no chips", () => {
      render(
        <PersonaCatalogClient
          initialIndex={{ entries: [EVERYONE_ENTRY, MATURE_ENTRY], fetchedAt: "2026-07-26T00:00:00Z", unreachable: false }}
        />
      );

      const lenaCard = cardFor("Late Night Lena");
      expect(within(lenaCard).getByText("late-night")).toBeInTheDocument();
      expect(within(lenaCard).getByText("chill")).toBeInTheDocument();

      expect(within(cardFor("Gritty Gary")).queryByLabelText("Best for")).toBeNull();
    });

    it("loads author, description, and sample patter into the detail panel on click", async () => {
      global.fetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeJsonResponse(200, LENA_DETAIL)) as unknown as typeof fetch;

      render(<PersonaCatalogClient initialIndex={{ entries: [EVERYONE_ENTRY], fetchedAt: "2026-07-26T00:00:00Z", unreachable: false }} />);

      fireEvent.click(screen.getByRole("button", { name: /Late Night Lena/ }));

      await waitFor(() => {
        expect(screen.getByText("A warm late-night voice.")).toBeInTheDocument();
      });
      expect(screen.getByText("By Test Author")).toBeInTheDocument();
      expect(screen.getByText("Line one.")).toBeInTheDocument();
      expect(screen.getByText("Line two.")).toBeInTheDocument();
      expect(global.fetch).toHaveBeenCalledWith("/api/catalog/entries/late-night-lena");
    });

    it("shows a visibly present but disabled Import button with a tooltip (T103 wires it)", async () => {
      global.fetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeJsonResponse(200, LENA_DETAIL)) as unknown as typeof fetch;

      render(<PersonaCatalogClient initialIndex={{ entries: [EVERYONE_ENTRY], fetchedAt: "2026-07-26T00:00:00Z", unreachable: false }} />);
      fireEvent.click(screen.getByRole("button", { name: /Late Night Lena/ }));

      const importButton = await screen.findByRole("button", { name: "Import" });
      expect(importButton).toHaveAttribute("aria-disabled", "true");

      fireEvent.focus(importButton);
      expect(screen.getByText("Import is coming in the next update")).toBeInTheDocument();
    });
  });

  describe("Scenario: stale detail responses never overwrite a newer selection (T102 review, HIGH)", () => {
    it("does not reopen the panel once a fetch resolves after the operator already collapsed it", async () => {
      const pending = deferred<Response>();
      global.fetch = jest.fn<typeof fetch>().mockReturnValue(pending.promise) as unknown as typeof fetch;

      render(<PersonaCatalogClient initialIndex={{ entries: [EVERYONE_ENTRY], fetchedAt: "2026-07-26T00:00:00Z", unreachable: false }} />);

      // Open — the fetch is still in flight.
      fireEvent.click(screen.getByRole("button", { name: /Late Night Lena/ }));
      expect(screen.getByRole("region", { name: "Persona details" })).toBeInTheDocument();

      // Collapse before the fetch resolves.
      fireEvent.click(screen.getByRole("button", { name: /Late Night Lena/ }));
      expect(screen.queryByRole("region", { name: "Persona details" })).not.toBeInTheDocument();

      // The stale fetch finally resolves — must NOT reopen the panel the operator already closed.
      await act(async () => {
        pending.resolve(makeJsonResponse(200, LENA_DETAIL));
        await Promise.resolve();
      });

      expect(screen.queryByRole("region", { name: "Persona details" })).not.toBeInTheDocument();
      expect(screen.queryByText("A warm late-night voice.")).not.toBeInTheDocument();
    });

    it("does not let a slow first selection's response overwrite a faster second selection", async () => {
      const lenaPending = deferred<Response>();
      const mockFetch = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === "/api/catalog/entries/late-night-lena") return lenaPending.promise;
        if (url === "/api/catalog/entries/gritty-gary") return makeJsonResponse(200, GARY_DETAIL);
        throw new Error(`unexpected fetch ${url}`);
      });
      global.fetch = mockFetch as unknown as typeof fetch;

      render(
        <PersonaCatalogClient
          initialIndex={{ entries: [EVERYONE_ENTRY, MATURE_ENTRY], fetchedAt: "2026-07-26T00:00:00Z", unreachable: false }}
        />
      );

      // Select Lena — her fetch hangs.
      fireEvent.click(screen.getByRole("button", { name: /Late Night Lena/ }));
      // Select Gary before Lena's fetch resolves — his resolves immediately.
      fireEvent.click(screen.getByRole("button", { name: /Gritty Gary/ }));

      await waitFor(() => {
        expect(screen.getByText("Gritty Gary's bio.")).toBeInTheDocument();
      });

      // Lena's slow response finally lands — must NOT flip the panel/highlight back to her.
      await act(async () => {
        lenaPending.resolve(makeJsonResponse(200, LENA_DETAIL));
        await Promise.resolve();
      });

      expect(screen.getByText("Gritty Gary's bio.")).toBeInTheDocument();
      expect(screen.queryByText("A warm late-night voice.")).not.toBeInTheDocument();
      expect(cardFor("Gritty Gary")).toHaveAttribute("aria-expanded", "true");
      expect(cardFor("Late Night Lena")).toHaveAttribute("aria-expanded", "false");
    });
  });

  describe("Scenario: plain text only (F90.6, AC2)", () => {
    it("renders an HTML/markdown-laden description and sample line verbatim, never interpreted", async () => {
      const dangerousDetail: CatalogEntryDetailDto = {
        ...LENA_DETAIL,
        description: "<b>HTML</b> and **markdown** should stay literal.",
        samplePatter: ["<script>alert('x')</script>"],
      };
      global.fetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeJsonResponse(200, dangerousDetail)) as unknown as typeof fetch;

      render(<PersonaCatalogClient initialIndex={{ entries: [EVERYONE_ENTRY], fetchedAt: "2026-07-26T00:00:00Z", unreachable: false }} />);
      fireEvent.click(screen.getByRole("button", { name: /Late Night Lena/ }));

      await waitFor(() => {
        expect(screen.getByText("<b>HTML</b> and **markdown** should stay literal.")).toBeInTheDocument();
      });
      expect(screen.getByText("<script>alert('x')</script>")).toBeInTheDocument();

      // Never interpreted as real markup — no actual <b> or <script> element exists anywhere.
      expect(document.querySelector("b")).toBeNull();
      expect(document.querySelector("script")).toBeNull();
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: unreachable is a state, not an error (AC3, sad path)", () => {
    it("renders a graceful catalog-unreachable empty state — no error page", () => {
      render(<PersonaCatalogClient initialIndex={{ entries: null, fetchedAt: null, unreachable: true }} />);

      expect(screen.getByText("Catalog unreachable")).toBeInTheDocument();
      expect(screen.getByText(/shelf will return/i)).toBeInTheDocument();
    });
  });

  describe("Scenario: an empty catalog is not the same as unreachable (sad path)", () => {
    it("renders the 'shelf will be stocked soon' empty state when entries is an empty array", () => {
      render(<PersonaCatalogClient initialIndex={{ entries: [], fetchedAt: "2026-07-26T00:00:00Z", unreachable: false }} />);

      expect(screen.getByText("Nothing on the shelf yet")).toBeInTheDocument();
      // Exact copy pinned (T102 review) so a future wording edit gets caught here.
      expect(
        screen.getByText("The shelf will be stocked soon — check back once the community catalog has entries.")
      ).toBeInTheDocument();
      expect(screen.queryByText("Catalog unreachable")).not.toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: The server page (page.tsx) wiring
// ---------------------------------------------------------------------------

/** Minimal tree-walker (mirrors catalog-pages.spec.ts's own local copy) — finds the first element
 * in a server component's returned tree whose function-component reference is `type`. */
function findElementByType(
  node: ReactNode,
  type: unknown
): { type: unknown; props: Record<string, unknown> } | null {
  if (node === null || node === undefined || typeof node !== "object") return null;
  if (Array.isArray(node)) {
    for (const child of node) {
      const found = findElementByType(child, type);
      if (found !== null) return found;
    }
    return null;
  }
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el.props !== undefined) {
    if (el.type === type) return el as { type: unknown; props: Record<string, unknown> };
    if (el.props["children"] !== undefined) return findElementByType(el.props["children"] as ReactNode, type);
  }
  return null;
}

function collectStrings(node: ReactNode, out: string[] = []): string[] {
  if (node === null || node === undefined || typeof node === "boolean") return out;
  if (typeof node === "string" || typeof node === "number") {
    out.push(String(node));
    return out;
  }
  if (Array.isArray(node)) {
    for (const child of node) collectStrings(child, out);
    return out;
  }
  const el = node as { props?: Record<string, unknown> };
  if (el.props?.["children"] !== undefined) collectStrings(el.props["children"] as ReactNode, out);
  return out;
}

function treeContains(node: ReactNode, text: string): boolean {
  return collectStrings(node).some((s) => s.includes(text));
}

describe("Feature: The Persona Catalog server page", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: index route wiring", () => {
    it("hands the fetched index straight through to PersonaCatalogClient as initialIndex", async () => {
      const indexBody: CatalogIndexResponseDto = {
        entries: [EVERYONE_ENTRY],
        fetchedAt: "2026-07-26T00:00:00Z",
        unreachable: false,
      };
      global.fetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeJsonResponse(200, indexBody)) as unknown as typeof fetch;

      const { default: PersonaCatalogPage } = await import("../app/(authed)/persona-catalog/page");
      const node = await PersonaCatalogPage();

      const clientEl = findElementByType(node, PersonaCatalogClient);
      expect(clientEl?.props["initialIndex"]).toEqual(indexBody);
    });
  });

  describe("Scenario: disabled surface is a bare 404 (SPEC F90.1, sad path)", () => {
    it("renders an inline 'Not found' page when GET /api/catalog/index 404s", async () => {
      global.fetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeJsonResponse(404, {})) as unknown as typeof fetch;

      const { default: PersonaCatalogPage } = await import("../app/(authed)/persona-catalog/page");
      const node = await PersonaCatalogPage();

      expect(treeContains(node, "Not found")).toBe(true);
    });
  });
});
