// @jest-environment jsdom
// STORY-044 — Admin UI: station settings page
//
// Runner: Jest + jsdom. Drives SettingsForm via @testing-library/react with a
// mocked fetch. Covers GET rendering and PUT save path including 400 field errors.
// F2: fixtures now include kind/unit; tests cover checkbox, number input, unit hint,
// submit-only-changed behaviour, and no-change guard.

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

function makeSettings(overrides: Partial<SettingDto>[] = []): SettingDto[] {
  const defaults: SettingDto[] = [
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
  if (overrides.length > 0) {
    return overrides.map((o, i) => ({ ...defaults[i % defaults.length]!, ...o }));
  }
  return defaults;
}

function makeBooleanSetting(override: Partial<SettingDto> = {}): SettingDto {
  return {
    key: "Station:Cadence:LeadInBeforeEachTrack",
    value: "true",
    source: "default",
    applyMode: "live",
    kind: "boolean",
    unit: "",
    ...override,
  };
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
      // Real shape: ASP.NET Core ValidationProblemDetails with errors keyed under "settings"
      const validationProblem = {
        errors: { settings: ["Must be between -40 and 0"] },
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
  });
});
