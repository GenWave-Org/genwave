// @jest-environment jsdom
// STORY-287 — Save-as-own, the editor flow (SPEC F104.13 · PLAN T207); API half in
// tests/GenWave.Host.Tests/Specs/Story287_SaveAsOwn.cs, including the byte-identical-copy proof
// (AC3) — this file only pins the CLIENT wiring: what EditorClient/SaveAsOwnModal do with a name/slug
// an operator supplies and with whatever response POST /api/themes/{slug}/save-as-own returns, the
// same "server owns the real gate, client renders its answer verbatim" split
// theme-catalog-preview-install.spec.tsx's own "shows the server's error copy" scenario already
// establishes for the sibling install flow.
//
// Mirrors theme-editor.spec.tsx's own fixtures (a two-theme picker, the assignable face set) and
// theme-catalog-preview-install.spec.tsx's own routed-fetch-mock + Toaster harness — this file needs
// both: EditorClient's own preview POST (fired on every mount/assignment) AND the save-as-own POST
// the new modal issues on Confirm.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, waitFor, within, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { Toaster } from "@/components/ui/toast";
import { EditorClient } from "../app/(authed)/editor/EditorClient";
import type { ThemeSummaryDto } from "../app/(authed)/editor/types";

const CATS_WHISKER: ThemeSummaryDto = {
  slug: "cats-whisker",
  name: "Cat's Whisker",
  author: "GenWave",
  fonts: {
    display: {
      family: "Fraunces",
      assets: [{ src: "/fonts/fraunces-variable-latin.woff2", weight: "400 600", style: "normal" }],
    },
    sans: {
      family: "Source Sans 3",
      assets: [{ src: "/fonts/source-sans-3-variable-latin.woff2", weight: "400", style: "normal" }],
    },
  },
  modes: {
    light: { bg: "#f6efe3", ink: "#2b2320" },
    dark: { bg: "#1e1713", ink: "#f0e7d8" },
  },
};

const TEST_PATTERN: ThemeSummaryDto = {
  slug: "test-pattern",
  name: "Test Pattern",
  author: "GenWave",
  fonts: {
    display: {
      family: "Fraunces",
      assets: [{ src: "/fonts/fraunces-variable-latin.woff2", weight: "400 600", style: "normal" }],
    },
    sans: {
      family: "Source Sans 3",
      assets: [{ src: "/fonts/source-sans-3-variable-latin.woff2", weight: "400", style: "normal" }],
    },
  },
  modes: {
    light: { bg: "#f6f4f1", ink: "#29231f" },
    dark: { bg: "#1a1511", ink: "#f2efe9" },
  },
};

const THEMES: ThemeSummaryDto[] = [CATS_WHISKER, TEST_PATTERN];
const ASSIGNABLE_FACES = [
  { family: "Fraunces", src: "/fonts/fraunces-variable-latin.woff2" },
  { family: "Source Sans 3", src: "/fonts/source-sans-3-variable-latin.woff2" },
];

const PREVIEW_URL = "/api/themes/preview";

function makeCssResponse(): Response {
  return {
    ok: true,
    status: 200,
    text: jest.fn<() => Promise<string>>().mockResolvedValue(".theme-live-preview {}\n"),
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue({}),
    headers: new Headers({ "content-type": "text/css" }),
  } as unknown as Response;
}

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    text: jest.fn<() => Promise<string>>().mockResolvedValue(JSON.stringify(body)),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

/** Routes every request this feature's flow can issue — the transient preview compose (fired on
 * mount and on every assignment) and one scriptable save-as-own POST — to scriptable responses.
 * Anything else throws, so a stray request fails the test loudly (mirrors
 * theme-catalog-preview-install.spec.tsx's own `themeFlowFetchMock`). */
function editorSaveFetchMock(saveResponse: Response, saveUrl: string): jest.MockedFunction<typeof fetch> {
  return jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const url = String(input);
    if (url === PREVIEW_URL) return makeCssResponse();
    if (url === saveUrl && init?.method === "POST") return saveResponse;
    throw new Error(`unexpected fetch ${url}`);
  }) as unknown as jest.MockedFunction<typeof fetch>;
}

async function renderEditor(fetchMock: jest.MockedFunction<typeof fetch>): Promise<void> {
  global.fetch = fetchMock;
  render(
    <>
      <EditorClient themes={THEMES} assignableFaces={ASSIGNABLE_FACES} />
      <Toaster />
    </>
  );
  await screen.findByTestId("theme-live-preview");
}

async function openSaveDialog(fetchMock: jest.MockedFunction<typeof fetch>): Promise<void> {
  await renderEditor(fetchMock);
  fireEvent.click(screen.getByRole("button", { name: "Save as own" }));
  await screen.findByRole("dialog");
}

