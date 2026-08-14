// @jest-environment jsdom
// gh-#145 — settings help moves to a ? flyover on the setting title.
//
// F54 pushed help coverage to 100% of the allowlist, which made the always-on help paragraphs a
// wall of prose. The copy now lives in a per-title `?` flyover: shown on hover, on keyboard
// focus, and pinned open by click/tap (touch has no hover). The panel stays MOUNTED and merely
// `hidden` while closed — that is what keeps the settings-help-coverage parity gate's
// `setting-help-<key>` testids working unchanged, and gives `aria-describedby` a stable target.
// Inline space under the control is reserved for warnings (ApplyModeBadge, the rotation-coupling
// notice, SafeScope badges, validation errors) — pinned here by the "warnings stay inline"
// scenario.
//
// Runner: Jest (jsdom) + @testing-library/react — renderWithProviders style per
// settings-corrections-control.spec.tsx.

import { describe, it, expect } from "@jest/globals";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
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

function makeNumberSetting(key: string, value: string, overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key,
    value,
    source: "default",
    applyMode: "live",
    kind: "number",
    unit: "count",
    ...overrides,
  };
}

function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function helpTrigger(key: string): HTMLElement {
  // The trigger's accessible name speaks the key as words (colons/underscores are separator
  // noise when announced) — which also keeps the house `getByLabelText(new RegExp(key))` idiom
  // pointing solely at the field input.
  return screen.getByRole("button", { name: `Help: ${key.replace(/[:_]/g, " ")}` });
}

function helpPanel(key: string): HTMLElement {
  return screen.getByTestId(`setting-help-${key}`);
}

// ---------------------------------------------------------------------------
// Feature: settings help lives in a ? flyover on the title (gh-#145)
// ---------------------------------------------------------------------------

describe("Feature: settings help lives in a ? flyover on the title", () => {
  describe("Scenario: the page renders without the help prose visible", () => {
    it("the help panel is mounted (parity gate keeps its testid) but hidden until asked for", () => {
      renderWithProviders(
        <SettingsForm settings={[makeNumberSetting(RECENT_WINDOW_KEY, "50")]} />
      );

      const panel = helpPanel(RECENT_WINDOW_KEY);
      expect(panel).toBeInTheDocument();
      expect(panel).not.toBeVisible();
      expect(panel.textContent ?? "").toMatch(/recently-played tracks/i);
    });

    it("a ? affordance renders at the setting title, collapsed", () => {
      renderWithProviders(
        <SettingsForm settings={[makeNumberSetting(RECENT_WINDOW_KEY, "50")]} />
      );

      expect(helpTrigger(RECENT_WINDOW_KEY)).toHaveAttribute("aria-expanded", "false");
    });
  });

  describe("Scenario: click/tap toggles the flyover (touch has no hover)", () => {
    it("clicking the ? pins the help open, and clicking again dismisses it", () => {
      renderWithProviders(
        <SettingsForm settings={[makeNumberSetting(RECENT_WINDOW_KEY, "50")]} />
      );
      const trigger = helpTrigger(RECENT_WINDOW_KEY);

      fireEvent.click(trigger);
      expect(helpPanel(RECENT_WINDOW_KEY)).toBeVisible();
      expect(trigger).toHaveAttribute("aria-expanded", "true");

      fireEvent.click(trigger);
      expect(helpPanel(RECENT_WINDOW_KEY)).not.toBeVisible();
      expect(trigger).toHaveAttribute("aria-expanded", "false");
    });
  });

  describe("Scenario: keyboard access", () => {
    it("focusing the ? reveals the help, blurring hides it", () => {
      renderWithProviders(
        <SettingsForm settings={[makeNumberSetting(RECENT_WINDOW_KEY, "50")]} />
      );
      const trigger = helpTrigger(RECENT_WINDOW_KEY);

      fireEvent.focus(trigger);
      expect(helpPanel(RECENT_WINDOW_KEY)).toBeVisible();

      fireEvent.blur(trigger);
      expect(helpPanel(RECENT_WINDOW_KEY)).not.toBeVisible();
    });

    it("Escape dismisses a pinned flyover", () => {
      renderWithProviders(
        <SettingsForm settings={[makeNumberSetting(RECENT_WINDOW_KEY, "50")]} />
      );
      const trigger = helpTrigger(RECENT_WINDOW_KEY);

      fireEvent.click(trigger);
      expect(helpPanel(RECENT_WINDOW_KEY)).toBeVisible();

      fireEvent.keyDown(trigger, { key: "Escape" });
      expect(helpPanel(RECENT_WINDOW_KEY)).not.toBeVisible();
    });
  });

  describe("Scenario: hover access", () => {
    it("hovering the ? reveals the help, leaving hides it", () => {
      renderWithProviders(
        <SettingsForm settings={[makeNumberSetting(RECENT_WINDOW_KEY, "50")]} />
      );
      const trigger = helpTrigger(RECENT_WINDOW_KEY);

      fireEvent.mouseEnter(trigger);
      expect(helpPanel(RECENT_WINDOW_KEY)).toBeVisible();

      fireEvent.mouseLeave(trigger);
      expect(helpPanel(RECENT_WINDOW_KEY)).not.toBeVisible();
    });
  });

  describe("Scenario: assistive-tech wiring", () => {
    it("the trigger controls the panel and the field is described by it, open or closed", () => {
      renderWithProviders(
        <SettingsForm settings={[makeNumberSetting(RECENT_WINDOW_KEY, "50")]} />
      );

      const panel = helpPanel(RECENT_WINDOW_KEY);
      expect(panel.id).not.toBe("");
      expect(helpTrigger(RECENT_WINDOW_KEY)).toHaveAttribute("aria-controls", panel.id);

      // aria-describedby points at the (possibly hidden) panel — a hidden describedby target is
      // still read by assistive tech, so the description holds while the flyover is closed.
      const input = screen.getByLabelText(new RegExp(RECENT_WINDOW_KEY));
      expect(input).toHaveAttribute("aria-describedby", panel.id);
    });
  });

  describe("Scenario: warnings stay inline — only the help prose moved", () => {
    it("the rotation-coupling notice renders always-visible while the help stays in the flyover", () => {
      renderWithProviders(
        <SettingsForm
          settings={[
            makeNumberSetting(RECENT_WINDOW_KEY, "5"),
            makeNumberSetting(ARTIST_SEPARATION_KEY, "10"),
          ]}
        />
      );

      expect(screen.getByTestId("rotation-coupling-notice")).toBeVisible();
      expect(helpPanel(ARTIST_SEPARATION_KEY)).not.toBeVisible();
    });

    it("the applyMode badge stays inline in the title row", () => {
      renderWithProviders(
        <SettingsForm
          settings={[
            makeNumberSetting("GW_XFADE_MIN", "2", {
              applyMode: "engine-restart",
              unit: "seconds",
            }),
          ]}
        />
      );

      expect(
        screen.getByLabelText("Apply mode: applies after engine restart")
      ).toBeVisible();
      expect(helpPanel("GW_XFADE_MIN")).not.toBeVisible();
    });
  });
});
