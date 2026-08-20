// @jest-environment jsdom
// STORY-333 — The worn face: Personas-page UI halves (PLAN T296).
//
// Runner: Jest. Turned live at T296 (was 7 it.todos). Backend halves live in
// tests/GenWave.Host.Tests/Specs/Story333_TheWornFace.cs. Drives the REAL PersonasClient (same
// harness as personas-card-editor.spec.tsx: fetch mock by METHOD+URL, ConfirmDialogProvider +
// Toaster) rather than a bespoke render of PersonaFace/PersonaFaceEditor/PersonaAvatarPackPicker/
// BulkApplySuggestedModal in isolation — this page's own card/detail split, and the toolbar's own
// "smaller honest surface" placement, are as much this feature's claim as any one component's own
// behavior.
//
// `<img>` never fires a real network request in jsdom — `fireEvent.error`/no-event-at-all stand in
// for "the browser tried to load this URL and it failed/succeeded", the same substitution
// AvatarItemFace's own sibling spec (wardrobe-avatar-packs.spec.tsx) already makes for the
// `file === null` branch; this file additionally proves the genuine `onError` branch, which that
// one never exercises.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const LENA: PersonaDto = {
  id: 4,
  name: "Late Night Lena",
  backstory: "A grizzled late-night jock.",
  style: "hushed, confiding",
  voice: "af_heart",
  slug: "late-night-lena",
  importedFrom: null,
  importedAt: null,
  soul: "",
  quirks: [],
  lore: [],
};

const REX: PersonaDto = {
  id: 1,
  name: "Radio Rex",
  backstory: "A grizzled late-night jock.",
  style: "Warm, gravelly, brief.",
  voice: "af_alloy",
  slug: "radio-rex",
  importedFrom: null,
  importedAt: null,
  soul: "",
  quirks: [],
  lore: [],
};

/** One installed pack (PLAN T294 wire shape): "Classic" offers itself to Lena by slug, "Neutral"
 * carries no suggestion at all, and "Grumpy" suggests a slug NO persona on the fixture roster
 * carries — the exact "only matches where a persona with that slug exists" case SPEC F128.5 draws. */
const WARM_GRINS_PACK = {
  slug: "warm-grins",
  name: "Warm Grins",
  items: [
    { name: "Classic", suggestedPersona: "late-night-lena" },
    { name: "Neutral", suggestedPersona: null },
    { name: "Grumpy", suggestedPersona: "never-hired-slug" },
  ],
};

// ---------------------------------------------------------------------------
// Fetch mock — METHOD+URL dispatch, same idiom as personas-card-editor.spec.tsx.
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

function makeFetchMock(routes: Record<string, RouteResponseSpec>): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = (init?.method ?? "GET").toUpperCase();
    const key = `${method} ${String(input)}`;
    const spec = routes[key];
    if (spec === undefined) {
      // VoiceControl's own mount fetch and the avatar-packs picker/toolbar's own mount fetch —
      // answered generically so a scenario that doesn't care about either doesn't have to declare
      // it every time (mirrors personas-card-editor.spec.tsx's own GET /api/voices default).
      if (key === "GET /api/voices") {
        return jsonResponse(200, ["af_alloy", "af_heart"]);
      }
      if (key === "GET /api/avatar-packs") {
        return jsonResponse(200, []);
      }
      throw new Error(`Unexpected fetch in this suite: ${key}`);
    }
    return jsonResponse(spec.status, spec.body ?? {});
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function jsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

function callsTo(mockFetch: jest.MockedFunction<typeof fetch>, method: string, urlSubstring: string): unknown[] {
  return mockFetch.mock.calls.filter(
    ([input, init]) =>
      (init?.method ?? "GET").toUpperCase() === method && String(input).includes(urlSubstring)
  );
}

