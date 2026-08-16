// @jest-environment jsdom
// PLAN T175 — `SettingKind.Choice` gets a dedicated Settings control (SPEC F102.14, STORY-265).
//
// Runner: Jest (jsdom) + @testing-library/react, mirroring settings-audience-control.spec.tsx and
// settings-semantic-controls.spec.tsx's house pattern (renderWithProviders,
// makeSequencedFetchMock) — SettingsForm calls useConfirm() unconditionally, so every render
// needs a ConfirmDialogProvider ancestor.
//
// `Station:Theme` is the only shipped `kind: "choice"` setting today, so it doubles as the
// coverage vehicle for `ChoiceSettingControl` — a generic control driven purely by
// `setting.choices`, not a Theme-specific one (see that component's own remarks).

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

// Labels are deliberately distinct from their slugs (never a slug-derived transform, e.g. title-
// casing the value) — this is what makes "the select shows the label, not the raw slug" and "the
// PUT payload carries the value, not the label" two independently provable claims (T175 ruling
// #3), not one assertion that happens to pass because label === value in every fixture.
//
// cats-whisker carries isDefault: true, mirroring StationSettingsAllowlist's real
// ShippedThemeChoices (T175 follow-up #1) — the "staged value is empty" scenarios below exercise
// the same server-flagged-default wiring the live allowlist uses, not a fixture-only shortcut.
const THEME_CHOICES = [
  { value: "cats-whisker", label: "Cat's Whisker", isDefault: true },
  { value: "valve-glow", label: "Valve Glow" },
];

function makeThemeSetting(overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key: "Station:Theme",
    value: "cats-whisker",
    source: "default",
    applyMode: "live",
    kind: "choice",
    unit: "",
    choices: THEME_CHOICES,
    ...overrides,
  };
}

interface MockResponseSpec {
  status: number;
  body?: unknown;
}

