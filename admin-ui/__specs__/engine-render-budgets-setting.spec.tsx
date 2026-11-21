// @jest-environment jsdom
// PLAN T750a — "Time limit per voice engine" on the Settings page (SPEC F219.6, STORY-554)
// Runner: Jest (jsdom) + @testing-library/react.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import { settingDto } from "./setting-fixture";

const ENGINES_BODY = {
  active: "kokoro",
  engines: [
    { name: "kokoro", profile: "kokoro", renderBudgetSeconds: 30, configRenderBudgetSeconds: 30 },
    { name: "chatterbox", profile: "chatterbox", renderBudgetSeconds: 120, configRenderBudgetSeconds: 120 },
  ],
};

function jsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

function mockApi(): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    if (init?.method === "PUT") return jsonResponse(200, {});
    if (String(input).includes("/api/voices/engines")) return jsonResponse(200, ENGINES_BODY);
    return jsonResponse(200, {});
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

async function renderForm(stored: string): Promise<void> {
  render(
    <ConfirmDialogProvider>
      <SettingsForm
        settings={[
          settingDto({
            key: "Tts:EngineRenderBudgets",
            label: "Time limit per voice engine",
            value: stored,
            source: "default",
            applyMode: "live",
            kind: "string",
            unit: "",
          }),
        ]}
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
  await screen.findByLabelText("chatterbox time limit in seconds");
}

function field(engine: string): HTMLInputElement {
  return screen.getByLabelText(`${engine} time limit in seconds`) as HTMLInputElement;
}

function savedBody(mockFetch: jest.MockedFunction<typeof fetch>): unknown {
  const put = mockFetch.mock.calls.find((c) => (c[1] as RequestInit | undefined)?.method === "PUT");
  return JSON.parse((put?.[1] as RequestInit).body as string);
}

async function clickSave(): Promise<void> {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
    await Promise.resolve();
  });
}

describe("Feature: the time limit per voice engine setting", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: two engines and nothing saved", () => {
    it("shows one field per engine", async () => {
      mockApi();
      await renderForm("");

      expect(screen.getAllByRole("spinbutton")).toHaveLength(2);
    });

    it("shows each engine's current limit as its placeholder", async () => {
      mockApi();
      await renderForm("");

      expect(field("chatterbox")).toHaveAttribute("placeholder", "120");
    });

    it("saves the typed limit as a JSON object", async () => {
      const mockFetch = mockApi();
      await renderForm("");

      fireEvent.change(field("chatterbox"), { target: { value: "45" } });
      await clickSave();

      expect(savedBody(mockFetch)).toEqual([{ key: "Tts:EngineRenderBudgets", value: '{"chatterbox":45}' }]);
    });
  });

  describe("Scenario: a limit is already saved", () => {
    it("fills that engine's field", async () => {
      mockApi();
      await renderForm('{"chatterbox":45}');

      expect(field("chatterbox")).toHaveValue(45);
    });

    it("saves an empty value once every field is cleared", async () => {
      const mockFetch = mockApi();
      await renderForm('{"chatterbox":45}');

      fireEvent.change(field("chatterbox"), { target: { value: "" } });
      await clickSave();

      expect(savedBody(mockFetch)).toEqual([{ key: "Tts:EngineRenderBudgets", value: "" }]);
    });

    it("keeps the other engine's limit when one is cleared", async () => {
      const mockFetch = mockApi();
      await renderForm('{"chatterbox":45,"kokoro":20}');

      fireEvent.change(field("chatterbox"), { target: { value: "" } });
      await clickSave();

      expect(savedBody(mockFetch)).toEqual([{ key: "Tts:EngineRenderBudgets", value: '{"kokoro":20}' }]);
    });
  });

  describe("Scenario: a saved override changes the engine's effective limit", () => {
    it("still shows the engine's own limit as the placeholder", async () => {
      global.fetch = jest.fn<typeof fetch>().mockImplementation(async () =>
        jsonResponse(200, {
          active: "kokoro",
          engines: [
            { name: "kokoro", profile: "kokoro", renderBudgetSeconds: 30, configRenderBudgetSeconds: 30 },
            { name: "chatterbox", profile: "chatterbox", renderBudgetSeconds: 45, configRenderBudgetSeconds: 120 },
          ],
        })
      ) as unknown as typeof fetch;
      await renderForm('{"chatterbox":45}');

      expect(field("chatterbox")).toHaveAttribute("placeholder", "120");
    });
  });

  describe("Scenario: the stored keys do not match the registry exactly", () => {
    it("drops a key that is no engine when saving", async () => {
      const mockFetch = mockApi();
      await renderForm('{"oldengine":45,"kokoro":20}');

      fireEvent.change(field("kokoro"), { target: { value: "25" } });
      await clickSave();

      expect(savedBody(mockFetch)).toEqual([{ key: "Tts:EngineRenderBudgets", value: '{"kokoro":25}' }]);
    });

    it("fills the field of a key written in other case", async () => {
      mockApi();
      await renderForm('{"Chatterbox":45}');

      expect(field("chatterbox")).toHaveValue(45);
    });

    it("saves an edit under the engine's canonical name", async () => {
      const mockFetch = mockApi();
      await renderForm('{"Chatterbox":45}');

      fireEvent.change(field("chatterbox"), { target: { value: "60" } });
      await clickSave();

      expect(savedBody(mockFetch)).toEqual([{ key: "Tts:EngineRenderBudgets", value: '{"chatterbox":60}' }]);
    });
  });

  describe("Scenario: the stored value is not a JSON object", () => {
    it("shows empty fields rather than failing", async () => {
      mockApi();
      await renderForm("not json");

      await waitFor(() => expect(field("kokoro")).toHaveValue(null));
    });
  });
});
