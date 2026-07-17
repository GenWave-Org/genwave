// @jest-environment jsdom
// STORY-152 — The rotation knobs disclose their coupling (Epic Y / SPEC F56, closes gitea-#227).
//
// Runner: Jest (jsdom) + @testing-library/react. Implemented Y6 (2026-07-15): the F56.1 help
// copy (naming the RecentWindow coupling) and the F56.2 non-blocking cross-field notice, both in
// SettingsForm.tsx. No server-side rule exists or is added (F56.4) — the capped shape is legal.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const RECENT_WINDOW_KEY = "Station:Rotation:RecentWindow";
const ARTIST_SEPARATION_KEY = "Station:Rotation:ArtistSeparation";

function rotationSetting(key: string, value: string): SettingDto {
  return {
    key,
    value,
    source: "default",
    applyMode: "live",
    kind: "number",
    unit: "tracks",
  };
}

function makeRotationSettings(recentWindow: string, artistSeparation: string): SettingDto[] {
  return [
    rotationSetting(RECENT_WINDOW_KEY, recentWindow),
    rotationSetting(ARTIST_SEPARATION_KEY, artistSeparation),
  ];
}

function makeFetchMock(status: number, body: unknown = {}): jest.MockedFunction<typeof fetch> {
  const fn = jest
    .fn<typeof fetch>()
    .mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
      headers: new Headers({ "content-type": "application/json" }),
    } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

// ---------------------------------------------------------------------------
// Feature: ArtistSeparation discloses its RecentWindow coupling
// ---------------------------------------------------------------------------

describe("Feature: ArtistSeparation discloses its RecentWindow coupling", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the help text names the coupling", () => {
    it("Station:Rotation:ArtistSeparation's help text states separation is limited by RecentWindow (F56.1)", () => {
      renderWithProviders(<SettingsForm settings={makeRotationSettings("20", "2")} />);

      expect(
        screen.getByTestId(`setting-help-${ARTIST_SEPARATION_KEY}`)
      ).toHaveTextContent(/limited by RecentWindow/i);
    });

    it("the help text states RecentWindow=0 disables artist separation too (F56.1)", () => {
      renderWithProviders(<SettingsForm settings={makeRotationSettings("20", "2")} />);

      expect(
        screen.getByTestId(`setting-help-${ARTIST_SEPARATION_KEY}`)
      ).toHaveTextContent(/RecentWindow=0 disables artist separation too/i);
    });
  });

  describe("Scenario: a live inline notice flags the capped shape", () => {
    it("renders a non-blocking notice when the form holds ArtistSeparation > RecentWindow (F56.2)", () => {
      renderWithProviders(<SettingsForm settings={makeRotationSettings("5", "10")} />);

      const notice = screen.getByTestId("rotation-coupling-notice");
      expect(notice).toBeInTheDocument();
      expect(notice).not.toHaveAttribute("role", "alert");
    });

    it("clears the notice when either field is edited back below the threshold (F56.2)", () => {
      // Editing ArtistSeparation down clears it.
      const { unmount } = renderWithProviders(
        <SettingsForm settings={makeRotationSettings("5", "10")} />
      );
      expect(screen.getByTestId("rotation-coupling-notice")).toBeInTheDocument();

      fireEvent.change(screen.getByLabelText(new RegExp(ARTIST_SEPARATION_KEY)), {
        target: { value: "3" },
      });
      expect(screen.queryByTestId("rotation-coupling-notice")).not.toBeInTheDocument();
      unmount();

      // Editing RecentWindow up clears it too.
      renderWithProviders(<SettingsForm settings={makeRotationSettings("5", "10")} />);
      expect(screen.getByTestId("rotation-coupling-notice")).toBeInTheDocument();

      fireEvent.change(screen.getByLabelText(new RegExp(RECENT_WINDOW_KEY)), {
        target: { value: "12" },
      });
      expect(screen.queryByTestId("rotation-coupling-notice")).not.toBeInTheDocument();
    });

    it("uses the form's CURRENT values, pre-submit — not the persisted ones (F56.2)", () => {
      // Persisted shape is NOT capped (2 <= 20) — the notice must be absent on load.
      renderWithProviders(<SettingsForm settings={makeRotationSettings("20", "2")} />);
      expect(screen.queryByTestId("rotation-coupling-notice")).not.toBeInTheDocument();

      // A live, unsaved edit pushes ArtistSeparation past RecentWindow — the notice must react
      // to this in-progress value, never the original settings prop.
      fireEvent.change(screen.getByLabelText(new RegExp(ARTIST_SEPARATION_KEY)), {
        target: { value: "25" },
      });
      expect(screen.getByTestId("rotation-coupling-notice")).toBeInTheDocument();
    });
  });

  describe("Scenario: the capped configuration still saves", () => {
    it("submission proceeds with the notice showing — it is a hint, not a validation error (F56.2, F56.4)", async () => {
      const mockFetch = makeFetchMock(200);
      // Starts uncapped so the edit below both changes the field AND creates the capped shape.
      renderWithProviders(<SettingsForm settings={makeRotationSettings("5", "5")} />);

      fireEvent.change(screen.getByLabelText(new RegExp(ARTIST_SEPARATION_KEY)), {
        target: { value: "10" },
      });
      expect(screen.getByTestId("rotation-coupling-notice")).toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: ARTIST_SEPARATION_KEY, value: "10" }]);

      await waitFor(() => {
        expect(screen.getByText("Settings saved.")).toBeInTheDocument();
      });
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });
  });
});
