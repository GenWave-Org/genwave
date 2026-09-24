// @jest-environment jsdom
// STORY-478 — Settings page renders the descriptor (gh-#778 · SPEC F205.4–F205.5 · PLAN T576 T577)
//
// BDD specification — Jest. RED at plan time: every specification was it.todo — T576 turns AC1–AC4
// and AC8–AC11 into real specs (the DTO-driven label/help/min/max render, plus the source-tree facts
// that FIELD_HELP_TEXT/settings-help-keys.ts/settings-tabs.ts/settings-help-coverage.spec.tsx and the
// C# FeatureSettingsHelpKeysParity law are gone). AC5–AC7 stay it.todo for T577 (the grouped,
// searchable descriptor index — SettingsForm doesn't build that surface yet).
//
// Runner: Jest (jsdom) + @testing-library/react, mirroring settings-persona-control.spec.tsx's house
// pattern (renderWithProviders, makeSequencedFetchMock) — SettingsForm calls useConfirm()
// unconditionally, so every render needs a ConfirmDialogProvider ancestor.
//
// Round 2 review fix: every scenario's render moved into that scenario's own `beforeEach` (never
// `beforeAll` — @testing-library/react's auto `cleanup()` unmounts the tree after every `it`, so a
// `beforeAll` render would leave the second test in a scenario looking at an empty document) and
// each `it` dropped to one `expect`. AC9 splits into one `it` per fact (label, help, min, max)
// instead of one bundled `toEqual`.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import { settingDto } from "./setting-fixture";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const SKIPPED_DIRS = new Set(["node_modules", ".next", "bin", "obj"]);

/** Recursively lists every file under `dir` whose extension is in `exts`. */
function collectFiles(dir: string, exts: string[], out: string[] = []): string[] {
  for (const entry of readdirSync(dir)) {
    if (SKIPPED_DIRS.has(entry)) continue;
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) {
      collectFiles(full, exts, out);
    } else if (exts.includes(path.extname(full))) {
      out.push(full);
    }
  }
  return out;
}

function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

interface MockResponseSpec {
  status: number;
  body?: unknown;
}

/** The last spec in a non-empty list — arrange-time only, never an assertion. */
function lastSpec(specs: MockResponseSpec[]): MockResponseSpec {
  const spec = specs[specs.length - 1];
  if (spec === undefined) {
    throw new Error("makeSequencedFetchMock requires at least one response spec");
  }
  return spec;
}

