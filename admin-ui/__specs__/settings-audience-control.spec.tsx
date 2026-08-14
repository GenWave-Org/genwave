// @jest-environment jsdom
// STORY-250 — `Station:Audience` gets a dedicated control on Settings (Epic F95, PLAN T116).
//
// Runner: Jest (jsdom) + @testing-library/react, mirroring settings-semantic-controls.spec.tsx's
// house pattern (renderWithProviders, makeSequencedFetchMock) — SettingsForm calls useConfirm()
// unconditionally, so every render needs a ConfirmDialogProvider ancestor.

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

function makeAudienceSetting(overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key: "Station:Audience",
    value: "everyone",
    source: "default",
    applyMode: "live",
    kind: "string",
    unit: "",
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
// Feature: Station:Audience's dedicated Settings control
// ---------------------------------------------------------------------------

describe("Feature: Station:Audience's dedicated Settings control", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the field renders as an everyone/mature dropdown", () => {
    it("renders a select preset to the current value (F95.1)", () => {
      renderWithProviders(<SettingsForm settings={[makeAudienceSetting({ value: "mature" })]} />);

      const select = screen.getByLabelText(/Station:Audience/) as HTMLSelectElement;
      expect(select.tagName).toBe("SELECT");
      expect(select.value).toBe("mature");
    });

    it("defaults to 'everyone' for the shipped default value (F95.1)", () => {
      renderWithProviders(<SettingsForm settings={[makeAudienceSetting({ value: "everyone" })]} />);

      const select = screen.getByLabelText(/Station:Audience/) as HTMLSelectElement;
      expect(select.value).toBe("everyone");
    });

    it("offers exactly the two accepted values as options (F95.1)", () => {
      renderWithProviders(<SettingsForm settings={[makeAudienceSetting()]} />);

      const select = screen.getByLabelText(/Station:Audience/) as HTMLSelectElement;
      const optionValues = Array.from(select.options).map((o) => o.value);
      expect(optionValues).toEqual(["everyone", "mature"]);
    });

    it("still shows the existing help text alongside the dedicated control", () => {
      renderWithProviders(<SettingsForm settings={[makeAudienceSetting()]} />);

      expect(screen.getByTestId("setting-help-Station:Audience")).toBeInTheDocument();
    });
  });

  describe("Scenario: flipping posture rides the shipped save/PUT plumbing", () => {
    it("picking 'mature' and saving PUTs the changed key (F95.1, F54.4)", async () => {
      const mockFetch = makeSequencedFetchMock([{ status: 200 }]);
      renderWithProviders(<SettingsForm settings={[makeAudienceSetting({ value: "everyone" })]} />);

      const select = screen.getByLabelText(/Station:Audience/) as HTMLSelectElement;
      fireEvent.change(select, { target: { value: "mature" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/settings");
      expect(init.method).toBe("PUT");
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Station:Audience", value: "mature" }]);
    });

    it("a 400 validation error surfaces inline at the field (F28.9)", async () => {
      makeSequencedFetchMock([
        {
          status: 400,
          body: { errors: { settings: ["Value 'loud' is not valid for 'Station:Audience'. Must be one of: everyone, mature."] } },
        },
      ]);
      renderWithProviders(<SettingsForm settings={[makeAudienceSetting({ value: "everyone" })]} />);

      const select = screen.getByLabelText(/Station:Audience/) as HTMLSelectElement;
      fireEvent.change(select, { target: { value: "mature" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText(/Must be one of: everyone, mature\./)).toBeInTheDocument();
      });
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
  });
});
