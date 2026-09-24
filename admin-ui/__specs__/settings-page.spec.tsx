// @jest-environment jsdom
// STORY-044 — Admin UI: station settings page
//
// Runner: Jest + jsdom. Drives SettingsForm via @testing-library/react with a
// mocked fetch. Covers GET rendering and PUT save path including 400 field errors.
// F2: fixtures now include kind/unit; tests cover checkbox, number input, unit hint,
// submit-only-changed behaviour, and no-change guard.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";
import { settingDto } from "./setting-fixture";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Pre-descriptor base fields for the two settings this file cares about, one `settingDto()` call
 * away from a full `SettingDto` — kept as raw fields, not already-built `SettingDto`s, so an
 * override merges BEFORE `settingDto()` computes its `label: fields.key` default. Merging onto an
 * already-built default (label already resolved to the OLD key) would need a hand-written label
 * fallback for an override that changes `key`; merging onto the raw fields lets `settingDto()`'s
 * own default do that job once, the same way every other spec's fixture already relies on it. */
const DEFAULT_SETTING_FIELDS: Array<
  Pick<SettingDto, "key" | "value" | "source" | "applyMode" | "kind" | "unit"> & Partial<SettingDto>
> = [
  {
    key: "Loudness:TargetLufs",
    value: "-16",
    source: "default",
    applyMode: "live",
    kind: "number",
    unit: "LUFS",
  },
  {
    key: "GW_XFADE_MAX",
    value: "8",
    source: "override",
    applyMode: "engine-restart",
    kind: "number",
    unit: "seconds",
  },
];

/** Indexes DEFAULT_SETTING_FIELDS, wrapping around — arrange-time only, never an assertion. */
function defaultFieldsAt(index: number): (typeof DEFAULT_SETTING_FIELDS)[number] {
  const fields = DEFAULT_SETTING_FIELDS[index % DEFAULT_SETTING_FIELDS.length];
  if (fields === undefined) {
    throw new Error(`DEFAULT_SETTING_FIELDS has no entry for index ${index}`);
  }
  return fields;
}

function makeSettings(overrides: Partial<SettingDto>[] = []): SettingDto[] {
  if (overrides.length > 0) {
    return overrides.map((o, i) => settingDto({ ...defaultFieldsAt(i), ...o }));
  }
  return DEFAULT_SETTING_FIELDS.map((fields) => settingDto(fields));
}

function makeBooleanSetting(override: Partial<SettingDto> = {}): SettingDto {
  return settingDto({
    key: "Station:Cadence:LeadInBeforeEachTrack",
    value: "true",
    source: "default",
    applyMode: "live",
    kind: "boolean",
    unit: "",
    ...override,
  });
}

