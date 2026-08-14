// @jest-environment jsdom
// STORY-275 — Imported-theme provenance (SPEC F103.11, PLAN T187; review F1/F2)
//
// Runner: Jest. Every IMPORTED theme choice gets its own row, "<label> — Imported · <source> ·
// <date>" (the F90.7/db-25 persona-provenance pattern, folded into one text node — see
// `ThemeProvenanceBadge`'s own remarks in `SettingsForm.tsx`), so an owner can tell which themes
// were installed and re-find their source. A shipped default never gets a row of its own, and a
// shipped-only catalog renders no list at all.
//
// Surface (PLAN T187): the Settings page's `Station:Theme` field — the one owner-facing surface
// that names every shipped ∪ owner theme by choice (`StationSettingsAllowlist.ThemeChoices`,
// PLAN T183), mirroring `settings-choice-control.spec.tsx`'s own harness (`SettingsForm` driven
// directly, no fetch needed for a read-only render).
//
// review F1: the list renders off EVERY choice carrying provenance, not just the field's currently
// SAVED value — T186's catalog install makes a theme SELECTABLE, not active, so the primary
// STORY-275 case (a just-installed catalog theme, not yet chosen) must show a row too. The old
// savedValue-gated badge showed at most the ACTIVE theme's row; that gate is gone.
//
// review F2: `timeZone="UTC"` is pinned explicitly on every render — the persona precedent's own
// test-injection idiom (`PersonasClientProps.timeZone`, `personas-page.spec.tsx`) — rather than
// relying solely on jest.config.js's process-wide TZ/locale pin, which a single-file in-band
// `npx jest` run (jest.config.js's own documented boundary) can't retroactively fix once the
// process's ICU locale/timezone is already resolved; reviewer-verified to fail without this pin
// under `LC_ALL=de_DE.UTF-8`.

import { describe, it, expect } from "@jest/globals";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const CATALOG_IMPORTED_AT = "2026-07-21T09:05:00Z";
const FILE_IMPORTED_AT = "2026-07-20T14:32:00Z";

/** One shipped default plus two imported choices (one catalog, one file) — the common case a
 * station accumulates over time; `value` (the currently SAVED theme) defaults to the shipped
 * default so tests can flip it independently of which choices carry provenance (review F1). */
function makeThemeSetting(overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key: "Station:Theme",
    value: "cats-whisker",
    source: "default",
    applyMode: "live",
    kind: "choice",
    unit: "",
    choices: [
      { value: "cats-whisker", label: "Cat's Whisker", isDefault: true },
      {
        value: "midnight-drive",
        label: "Midnight Drive",
        importedFrom: "midnight-drive-catalog-entry",
        importedAt: CATALOG_IMPORTED_AT,
      },
      { value: "aurora-glow", label: "Aurora Glow", importedFrom: "file", importedAt: FILE_IMPORTED_AT },
    ],
    ...overrides,
  };
}

/** Every choice is a shipped default — no `station.theme` row backs any of them, so none carry
 * `importedFrom`/`importedAt` (SPEC F103.11, "no owner row exists"). */
function makeShippedOnlyThemeSetting(): SettingDto {
  return {
    key: "Station:Theme",
    value: "cats-whisker",
    source: "default",
    applyMode: "live",
    kind: "choice",
    unit: "",
    choices: [
      { value: "cats-whisker", label: "Cat's Whisker", isDefault: true },
      { value: "sunroom", label: "Sunroom" },
    ],
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

// ---------------------------------------------------------------------------
// Feature: imported-theme provenance list
// ---------------------------------------------------------------------------

describe("Feature: imported-theme provenance list", () => {
  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: every imported choice shows its own provenance row", () => {
    it('shows "<label> — Imported · <source> · <date>" for a catalog-imported theme (T187, AC1)', () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting()]} timeZone="UTC" />);

      expect(
        screen.getByText("Midnight Drive — Imported · midnight-drive-catalog-entry · Jul 21, 2026")
      ).toBeInTheDocument();
    });

    it('shows "<label> — Imported · file · <date>" for a file-imported theme (T187, AC1)', () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting()]} timeZone="UTC" />);

      expect(screen.getByText("Aurora Glow — Imported · file · Jul 20, 2026")).toBeInTheDocument();
    });

    it("shows a row for every imported choice at once, on the same render (review F1: multiple choices)", () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting()]} timeZone="UTC" />);

      expect(
        screen.getByText("Midnight Drive — Imported · midnight-drive-catalog-entry · Jul 21, 2026")
      ).toBeInTheDocument();
      expect(screen.getByText("Aurora Glow — Imported · file · Jul 20, 2026")).toBeInTheDocument();
    });
  });

  describe("Scenario: which row appears no longer depends on which theme is currently saved (review F1)", () => {
    it("still shows an imported choice's row when that theme is NOT the saved value", () => {
      // The saved value is the shipped default here — under the old savedValue-gated badge,
      // neither imported choice would render anything at all.
      renderWithProviders(
        <SettingsForm settings={[makeThemeSetting({ value: "cats-whisker" })]} timeZone="UTC" />
      );

      expect(
        screen.getByText("Midnight Drive — Imported · midnight-drive-catalog-entry · Jul 21, 2026")
      ).toBeInTheDocument();
      expect(screen.getByText("Aurora Glow — Imported · file · Jul 20, 2026")).toBeInTheDocument();
    });

    it("still shows the NON-active imported choice's row when a different imported theme is saved", () => {
      renderWithProviders(
        <SettingsForm settings={[makeThemeSetting({ value: "aurora-glow" })]} timeZone="UTC" />
      );

      // midnight-drive is imported but not the saved value.
      expect(
        screen.getByText("Midnight Drive — Imported · midnight-drive-catalog-entry · Jul 21, 2026")
      ).toBeInTheDocument();
    });
  });

  describe("Scenario: a shipped default never gets a row of its own", () => {
    it("renders no row for the embedded default theme, even while other choices are imported (T187, AC2)", () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting()]} timeZone="UTC" />);

      // The label alone (the `<select>` option) is expected; only a provenance ROW for it is not.
      expect(screen.queryByText(/^Cat's Whisker — Imported ·/)).not.toBeInTheDocument();
    });
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: a shipped-only catalog shows no list at all", () => {
    it("renders no provenance row anywhere when nothing has ever been imported (T187, AC2)", () => {
      renderWithProviders(<SettingsForm settings={[makeShippedOnlyThemeSetting()]} timeZone="UTC" />);

      expect(screen.queryByText(/Imported ·/)).not.toBeInTheDocument();
    });
  });
});
