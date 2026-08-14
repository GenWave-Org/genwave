// @jest-environment jsdom
// STORY-235 — One click, eyes open: informed catalog import (SPEC F90.5, F90.6;
// ARCHITECTURE.md "Trust ruling"; PLAN T103)
//
// Runner: Jest (jsdom) + @testing-library/react. `PersonaCardReviewModal` is driven directly
// (mirrors persona-export-import.spec.tsx's style for PersonaImportPanel) with a mocked global
// fetch — this component needs no ConfirmDialogProvider/Toaster context, it owns its own Radix
// Dialog and its own POST.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { useState, type ReactNode } from "react";
import {
  PersonaCardReviewModal,
  type PersonaCardReviewImportResult,
} from "../app/(authed)/_components/PersonaCardReviewModal";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

/** A full card with every F90.6-required section populated, including hostile markdown/HTML in
 * the free-text fields — the review must render every one of these verbatim, never interpreted. */
function fullCardJson(overrides: Record<string, unknown> = {}): string {
  return JSON.stringify({
    schemaVersion: 1,
    name: "Radio Rex",
    tagline: "Late-night lore",
    soul: "<b>Backstory</b>: a grizzled jock who **never** sleeps.",
    quirks: ["hums between tracks", "<script>alert('quirk')</script>"],
    voice: { engine: "kokoro", voiceId: "af_alloy", pace: 1.0, language: "en" },
    energyDisposition: 0.4,
    lore: ["Once played a 40-minute Zeppelin side.", "_italic-looking_ lore line"],
    corrections: [{ from: "<i>Rex</i>", to: "Rex" }],
    taste: [
      {
        predicate: { artist: "Radiohead", genre: null, tag: null },
        context: { daysOfWeek: [0, 3], startHour: 18, endHour: 22 },
        weight: 0.4,
      },
      {
        predicate: { artist: null, genre: null, tag: null },
        context: { daysOfWeek: [], startHour: null, endHour: null },
        weight: -0.6,
      },
    ],
    ...overrides,
  });
}

function makeResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
  } as unknown as Response;
}

function noop(): void {
  // intentionally empty — the default no-op handler for props this suite doesn't assert on
}

/** Waits one real macrotask — `PersonaCardReviewModal`'s backdrop dismissal (Radix's own
 * `DismissableLayer`) attaches its outside-pointerdown listener via a `setTimeout(fn, 0)`, so a
 * pointerdown fired synchronously right after `render()` lands before that listener exists. */
function tick(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/** A minimal opener + conditional mount, mirroring how `PersonaCatalogClient` actually renders
 * this modal (fresh mount on click, unmount on cancel/import) — the harness every focus-management
 * spec below drives, since `PersonaCardReviewModal` has no `Dialog.Trigger` of its own for Radix
 * to auto-restore focus to (review finding #1: that's exactly the defect being pinned here). */
function FocusHarness({
  onCancel,
  onImported,
}: {
  onCancel: () => void;
  onImported: (result: PersonaCardReviewImportResult) => void;
}): ReactNode {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Open review
      </button>
      {open && (
        <PersonaCardReviewModal
          cardText={fullCardJson()}
          onCancel={() => {
            setOpen(false);
            onCancel();
          }}
          onImported={(result) => {
            setOpen(false);
            onImported(result);
          }}
        />
      )}
    </>
  );
}

/** Opens `FocusHarness`'s modal the way a real click does — `.focus()` first, since jsdom (unlike
 * a real browser) never moves focus on a bare synthetic click (`feedback-primitives.spec.tsx`'s
 * own `openDialog` helper does the same for `confirm-dialog.tsx`). */
async function openHarnessReview(opener: HTMLElement): Promise<void> {
  opener.focus();
  fireEvent.click(opener);
  await screen.findByRole("dialog");
}

// ---------------------------------------------------------------------------
// Feature: The full-card review modal
// ---------------------------------------------------------------------------

