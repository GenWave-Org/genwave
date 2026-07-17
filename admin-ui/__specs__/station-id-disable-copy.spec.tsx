// @jest-environment jsdom
// STORY-136 — Station IDs: no boot blast, a real off switch (Epic V / SPEC F42.2, closes gitea-#216) —
// UI copy half. The behavior halves live in Orchestration.Tests and Host.Tests
// Story136_* spec files.
//
// Runner: Jest (jsdom) + @testing-library/react. Implemented V6 (2026-07-14): the "0 disables"
// helper copy is delivered through SettingsForm's FIELD_HELP_TEXT map (a small additive lookup,
// same shape as EMPTY_LIST_POLICIES) rather than a new metadata layer.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";

const STATION_ID_KEY = "Station:Cadence:StationIdEveryNUnits";

function makeStationIdSetting(overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key: STATION_ID_KEY,
    value: "4",
    source: "default",
    applyMode: "live",
    kind: "number",
    unit: "count",
    ...overrides,
  };
}

function makeFetchMock(status: number, body: unknown = {}): jest.MockedFunction<typeof fetch> {
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
 * SettingsForm calls useConfirm() unconditionally, so every render needs a
 * ConfirmDialogProvider ancestor; Toaster renders the success/error toasts.
 */
function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

describe("Feature: The station-ID field says what zero means", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the off switch is discoverable", () => {
    it("states '0 disables' on the StationIdEveryNUnits settings field (F42.2)", () => {
      renderWithProviders(<SettingsForm settings={[makeStationIdSetting()]} />);

      expect(screen.getByText(/0 disables station IDs/i)).toBeInTheDocument();
    });

    it("accepts a 0 submission without an inline validation error (F42.2)", async () => {
      const mockFetch = makeFetchMock(200);
      renderWithProviders(<SettingsForm settings={[makeStationIdSetting()]} />);

      const input = screen.getByLabelText(new RegExp(STATION_ID_KEY));
      fireEvent.change(input, { target: { value: "0" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: STATION_ID_KEY, value: "0" }]);

      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });
  });
});