function renderClient(personas: PersonaDto[]): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      <PersonasClient initialPersonas={personas} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function clickEdit(name: string): void {
  const row = screen.getByText(name).closest("tr");
  if (row === null) throw new Error(`no roster row for ${name}`);
  fireEvent.click(within(row).getByRole("button", { name: /edit/i }));
}

describe("Feature: Personas wear faces in the console", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the face renders where the persona does", () => {
    it("shows the worn face on the persona card and detail", () => {
      makeFetchMock({});
      renderClient([LENA]);

      // "card" — the roster row's own thumbnail, rendered before any edit is opened.
      expect(screen.getByAltText("Late Night Lena's face")).toHaveAttribute(
        "src",
        "/api/personas/4/avatar"
      );

      // "detail" — the editor's own portrait, once opened; card and detail both point at the
      // SAME admin read route (no cache-bust query param yet — no write has happened this
      // session).
      clickEdit(LENA.name);
      const faceImages = screen.getAllByAltText("Late Night Lena's face");
      expect(faceImages).toHaveLength(2);
      for (const img of faceImages) {
        expect(img).toHaveAttribute("src", "/api/personas/4/avatar");
      }
    });

    it("shows the neutral Wireless placeholder for a faceless persona, never a broken image", () => {
      makeFetchMock({});
      renderClient([LENA]);

      // The browser attempts the card's own <img> and fails (a genuinely faceless persona's
      // honest 404, from this component's own point of view — PersonaFace draws no distinction).
      fireEvent.error(screen.getByAltText("Late Night Lena's face"));

      // Then the placeholder — a house glyph, never a broken <img> — replaces it outright.
      expect(screen.queryByAltText("Late Night Lena's face")).not.toBeInTheDocument();
      expect(screen.getByRole("img", { name: "Late Night Lena has no face set" })).toBeInTheDocument();
    });
  });

  describe("Scenario: suggestions offer, never write", () => {
    it("highlights a pack item whose suggestedPersona matches a persona slug", async () => {
      const mockFetch = makeFetchMock({ "GET /api/avatar-packs": { status: 200, body: [WARM_GRINS_PACK] } });
      renderClient([LENA, REX]);

      clickEdit(LENA.name);

      const classicRow = (await screen.findByText("Classic")).closest("li");
      if (classicRow === null) throw new Error("no row for Classic");
      expect(within(classicRow).getByText("Suggested")).toBeInTheDocument();

      const neutralRow = screen.getByText("Neutral").closest("li");
      if (neutralRow === null) throw new Error("no row for Neutral");
      expect(within(neutralRow).queryByText("Suggested")).not.toBeInTheDocument();

      // A highlighted suggestion is an OFFER, never a write on its own — the picker half of the
      // same no-auto-write invariant the bulk modal's own facts already pin below.
      expect(callsTo(mockFetch, "POST", "/from-pack")).toHaveLength(0);
    });

    it("bulk apply sits behind ONE confirm listing the exact item→persona mapping", async () => {
      const mockFetch = makeFetchMock({
        "GET /api/avatar-packs": { status: 200, body: [WARM_GRINS_PACK] },
      });
      renderClient([LENA, REX]);

      fireEvent.click(screen.getByRole("button", { name: "Apply suggested faces" }));

      const dialog = screen.getByRole("dialog", { name: "Apply suggested faces" });
      const mappingList = await within(dialog).findByRole("list", { name: "Suggested mapping" });
      const rows = within(mappingList).getAllByRole("listitem");

      // Exactly the ONE valid mapping — Neutral offers nothing, and Grumpy's own suggestion names
      // a slug no persona on this roster carries (SPEC F128.5's own filter).
      expect(rows).toHaveLength(1);
      expect(within(rows[0]!).getByText(/Classic/)).toBeInTheDocument();
      expect(within(rows[0]!).getByText("Late Night Lena")).toBeInTheDocument();
      expect(callsTo(mockFetch, "POST", "/from-pack")).toHaveLength(0);
    });

    it("closing the confirm issues zero writes", async () => {
      const mockFetch = makeFetchMock({
        "GET /api/avatar-packs": { status: 200, body: [WARM_GRINS_PACK] },
      });
      renderClient([LENA, REX]);

      fireEvent.click(screen.getByRole("button", { name: "Apply suggested faces" }));
      const dialog = await screen.findByRole("dialog", { name: "Apply suggested faces" });
      await within(dialog).findByRole("list", { name: "Suggested mapping" });

      fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

      expect(screen.queryByRole("dialog", { name: "Apply suggested faces" })).not.toBeInTheDocument();
      expect(callsTo(mockFetch, "POST", "/from-pack")).toHaveLength(0);
    });
  });

  describe("Scenario: upload and remove controls", () => {
    it("the upload control PUTs the chosen file to the persona avatar endpoint", async () => {
      const mockFetch = makeFetchMock({
        "PUT /api/personas/4/avatar": {
          status: 200,
          body: { personaId: 4, token: "a".repeat(32), source: "upload", importedFrom: null, updatedAt: "2026-08-16T00:00:00Z" },
        },
      });
      renderClient([LENA]);
      clickEdit(LENA.name);

      const file = new File(["fake-png-bytes"], "face.png", { type: "image/png" });
      fireEvent.change(screen.getByLabelText("Upload a face for Late Night Lena"), {
        target: { files: [file] },
      });

      await waitFor(() => {
        expect(callsTo(mockFetch, "PUT", "/api/personas/4/avatar")).toHaveLength(1);
      });
      const [, init] = callsTo(mockFetch, "PUT", "/api/personas/4/avatar")[0] as [unknown, RequestInit];
      expect(init.body).toBe(file);

      // The write bumps `avatarVersion`, which `PersonaFace` turns into a `?v=` cache-bust query
      // param on its own `src` (PersonaFace's own remarks) — the SOLE mechanism that forces the
      // browser to re-fetch this persona's face rather than keep showing the stale one it already
      // cached under the un-suffixed URL.
      await waitFor(() => {
        for (const img of screen.getAllByAltText("Late Night Lena's face")) {
          expect(img).toHaveAttribute("src", expect.stringMatching(/\?v=\d+$/));
        }
      });
    });

    it("refuses an oversized file client-side with the honest 4 MiB message, and never PUTs it", async () => {
      const mockFetch = makeFetchMock({});
      renderClient([LENA]);
      clickEdit(LENA.name);

      const oversized = new File(["a".repeat(4 * 1024 * 1024 + 1)], "huge.png", { type: "image/png" });
      fireEvent.change(screen.getByLabelText("Upload a face for Late Night Lena"), {
        target: { files: [oversized] },
      });

      await waitFor(() => {
        expect(screen.getByText(/over the 4 MiB limit/i)).toBeInTheDocument();
      });
      expect(callsTo(mockFetch, "PUT", "/api/personas/4/avatar")).toHaveLength(0);
    });

    it("remove issues the DELETE and the placeholder returns", async () => {
      const mockFetch = makeFetchMock({
        "DELETE /api/personas/4/avatar": { status: 204 },
      });
      renderClient([LENA]);
      clickEdit(LENA.name);

      fireEvent.click(screen.getByRole("button", { name: "Remove Late Night Lena's face" }));

      await waitFor(() => {
        expect(callsTo(mockFetch, "DELETE", "/api/personas/4/avatar")).toHaveLength(1);
      });

      // The version bump remounts a fresh <img> (still pointed at the same route) — the browser
      // then genuinely fails to load it (the face is gone), which this fires manually (see file
      // header comment).
      const freshImages = await screen.findAllByAltText("Late Night Lena's face");
      for (const img of freshImages) fireEvent.error(img);

      expect(screen.queryByAltText("Late Night Lena's face")).not.toBeInTheDocument();
      expect(screen.getAllByRole("img", { name: "Late Night Lena has no face set" }).length).toBeGreaterThan(0);
    });
  });
});
