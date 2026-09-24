// @jest-environment jsdom
// STORY-478 — Settings page renders the descriptor (gh-#778 · SPEC F205.4–F205.5 · PLAN T576 T577)
//
// BDD specification — Jest. RED at plan time: every specification was it.todo — T576 turned AC1–AC4
// and AC8–AC11 into real specs (the DTO-driven label/help/min/max render, plus the source-tree facts
// that FIELD_HELP_TEXT/settings-help-keys.ts/settings-tabs.ts/settings-help-coverage.spec.tsx and the
// C# FeatureSettingsHelpKeysParity law are gone). T577 turns AC5–AC7 into real specs too (sections by
// `group` in enum order, the sticky index, and label/help search) and adds the unknown-group,
// search-preserves-state, and no-matches edge scenarios the T577 ruling called for.
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
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingGroupDto } from "../app/(authed)/settings/settings-types";
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

/**
 * Deselects every option of a `<select multiple>` and fires the change event React's synthetic
 * `onChange` listens for — the same jsdom idiom `main-scope-picker.spec.tsx`'s own `selectOptions`
 * uses, needed here (M3) to trigger SPEC F23.5's client-side empty-selection block.
 */
function clearMultiSelect(select: HTMLSelectElement): void {
  Array.from(select.options).forEach((opt) => { opt.selected = false; });
  fireEvent.change(select);
}

