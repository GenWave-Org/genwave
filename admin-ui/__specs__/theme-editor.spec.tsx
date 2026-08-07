// @jest-environment jsdom
// STORY-286 — The editor mixes components (SPEC F104.11, F104.12 · PLAN T206)
//
// Runner: Jest. The v2 editor's base-theme + role pickers compose a transient scoped live preview
// through the SAME POST /api/themes/preview mechanism the theme catalog's own detail preview uses
// (T186, ThemeDetailPreview reused verbatim by EditorClient, not re-implemented) — this file pins
// the CLIENT wiring: which requests fire when a face is assigned, that the role pickers offer
// exactly vendored ∪ installed, and that nothing survives a fresh mount (the "closing the editor"
// proxy this environment can exercise — a real reload is proven structurally: EditorClient writes to
// no cookie/localStorage/sessionStorage anywhere in its own module, asserted directly below). The
// composer itself (ThemeCssComposer.ComposeScoped) is proven server-side in
// tests/GenWave.Host.Tests/Specs/Story274_ThemeCatalogPreview.cs; the two new GET routes this page's
// server component reads (GET /api/themes, GET /api/fonts/vendored) are proven server-side in
// tests/GenWave.Host.Tests/Specs/Story286_EditorComposesTheRemix.cs — this file only pins what
// EditorClient itself does with props it already has in hand.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { EditorClient } from "../app/(authed)/editor/EditorClient";
import type { ThemeSummaryDto } from "../app/(authed)/editor/types";

// ---------------------------------------------------------------------------
// Fixtures — two resolvable themes (mirrors the two real shipped manifests, cats-whisker/
// test-pattern) and the editor's own assignable face set, AS ALREADY UNIONED SERVER-SIDE (T206
// review finding F4: `GET /api/fonts/vendored` now returns vendored ∪ installed itself — Space
// Grotesk, the M1 golden installed pack, rides the SAME `vendoredFaces` prop as the curated set;
// `EditorClient` no longer takes a separate installed-packs prop to re-merge client-side).
// ---------------------------------------------------------------------------

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

// A theme whose display face isn't in VENDORED_FACES at all — proves review finding F4's "the base
// theme's current face is not among the options" case: the picker must show it as the truthfully
// selected value rather than a lying default.
const OFF_ASSIGNABLE_SET_THEME: ThemeSummaryDto = {
  slug: "off-assignable-theme",
  name: "Off Assignable",
  author: "GenWave",
  fonts: {
    display: {
      family: "Grenze Gotisch",
      assets: [{ src: "/fonts/grenze-gotisch-variable-latin.woff2", weight: "400", style: "normal" }],
    },
    sans: {
      family: "Source Sans 3",
      assets: [{ src: "/fonts/source-sans-3-variable-latin.woff2", weight: "400", style: "normal" }],
    },
  },
  modes: {
    light: { bg: "#f2f2f2", ink: "#1a1a1a" },
    dark: { bg: "#0f0f0f", ink: "#eaeaea" },
  },
};

const THEMES: ThemeSummaryDto[] = [CATS_WHISKER, TEST_PATTERN];

// The editor's own assignable set (`GET /api/fonts/vendored`, widened at T206 review finding F4) —
// vendored ∪ installed ALREADY unioned server-side; Space Grotesk (the M1 golden installed pack)
// rides this SAME array, not a separate installed-packs prop. Deliberately excludes "Grenze Gotisch"
// — OFF_ASSIGNABLE_SET_THEME's own display face — so its own Scenario below can prove the "not among
// the options" case against a real gap.
const VENDORED_FACES = [
  { family: "Fraunces", src: "/fonts/fraunces-variable-latin.woff2" },
  { family: "Source Sans 3", src: "/fonts/source-sans-3-variable-latin.woff2" },
  { family: "JetBrains Mono", src: "/fonts/jetbrains-mono-variable-latin.woff2" },
  { family: "Space Grotesk", src: "/fonts/space-grotesk-variable-latin.woff2" },
];

const PREVIEW_URL = "/api/themes/preview";

