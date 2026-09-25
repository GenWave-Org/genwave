// @jest-environment jsdom
// STORY-482 — Crosstalk shows as checkboxes (gh-#778 · SPEC F205.7g · PLAN T586)
//
// BDD specification — Jest. Was RED at plan time (every scenario `it.todo`); T586 turned each one
// into a real `it`. Each Given comment names the arrange the scenario needs.
//
// Runner: Jest (jsdom) + @testing-library/react, mirroring settings-choice-control.spec.tsx's
// house pattern (renderWithProviders, makeSequencedFetchMock, settingDto from
// __specs__/setting-fixture.ts) — SettingsForm calls useConfirm() unconditionally, so every render
// needs a ConfirmDialogProvider ancestor. Driven through SettingsForm, the real entry point, never
// MultiChoiceSettingControl directly.

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

const SHOW_CHOICES = [
  { value: "morning-drive", label: "Morning Drive" },
  { value: "late-late", label: "Late Late" },
  { value: "jazz-hour", label: "Jazz Hour" },
];

function makeShowsSetting(overrides: Partial<SettingDto> = {}): SettingDto {
  return settingDto({
    key: "Crosstalk:Shows",
    value: '["late-late"]',
    source: "default",
    applyMode: "live",
    kind: "multi-choice",
    unit: "",
    choices: SHOW_CHOICES,
    ...overrides,
  });
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

function makeSequencedFetchMock(specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  let callIndex = 0;
  const fn = jest.fn<typeof fetch>().mockImplementation(async () => {
    const spec = specs[Math.min(callIndex, specs.length - 1)];
    if (spec === undefined) {
      throw new Error("No mock response configured for this fetch call");
    }
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

// ---------------------------------------------------------------------------
// Feature: Crosstalk shows as checkboxes
// ---------------------------------------------------------------------------

describe("Feature: Crosstalk shows as checkboxes", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: three shows, one enabled", () => {
    // Given: a multi-choice DTO, choices morning-drive/late-late/jazz-hour, value ["late-late"]
    beforeEach(() => {
      renderWithProviders(<SettingsForm settings={[makeShowsSetting()]} />);
    });

    it("AC4 — three checkboxes render", () => {
      expect(screen.getAllByRole("checkbox")).toHaveLength(3);
    });

    it('AC4 — only "Late Late" is checked', () => {
      const checkedByLabel = SHOW_CHOICES.map(
        (choice) => (screen.getByRole("checkbox", { name: choice.label }) as HTMLInputElement).checked
      );
      expect(checkedByLabel).toEqual([false, true, false]);
    });
  });

  describe("Scenario: enabling a second show", () => {
    // Given: the same, then "Morning Drive" checked and the form saved (fetch captured)
    let mockFetch: jest.MockedFunction<typeof fetch>;

    beforeEach(async () => {
      mockFetch = makeSequencedFetchMock([{ status: 200 }]);
      renderWithProviders(<SettingsForm settings={[makeShowsSetting()]} />);

      fireEvent.click(screen.getByRole("checkbox", { name: "Morning Drive" }));
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
    });

    it('AC5 — the PUT carries Crosstalk:Shows = ["morning-drive","late-late"]', () => {
      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Crosstalk:Shows", value: '["morning-drive","late-late"]' }]);
    });
  });

  // ---- sad path ----
  describe("Scenario: the show list failed to load", () => {
    // Given: a multi-choice DTO with choicesFailed = true, value ["late-late"]
    beforeEach(() => {
      renderWithProviders(<SettingsForm settings={[makeShowsSetting({ choicesFailed: true })]} />);
    });

    it("AC8 — the checkboxes are disabled", () => {
      const allDisabled = screen
        .getAllByRole("checkbox")
        .every((checkbox) => (checkbox as HTMLInputElement).disabled);
      expect(allDisabled).toBe(true);
    });

    it("AC8 — no text input renders for the key", () => {
      expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    });
  });
});
