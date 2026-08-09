// @jest-environment jsdom
// gh-#431 — Station Imaging "Bed (optional)" gets a ? help flyover, matching the settings page's
// SettingHelpFlyover pattern: the shared `HelpFlyover` (gh-#209 extraction), the exact idiom the
// booth log's Mode column header already uses (LlmCallsFeed.tsx).
//
// Runner: Jest (jsdom) + @testing-library/react. BedPicker has no standalone page of its own —
// it renders inside SafeContentClient's Generate form (safe-content-redesign.spec.tsx's own
// coverage target), so this spec mounts that same client. VoiceControl fetches GET /api/voices
// on mount (STORY-098/F29.5); the fetch mock here is a generic ok/empty-array stub so that
// unrelated fetch never fails, mirroring safe-content-redesign.spec.tsx's own VOICES_MOUNT_SPEC
// precedent.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { Toaster } from "@/components/ui/toast";
import { SafeContentClient } from "../app/(authed)/safe-content/SafeContentClient";
import type { SafeContentClientProps } from "../app/(authed)/safe-content/SafeContentClient";
import type { LibraryDto } from "../lib/library";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeLibraries(): LibraryDto[] {
  return [{ id: 7, name: "safe", mediaCount: 2 }];
}

function renderClient(overrides: Partial<SafeContentClientProps> = {}): ReturnType<typeof render> {
  const props: SafeContentClientProps = {
    libraries: makeLibraries(),
    initialLibraryId: 7,
    initialSegments: [],
    initialOutOfScope: false,
    defaultText: "You're listening to {StationName}. We'll be right back — stay tuned.",
    defaultTitle: "Please Stand By",
    ...overrides,
  };
  return render(
    <>
      <SafeContentClient {...props} />
      <Toaster />
    </>
  );
}

// ---------------------------------------------------------------------------
// Feature: the Bed field explains itself (gh-#431)
// ---------------------------------------------------------------------------

describe("Feature: the Bed field explains itself", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    global.fetch = jest.fn<typeof fetch>().mockResolvedValue({
      ok: true,
      status: 200,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue([]),
      headers: new Headers({ "content-type": "application/json" }),
    } as unknown as Response) as unknown as typeof fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: a ? flyover sits next to the Bed label", () => {
    it("renders the ducking/padding/bake-in/deployment-setting help copy, mounted but hidden until asked for", () => {
      renderClient();

      const trigger = screen.getByRole("button", { name: "Help: Bed" });
      expect(trigger).toHaveAttribute("aria-expanded", "false");

      const panel = screen.getByTestId("bed-help");
      expect(panel).toBeInTheDocument();
      expect(panel).not.toBeVisible();

      const copy = panel.textContent ?? "";
      expect(copy).toMatch(/mixed UNDER the generated voice/);
      expect(copy).toMatch(/main-catalog jingle or instrumental/);
      expect(copy).toMatch(/ducked \(−12 dB\)/);
      expect(copy).toMatch(/padded 1\.5 s before and after the voice/);
      expect(copy).toMatch(/loops if shorter/);
      expect(copy).toMatch(/honours the track's cue points/);
      expect(copy).toMatch(/baked into the audio file at generate time/);
      expect(copy).toMatch(/regenerating the segment/);
      expect(copy).toMatch(/Duck\/pad amounts are deployment settings \(env\), not live settings/);
    });

    it("does not change the Bed field's accessible name (still labelled 'Bed (optional)')", () => {
      renderClient();

      expect(screen.getByRole("combobox", { name: /bed \(optional\)/i })).toBeInTheDocument();
    });
  });
});