describe("Feature: save-as-own from the editor", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: save writes an authored theme", () => {
    it("confirming save with a name lands the remix and it appears in the picker (T207, AC1)", async () => {
      const saveUrl = "/api/themes/my-remix/save-as-own";
      const fetchMock = editorSaveFetchMock(makeJsonResponse(200, { slug: "my-remix", name: "My Remix" }), saveUrl);
      await openSaveDialog(fetchMock);

      // The dialog opens with a SAFE default (never the base theme's own unedited name/slug — see
      // SaveAsOwnModal's own remarks) — this Fact overrides both with an operator-chosen name; the
      // slug field tracks it (untouched) down to "my-remix".
      const dialog = within(screen.getByRole("dialog"));
      fireEvent.change(dialog.getByLabelText("Name"), { target: { value: "My Remix" } });
      expect(dialog.getByLabelText("Slug")).toHaveValue("my-remix");

      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm save" }));
        await Promise.resolve();
      });

      // Exactly one save POST, to the route slug the operator typed, carrying the remix's own fonts
      // with the operator-supplied name/slug substituted in.
      const saveCalls = fetchMock.mock.calls.filter(([url]) => String(url) === saveUrl);
      expect(saveCalls).toHaveLength(1);
      const [, init] = saveCalls[0] as [string, RequestInit];
      expect(init.method).toBe("POST");
      const posted = JSON.parse(String(init.body)) as ThemeSummaryDto;
      expect(posted).toEqual({ ...CATS_WHISKER, slug: "my-remix", name: "My Remix" });

      // The dialog closes, a success toast confirms it, and the saved theme is IMMEDIATELY
      // selectable in the base-theme picker (SPEC F104.13) — no reload, no re-fetch.
      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
      expect(await screen.findByText('"My Remix" saved — selectable now.')).toBeInTheDocument();
      expect(screen.getByLabelText("Base theme")).toHaveValue("my-remix");
      const optionLabels = within(screen.getByLabelText("Base theme"))
        .getAllByRole("option")
        .map((option) => option.textContent);
      expect(optionLabels).toContain("My Remix");
    });
  });

  describe("Scenario: authored-overwrite disclosure (T207, F2)", () => {
    it("re-saving onto a theme this session already authored discloses an update, not a refusal warning", async () => {
      // Given a remix already saved once this session (its slug is now BOTH in the base-theme
      // picker's own themes list AND known-authored — SaveAsOwnModal's own authoredSlugs prop),
      const saveUrl = "/api/themes/my-remix/save-as-own";
      const fetchMock = editorSaveFetchMock(makeJsonResponse(200, { slug: "my-remix", name: "My Remix" }), saveUrl);
      await openSaveDialog(fetchMock);
      const firstDialog = within(screen.getByRole("dialog"));
      fireEvent.change(firstDialog.getByLabelText("Name"), { target: { value: "My Remix" } });
      await act(async () => {
        fireEvent.click(firstDialog.getByRole("button", { name: "Confirm save" }));
        await Promise.resolve();
      });
      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

      // When the operator re-opens Save-as-own and types that SAME name (the slug field tracks it
      // down to the identical "my-remix" slug, untouched),
      fireEvent.click(screen.getByRole("button", { name: "Save as own" }));
      const reopenedDialog = within(await screen.findByRole("dialog"));
      fireEvent.change(reopenedDialog.getByLabelText("Name"), { target: { value: "My Remix" } });

      // Then the dialog discloses an UPDATE to the theme it already knows it authored — never the
      // imported/shipped refusal warning, which would be a false alarm for a theme this route itself
      // just wrote.
      expect(await reopenedDialog.findByRole("status")).toHaveTextContent(
        'Will update your existing theme "My Remix".'
      );
    });
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: saves pass the same law", () => {
    it("a refused save surfaces the import route's copy verbatim (T207, AC3)", async () => {
      // The BYTE-IDENTICAL claim itself (import vs save-as-own, same bad manifest) is proven
      // server-side (Story287_SaveAsOwn.cs's own ALawViolatingSaveRefusesWithTheImportRoutesExactCopy)
      // — this Fact proves the CLIENT'S half: whatever `detail` the response carries renders
      // verbatim, unmodified, exactly the split theme-catalog-preview-install.spec.tsx's own
      // "shows the server's error copy inside the still-open dialog" scenario already establishes.
      const saveUrl = "/api/themes/my-remix/save-as-own";
      const detail =
        "theme 'my-remix' references font(s) outside GenWave's vendored ∪ installed set: " +
        "/fonts/nonexistent.woff2 (vendored set: /fonts/fraunces-variable-latin.woff2, " +
        "/fonts/source-sans-3-variable-latin.woff2)";
      const fetchMock = editorSaveFetchMock(makeJsonResponse(400, { detail }), saveUrl);
      await openSaveDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      fireEvent.change(dialog.getByLabelText("Name"), { target: { value: "My Remix" } });

      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm save" }));
        await Promise.resolve();
      });

      // The refusal's own detail text renders verbatim inside the still-open dialog — never
      // paraphrased, truncated, or swallowed — and nothing was reported as saved.
      expect(await screen.findByRole("alert")).toHaveTextContent(detail);
      expect(screen.getByRole("dialog")).toBeInTheDocument();
      expect(screen.queryByText(/saved — selectable now\./)).not.toBeInTheDocument();
    });
  });
});
