// @jest-environment jsdom
// STORY-150 — Voice and persona picked by name, not typed by id (Epic Y / SPEC F54,
// closes gitea-#224, gitea-#225).
//
// Runner: Jest (jsdom) + @testing-library/react. Drives SettingsForm via
// @testing-library/react with a mocked fetch — mirrors settings-page.spec.tsx and
// voice-dropdown.spec.tsx in style (renderWithProviders, makeSequencedFetchMock).

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

const VOICE_IDS = ["af_alloy", "af_aoede"];

function makePersonas(): Array<{ id: number; name: string; backstory: string; style: string; voice: string }> {
  return [
    { id: 1, name: "Marlowe", backstory: "", style: "", voice: "" },
    { id: 2, name: "Nightingale", backstory: "", style: "", voice: "" },
  ];
}

function makeVoiceSetting(overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key: "Station:Voice",
    value: "af_alloy",
    source: "default",
    applyMode: "live",
    kind: "string",
    unit: "",
    ...overrides,
  };
}

function makePersonaSetting(overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key: "Station:Persona:ActiveId",
    value: "0",
    source: "default",
    applyMode: "live",
    kind: "number",
    unit: "",
    ...overrides,
  };
}

interface MockResponseSpec {
  status: number;
  body?: unknown;
  networkError?: boolean;
}

