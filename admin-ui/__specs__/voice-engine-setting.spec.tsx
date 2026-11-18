// @jest-environment jsdom
// PLAN T750 — "Voice engine" on the Settings page (SPEC F219.2, STORY-555 AC4)
// Runner: Jest (jsdom) + @testing-library/react.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";
import { settingDto } from "./setting-fixture";

const ENGINES_BODY = {
  active: "kokoro",
  engines: [
    { name: "kokoro", profile: "kokoro" },
    { name: "chatterbox", profile: "chatterbox" },
  ],
};

function engineSetting(): SettingDto {
  return settingDto({
    key: "Tts:Engine",
    label: "Voice engine",
    value: "kokoro",
    source: "default",
    applyMode: "live",
    kind: "string",
    unit: "",
  });
}

function jsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

/**
 * Routes GET /api/voices/engines to the registry body and PUT /api/settings to `putResponse`.
 * The active engine is server state: it moves to the saved value only when the PUT returns 200.
 */
function mockApi(putResponse: Response): jest.MockedFunction<typeof fetch> {
  let active = ENGINES_BODY.active;
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    if (init?.method === "PUT") {
      if (putResponse.ok) {
        const sent = JSON.parse(init.body as string) as { key: string; value: string }[];
        active = sent[0]?.value ?? active;
      }
      return putResponse;
    }
    if (String(input).includes("/api/voices/engines")) {
      return jsonResponse(200, { ...ENGINES_BODY, active });
    }
    return jsonResponse(200, {});
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

async function renderForm(): Promise<HTMLSelectElement> {
  render(
    <ConfirmDialogProvider>
      <SettingsForm settings={[engineSetting()]} />
      <Toaster />
    </ConfirmDialogProvider>
  );
  const select = screen.getByLabelText("Voice engine") as HTMLSelectElement;
  await waitFor(() => expect(select.options.length).toBeGreaterThan(1));
  return select;
}

function optionTexts(select: HTMLSelectElement): string[] {
  return Array.from(select.options).map((o) => o.text);
}

async function clickSave(): Promise<void> {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
    await Promise.resolve();
  });
}

describe("Feature: the voice engine setting", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: two engines in the registry", () => {
    it("lists both engines", async () => {
      mockApi(jsonResponse(200, {}));
      const select = await renderForm();

      expect(Array.from(select.options).map((o) => o.value)).toEqual(["kokoro", "chatterbox"]);
    });

    it("marks the active engine", async () => {
      mockApi(jsonResponse(200, {}));
      const select = await renderForm();

      expect(optionTexts(select)).toEqual(["kokoro (active)", "chatterbox"]);
    });

    it("saves the chosen engine", async () => {
      const mockFetch = mockApi(jsonResponse(200, {}));
      const select = await renderForm();

      fireEvent.change(select, { target: { value: "chatterbox" } });
      await clickSave();

      const put = mockFetch.mock.calls.find((c) => (c[1] as RequestInit | undefined)?.method === "PUT");
      expect(JSON.parse((put?.[1] as RequestInit).body as string)).toEqual([
        { key: "Tts:Engine", value: "chatterbox" },
      ]);
    });
  });

  describe("Scenario: the save succeeds", () => {
    it("marks the newly saved engine active", async () => {
      mockApi(jsonResponse(200, {}));
      const select = await renderForm();

      fireEvent.change(select, { target: { value: "chatterbox" } });
      await clickSave();

      await waitFor(() => expect(optionTexts(select)).toEqual(["kokoro", "chatterbox (active)"]));
    });
  });

  describe("Scenario: the save is refused", () => {
    async function refuseChatterbox(): Promise<HTMLSelectElement> {
      mockApi(
        jsonResponse(400, {
          errors: { "Tts:Engine": ["Must be one of the configured engines: kokoro, chatterbox"] },
        })
      );
      const select = await renderForm();
      fireEvent.change(select, { target: { value: "chatterbox" } });
      await clickSave();
      return select;
    }

    it("shows the error", async () => {
      await refuseChatterbox();

      expect(await screen.findByRole("alert")).toHaveTextContent(/configured engines/i);
    });

    it("keeps the old engine marked active", async () => {
      const select = await refuseChatterbox();
      await screen.findByRole("alert");

      expect(optionTexts(select)).toEqual(["kokoro (active)", "chatterbox"]);
    });
  });
});
