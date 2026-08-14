// @jest-environment jsdom
// gh-#426 — `Context:Weather:PersonaId`/`Context:History:PersonaId` get a persona dropdown
// instead of a bare number input. Both keys hold a persona ROW ID as a string on the wire;
// null/0 means "the on-air DJ (default)" (SPEC F107.7, SettingValidator's ContextPersonaIdMin
// remarks).
//
// Runner: Jest (jsdom) + @testing-library/react, mirroring settings-audience-control.spec.tsx's
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

const WEATHER_PERSONA_KEY = "Context:Weather:PersonaId";
const HISTORY_PERSONA_KEY = "Context:History:PersonaId";

/** Minimal roster fixture — `usePersonaList` only reads `id`/`name` off each row. */
const PERSONAS = [
  { id: 3, name: "Flip" },
  { id: 7, name: "Mike Rophone" },
];

function makePersonaIdSetting(
  key: string,
  overrides: Partial<SettingDto> = {}
): SettingDto {
  return {
    key,
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
}

/** A fetch mock that replays one response per call, in order (last spec repeats if exhausted). */
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
// Feature: Context:*:PersonaId's dedicated Settings control
// ---------------------------------------------------------------------------

describe("Feature: Context:*:PersonaId's dedicated Settings control", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the field renders as a persona dropdown", () => {
    it("renders a select fed by GET /api/personas, default option first, current value preselected (gh-#426)", async () => {
      makeSequencedFetchMock([{ status: 200, body: PERSONAS }]);
      renderWithProviders(
        <SettingsForm settings={[makePersonaIdSetting(WEATHER_PERSONA_KEY, { value: "7" })]} />
      );

      // "7" is already selectable before the roster loads (an unrecognized value always renders
      // its own option — see the "unknown current persona id" scenario below), so wait for the
      // roster itself to land rather than for `select.value`, which is stable across both states.
      await waitFor(() => {
        const select = screen.getByLabelText(new RegExp(WEATHER_PERSONA_KEY)) as HTMLSelectElement;
        const optionLabels = Array.from(select.options).map((o) => o.textContent);
        expect(optionLabels).toContain("Flip");
      });

      const select = screen.getByLabelText(new RegExp(WEATHER_PERSONA_KEY)) as HTMLSelectElement;
      expect(select.tagName).toBe("SELECT");
      expect(select.value).toBe("7");
      const optionLabels = Array.from(select.options).map((o) => o.textContent);
      expect(optionLabels[0]).toBe("On-air DJ (default)");
      expect(optionLabels).toContain("Mike Rophone");
    });

    it("preselects the default option for value '0' (unset means the on-air DJ, F107.7)", async () => {
      makeSequencedFetchMock([{ status: 200, body: PERSONAS }]);
      renderWithProviders(
        <SettingsForm settings={[makePersonaIdSetting(WEATHER_PERSONA_KEY, { value: "0" })]} />
      );

      await waitFor(() => {
        const select = screen.getByLabelText(new RegExp(WEATHER_PERSONA_KEY)) as HTMLSelectElement;
        expect(select.value).toBe("0");
      });
    });

    it("an unrecognized current persona id gets its own 'Unknown persona (#id)' option, not a silent drop", async () => {
      makeSequencedFetchMock([{ status: 200, body: PERSONAS }]);
      renderWithProviders(
        <SettingsForm settings={[makePersonaIdSetting(WEATHER_PERSONA_KEY, { value: "42" })]} />
      );

      await waitFor(() => {
        const select = screen.getByLabelText(new RegExp(WEATHER_PERSONA_KEY)) as HTMLSelectElement;
        expect(select.value).toBe("42");
      });

      expect(screen.getByText("Unknown persona (#42)")).toBeInTheDocument();
    });
  });

  describe("Scenario: submission plumbing is untouched", () => {
    it("picking a persona by name submits the id string on the shipped changed-keys PUT batch (F54.4)", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 200, body: PERSONAS },
        { status: 200 },
      ]);
      renderWithProviders(
        <SettingsForm settings={[makePersonaIdSetting(WEATHER_PERSONA_KEY, { value: "0" })]} />
      );

      const select = await waitFor(
        () => screen.getByLabelText(new RegExp(WEATHER_PERSONA_KEY)) as HTMLSelectElement
      );
      fireEvent.change(select, { target: { value: "3" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      const [url, init] = mockFetch.mock.calls[1] as [string, RequestInit];
      expect(url).toBe("/api/settings");
      expect(init.method).toBe("PUT");
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: WEATHER_PERSONA_KEY, value: "3" }]);
    });

    it("picking 'On-air DJ (default)' submits '0' explicitly", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 200, body: PERSONAS },
        { status: 200 },
      ]);
      renderWithProviders(
        <SettingsForm settings={[makePersonaIdSetting(WEATHER_PERSONA_KEY, { value: "3" })]} />
      );

      const select = await waitFor(
        () => screen.getByLabelText(new RegExp(WEATHER_PERSONA_KEY)) as HTMLSelectElement
      );
      fireEvent.change(select, { target: { value: "0" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      const [, init] = mockFetch.mock.calls[1] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: WEATHER_PERSONA_KEY, value: "0" }]);
    });
  });

  describe("Scenario: both Context providers register the same control", () => {
    it("Context:History:PersonaId also renders as a persona dropdown (gh-#426)", async () => {
      makeSequencedFetchMock([{ status: 200, body: PERSONAS }]);
      renderWithProviders(
        <SettingsForm settings={[makePersonaIdSetting(HISTORY_PERSONA_KEY, { value: "0" })]} />
      );

      await waitFor(() => {
        const select = screen.getByLabelText(new RegExp(HISTORY_PERSONA_KEY)) as HTMLSelectElement;
        expect(select.tagName).toBe("SELECT");
      });
    });
  });
});