describe("Feature: The full-card review modal", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: every card section renders, plain text only (AC1, F90.6)", () => {
    it("renders name/tagline/soul/quirks/voice/energy/corrections/lore/taste verbatim, plus samples when given", () => {
      render(
        <PersonaCardReviewModal
          cardText={fullCardJson()}
          catalogSlug="late-night-lena"
          samples={["Sample line one.", "<script>alert('sample')</script>"]}
          onCancel={noop}
          onImported={noop}
        />
      );

      const dialog = within(screen.getByRole("dialog"));

      expect(dialog.getByText("Radio Rex")).toBeInTheDocument();
      expect(dialog.getByText("Late-night lore")).toBeInTheDocument();
      expect(dialog.getByText("<b>Backstory</b>: a grizzled jock who **never** sleeps.")).toBeInTheDocument();
      expect(dialog.getByText("hums between tracks")).toBeInTheDocument();
      expect(dialog.getByText("<script>alert('quirk')</script>")).toBeInTheDocument();
      expect(dialog.getByText("Engine: kokoro · Voice: af_alloy")).toBeInTheDocument();
      expect(dialog.getByText("0.4")).toBeInTheDocument();
      expect(dialog.getByText("<i>Rex</i>")).toBeInTheDocument();
      expect(dialog.getByText("Rex")).toBeInTheDocument();
      expect(dialog.getByText("Once played a 40-minute Zeppelin side.")).toBeInTheDocument();
      expect(dialog.getByText("_italic-looking_ lore line")).toBeInTheDocument();
      expect(dialog.getByText("artist: Radiohead")).toBeInTheDocument();
      expect(dialog.getByText("Sun, Wed · 18:00–22:00 · weight +0.40")).toBeInTheDocument();
      expect(dialog.getByText("any track")).toBeInTheDocument();
      expect(dialog.getByText("any time · weight -0.60")).toBeInTheDocument();
      expect(dialog.getByText("Sample line one.")).toBeInTheDocument();
      expect(dialog.getByText("<script>alert('sample')</script>")).toBeInTheDocument();

      // React's default escaping only — none of the hostile strings ever became real markup.
      expect(document.querySelector("script")).toBeNull();
      expect(document.querySelector("b")).toBeNull();
      expect(document.querySelector("i")).toBeNull();
    });

    it("omits the Sample patter section entirely when no samples are given", () => {
      render(<PersonaCardReviewModal cardText={fullCardJson()} onCancel={noop} onImported={noop} />);

      expect(screen.queryByText("Sample patter")).not.toBeInTheDocument();
    });

    it("shows 'None' for empty quirks/lore/corrections/taste rather than an empty section", () => {
      render(
        <PersonaCardReviewModal
          cardText={fullCardJson({ quirks: [], lore: [], corrections: [], taste: [] })}
          onCancel={noop}
          onImported={noop}
        />
      );

      const dialog = within(screen.getByRole("dialog"));
      expect(dialog.getAllByText("None").length).toBe(4);
    });

    it("labels a correction row that isn't a real {from, to} pair instead of rendering a blank arrow", () => {
      render(
        <PersonaCardReviewModal
          cardText={fullCardJson({ corrections: [{ from: 42, to: null }] })}
          onCancel={noop}
          onImported={noop}
        />
      );

      expect(screen.getByText("Unreadable correction entry")).toBeInTheDocument();
    });
  });

  describe("Scenario: fields the review's named sections don't already show (review finding #6)", () => {
    it("renders an unknown top-level card field under 'Other fields in this card', verbatim value", () => {
      render(
        <PersonaCardReviewModal
          cardText={fullCardJson({ futureFeature: "some new capability" })}
          onCancel={noop}
          onImported={noop}
        />
      );

      const dialog = within(screen.getByRole("dialog"));
      expect(dialog.getByText("Other fields in this card")).toBeInTheDocument();
      expect(dialog.getByText("futureFeature")).toBeInTheDocument();
      expect(dialog.getByText(/"some new capability"/)).toBeInTheDocument();
    });

    it("also surfaces schemaVersion here — it isn't rendered by name anywhere else in the review", () => {
      render(<PersonaCardReviewModal cardText={fullCardJson()} onCancel={noop} onImported={noop} />);

      const dialog = within(screen.getByRole("dialog"));
      expect(dialog.getByText("Other fields in this card")).toBeInTheDocument();
      expect(dialog.getByText("schemaVersion")).toBeInTheDocument();
    });

    it("omits the 'Other fields' section entirely when every top-level key is already shown elsewhere", () => {
      const onlyKnownKeys = JSON.stringify({
        name: "Radio Rex",
        tagline: "",
        soul: "",
        quirks: [],
        voice: { engine: "", voiceId: "" },
        energyDisposition: 0,
        corrections: [],
        lore: [],
        taste: [],
      });
      render(<PersonaCardReviewModal cardText={onlyKnownKeys} onCancel={noop} onImported={noop} />);

      expect(screen.queryByText("Other fields in this card")).not.toBeInTheDocument();
    });

    it("surfaces a top-level __proto__ key in the Other-fields section instead of silently vanishing (review follow-up #1)", () => {
      // A raw JSON STRING, not a JS object literal — see persona-card-review.spec.ts's matching
      // unit test for why `{ __proto__: "pwned" }` written as source would test the wrong thing.
      render(
        <PersonaCardReviewModal
          cardText='{"name":"Radio Rex","__proto__":"pwned"}'
          onCancel={noop}
          onImported={noop}
        />
      );

      const dialog = within(screen.getByRole("dialog"));
      expect(dialog.getByText("Other fields in this card")).toBeInTheDocument();
      expect(dialog.getByText("__proto__")).toBeInTheDocument();
      expect(dialog.getByText(/"pwned"/)).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // Scenario: no import request without confirm (jest-pin, F90.6)
  // -------------------------------------------------------------------------

  describe("Scenario: no import request is ever issued without confirm", () => {
    it("issues zero fetch calls on open, on scroll, and on cancel", () => {
      const mockFetch = jest.fn<typeof fetch>();
      global.fetch = mockFetch as unknown as typeof fetch;
      const onCancel = jest.fn();

      render(<PersonaCardReviewModal cardText={fullCardJson()} catalogSlug="late-night-lena" onCancel={onCancel} onImported={noop} />);
      expect(mockFetch).not.toHaveBeenCalled();

      fireEvent.scroll(screen.getByRole("dialog"));
      expect(mockFetch).not.toHaveBeenCalled();

      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
      expect(onCancel).toHaveBeenCalledTimes(1);
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("calls onCancel and issues no fetch on Escape", () => {
      const mockFetch = jest.fn<typeof fetch>();
      global.fetch = mockFetch as unknown as typeof fetch;
      const onCancel = jest.fn();

      render(<PersonaCardReviewModal cardText={fullCardJson()} onCancel={onCancel} onImported={noop} />);

      fireEvent.keyDown(document, { key: "Escape", code: "Escape" });

      expect(onCancel).toHaveBeenCalledTimes(1);
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("calls onCancel and issues no fetch on a backdrop click (the third exit, review finding #4)", async () => {
      const mockFetch = jest.fn<typeof fetch>();
      global.fetch = mockFetch as unknown as typeof fetch;
      const onCancel = jest.fn();

      render(<PersonaCardReviewModal cardText={fullCardJson()} catalogSlug="late-night-lena" onCancel={onCancel} onImported={noop} />);
      await tick();

      const overlay = screen.getByTestId("persona-card-review-overlay");
      fireEvent.pointerDown(overlay);
      fireEvent.click(overlay);

      await waitFor(() => expect(onCancel).toHaveBeenCalledTimes(1));
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("initial focus is never the Confirm button, and activating whatever IS focused issues zero fetches", () => {
      // Today's Cancel-before-Confirm footer order is not what this pins — it pins the INVARIANT:
      // Radix moves initial focus to the first tabbable descendant, so if Confirm ever became that
      // element (a footer reorder), this goes red before a browser's native Enter-activates-the-
      // focused-button behavior could fire an import with nobody having clicked Confirm at all.
      const mockFetch = jest.fn<typeof fetch>();
      global.fetch = mockFetch as unknown as typeof fetch;

      render(<PersonaCardReviewModal cardText={fullCardJson()} catalogSlug="late-night-lena" onCancel={noop} onImported={noop} />);

      const confirmButton = screen.getByRole("button", { name: "Confirm import" });
      const initiallyFocused = document.activeElement;
      expect(initiallyFocused).not.toBeNull();
      expect(initiallyFocused).not.toBe(confirmButton);

      // `fireEvent.keyDown` alone never triggers a real <button>'s native Enter-activation in
      // jsdom (no `user-event` dependency here) — a direct click on whatever IS focused is the
      // faithful stand-in for what a real browser does when Enter lands on a focused button.
      if (initiallyFocused !== null) {
        fireEvent.keyDown(initiallyFocused, { key: "Enter", code: "Enter" });
        fireEvent.click(initiallyFocused);
      }

      expect(mockFetch).not.toHaveBeenCalled();
    });
  });

  // -------------------------------------------------------------------------
  // Scenario: focus restoration on close (review finding #1 — probe-verified defect)
  // -------------------------------------------------------------------------

  describe("Scenario: focus restoration on close", () => {
    it("restores focus to the element that opened it, on Cancel", async () => {
      const onCancel = jest.fn();
      render(<FocusHarness onCancel={onCancel} onImported={noop} />);
      const opener = screen.getByRole("button", { name: "Open review" });
      await openHarnessReview(opener);

      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

      expect(onCancel).toHaveBeenCalledTimes(1);
      await waitFor(() => expect(opener).toHaveFocus());
    });

    it("restores focus to the element that opened it, on Escape", async () => {
      render(<FocusHarness onCancel={noop} onImported={noop} />);
      const opener = screen.getByRole("button", { name: "Open review" });
      await openHarnessReview(opener);

      fireEvent.keyDown(document, { key: "Escape", code: "Escape" });

      await waitFor(() => expect(opener).toHaveFocus());
    });

    it("restores focus to the element that opened it, after a successful confirm", async () => {
      const mockFetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeResponse(201, { name: "Radio Rex", warnings: [] }));
      global.fetch = mockFetch as unknown as typeof fetch;

      render(<FocusHarness onCancel={noop} onImported={noop} />);
      const opener = screen.getByRole("button", { name: "Open review" });
      await openHarnessReview(opener);

      fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));

      await waitFor(() => expect(opener).toHaveFocus());
    });
  });

  // -------------------------------------------------------------------------
  // Scenario: confirm posts the raw bytes, not a re-serialization
  // -------------------------------------------------------------------------

  describe("Scenario: confirm posts the card's original bytes", () => {
    it("POSTs cardText verbatim to /api/personas/{slug}/import?catalogSlug=... when a catalog origin", async () => {
      const raw = fullCardJson();
      const mockFetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeResponse(201, { name: "Radio Rex", warnings: [] }));
      global.fetch = mockFetch as unknown as typeof fetch;
      const onImported = jest.fn<(result: PersonaCardReviewImportResult) => void>();

      render(
        <PersonaCardReviewModal cardText={raw} catalogSlug="late-night-lena" onCancel={noop} onImported={onImported} />
      );

      fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/personas/radio-rex/import?catalogSlug=late-night-lena");
      expect(init.method).toBe("POST");
      expect(init.headers).toEqual({ "Content-Type": "application/json" });
      expect(init.body).toBe(raw);

      await waitFor(() =>
        expect(onImported).toHaveBeenCalledWith({ name: "Radio Rex", created: true, warnings: [] })
      );
    });

    it("omits catalogSlug entirely for a file-upload origin (the T104 seam)", async () => {
      const raw = fullCardJson();
      const mockFetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeResponse(200, { name: "Radio Rex", warnings: [] }));
      global.fetch = mockFetch as unknown as typeof fetch;

      render(<PersonaCardReviewModal cardText={raw} onCancel={noop} onImported={noop} />);

      fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/personas/radio-rex/import");
    });

    it("surfaces F79.4 voice-resolution warnings on the result passed to onImported", async () => {
      const mockFetch = jest.fn<typeof fetch>().mockResolvedValue(
        makeResponse(200, { name: "Radio Rex", warnings: ['Voice "af_ghost" is not available.'] })
      );
      global.fetch = mockFetch as unknown as typeof fetch;
      const onImported = jest.fn<(result: PersonaCardReviewImportResult) => void>();

      render(<PersonaCardReviewModal cardText={fullCardJson()} onCancel={noop} onImported={onImported} />);
      fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));

      await waitFor(() =>
        expect(onImported).toHaveBeenCalledWith({
          name: "Radio Rex",
          created: false,
          warnings: ['Voice "af_ghost" is not available.'],
        })
      );
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: a malformed card is an error state, never a crash (sad path)", () => {
    it("shows an error state and disables Confirm for unparsable JSON", () => {
      const mockFetch = jest.fn<typeof fetch>();
      global.fetch = mockFetch as unknown as typeof fetch;

      render(<PersonaCardReviewModal cardText="not valid json" onCancel={noop} onImported={noop} />);

      expect(screen.getByRole("alert")).toHaveTextContent(/couldn.t be read/i);
      expect(screen.getByRole("button", { name: "Confirm import" })).toBeDisabled();

      fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("shows an error state for a card missing a usable name", () => {
      render(<PersonaCardReviewModal cardText={JSON.stringify({ tagline: "no name" })} onCancel={noop} onImported={noop} />);

      expect(screen.getByRole("alert")).toHaveTextContent(/couldn.t be read/i);
    });
  });

  describe("Scenario: the server rejects the import (sad path)", () => {
    it("renders the ProblemDetails detail verbatim and never calls onImported", async () => {
      const mockFetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeResponse(400, { detail: "Card schema version 7 is newer than this station's supported version 1." }));
      global.fetch = mockFetch as unknown as typeof fetch;
      const onImported = jest.fn();

      render(<PersonaCardReviewModal cardText={fullCardJson()} onCancel={noop} onImported={onImported} />);
      fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));

      await waitFor(() => {
        expect(screen.getByRole("alert")).toHaveTextContent(
          "Card schema version 7 is newer than this station's supported version 1."
        );
      });
      expect(onImported).not.toHaveBeenCalled();
    });

    it("renders a 409 name conflict the same way", async () => {
      const mockFetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(makeResponse(409, { detail: 'A persona named "Radio Rex" already exists.' }));
      global.fetch = mockFetch as unknown as typeof fetch;

      render(<PersonaCardReviewModal cardText={fullCardJson()} onCancel={noop} onImported={noop} />);
      fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));

      await waitFor(() => {
        expect(screen.getByRole("alert")).toHaveTextContent('A persona named "Radio Rex" already exists.');
      });
    });

    it("falls back to a generic message on a network error", async () => {
      global.fetch = jest.fn<typeof fetch>().mockRejectedValue(new Error("boom")) as unknown as typeof fetch;

      render(<PersonaCardReviewModal cardText={fullCardJson()} onCancel={noop} onImported={noop} />);
      fireEvent.click(screen.getByRole("button", { name: "Confirm import" }));

      await waitFor(() => {
        expect(screen.getByRole("alert")).toHaveTextContent("Network error — check your connection");
      });
    });
  });
});
