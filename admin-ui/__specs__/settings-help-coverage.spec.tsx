// @jest-environment jsdom
// STORY-151 — Every setting explains itself (Epic Y / SPEC F55.2–F55.3, closes gitea-#230, gitea-#231) —
// help-text half. The seeded-defaults half lives in Host.Tests/Specs/Story151_SeededDefaults.cs.
//
// Runner: Jest (jsdom) + @testing-library/react. Implemented Y6 (2026-07-15): FIELD_HELP_TEXT
// grows from 3 entries to full allowlist coverage. Parity between the C# allowlist and this
// jest spec's fixture is pinned two ways:
//   - `settings-help-keys.ts` mirrors `StationSettingsAllowlist.All`'s key list; this spec's
//     synthetic fixture is built directly from it (not a hand-typed duplicate), so a key added
//     there is exercised here automatically.
//   - An xUnit fact (`FeatureSettingsHelpKeysParity`, Story151_SeededDefaults.cs) string-parses
//     that same .ts file and asserts it equals `StationSettingsAllowlist.All` — so a key added to
//     only ONE side (the C# allowlist, or this TS module) fails a spec on both toolchains.
// `FIELD_HELP_TEXT` itself is typed `Record<SettingsHelpKey, string>` in SettingsForm.tsx, so a
// key missing an entry (or a stray extra one) is also a `tsc` failure, not just a spec failure.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";
import { SETTINGS_HELP_KEYS, type SettingsHelpKey } from "../app/(authed)/settings/settings-help-keys";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const BOOLEAN_KEYS = new Set<SettingsHelpKey>([
  "Station:Cadence:LeadInBeforeEachTrack",
  "Station:Cadence:BackAnnounceAfterEachTrack",
  "Library:YearLookup:Enabled",
]);

const NUMBER_LIST_KEYS = new Set<SettingsHelpKey>([
  "Station:Scope:LibraryIds",
  "Station:SafeScope:LibraryIds",
]);

const STRING_KEYS = new Set<SettingsHelpKey>([
  "Station:Name",
  "Station:Voice",
  "Tts:Endpoint",
  "Llm:Endpoint",
  "Llm:Model",
  "Library:YearLookup:Endpoint",
]);

function kindAndUnitFor(key: SettingsHelpKey): Pick<SettingDto, "kind" | "unit"> {
  if (BOOLEAN_KEYS.has(key)) return { kind: "boolean", unit: "" };
  if (NUMBER_LIST_KEYS.has(key)) return { kind: "number-list", unit: "" };
  if (STRING_KEYS.has(key)) return { kind: "string", unit: "" };
  return { kind: "number", unit: "count" };
}

function valueFor(key: SettingsHelpKey): string {
  const { kind } = kindAndUnitFor(key);
  switch (kind) {
    case "boolean":
      return "true";
    case "number-list":
      return "[1]";
    case "string":
      return "https://example.test";
    default:
      return "1";
  }
}

/** One synthetic SettingDto per key the ALLOWLIST serves — every rendered key, in one page. */
function makeFullAllowlistSettings(): SettingDto[] {
  return SETTINGS_HELP_KEYS.map((key) => ({
    key,
    value: valueFor(key),
    source: "default",
    applyMode: "live",
    ...kindAndUnitFor(key),
  }));
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
// Feature: Every settings field explains itself
// ---------------------------------------------------------------------------

describe("Feature: Every settings field explains itself", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: full help-text coverage, parity-guarded", () => {
    it("every key the settings page receives renders a help sentence (F55.3)", () => {
      renderWithProviders(<SettingsForm settings={makeFullAllowlistSettings()} />);

      for (const key of SETTINGS_HELP_KEYS) {
        const help = screen.queryByTestId(`setting-help-${key}`);
        if (help === null) {
          throw new Error(`expected help text for '${key}'`);
        }
        expect(help.textContent?.trim().length ?? 0).toBeGreaterThan(0);
      }
    });

    it("the parity guard fails when a rendered key lacks a FIELD_HELP_TEXT entry (F55.3)", () => {
      renderWithProviders(<SettingsForm settings={makeFullAllowlistSettings()} />);

      // One help paragraph per allowlisted key, no more, no fewer — a key rendered without a
      // FIELD_HELP_TEXT entry would leave a gap here, exactly what this guard exists to catch on
      // a future allowlist addition (the C#-side companion is FeatureSettingsHelpKeysParity).
      const helpNodes = screen.getAllByTestId(/^setting-help-/);
      expect(helpNodes).toHaveLength(SETTINGS_HELP_KEYS.length);
    });

    it("F53-bounded keys state their accepted range in the help copy (F53.3, F55.3)", () => {
      renderWithProviders(<SettingsForm settings={makeFullAllowlistSettings()} />);

      expect(screen.getByTestId("setting-help-Library:EnrichmentConcurrency")).toHaveTextContent(
        /1.*32/
      );
      expect(screen.getByTestId("setting-help-Admin:PlayHistoryCapacity")).toHaveTextContent(
        /1.*5000/
      );
      expect(
        screen.getByTestId("setting-help-Station:Rotation:ArtistSeparation")
      ).toHaveTextContent(/0.*100/);
      expect(screen.getByTestId("setting-help-GW_XFADE_MIN")).toHaveTextContent(
        /greater than 0.*30/i
      );
      expect(
        screen.getByTestId("setting-help-Library:CueDetection:MinSilenceDurationSec")
      ).toHaveTextContent(/greater than 0.*60/i);
    });
  });

  describe("Scenario: the YearLookup copy reads like English", () => {
    it(
      "Library:YearLookup:Enabled shows the F55.2 reword — on: fills missing years from " +
        "MusicBrainz; off: stops future lookups; filled years stay (gitea-#230)",
      () => {
        renderWithProviders(<SettingsForm settings={makeFullAllowlistSettings()} />);

        expect(screen.getByTestId("setting-help-Library:YearLookup:Enabled")).toHaveTextContent(
          "When on, tracks missing a release year get one looked up from MusicBrainz during " +
            "enrichment. Turning it off stops future lookups; years already filled stay."
        );
      }
    );
  });
});
