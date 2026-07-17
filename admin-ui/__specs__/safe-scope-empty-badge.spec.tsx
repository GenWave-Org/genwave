// @jest-environment jsdom
// STORY-070 — Admin UI: SafeScope silent-on-drain badge (N3, SPEC F25.4/F25.5)
//
// Runner: Jest + jsdom + @testing-library/react. When GET /api/settings returns
// Station:SafeScope:LibraryIds with an effective value of [], the settings
// page's SafeScope picker renders an inline "Silent on drain — mksafe engaged"
// badge (or the equivalent copy documented on the story). The badge reflects
// the EFFECTIVE (bound) value from the GET response, not the form's staged
// (unsubmitted) selection — the shipped K5 confirm dialog handles the
// submission intent for a non-empty→empty change.
//
// Specs are it.todo pending until N3 ships (matches the M6 UI half's shipped
// pattern of it.todo stubs, PLAN.md notes). Un-pin against the rendered
// SettingsForm badge element once the copy is finalized in code review.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";
import type { LibraryDto } from "../lib/library";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SAFE_SCOPE_KEY = "Station:SafeScope:LibraryIds";

function makeSafeScopeSetting(override: Partial<SettingDto> = {}): SettingDto {
  return {
    key: SAFE_SCOPE_KEY,
    value: "[1]",
    source: "override",
    applyMode: "live",
    kind: "number-list",
    unit: "",
    ...override,
  };
}

function makeLibraries(): LibraryDto[] {
  return [
    { id: 1, name: "Lib Alpha", mediaCount: 10 },
    { id: 2, name: "Lib Beta", mediaCount: 5 },
  ];
}

/**
 * Change the selection state of a <select multiple> element in jsdom.
 * Sets the given option values as selected and fires a change event so
 * React's synthetic onChange sees the updated selectedOptions.
 */
function selectOptions(select: HTMLSelectElement, selectedValues: string[]): void {
  const selectedSet = new Set(selectedValues);
  Array.from(select.options).forEach((opt) => {
    opt.selected = selectedSet.has(opt.value);
  });
  fireEvent.change(select);
}

/**
 * SettingsForm calls useConfirm() unconditionally (SafeScope-empty save opens
 * the shared modal — SPEC F28.9, window.confirm is gone), so every render needs
 * a ConfirmDialogProvider ancestor.
 */
function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/**
 * The SafeScope field mounts SafeScopeAvailabilityBadge (STORY-105, SPEC F31.4–F31.5),
 * which mount-fetches GET /api/status. Tests that don't otherwise await a fetch-driven
 * change still need to let that pending promise settle inside act() once, or React logs
 * an "update not wrapped in act(...)" warning for the eventual setState.
 */
async function flushMountFetch(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: Silent-on-drain badge on the SafeScope picker
// ---------------------------------------------------------------------------

describe("Feature: Silent-on-drain badge on the SafeScope picker", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: The badge reflects the effective bound value", () => {
    // AC1 — effective [] triggers the badge.
    it(
      "AC1 — renders 'Silent on drain — mksafe engaged' when the effective SafeScope value is []",
      async () => {
        renderWithProviders(
          <SettingsForm
            settings={[makeSafeScopeSetting({ value: "[]" })]}
            libraries={makeLibraries()}
          />
        );

        expect(screen.getByText(/silent on drain/i)).toBeInTheDocument();

        await flushMountFetch();
      }
    );

    // AC2 — a non-empty effective value hides the badge.
    it(
      "AC2 — no silent-on-drain badge is present when the effective SafeScope value is non-empty",
      async () => {
        renderWithProviders(
          <SettingsForm
            settings={[makeSafeScopeSetting({ value: "[1]" })]}
            libraries={makeLibraries()}
          />
        );

        // Regression guard: no silent-on-drain badge with a non-empty effective value.
        expect(screen.queryByText(/silent on drain/i)).not.toBeInTheDocument();

        await flushMountFetch();
      }
    );
  });

  // -------------------------------------------------------------------------
  describe("Scenario: The badge tracks the effective value, not the staged selection", () => {
    // AC3 — when effective is non-empty and the operator deselects everything in the
    // picker, the badge does NOT appear (the K5 confirm dialog + PUT round-trip is
    // what commits the intent; staged UI state is not badged).
    it(
      "AC3 — deselecting every option in the picker without submitting does not surface the badge",
      async () => {
        renderWithProviders(
          <SettingsForm
            settings={[makeSafeScopeSetting({ value: "[1]" })]}
            libraries={makeLibraries()}
          />
        );

        // Stage an empty selection in the picker without submitting
        const select = screen.getByRole("listbox") as HTMLSelectElement;
        selectOptions(select, []);

        // Badge must NOT appear — it reflects the effective (bound) value, not staged selection
        expect(screen.queryByText(/silent on drain/i)).not.toBeInTheDocument();

        await flushMountFetch();
      }
    );
  });

  // -------------------------------------------------------------------------
  describe("Scenario: The form remains usable when the badge is shown", () => {
    // AC4 — with the badge visible, selecting one or more libraries and submitting
    // succeeds; on 200 the badge disappears on the refreshed effective value.
    it.todo(
      "AC4 — with the badge visible, submitting a non-empty selection returns 200 and the badge disappears on the refreshed GET"
    );
  });
});