function makeFetchMock(
  status: number,
  body: unknown = {}
): jest.MockedFunction<typeof fetch> {
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

/**
 * SettingsForm calls useConfirm() unconditionally (SafeScope-empty save opens
 * the shared modal — SPEC F28.9, window.confirm is gone), so every render needs
 * a ConfirmDialogProvider ancestor. Toaster is mounted alongside it so tests can
 * assert on the toast copy that replaced the shipped inline "Settings saved."
 * status banner.
 */
function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

// ---------------------------------------------------------------------------
// Feature: Edit station settings (client form)
// ---------------------------------------------------------------------------

describe("Feature: Edit station settings", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: viewing settings", () => {
    it("renders all settings passed from the server component", () => {
      const settings = makeSettings();
      renderWithProviders(<SettingsForm settings={settings} />);

      expect(screen.getByLabelText(/Loudness:TargetLufs/)).toBeInTheDocument();
      expect(screen.getByLabelText(/GW_XFADE_MAX/)).toBeInTheDocument();
    });

    it("each knob renders with its applyMode badge (live vs 'applies after engine restart')", () => {
      const settings = makeSettings();
      renderWithProviders(<SettingsForm settings={settings} />);

      expect(screen.getByText(/live/)).toBeInTheDocument();
      expect(screen.getByText(/applies after engine restart/)).toBeInTheDocument();
    });

    it("shows 'default' source badge for env/appsettings knobs", () => {
      const settings = makeSettings([
        { key: "Loudness:TargetLufs", value: "-16", source: "default", applyMode: "live", kind: "number", unit: "LUFS" },
      ]);
      renderWithProviders(<SettingsForm settings={settings} />);

      expect(screen.getByText(/\[default\]/)).toBeInTheDocument();
    });

    it("shows 'override' source badge for DB-overlay knobs", () => {
      const settings = makeSettings([
        { key: "GW_XFADE_MAX", value: "8", source: "override", applyMode: "engine-restart", kind: "number", unit: "seconds" },
      ]);
      renderWithProviders(<SettingsForm settings={settings} />);

      expect(screen.getByText(/\[override\]/)).toBeInTheDocument();
    });

    it("a number setting renders an <input type='number'>", () => {
      const settings = makeSettings([
        { key: "Loudness:TargetLufs", value: "-16", source: "default", applyMode: "live", kind: "number", unit: "LUFS" },
      ]);
      renderWithProviders(<SettingsForm settings={settings} />);

      const input = screen.getByLabelText(/Loudness:TargetLufs/) as HTMLInputElement;
      expect(input.type).toBe("number");
    });

    it("a string setting (e.g. Tts:Endpoint) renders an <input type='text'>", () => {
      // STORY-124 — Tts:Endpoint/Llm:Endpoint/Llm:Model are free-text, not numeric.
      const settings = makeSettings([
        { key: "Tts:Endpoint", value: "http://kokoro:8880", source: "default", applyMode: "live", kind: "string", unit: "" },
      ]);
      renderWithProviders(<SettingsForm settings={settings} />);

      const input = screen.getByLabelText(/Tts:Endpoint/) as HTMLInputElement;
      expect(input.type).toBe("text");
      expect(input.value).toBe("http://kokoro:8880");
    });

    it("a boolean setting renders an <input type='checkbox'>", () => {
      renderWithProviders(<SettingsForm settings={[makeBooleanSetting()]} />);

      const checkbox = screen.getByLabelText(/Station:Cadence:LeadInBeforeEachTrack/) as HTMLInputElement;
      expect(checkbox.type).toBe("checkbox");
    });

    it("a boolean setting with value 'true' renders checked", () => {
      renderWithProviders(<SettingsForm settings={[makeBooleanSetting({ value: "true" })]} />);

      const checkbox = screen.getByLabelText(/Station:Cadence:LeadInBeforeEachTrack/) as HTMLInputElement;
      expect(checkbox.checked).toBe(true);
    });

    it("a boolean setting with value 'false' renders unchecked", () => {
      renderWithProviders(<SettingsForm settings={[makeBooleanSetting({ value: "false" })]} />);

      const checkbox = screen.getByLabelText(/Station:Cadence:LeadInBeforeEachTrack/) as HTMLInputElement;
      expect(checkbox.checked).toBe(false);
    });

    it("a boolean setting with value 'True' (capital T — the .NET config provider's own casing) renders checked", () => {
      // The .NET JSON configuration provider surfaces an appsettings.json `true` literal as the
      // string "True", not "true". A case-sensitive `=== "true"` check would render this
      // appsettings-sourced boolean as unchecked while the knob is actually on (the Y6-smoke
      // gitea-#230 regression: Library:YearLookup:Enabled and both Station:Cadence:* toggles).
      renderWithProviders(<SettingsForm settings={[makeBooleanSetting({ value: "True" })]} />);

      const checkbox = screen.getByLabelText(/Station:Cadence:LeadInBeforeEachTrack/) as HTMLInputElement;
      expect(checkbox.checked).toBe(true);
    });

    it("a number setting shows its unit label", () => {
      const settings = makeSettings([
        { key: "Loudness:TargetLufs", value: "-16", source: "default", applyMode: "live", kind: "number", unit: "LUFS" },
      ]);
      renderWithProviders(<SettingsForm settings={settings} />);

      // The unit appears in parentheses next to the key name
      expect(screen.getByText(/\(LUFS\)/)).toBeInTheDocument();
    });

    it("a boolean setting does not show a unit label", () => {
      renderWithProviders(<SettingsForm settings={[makeBooleanSetting({ unit: "" })]} />);

      // No unit parenthesised text with an empty string — the setting has no unit span rendered
      // queryAllByText returns [] when nothing matches, no throw
      const emptyParens = screen.queryAllByText(/^\(\)$/);
      expect(emptyParens).toHaveLength(0);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: saving a live setting", () => {
    it("changing a number knob and saving PUTs only the changed field", async () => {
      const mockFetch = makeFetchMock(200);
      // Two settings: only Loudness:TargetLufs will change
      const settings = makeSettings();
      renderWithProviders(<SettingsForm settings={settings} />);

      const lufsInput = screen.getByLabelText(/Loudness:TargetLufs/);
      fireEvent.change(lufsInput, { target: { value: "-18" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/settings");
      expect(init.method).toBe("PUT");

      const headers = init.headers as Record<string, string>;
      expect(headers["Content-Type"]).toBe("application/json");

      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      // Only the changed field is in the body
      expect(body).toHaveLength(1);
      expect(body).toContainEqual({ key: "Loudness:TargetLufs", value: "-18" });
      // The unchanged GW_XFADE_MAX must NOT be in the body
      expect(body.some((e) => e.key === "GW_XFADE_MAX")).toBe(false);
    });

    it("a successful save toasts 'Settings saved.' (SPEC F28.9 — mutation outcomes are toasts)", async () => {
      makeFetchMock(200);
      const settings = makeSettings();
      renderWithProviders(<SettingsForm settings={settings} />);

      // Change one field so the form is submitted
      const lufsInput = screen.getByLabelText(/Loudness:TargetLufs/);
      fireEvent.change(lufsInput, { target: { value: "-18" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Settings saved.")).toBeInTheDocument();
      });
    });

    it("saving with no changes does not PUT and shows 'No changes to save.'", async () => {
      const mockFetch = makeFetchMock(200);
      renderWithProviders(<SettingsForm settings={makeSettings()} />);

      // Do NOT change any field — click Save immediately
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("status")).toHaveTextContent("No changes to save.");
      });

      // fetch must NOT have been called
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("changing a string knob (e.g. Tts:Endpoint) and saving PUTs only the changed field", async () => {
      const mockFetch = makeFetchMock(200);
      const settings = makeSettings([
        { key: "Tts:Endpoint", value: "http://kokoro:8880", source: "default", applyMode: "live", kind: "string", unit: "" },
      ]);
      renderWithProviders(<SettingsForm settings={settings} />);

      const endpointInput = screen.getByLabelText(/Tts:Endpoint/);
      fireEvent.change(endpointInput, { target: { value: "http://kokoro-2:8880" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Tts:Endpoint", value: "http://kokoro-2:8880" }]);
    });

    it("toggling a boolean checkbox and saving PUTs only that changed field", async () => {
      const mockFetch = makeFetchMock(200);
      renderWithProviders(<SettingsForm settings={[makeBooleanSetting({ value: "true" })]} />);

      const checkbox = screen.getByLabelText(/Station:Cadence:LeadInBeforeEachTrack/);
      fireEvent.click(checkbox);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toHaveLength(1);
      expect(body).toContainEqual({
        key: "Station:Cadence:LeadInBeforeEachTrack",
        value: "false",
      });
    });

    it("toggling a boolean seeded as 'True' (capital T) still submits lowercase 'false'", async () => {
      // The write path is unaffected by the render-side case-insensitivity fix: the checkbox's
      // own onChange always emits lowercase, regardless of how the original value was cased.
      const mockFetch = makeFetchMock(200);
      renderWithProviders(<SettingsForm settings={[makeBooleanSetting({ value: "True" })]} />);

      const checkbox = screen.getByLabelText(/Station:Cadence:LeadInBeforeEachTrack/) as HTMLInputElement;
      expect(checkbox.checked).toBe(true);
      fireEvent.click(checkbox);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Station:Cadence:LeadInBeforeEachTrack", value: "false" }]);
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: a rejected setting is surfaced", () => {
    it("a 400 ValidationProblemDetails shows the real backend message and does not claim success", async () => {
      // Real shape (gh-#425): ASP.NET Core ValidationProblemDetails, keyed by the actual
      // offending setting key — not a flat "settings" bucket.
      const validationProblem = {
        errors: { "Loudness:TargetLufs": ["Must be between -40 and 0"] },
        title: "One or more settings values are invalid.",
        status: 400,
      };
      makeFetchMock(400, validationProblem);
      const settings = makeSettings();
      renderWithProviders(<SettingsForm settings={settings} />);

      // Change a field so the form is submitted
      const lufsInput = screen.getByLabelText(/Loudness:TargetLufs/);
      fireEvent.change(lufsInput, { target: { value: "-18" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        // The exact backend message must be visible in an alert region
        expect(screen.getByRole("alert")).toBeInTheDocument();
        expect(screen.getByText("Must be between -40 and 0")).toBeInTheDocument();
      });

      // "Settings saved." must NOT appear
      expect(screen.queryByRole("status")).toBeNull();
    });

    it("a per-key error paints only the offending field, not every changed field (gh-#425)", async () => {
      // Two fields changed; the backend rejects only one of them, keyed by ITS key alone.
      const validationProblem = {
        errors: { "Loudness:TargetLufs": ["Must be between -40 and 0"] },
        title: "One or more settings values are invalid.",
        status: 400,
      };
      makeFetchMock(400, validationProblem);
      const settings = makeSettings();
      renderWithProviders(<SettingsForm settings={settings} />);

      fireEvent.change(screen.getByLabelText(/Loudness:TargetLufs/), { target: { value: "50" } });
      fireEvent.change(screen.getByLabelText(/GW_XFADE_MAX/), { target: { value: "10" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        // Exactly one alert region exists, and it names the offending key's own message — the
        // valid GW_XFADE_MAX change gets no error at all.
        expect(screen.getAllByRole("alert")).toHaveLength(1);
        expect(screen.getByRole("alert")).toHaveTextContent("Must be between -40 and 0");
      });
    });

    it("moves focus to the first offending field, in DOM order, on a 400 (gh-#144, gh-#425)", async () => {
      // Re-homed from the now-deleted settings-area-tabs.spec.tsx's "auto-switches to the first
      // offending tab" scenario (gh-#144's tab strip retired, T576): with every section on one
      // page now, there is no tab to switch to — a rejected field far above Save is otherwise
      // silent, so the fix moves DOM focus there instead. Both changed keys are rejected here so
      // the assertion actually exercises "first in DOM order", not merely "the only errored key" —
      // Loudness:TargetLufs renders before GW_XFADE_MAX (the Loudness section precedes Playout).
      // The 400 body deliberately lists GW_XFADE_MAX (the LATER field) first — proving the fix
      // reads DOM order off the rendered settings, not insertion order off the error map (R2-4:
      // with Loudness:TargetLufs first here too, the two orders would coincide and the assertion
      // below would pass even if the implementation just took Object.keys(fieldErrors)[0]).
      const validationProblem = {
        errors: {
          GW_XFADE_MAX: ["Must be a positive number of seconds"],
          "Loudness:TargetLufs": ["Must be between -40 and 0"],
        },
        title: "One or more settings values are invalid.",
        status: 400,
      };
      makeFetchMock(400, validationProblem);
      const settings = makeSettings();
      renderWithProviders(<SettingsForm settings={settings} />);

      fireEvent.change(screen.getByLabelText(/Loudness:TargetLufs/), { target: { value: "50" } });
      fireEvent.change(screen.getByLabelText(/GW_XFADE_MAX/), { target: { value: "-1" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(document.activeElement).toBe(screen.getByLabelText(/Loudness:TargetLufs/));
      });
    });
  });

  describe("Scenario: the offending field is a registry control, not a plain input (R2-2)", () => {
    // CorrectionsSettingControl (SETTING_CONTROL_REGISTRY) puts id={controlId} on a wrapper <div>
    // rather than a native input/select — `.focus()` is a no-op on a div with no tabIndex, so this
    // proves the gh-#144 focus fix reaches a registry control too, not just the kind-chain inputs
    // the test above already covers.
    beforeEach(async () => {
      makeFetchMock(400, {
        errors: { "Tts:Corrections": ["Rule 1's 'from' text is blank"] },
        title: "One or more settings values are invalid.",
        status: 400,
      });
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "Tts:Corrections",
              value: JSON.stringify([{ from: "MacLeod", to: "Muh-cloud" }]),
              source: "override",
              applyMode: "live",
              kind: "string",
              unit: "",
            }),
          ]}
        />
      );

      fireEvent.change(screen.getByLabelText("To text for rule 1"), {
        target: { value: "Mick-loud" },
      });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });
    });

    it("moves focus to the setting-Tts:Corrections control on a 400", async () => {
      await waitFor(() => {
        expect(document.activeElement).toBe(document.getElementById("setting-Tts:Corrections"));
      });
    });
  });
});