// The four `SettingGroup` members STORY-478 AC5/AC6 name explicitly — ids/labels as the server
// sends them (`GenWave.Host.Configuration.SettingGroup`'s lowercase member names, PLAN T574).
const GROUP_SOUND: SettingGroupDto = { id: "sound", label: "Sound" };
const GROUP_SPONSORS: SettingGroupDto = { id: "sponsors", label: "Sponsors" };
const GROUP_STATION: SettingGroupDto = { id: "station", label: "Station" };
const GROUP_SYSTEM: SettingGroupDto = { id: "system", label: "System" };

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
    // Given: descriptors across Sound, Sponsors, Station, System (T577), fed in scrambled order —
    // the rendered order must come from `GROUP_ORDER`, never from arrival order.
    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "System:Zeta",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              group: GROUP_SYSTEM,
            }),
            settingDto({
              key: "Sound:Alpha",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              group: GROUP_SOUND,
            }),
            settingDto({
              key: "Station:Beta",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              group: GROUP_STATION,
            }),
            settingDto({
              key: "Sponsors:Gamma",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              group: GROUP_SPONSORS,
            }),
          ]}
        />
      );
    });

    it("AC5 — sections appear in enum order, present ones only", () => {
      const headings = screen.getAllByRole("heading", { level: 2 }).map((h) => h.textContent);
      expect(headings).toEqual(["Sound", "Sponsors", "Station", "System"]);
    });

    it("AC6 — the index lists one entry per present section", () => {
      const nav = screen.getByRole("navigation", { name: "Settings sections" });
      const links = within(nav)
        .getAllByRole("link")
        .map((link) => link.textContent);
      expect(links).toEqual(["Sound", "Sponsors", "Station", "System"]);
    });
  });

  describe("Scenario: a search for 'sponsor'", () => {
    // Given: one descriptor matching by label, one matching only by help, one matching neither —
    // the search box filled with "sponsor"
    const LABEL_MATCH = "Sponsor duck level";
    const HELP_MATCH = "Weekend override";
    const NO_MATCH = "Loudness ceiling";

    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "A:LabelMatch",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              label: LABEL_MATCH,
            }),
            settingDto({
              key: "A:HelpMatch",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              label: HELP_MATCH,
              help: "Applies during a live sponsor read.",
            }),
            settingDto({
              key: "A:NoMatch",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              label: NO_MATCH,
              help: "Peak ceiling for the master bus.",
            }),
          ]}
        />
      );
      // Mixed case ("SPONSOR", not "sponsor") — T577 round 2 review F4 — pins that the query
      // itself is lowercased before matching, not just the label/help it's compared against.
      fireEvent.change(screen.getByLabelText(/search settings/i), { target: { value: "SPONSOR" } });
    });

    it('AC7 — only descriptors whose label or help contains "sponsor" render, case-insensitively', () => {
      const renderedLabels = screen.getAllByRole("spinbutton").map((input) => {
        const label = document.querySelector(`label[for="${input.id}"]`);
        return label?.textContent?.trim() ?? "";
      });
      expect(renderedLabels).toEqual([LABEL_MATCH, HELP_MATCH]);
    });
  });

  describe("Scenario: a group id admin-ui has never seen", () => {
    // Given: a descriptor in an unrecognized group, alongside one in a known group
    const UNKNOWN_GROUP: SettingGroupDto = { id: "future-group", label: "Future Group" };

    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "Zz:Unknown",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              group: UNKNOWN_GROUP,
            }),
            settingDto({
              key: "Sound:Known",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              group: GROUP_SOUND,
            }),
          ]}
        />
      );
    });

    it("renders the unrecognized group's section after every known one", () => {
      const headings = screen.getAllByRole("heading", { level: 2 }).map((h) => h.textContent);
      expect(headings).toEqual(["Sound", "Future Group"]);
    });
  });

  describe("Scenario: an edit to a field a search then hides", () => {
    // Given: one field, edited, then hidden by a search that doesn't match it
    const HIDDEN_KEY = "Sponsors:DuckLevel";
    const HIDDEN_LABEL = "Sponsor duck level";

    let originalFetch: typeof fetch;
    let mockFetch: jest.MockedFunction<typeof fetch>;

    beforeEach(async () => {
      originalFetch = global.fetch;
      mockFetch = makeSequencedFetchMock([{ status: 200 }]);
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: HIDDEN_KEY,
              value: "-6",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "dB",
              label: HIDDEN_LABEL,
            }),
          ]}
        />
      );
      fireEvent.change(screen.getByLabelText(new RegExp(HIDDEN_LABEL)), { target: { value: "-9" } });
      fireEvent.change(screen.getByLabelText(/search settings/i), { target: { value: "no-match-at-all" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });
      // T577 round 2 review F3 — arrange-only: waits for the PUT to have landed, asserts
      // nothing itself (a thrown Error, not an `expect`, is what makes `waitFor` retry).
      await waitFor(() => {
        if (mockFetch.mock.calls.length < 1) throw new Error("no PUT yet");
      });
    });

    afterEach(() => {
      global.fetch = originalFetch;
      jest.clearAllMocks();
    });

    it("still PUTs the edit once the field is hidden by the search", () => {
      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: HIDDEN_KEY, value: "-9" }]);
    });
  });

  describe("Scenario: a rejected save on a filtered-out field", () => {
    // Given: one field, hidden by a search, whose save is then rejected
    const REJECTED_KEY = "Sponsors:DuckLevel";
    const REJECTED_LABEL = "Sponsor duck level";

    let originalFetch: typeof fetch;

    beforeEach(() => {
      originalFetch = global.fetch;
      makeSequencedFetchMock([
        { status: 400, body: { errors: { [REJECTED_KEY]: ["Must be between -40 and 0"] } } },
      ]);
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: REJECTED_KEY,
              value: "-6",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "dB",
              label: REJECTED_LABEL,
            }),
          ]}
        />
      );
      fireEvent.change(screen.getByLabelText(new RegExp(REJECTED_LABEL)), { target: { value: "-99" } });
      fireEvent.change(screen.getByLabelText(/search settings/i), { target: { value: "no-match-at-all" } });
    });

    afterEach(() => {
      global.fetch = originalFetch;
      jest.clearAllMocks();
    });

    it("clears the search so the rejected field renders and takes focus", async () => {
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByLabelText(new RegExp(REJECTED_LABEL))).toHaveFocus();
      });
    });
  });

  describe("Scenario: a search typed while the rejected save is still pending", () => {
    // T577 round 2 review F1 — the earlier fix decided whether to clear the search at Save-click
    // time, so a search typed AFTER the click but before the (late-resolving) response landed
    // was read through a stale closure and never cleared. Given: a field edited and saved with a
    // fetch that stays pending until released, a search typed WHILE it's still pending that hides
    // the field, then a 400 naming that field.
    const PENDING_KEY = "Sponsors:DuckLevel";
    const PENDING_LABEL = "Sponsor duck level";

    let originalFetch: typeof fetch;
    let releaseFetch: (response: Response) => void;

    beforeEach(() => {
      originalFetch = global.fetch;
      releaseFetch = () => {
        throw new Error("releaseFetch invoked before fetch was called");
      };
      global.fetch = jest
        .fn<typeof fetch>()
        .mockImplementation(
          () => new Promise<Response>((resolve) => { releaseFetch = resolve; })
        ) as unknown as typeof fetch;

      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: PENDING_KEY,
              value: "-6",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "dB",
              label: PENDING_LABEL,
            }),
          ]}
        />
      );
      fireEvent.change(screen.getByLabelText(new RegExp(PENDING_LABEL)), { target: { value: "-99" } });
    });

    afterEach(() => {
      global.fetch = originalFetch;
      jest.clearAllMocks();
    });

    it("still focuses the rejected field after a search hides it mid-flight", async () => {
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      // Typed AFTER Save was clicked, WHILE the PUT is still pending.
      fireEvent.change(screen.getByLabelText(/search settings/i), { target: { value: "no-match-at-all" } });

      await act(async () => {
        releaseFetch({
          ok: false,
          status: 400,
          json: async () => ({ errors: { [PENDING_KEY]: ["Must be between -40 and 0"] } }),
          headers: new Headers(),
        } as unknown as Response);
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByLabelText(new RegExp(PENDING_LABEL))).toHaveFocus();
      });
    });
  });

  describe("Scenario: a search with no matches", () => {
    // Given: one descriptor, a search that matches nothing
    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "A:One",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              label: "Loudness ceiling",
            }),
          ]}
        />
      );
      fireEvent.change(screen.getByLabelText(/search settings/i), { target: { value: "zzz-nothing-matches" } });
    });

    it('shows "No settings match"', () => {
      expect(screen.getByText("No settings match")).toBeInTheDocument();
    });
  });

  describe("Scenario: a client-side block on a field hidden by search (M3)", () => {
    // Given: the main-scope field (SPEC F23.5's "block", not "confirm", empty-list policy),
    // emptied, then hidden by a search that matches neither its label nor its key
    const MAIN_SCOPE_KEY = "Station:Scope:LibraryIds";
    const MAIN_SCOPE_LABEL = "Main rotation scope";

    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: MAIN_SCOPE_KEY,
              value: "[1]",
              source: "override",
              applyMode: "live",
              kind: "number-list",
              unit: "",
              label: MAIN_SCOPE_LABEL,
            }),
          ]}
          libraries={[{ id: 1, name: "Lib Alpha", mediaCount: 10 }]}
        />
      );
      clearMultiSelect(screen.getByLabelText(new RegExp(MAIN_SCOPE_LABEL)) as HTMLSelectElement);
      fireEvent.change(screen.getByLabelText(/search settings/i), { target: { value: "no-match-at-all" } });
    });

    it("clears the search so the blocked field renders and takes focus", async () => {
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByLabelText(new RegExp(MAIN_SCOPE_LABEL))).toHaveFocus();
      });
    });
  });

  describe("Scenario: a search matching only a setting's key (M4)", () => {
    // Given: a descriptor whose KEY contains "duck" but whose label and help do not (SPEC F205.5
    // AC7: search matches label + help only, never the key)
    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "Sponsors:DuckLevel",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              label: "Loudness offset",
              help: "Applied to the master bus.",
            }),
          ]}
        />
      );
      fireEvent.change(screen.getByLabelText(/search settings/i), { target: { value: "duck" } });
    });

    it("renders no fields, since a key-only match never counts", () => {
      expect(screen.queryAllByRole("spinbutton")).toHaveLength(0);
    });
  });

  describe("Scenario: trailingContent while searching (M5)", () => {
    // Given: a settings page with trailingContent, and text typed into the search box
    const TRAILING_TEXT = "Pronunciation rules control";

    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "A:One",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
            }),
          ]}
          trailingContent={<p>{TRAILING_TEXT}</p>}
        />
      );
      fireEvent.change(screen.getByLabelText(/search settings/i), { target: { value: "one" } });
    });

    it("hides trailingContent while the search box has text", () => {
      expect(screen.queryByText(TRAILING_TEXT)).not.toBeInTheDocument();
    });
  });

  describe("Scenario: a search that empties one section but not another (M6)", () => {
    // Given: two descriptors in different groups, a search matching only one of them
    const MATCHING_LABEL = "Sponsor duck level";
    const OTHER_LABEL = "Loudness ceiling";

    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "Sponsors:DuckLevel",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              label: MATCHING_LABEL,
              group: GROUP_SPONSORS,
            }),
            settingDto({
              key: "Sound:Ceiling",
              value: "1",
              source: "default",
              applyMode: "live",
              kind: "number",
              unit: "",
              label: OTHER_LABEL,
              group: GROUP_SOUND,
            }),
          ]}
        />
      );
      fireEvent.change(screen.getByLabelText(/search settings/i), { target: { value: "sponsor" } });
    });

    it("drops the emptied section's own index entry from the nav", () => {
      const nav = screen.getByRole("navigation", { name: "Settings sections" });
      expect(within(nav).queryByRole("link", { name: "Sound" })).not.toBeInTheDocument();
    });
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