function makeSequencedFetchMock(specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  let callIndex = 0;
  const fn = jest.fn<typeof fetch>().mockImplementation(async () => {
    const spec = specs[callIndex] ?? specs[specs.length - 1]!;
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

function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

// ---------------------------------------------------------------------------
// Feature: SettingKind.Choice's dedicated Settings control
// ---------------------------------------------------------------------------

describe("Feature: SettingKind.Choice's dedicated Settings control", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: Station:Theme renders as a closed-choice dropdown", () => {
    it("renders a select preset to the current value (F102.14)", () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting({ value: "valve-glow" })]} />);

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;
      expect(select.tagName).toBe("SELECT");
      expect(select.value).toBe("valve-glow");
    });

    it("offers exactly the API's choices as options, in order (F102.14)", () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting()]} />);

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;
      const optionValues = Array.from(select.options).map((o) => o.value);
      expect(optionValues).toEqual(THEME_CHOICES.map((c) => c.value));
    });

    it("renders each option's server-supplied label, not its raw slug (T175)", () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting()]} />);

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;
      const optionLabels = Array.from(select.options).map((o) => o.textContent);
      expect(optionLabels).toEqual(THEME_CHOICES.map((c) => c.label));
      // Never the raw slug as visible text — the exact regression this control exists to prevent.
      expect(screen.queryByText("cats-whisker")).not.toBeInTheDocument();
    });

    it("still shows the existing help text alongside the dedicated control", () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting()]} />);

      expect(screen.getByTestId("setting-help-Station:Theme")).toBeInTheDocument();
    });
  });

  describe("Scenario: a staged value the choices list doesn't carry", () => {
    it("still renders it, marked distinctly from the closed set (VoiceSettingControl precedent)", () => {
      renderWithProviders(
        <SettingsForm settings={[makeThemeSetting({ value: "retired-theme" })]} />
      );

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;
      const optionValues = Array.from(select.options).map((o) => o.value);
      expect(optionValues).toContain("retired-theme");
      expect(select.value).toBe("retired-theme");
      expect(screen.getByText(/retired-theme.*current/i)).toBeInTheDocument();
    });
  });

  describe("Scenario: the staged value is the empty string (T163's unseeded floor — never explicitly set)", () => {
    it("renders unset as its own explicit option instead of silently matching the first choice (operator trap)", () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting({ value: "" })]} />);

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;
      expect(select.value).toBe("");
      expect(select.options[0]).toHaveValue("");
    });

    it("names the API-flagged default choice in the label — never a hardcoded theme name", () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting({ value: "" })]} />);

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;
      expect(select.options[0]).toHaveTextContent("Station default (Cat's Whisker)");
    });

    it("follows the isDefault flag to whichever choice carries it, not list position", () => {
      renderWithProviders(
        <SettingsForm
          settings={[
            makeThemeSetting({
              value: "",
              choices: [
                { value: "cats-whisker", label: "Cat's Whisker" },
                { value: "valve-glow", label: "Valve Glow", isDefault: true },
              ],
            }),
          ]}
        />
      );

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;
      expect(select.options[0]).toHaveTextContent("Station default (Valve Glow)");
    });

    it("falls back to a neutral label when the API marks no choice as default", () => {
      renderWithProviders(
        <SettingsForm
          settings={[
            makeThemeSetting({
              value: "",
              choices: [
                { value: "cats-whisker", label: "Cat's Whisker" },
                { value: "valve-glow", label: "Valve Glow" },
              ],
            }),
          ]}
        />
      );

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;
      expect(select.options[0]).toHaveTextContent("Station default");
      expect(select.options[0]).not.toHaveTextContent(/\(/);
    });
  });

  describe("Scenario: the API sends no choices for a choice-kind setting", () => {
    it("fails visibly instead of rendering an empty, unusable select", () => {
      renderWithProviders(
        <SettingsForm settings={[makeThemeSetting({ choices: undefined })]} />
      );

      expect(screen.queryByLabelText(/Station:Theme/)).not.toBeInTheDocument();
      expect(screen.getByRole("alert")).toHaveTextContent(/no choices available/i);
    });

    it("also fails visibly for an empty choices array", () => {
      renderWithProviders(<SettingsForm settings={[makeThemeSetting({ choices: [] })]} />);

      expect(screen.getByRole("alert")).toHaveTextContent(/no choices available/i);
    });
  });

  describe("Scenario: picking a theme rides the shipped save/PUT plumbing", () => {
    it("picking a different theme and saving PUTs the changed key (F102.14, F54.4)", async () => {
      const mockFetch = makeSequencedFetchMock([{ status: 200 }]);
      renderWithProviders(<SettingsForm settings={[makeThemeSetting({ value: "cats-whisker" })]} />);

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;
      fireEvent.change(select, { target: { value: "valve-glow" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/settings");
      expect(init.method).toBe("PUT");
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Station:Theme", value: "valve-glow" }]);
    });

    it("a 400 validation error surfaces inline at the field (F28.9)", async () => {
      makeSequencedFetchMock([
        {
          status: 400,
          body: {
            errors: {
              settings: [
                "Value 'not-a-theme' is not valid for 'Station:Theme'. Must be one of the shipped theme slugs.",
              ],
            },
          },
        },
      ]);
      renderWithProviders(<SettingsForm settings={[makeThemeSetting({ value: "cats-whisker" })]} />);

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;
      fireEvent.change(select, { target: { value: "valve-glow" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText(/Must be one of the shipped theme slugs\./)).toBeInTheDocument();
      });
    });

    it("a second edit after a successful save still registers as a change (gh-#140)", async () => {
      const mockFetch = makeSequencedFetchMock([{ status: 200 }, { status: 200 }]);
      renderWithProviders(<SettingsForm settings={[makeThemeSetting({ value: "cats-whisker" })]} />);

      const select = screen.getByLabelText(/Station:Theme/) as HTMLSelectElement;

      // First edit + save — re-baselines `original` to "valve-glow" (gh-#140's fix).
      fireEvent.change(select, { target: { value: "valve-glow" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));

      // Second edit lands back on the ORIGINAL page-load value. A frozen (mount-time) baseline
      // would see this as "unchanged" and silently drop it — the exact gh-#140 regression class.
      fireEvent.change(select, { target: { value: "cats-whisker" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      const [url, init] = mockFetch.mock.calls[1] as [string, RequestInit];
      expect(url).toBe("/api/settings");
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Station:Theme", value: "cats-whisker" }]);
    });
  });

  describe("Scenario: Station:IconPack shares the same generic control (T303 review finding F1)", () => {
    // Station:IconPack has NO registry entry of its own (SettingsForm.tsx's own kind-chain
    // fallback routes it here) — a second, independent proof that ChoiceSettingControl truly
    // generalizes past Theme, and the vehicle for pinning the two shapes the T303 fix round
    // corrected: a zero-packs station renders a WORKING dropdown rather than the "no choices"
    // alert (review finding F1), and a dangling pack slug is visible with an inline notice, never
    // an error (STORY-337 AC6).
    const HOUSE_ICONS_ONLY_CHOICES = [{ value: "", label: "House icons", isDefault: true }];

    // A station with packs installed — house icons still leads (server-flagged isDefault), then
    // every installed slug in order, each doubling as its own label (no display name in the
    // manifest — StationSettingsAllowlist.IconPackChoices' own remarks).
    const PACKS_INSTALLED_CHOICES = [
      { value: "", label: "House icons", isDefault: true },
      { value: "line-icons", label: "line-icons" },
      { value: "solid-icons", label: "solid-icons" },
    ];

    function makeIconPackSetting(overrides: Partial<SettingDto> = {}): SettingDto {
      return {
        key: "Station:IconPack",
        value: "",
        source: "default",
        applyMode: "live",
        kind: "choice",
        unit: "",
        choices: HOUSE_ICONS_ONLY_CHOICES,
        ...overrides,
      };
    }

    it("a zero-packs station renders a working dropdown, never the 'no choices' alert", () => {
      renderWithProviders(<SettingsForm settings={[makeIconPackSetting()]} />);

      const select = screen.getByLabelText(/Station:IconPack/) as HTMLSelectElement;
      expect(select.tagName).toBe("SELECT");
      expect(select.value).toBe("");
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });

    it("a zero-packs station's dropdown holds exactly ONE \"\"-valued option, labeled House icons — never stacked with a synthesized second one (F1-residual)", () => {
      // Regression pin: ChoiceSettingControl's own isUnset branch used to synthesize its own
      // `<option value="">` unconditionally, which STACKED with IconPack's real "" choice below —
      // every fresh station showed BOTH "Station default (House icons)" AND "House icons", both
      // submitting "". A substring `toHaveTextContent("House icons")` against options[0] passed
      // either way (it matches the FIRST word of "Station default (House icons)" too), which is
      // exactly what let the duplicate ship unnoticed — so this asserts the full option list.
      renderWithProviders(<SettingsForm settings={[makeIconPackSetting()]} />);

      const select = screen.getByLabelText(/Station:IconPack/) as HTMLSelectElement;
      const optionPairs = Array.from(select.options).map((o) => [o.value, o.textContent]);
      expect(optionPairs).toEqual(HOUSE_ICONS_ONLY_CHOICES.map((c) => [c.value, c.label]));
    });

    it("a packs-installed station's dropdown lists house icons first, then every installed slug — still no synthesized duplicate", () => {
      renderWithProviders(
        <SettingsForm settings={[makeIconPackSetting({ choices: PACKS_INSTALLED_CHOICES })]} />
      );

      const select = screen.getByLabelText(/Station:IconPack/) as HTMLSelectElement;
      const optionPairs = Array.from(select.options).map((o) => [o.value, o.textContent]);
      expect(optionPairs).toEqual(PACKS_INSTALLED_CHOICES.map((c) => [c.value, c.label]));
    });

    it("a dangling pack slug renders visibly with an inline notice, and nothing errors (STORY-337 AC6)", () => {
      renderWithProviders(
        <SettingsForm settings={[makeIconPackSetting({ value: "uninstalled-pack" })]} />
      );

      const select = screen.getByLabelText(/Station:IconPack/) as HTMLSelectElement;
      expect(select.value).toBe("uninstalled-pack");
      expect(screen.getByText(/uninstalled-pack.*current/i)).toBeInTheDocument();
      expect(screen.getByText(/isn.t one of the choices offered/i)).toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: keys absent from the registry are unaffected (sad path)", () => {
    it("a plain string setting still renders the shipped text input (F54.1)", () => {
      renderWithProviders(
        <SettingsForm
          settings={[
            { key: "Llm:Model", value: "gpt", source: "default", applyMode: "live", kind: "string", unit: "" },
          ]}
        />
      );

      const input = screen.getByLabelText(/Llm:Model/) as HTMLInputElement;
      expect(input.tagName).toBe("INPUT");
      expect(input.type).toBe("text");
    });

    it("an unregistered choice setting still renders the dedicated select, not the number input (T175 follow-up #2)", () => {
      // Station:Theme is the only kind: "choice" entry in SETTING_CONTROL_REGISTRY today, so this
      // is a hypothetical SECOND Choice-kind setting that never got a registry entry. Before the
      // fix, SettingField's kind chain (boolean / number-list / string / else-number) had no
      // "choice" branch at all, so this fell all the way through to the plain NUMBER input —
      // worse than T163's original stopgap (a free-text box that at least holds a slug).
      renderWithProviders(
        <SettingsForm
          settings={[
            {
              key: "Station:UnregisteredChoice",
              value: "a",
              source: "default",
              applyMode: "live",
              kind: "choice",
              unit: "",
              choices: [
                { value: "a", label: "Option A" },
                { value: "b", label: "Option B" },
              ],
            },
          ]}
        />
      );

      const select = screen.getByLabelText(/Station:UnregisteredChoice/) as HTMLSelectElement;
      expect(select.tagName).toBe("SELECT");
      expect(select.value).toBe("a");
    });
  });
});