/** A fetch mock that replays one response per call, in order (last spec repeats if exhausted). */
function makeSequencedFetchMock(specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  let callIndex = 0;
  const fn = jest.fn<typeof fetch>().mockImplementation(async () => {
    const spec = specs[callIndex] ?? lastSpec(specs);
    callIndex += 1;
    return {
      ok: spec.status >= 200 && spec.status < 300,
      status: spec.status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
      headers: new Headers(),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

describe("Feature: Settings page renders the descriptor", () => {
  describe("Scenario: a Number descriptor", () => {
    // Given: label "Music under sponsor spots", help, min −30, max 0
    const SPONSOR_MUSIC_KEY = "Sponsors:MusicDuckDb";
    const SPONSOR_MUSIC_LABEL = "Music under sponsor spots";
    const SPONSOR_MUSIC_HELP = "How many dB to duck the music bed under a live sponsor read.";

    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: SPONSOR_MUSIC_KEY,
              value: "-10",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "dB",
              label: SPONSOR_MUSIC_LABEL,
              help: SPONSOR_MUSIC_HELP,
              min: -30,
              max: 0,
            }),
          ]}
        />
      );
    });

    it("AC1 — the field label is the DTO label, not the key", () => {
      expect(screen.getByLabelText(new RegExp(SPONSOR_MUSIC_LABEL))).toBeInTheDocument();
    });

    it("AC2 — the help text is the DTO help", () => {
      expect(screen.getByTestId(`setting-help-${SPONSOR_MUSIC_KEY}`)).toHaveTextContent(
        SPONSOR_MUSIC_HELP
      );
    });

    it('AC3 — the number input has min="-30"', () => {
      const input = screen.getByLabelText(new RegExp(SPONSOR_MUSIC_LABEL)) as HTMLInputElement;
      expect(input).toHaveAttribute("min", "-30");
    });

    it('AC3 — the number input has max="0"', () => {
      const input = screen.getByLabelText(new RegExp(SPONSOR_MUSIC_LABEL)) as HTMLInputElement;
      expect(input).toHaveAttribute("max", "0");
    });
  });

  describe("Scenario: a Choice descriptor", () => {
    // Given: choices [{a,"Alpha"},{b,"Beta"}]
    const CHOICE_LABEL = "Example sponsor choice";

    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "Sponsors:ExampleChoice",
              value: "a",
              source: "default",
              applyMode: "live",
              kind: "choice",
              unit: "",
              label: CHOICE_LABEL,
              choices: [
                { value: "a", label: "Alpha" },
                { value: "b", label: "Beta" },
              ],
            }),
          ]}
        />
      );
    });

    it('AC4 — the select shows "Alpha" and "Beta"', () => {
      const select = screen.getByLabelText(new RegExp(CHOICE_LABEL)) as HTMLSelectElement;
      const optionLabels = Array.from(select.options).map((o) => o.textContent);
      expect(optionLabels).toEqual(["Alpha", "Beta"]);
    });
  });

  describe("Scenario: descriptors across four groups", () => {
    // Given: Sound, Sponsors, Station, System present (T577)
    it.todo("AC5 — sections appear in enum order, present ones only");
    it.todo("AC6 — the index lists one entry per present section");
  });

  describe("Scenario: a search for 'sponsor'", () => {
    // Given: the search box filled
    it.todo("AC7 — only descriptors whose label or help contains \"sponsor\" render");
  });

  describe("Scenario: a registry key", () => {
    // Given: a SETTING_CONTROL_REGISTRY entry (Persona editor)
    const PERSONA_KEY = "Context:Weather:PersonaId";
    const PERSONA_LABEL = "Weather forecast persona";
    const PERSONAS = [{ id: 3, name: "Flip" }];

    let originalFetch: typeof fetch;

    beforeEach(async () => {
      originalFetch = global.fetch;
      makeSequencedFetchMock([{ status: 200, body: PERSONAS }]);
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: PERSONA_KEY,
              value: "0",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              label: PERSONA_LABEL,
            }),
          ]}
        />
      );
      await waitFor(() => screen.getByLabelText(new RegExp(PERSONA_LABEL)));
    });

    afterEach(() => {
      global.fetch = originalFetch;
      jest.clearAllMocks();
    });

    it("AC8 — the specialised control renders", () => {
      const select = screen.getByLabelText(new RegExp(PERSONA_LABEL)) as HTMLSelectElement;
      expect(select.tagName).toBe("SELECT");
    });

    it("AC8 — the DTO label sits above it", () => {
      expect(screen.getByText(PERSONA_LABEL).tagName).toBe("LABEL");
    });
  });

  describe("Scenario: a key admin-ui has never seen", () => {
    // Given: a fake descriptor for "Zz:Never"
    const NEVER_SEEN_KEY = "Zz:Never";
    const NEVER_SEEN_LABEL = "Never-seen setting";
    const NEVER_SEEN_HELP = "A setting admin-ui has no bespoke code for.";

    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: NEVER_SEEN_KEY,
              value: "4",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              label: NEVER_SEEN_LABEL,
              help: NEVER_SEEN_HELP,
              min: 1,
              max: 9,
            }),
          ]}
        />
      );
    });

    it("AC9 — the label is the DTO label", () => {
      expect(screen.getByText(NEVER_SEEN_LABEL).tagName).toBe("LABEL");
    });

    it("AC9 — the help is the DTO help", () => {
      expect(screen.getByTestId(`setting-help-${NEVER_SEEN_KEY}`)).toHaveTextContent(
        NEVER_SEEN_HELP
      );
    });

    it('AC9 — the number input has min="1"', () => {
      const input = screen.getByLabelText(new RegExp(NEVER_SEEN_LABEL)) as HTMLInputElement;
      expect(input).toHaveAttribute("min", "1");
    });

    it('AC9 — the number input has max="9"', () => {
      const input = screen.getByLabelText(new RegExp(NEVER_SEEN_LABEL)) as HTMLInputElement;
      expect(input).toHaveAttribute("max", "9");
    });
  });

  describe("Scenario: the source tree", () => {
    // Given: admin-ui and tests scanned
    const settingsDir = path.resolve(__dirname, "../app/(authed)/settings");

    it("AC10 — settings-help-keys.ts does not exist", () => {
      expect(existsSync(path.join(settingsDir, "settings-help-keys.ts"))).toBe(false);
    });

    it("AC10 — settings-tabs.ts does not exist", () => {
      expect(existsSync(path.join(settingsDir, "settings-tabs.ts"))).toBe(false);
    });

    it("AC10 — settings-help-coverage.spec.tsx does not exist", () => {
      expect(existsSync(path.join(__dirname, "settings-help-coverage.spec.tsx"))).toBe(false);
    });

    it("AC10 — no file under admin-ui/app contains FIELD_HELP_TEXT", () => {
      const appDir = path.resolve(__dirname, "../app");
      const offenders = collectFiles(appDir, [".ts", ".tsx"]).filter((f) =>
        readFileSync(f, "utf-8").includes("FIELD_HELP_TEXT")
      );
      expect(offenders).toEqual([]);
    });

    it("AC11 — FeatureSettingsHelpKeysParity does not exist", () => {
      const testsDir = path.resolve(__dirname, "../../tests");
      const csFiles = collectFiles(testsDir, [".cs"]);
      const matches = csFiles.filter((f) =>
        /class\s+FeatureSettingsHelpKeysParity\b/.test(readFileSync(f, "utf-8"))
      );
      expect(matches).toEqual([]);
    });
  });
});