/** A fetch mock that replays one response per call, in order (last spec repeats if exhausted). */
function makeSequencedFetchMock(specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  let callIndex = 0;
  const fn = jest.fn<typeof fetch>().mockImplementation(async () => {
    const spec = specs[callIndex] ?? specs[specs.length - 1]!;
    callIndex += 1;
    if (spec.networkError === true) {
      throw new Error("network error");
    }
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
 * SettingsForm calls useConfirm() unconditionally, so every render needs a
 * ConfirmDialogProvider ancestor; Toaster is mounted alongside it for parity with the other
 * settings specs even though this file doesn't assert on toast copy.
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
// Feature: Semantic settings controls
// ---------------------------------------------------------------------------

describe("Feature: Semantic settings controls", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: Station:Voice is a voices dropdown", () => {
    it("renders a dropdown fed by GET /api/voices with the current value preselected (F54.2)", async () => {
      makeSequencedFetchMock([{ status: 200, body: VOICE_IDS }]);
      renderWithProviders(<SettingsForm settings={[makeVoiceSetting({ value: "af_aoede" })]} />);

      await waitFor(() => {
        const select = screen.getByLabelText(/Station:Voice/) as HTMLSelectElement;
        expect(select.tagName).toBe("SELECT");
        expect(select.value).toBe("af_aoede");
      });
    });

    it("offers no empty option — the validator requires non-blank (F54.2)", async () => {
      makeSequencedFetchMock([{ status: 200, body: VOICE_IDS }]);
      renderWithProviders(<SettingsForm settings={[makeVoiceSetting()]} />);

      await waitFor(() => {
        const select = screen.getByLabelText(/Station:Voice/) as HTMLSelectElement;
        const optionValues = Array.from(select.options).map((o) => o.value);
        expect(optionValues).not.toContain("");
      });
    });

    it("still offers a current value absent from the fetched list — external Tts:Endpoint voice sets (F54.2)", async () => {
      makeSequencedFetchMock([{ status: 200, body: VOICE_IDS }]);
      renderWithProviders(<SettingsForm settings={[makeVoiceSetting({ value: "custom-external-voice" })]} />);

      await waitFor(() => {
        const select = screen.getByLabelText(/Station:Voice/) as HTMLSelectElement;
        const optionValues = Array.from(select.options).map((o) => o.value);
        expect(optionValues).toContain("custom-external-voice");
        expect(select.value).toBe("custom-external-voice");
        // Marked distinctly from the fetched list's own entries
        expect(screen.getByText(/custom-external-voice.*current/i)).toBeInTheDocument();
      });
    });
  });

  describe("Scenario: Station:Persona:ActiveId shows names, submits ids", () => {
    it("lists persona NAMES from GET /api/personas and submits the picked persona's id (F54.3)", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 200, body: makePersonas() },
        { status: 200 },
      ]);
      renderWithProviders(<SettingsForm settings={[makePersonaSetting()]} />);

      const select = await screen.findByLabelText(/Station:Persona:ActiveId/) as HTMLSelectElement;
      await waitFor(() => {
        expect(screen.getByText("Nightingale")).toBeInTheDocument();
      });

      fireEvent.change(select, { target: { value: "2" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(2);
      });

      const [, init] = mockFetch.mock.calls[1] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Station:Persona:ActiveId", value: "2" }]);
    });

    it("offers 'None — persona-less patter' as value 0 (F54.3)", async () => {
      makeSequencedFetchMock([{ status: 200, body: makePersonas() }]);
      renderWithProviders(<SettingsForm settings={[makePersonaSetting({ value: "1" })]} />);

      await waitFor(() => {
        const select = screen.getByLabelText(/Station:Persona:ActiveId/) as HTMLSelectElement;
        const noneOption = Array.from(select.options).find((o) => o.value === "0");
        expect(noneOption?.textContent).toBe("None — persona-less patter");
      });
    });
  });

  describe("Scenario: submission plumbing is untouched", () => {
    it("a dropdown-picked change rides the same changed-keys PUT batch (F54.4)", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 200, body: VOICE_IDS },
        { status: 200 },
      ]);
      const settings = [
        makeVoiceSetting({ value: "af_alloy" }),
        { key: "Loudness:TargetLufs", value: "-16", source: "default" as const, applyMode: "live" as const, kind: "number" as const, unit: "LUFS" },
      ];
      renderWithProviders(<SettingsForm settings={settings} />);

      const select = await waitFor(() => screen.getByLabelText(/Station:Voice/) as HTMLSelectElement);
      fireEvent.change(select, { target: { value: "af_aoede" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(2);
      });

      const [url, init] = mockFetch.mock.calls[1] as [string, RequestInit];
      expect(url).toBe("/api/settings");
      expect(init.method).toBe("PUT");
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Station:Voice", value: "af_aoede" }]);
    });

    it("source and apply-mode badges render on overridden controls like any other field (F54.4)", async () => {
      makeSequencedFetchMock([{ status: 200, body: VOICE_IDS }]);
      renderWithProviders(
        <SettingsForm
          settings={[makeVoiceSetting({ source: "override", applyMode: "engine-restart" })]}
        />
      );

      await waitFor(() => {
        expect(screen.getByLabelText(/Station:Voice/)).toBeInTheDocument();
      });
      expect(screen.getByText(/\[override\]/)).toBeInTheDocument();
      expect(screen.getByText(/applies after engine restart/)).toBeInTheDocument();
    });

    it("keys absent from the registry keep the shipped kind-based rendering (F54.1)", () => {
      renderWithProviders(
        <SettingsForm
          settings={[
            { key: "Loudness:TargetLufs", value: "-16", source: "default", applyMode: "live", kind: "number", unit: "LUFS" },
          ]}
        />
      );

      const input = screen.getByLabelText(/Loudness:TargetLufs/) as HTMLInputElement;
      expect(input.tagName).toBe("INPUT");
      expect(input.type).toBe("number");
    });
  });

  describe("Scenario: fetch failure degrades to the shipped fallbacks", () => {
    it("an unreachable /api/voices degrades Station:Voice to free text with a notice (F54.2)", async () => {
      makeSequencedFetchMock([{ status: 502, body: { detail: "unreachable" } }]);
      renderWithProviders(<SettingsForm settings={[makeVoiceSetting({ value: "af_alloy" })]} />);

      await waitFor(() => {
        const field = screen.getByLabelText(/Station:Voice/) as HTMLInputElement;
        expect(field.tagName).toBe("INPUT");
        expect(field.type).toBe("text");
        expect(field.value).toBe("af_alloy");
      });
      expect(screen.getByText(/voice list unavailable/i)).toBeInTheDocument();
    });

    it("an unreachable /api/personas degrades ActiveId to the number input (F54.3)", async () => {
      makeSequencedFetchMock([{ status: 502, body: { detail: "unreachable" } }]);
      renderWithProviders(<SettingsForm settings={[makePersonaSetting({ value: "2" })]} />);

      await waitFor(() => {
        const field = screen.getByLabelText(/Station:Persona:ActiveId/) as HTMLInputElement;
        expect(field.tagName).toBe("INPUT");
        expect(field.type).toBe("number");
        expect(field.value).toBe("2");
      });
      expect(screen.getByText(/persona list unavailable/i)).toBeInTheDocument();
    });
  });
});