interface PostedManifest {
  fonts: {
    display: { family: string; assets: { src: string; weight: string; style: string }[] };
    sans: { family: string; assets: { src: string; weight: string; style: string }[] };
  };
}

/** The last `POST /api/themes/preview` this session issued, parsed back into a `PostedManifest` —
 * lets a Fact assert on the exact SHAPE the request body carries (weight/style included, not just
 * the family the CSS mock echoes back), the F3 fix's own load-bearing proof. */
function lastPostedManifest(fetchMock: jest.MockedFunction<typeof fetch>): PostedManifest {
  const calls = fetchMock.mock.calls.filter(([url]) => String(url) === PREVIEW_URL);
  const last = calls[calls.length - 1];
  if (last === undefined) throw new Error("no preview POST was ever made");
  const [, init] = last;
  return JSON.parse(String(init?.body)) as PostedManifest;
}

function makeCssResponse(status: number, css: string): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: jest.fn<() => Promise<string>>().mockResolvedValue(css),
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue({}),
    headers: new Headers({ "content-type": "text/css" }),
  } as unknown as Response;
}

/** Routes every request this component can ever issue to a scripted CSS response, echoing the
 * posted manifest's own display family into the returned custom property — the re-compose
 * assertions below read that back off the rendered `<style>` rather than asserting on the request
 * body a second way. Anything OTHER than a POST to PREVIEW_URL throws — a stray request (an
 * install/import/save call this component must never make, SPEC F104.12) fails the test loudly. */
function previewFetchMock(): jest.MockedFunction<typeof fetch> {
  return jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const url = String(input);
    if (url !== PREVIEW_URL) throw new Error(`unexpected fetch ${url}`);
    if (init?.method !== "POST") throw new Error(`unexpected ${String(init?.method)} to ${url}`);
    const posted = JSON.parse(String(init.body)) as PostedManifest;
    const css = `.theme-live-preview {\n  --font-display: "${posted.fonts.display.family}";\n  --font-sans: "${posted.fonts.sans.family}";\n}\n`;
    return makeCssResponse(200, css);
  }) as unknown as jest.MockedFunction<typeof fetch>;
}

/** Testing Library's default text queries ignore `<script>`/`<style>` content, so the composed
 * preview CSS — injected as a same-origin `<style>` element by `ThemeDetailPreview` — has to be read
 * directly off the DOM inside a `waitFor` rather than via `findByText`. Asserts the preview
 * container's own `<style>` text CONTAINS `expectedFragment` once the latest compose has landed. */
async function waitForPreviewCss(expectedFragment: string): Promise<void> {
  await waitFor(() => {
    const style = screen.getByTestId("theme-live-preview").querySelector("style");
    expect(style?.textContent ?? "").toContain(expectedFragment);
  });
}

