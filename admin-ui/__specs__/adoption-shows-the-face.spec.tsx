// STORY-334 — Faces arrive with adoption: the trust-modal UI half (PLAN T297).
// Runner: Jest. Backend halves live in tests/GenWave.Host.Tests/Specs/Story334_FacesArriveWithAdoption.cs.
//
// Drives PersonaCardReviewModal directly (mirrors persona-card-review-modal.spec.tsx's own style —
// a mocked global fetch, no ConfirmDialogProvider/Toaster context needed) with the new
// avatarFile/catalogSlug props T297 adds (SPEC F128.7). Every other section of the modal (full card
// text, taste/lore/corrections, the malformed-card error state) is already pinned by
// persona-card-review-modal.spec.tsx — this file's own job is the face render and its own
// zero-writes/no-empty-slot invariants, not a second copy of that coverage.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { PersonaCardReviewModal } from "../app/(authed)/_components/PersonaCardReviewModal";

function fullCardJson(): string {
  return JSON.stringify({
    schemaVersion: 1,
    name: "Radio Rex",
    tagline: "Late-night lore",
    soul: "A grizzled jock who never sleeps.",
    quirks: [],
    voice: { engine: "kokoro", voiceId: "af_alloy", pace: 1.0, language: "en" },
    energyDisposition: 0.4,
    lore: [],
    corrections: [],
  });
}

function noop(): void {
  // intentionally empty — the default no-op handler for props this suite doesn't assert on
}

describe("Feature: informed adoption shows the face", () => {
  // Mirrors catalog-hire-verb.spec.tsx's own idiom — the one Fact below that overrides
  // global.fetch never restored it, so a later spec file (or a later `it` in a different jest
  // worker slot) could inherit that mock; save/restore around every Fact regardless of whether
  // it happens to touch fetch itself.
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  describe("Scenario: the modal renders everything the import carries", () => {
    it("renders the entry's avatar image alongside the full card text", () => {
      render(
        <PersonaCardReviewModal
          cardText={fullCardJson()}
          catalogSlug="midnight-mabel"
          avatarFile="midnight-mabel.avatar.png"
          onCancel={noop}
          onImported={noop}
        />
      );

      const dialog = within(screen.getByRole("dialog"));
      const face = dialog.getByRole("img", { name: "Radio Rex" });
      expect(face).toHaveAttribute("src", "/api/catalog/entries/midnight-mabel/assets/midnight-mabel.avatar.png");
    });

    it("issues zero write requests before the explicit confirm (the F90 trust posture)", () => {
      const mockFetch = jest.fn<typeof fetch>();
      global.fetch = mockFetch as unknown as typeof fetch;

      render(
        <PersonaCardReviewModal
          cardText={fullCardJson()}
          catalogSlug="midnight-mabel"
          avatarFile="midnight-mabel.avatar.png"
          onCancel={noop}
          onImported={noop}
        />
      );

      // Opening the modal — face included — never touches fetch: the face's own image GET rides a
      // plain <img src>, and this posture's real subject is the modal's own POST-on-confirm-only
      // invariant (persona-card-review-modal.spec.tsx's own "zero fetch calls on open" pin, reaffirmed
      // here with a face present so a future regression can't reintroduce a write behind it).
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("a faceless entry's modal renders exactly as before — no empty image slot", () => {
      render(
        <PersonaCardReviewModal
          cardText={fullCardJson()}
          catalogSlug="midnight-mabel"
          avatarFile={null}
          onCancel={noop}
          onImported={noop}
        />
      );

      // No <img> at all — not a broken-image glyph, not a placeholder box (SPEC F128.9's own "no
      // fabricated art, no broken-image glyph" posture, extended here to the review modal).
      expect(screen.queryByRole("img")).not.toBeInTheDocument();
    });

    it("the file-upload origin (catalogSlug omitted) never mounts any face UI at all", () => {
      // Given the file-upload door — catalogSlug simply never passed at all (T104's own shape;
      // avatarFile deliberately set here too, proving it's catalogSlug === undefined ALONE that
      // gates PersonaCardReviewFace out of the tree, per ReviewBody's own remarks — not merely
      // that this door happens to never carry an avatarFile in practice),
      render(
        <PersonaCardReviewModal
          cardText={fullCardJson()}
          avatarFile="would-be-ignored.png"
          onCancel={noop}
          onImported={noop}
        />
      );

      // Then no <img> renders — the SAME "no face UI at all" shape a faceless catalog entry gets,
      // never an asset URL built off a slug this door has none of.
      expect(screen.queryByRole("img")).not.toBeInTheDocument();
    });
  });
});