describe("Feature: the editor mixes components", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
    window.localStorage.clear();
    window.sessionStorage.clear();
  });

  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: component mix only", () => {
    it("assigning a face to a role re-composes the scoped live preview (T206, AC1)", async () => {
      const fetchMock = previewFetchMock();
      global.fetch = fetchMock;
      render(<EditorClient themes={THEMES} vendoredFaces={VENDORED_FACES} />);

      // The preview already renders the base theme's OWN default face (Fraunces) before any
      // explicit assignment — AC1's "the editor with a base theme and the role pickers".
      const container = await screen.findByTestId("theme-live-preview");
      expect(container.querySelector("style")?.textContent).toContain('--font-display: "Fraunces"');

      // Assigning the installed Space Grotesk face to the Display role,
      fireEvent.change(screen.getByLabelText("Display face"), { target: { value: "/fonts/space-grotesk-variable-latin.woff2" } });

      // Re-composes the preview with the newly assigned family.
      await waitForPreviewCss('--font-display: "Space Grotesk"');
      expect(fetchMock.mock.calls.filter(([url]) => String(url) === PREVIEW_URL).length).toBeGreaterThanOrEqual(2);
    });

    it("role pickers offer vendored plus installed faces and nothing else (T206, AC1)", async () => {
      global.fetch = previewFetchMock();
      render(<EditorClient themes={THEMES} vendoredFaces={VENDORED_FACES} />);
      await screen.findByTestId("theme-live-preview");

      const displayPicker = within(screen.getByLabelText("Display face"));
      const optionLabels = displayPicker.getAllByRole("option").map((option) => option.textContent);

      // Exactly the vendored families plus the installed pack's family — no more, no less (AC1).
      expect(optionLabels).toEqual(["Fraunces", "Source Sans 3", "JetBrains Mono", "Space Grotesk"]);
      // The Sans picker offers the identical union — one shared option pool for both roles.
      const sansPicker = within(screen.getByLabelText("Sans face"));
      expect(sansPicker.getAllByRole("option").map((option) => option.textContent)).toEqual(optionLabels);
    });

    it("no token-level colour editing surface exists (T206, AC1)", async () => {
      global.fetch = previewFetchMock();
      render(<EditorClient themes={THEMES} vendoredFaces={VENDORED_FACES} />);
      await screen.findByTestId("theme-live-preview");

      // Exactly three controls exist — base theme, display face, sans face — no colour input, no
      // per-token swatch/text field anywhere (SPEC F104.11 "no token-level colour editing").
      expect(screen.getAllByRole("combobox")).toHaveLength(3);
      expect(screen.queryAllByRole("textbox")).toHaveLength(0);
      expect(document.querySelector('input[type="color"]')).toBeNull();
    });
  });

  describe("Scenario: the remix is ephemeral", () => {
    it("closing or reverting persists nothing (T206, AC2)", async () => {
      global.fetch = previewFetchMock();

      const { unmount } = render(<EditorClient themes={THEMES} vendoredFaces={VENDORED_FACES} />);
      await screen.findByTestId("theme-live-preview");

      // Assign a different base theme AND a different display face — a real, in-progress remix.
      fireEvent.change(screen.getByLabelText("Base theme"), { target: { value: "test-pattern" } });
      fireEvent.change(screen.getByLabelText("Display face"), { target: { value: "/fonts/space-grotesk-variable-latin.woff2" } });
      await waitForPreviewCss('--font-display: "Space Grotesk"');

      // Nothing was ever written to any client-side persistence mechanism — the remix lives in React
      // state alone (AC2's "nothing was persisted… at any point"). `jest.spyOn` doesn't reliably
      // wrap jsdom's Storage methods (a known jsdom Proxy quirk), so this reads the stores' own
      // state directly instead: zero keys is the honest "nothing written" proof either way.
      expect(window.localStorage).toHaveLength(0);
      expect(window.sessionStorage).toHaveLength(0);
      expect(document.cookie).toBe("");

      // "Closing the editor" (SPEC F104.12) is unmounting this component — a fresh mount (the
      // equivalent of reopening after a reload) starts from the FIRST theme's own default face
      // again, not the assignment just made.
      unmount();
      render(<EditorClient themes={THEMES} vendoredFaces={VENDORED_FACES} />);
      await screen.findByTestId("theme-live-preview");
      expect(screen.getByLabelText("Base theme")).toHaveValue("cats-whisker");
      expect(screen.getByLabelText("Display face")).toHaveValue("/fonts/fraunces-variable-latin.woff2");
    });

    it("only transient compose calls appear on the network (T206, AC2)", async () => {
      const fetchMock = previewFetchMock();
      global.fetch = fetchMock;
      render(<EditorClient themes={THEMES} vendoredFaces={VENDORED_FACES} />);
      await screen.findByTestId("theme-live-preview");

      fireEvent.change(screen.getByLabelText("Display face"), { target: { value: "/fonts/space-grotesk-variable-latin.woff2" } });
      await waitForPreviewCss('--font-display: "Space Grotesk"');
      fireEvent.change(screen.getByLabelText("Sans face"), { target: { value: "/fonts/jetbrains-mono-variable-latin.woff2" } });
      await waitForPreviewCss('--font-sans: "JetBrains Mono"');

      // Every single request this whole session issued targets POST /api/themes/preview. Fixed
      // (review finding N2): previewFetchMock's own throw on a stray request is NOT what proves
      // this — ThemeDetailPreview swallows any fetch rejection in its own try/catch and renders an
      // "error" state instead of letting it propagate, so a stray call fails silently in the UI, not
      // by throwing out of this test. `calls.every(...)` below, which inspects every URL this
      // session actually issued, is the real, load-bearing gate; `calls.length` only guards against
      // the vacuous "no bad calls because no calls at all" false positive.
      const calls = fetchMock.mock.calls;
      expect(calls.length).toBeGreaterThanOrEqual(3);
      expect(calls.every(([url]) => String(url) === PREVIEW_URL)).toBe(true);
    });
  });

  // ── T206 review finding F3: an unassigned role never degrades the base theme ─────────────

  describe("Scenario: an unassigned role never degrades the base theme (F3)", () => {
    it("composes the base theme's own font declaration byte-untouched when nothing is assigned", async () => {
      const fetchMock = previewFetchMock();
      global.fetch = fetchMock;
      render(<EditorClient themes={THEMES} vendoredFaces={VENDORED_FACES} />);
      await screen.findByTestId("theme-live-preview");
      await waitFor(() => expect(fetchMock).toHaveBeenCalled());

      // Neither role was ever assigned — the posted manifest's own fonts.display/fonts.sans are
      // CATS_WHISKER's verbatim declarations, weight "400 600" included, never the single-face/
      // 400/normal shape assignment produces (the bug this fix closes: an unmodified base theme
      // previewing with its weight range collapsed and any italic asset silently dropped).
      const posted = lastPostedManifest(fetchMock);
      expect(posted.fonts.display).toEqual(CATS_WHISKER.fonts.display);
      expect(posted.fonts.sans).toEqual(CATS_WHISKER.fonts.sans);
    });

    it("replaces only the assigned role with the single-face 400/normal shape", async () => {
      const fetchMock = previewFetchMock();
      global.fetch = fetchMock;
      render(<EditorClient themes={THEMES} vendoredFaces={VENDORED_FACES} />);
      await screen.findByTestId("theme-live-preview");

      fireEvent.change(screen.getByLabelText("Display face"), { target: { value: "/fonts/space-grotesk-variable-latin.woff2" } });
      await waitForPreviewCss('--font-display: "Space Grotesk"');

      // Display was assigned (replaced with the single-face 400/normal shape); Sans was never
      // touched, so it stays the base theme's own verbatim declaration, weight range included — both
      // halves pinned in the same Fact so a regression collapsing them back together (the original
      // F3 bug) fails on either.
      const posted = lastPostedManifest(fetchMock);
      expect(posted.fonts.display).toEqual({
        family: "Space Grotesk",
        assets: [{ src: "/fonts/space-grotesk-variable-latin.woff2", weight: "400", style: "normal" }],
      });
      expect(posted.fonts.sans).toEqual(CATS_WHISKER.fonts.sans);
    });
  });

  // ── T206 review finding F4: the picker never lies about the currently-selected face ──────

  describe("Scenario: the base theme's current face is always the truthfully selected value (F4)", () => {
    it("adds the base theme's own face as an extra option when it is outside the assignable set", async () => {
      global.fetch = previewFetchMock();
      render(
        <EditorClient
          themes={[CATS_WHISKER, TEST_PATTERN, OFF_ASSIGNABLE_SET_THEME]}
          vendoredFaces={VENDORED_FACES}
        />
      );
      await screen.findByTestId("theme-live-preview");

      // Switching to a base theme whose display face ("Grenze Gotisch") is NOT in VENDORED_FACES,
      fireEvent.change(screen.getByLabelText("Base theme"), { target: { value: "off-assignable-theme" } });

      // The Display picker shows it as the TRUTHFULLY selected value — never blank, never silently
      // defaulting to the first option (a lying default) — and still offers every real assignable
      // option alongside it.
      const displayPicker = screen.getByLabelText("Display face");
      await waitFor(() => expect(displayPicker).toHaveValue("/fonts/grenze-gotisch-variable-latin.woff2"));
      const optionLabels = within(displayPicker).getAllByRole("option").map((option) => option.textContent);
      expect(optionLabels).toEqual(["Fraunces", "Source Sans 3", "JetBrains Mono", "Space Grotesk", "Grenze Gotisch"]);
    });
  });
});
