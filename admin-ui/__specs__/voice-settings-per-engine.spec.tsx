// @jest-environment jsdom
// PLAN T754 — the voice settings take a voice per engine (SPEC F219.8, F219.10)
// Runner: Jest (jsdom). Drives SettingsForm with a fetch mock dispatched by "METHOD url".

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import { settingDto } from "./setting-fixture";

const ENGINES = {
  active: "kokoro",
  engines: [
    { name: "chatterbox", profile: "gpu", renderBudgetSeconds: 60, configRenderBudgetSeconds: 60 },
    { name: "kokoro", profile: "cpu", renderBudgetSeconds: 30, configRenderBudgetSeconds: 30 },
  ],
};

const ROUTES: Record<string, unknown> = {
  "GET /api/voices/engines": ENGINES,
  "GET /api/voices?engine=kokoro": ["af_alloy", "af_nova"],
  "GET /api/voices?engine=chatterbox": ["nova", "ada"],
  "PUT /api/settings": {},
};

let mockFetch: jest.MockedFunction<typeof fetch>;

function renderSetting(key: string, value: string): void {
  renderSettings([[key, value]]);
}

function renderSettings(entries: Array<[string, string]>): void {
  mockFetch = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const body = ROUTES[`${(init?.method ?? "GET").toUpperCase()} ${String(input)}`] ?? [];
    return { ok: true, status: 200, json: () => Promise.resolve(body), headers: new Headers() } as unknown as Response;
  });
  global.fetch = mockFetch as unknown as typeof fetch;
  render(
    <ConfirmDialogProvider>
      <SettingsForm
        settings={entries.map(([key, value]) =>
          settingDto({ key, value, source: "override", applyMode: "live", kind: "string", unit: "" })
        )}
      />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

async function savedValue(key: string): Promise<string> {
  fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
  await waitFor(() => expect(mockFetch).toHaveBeenCalledWith("/api/settings", expect.objectContaining({ method: "PUT" })));
  const call = mockFetch.mock.calls.find(([u]) => String(u) === "/api/settings");
  const body = JSON.parse(String(call?.[1]?.body)) as Array<{ key: string; value: string }>;
  return body.find((entry) => entry.key === key)?.value ?? "";
}

/** The wire sends `choices: null` for a non-choice setting; the engine list never answers. */
function renderBeforeEnginesLoad(key: string, value: string): void {
  global.fetch = jest.fn<typeof fetch>().mockImplementation(() => new Promise<Response>(() => undefined));
  render(
    <ConfirmDialogProvider>
      <SettingsForm
        settings={[settingDto({ key, value, source: "override", applyMode: "live", kind: "string", unit: "", choices: null })]}
      />
    </ConfirmDialogProvider>
  );
}

describe("Feature: the voice settings take a voice per engine", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the announcer voice", () => {
    it("saves a voice on a second engine as a map", async () => {
      renderSetting("Station:Ads:AnnouncerVoice", "af_nova");
      const chatterbox = await screen.findByRole("combobox", { name: "Voice on chatterbox" });
      await waitFor(() => expect(chatterbox).not.toBeDisabled());
      fireEvent.change(chatterbox, { target: { value: "ada" } });
      expect(await savedValue("Station:Ads:AnnouncerVoice")).toBe('{"kokoro":"af_nova","chatterbox":"ada"}');
    });
  });

  describe("Scenario: the engine list has not loaded and the server sent no choices", () => {
    it("renders the announcer voice as a text box", () => {
      renderBeforeEnginesLoad("Station:Ads:AnnouncerVoice", "af_nova");
      expect(screen.getByDisplayValue("af_nova")).toBeInTheDocument();
    });

    it("shows the cast as a text box until the engines load", () => {
      renderBeforeEnginesLoad("Station:Ads:CastVoices", "af_nova");
      expect(screen.getByDisplayValue("af_nova")).toBeInTheDocument();
    });
  });

  describe("Scenario: the cast", () => {
    it("shows one speaker per entry of the old comma list", async () => {
      renderSetting("Station:Ads:CastVoices", "af_nova,af_alloy");
      expect(await screen.findAllByRole("group", { name: /^Speaker \d$/ })).toHaveLength(2);
    });

    it("adds a speaker with a voice on a second engine", async () => {
      renderSetting("Station:Ads:CastVoices", "af_nova");
      fireEvent.click(await screen.findByRole("button", { name: "Add a speaker" }));
      const second = screen.getByRole("group", { name: "Speaker 2" });
      const chatterbox = within(second).getByRole("combobox", { name: "Voice on chatterbox" });
      await waitFor(() => expect(chatterbox).not.toBeDisabled());
      fireEvent.change(chatterbox, { target: { value: "ada" } });
      expect(await savedValue("Station:Ads:CastVoices")).toBe('["af_nova",{"chatterbox":"ada"}]');
    });

    it("removes a speaker", async () => {
      renderSetting("Station:Ads:CastVoices", '["af_nova","af_alloy"]');
      fireEvent.click(await screen.findByRole("button", { name: "Remove speaker 1" }));
      expect(await savedValue("Station:Ads:CastVoices")).toBe('["af_alloy"]');
    });
  });

  describe("Scenario: values the server would refuse", () => {
    it("gives the station voice no blank option on the one voice it holds", async () => {
      renderSetting("Station:Voice", "af_nova");
      const kokoro = await screen.findByRole("combobox", { name: "Voice on kokoro" });
      expect(within(kokoro).queryByRole("option", { name: "No voice" })).toBeNull();
    });

    it("offers the announcer a blank option worded as the station voice", async () => {
      renderSetting("Station:Ads:AnnouncerVoice", "af_nova");
      const kokoro = await screen.findByRole("combobox", { name: "Voice on kokoro" });
      expect(within(kokoro).getByRole("option", { name: "Station voice" })).toBeInTheDocument();
    });

    it("cannot remove the last speaker", async () => {
      renderSetting("Station:Ads:CastVoices", "af_nova");
      expect(await screen.findByRole("button", { name: "Remove speaker 1" })).toBeDisabled();
    });

    it("cannot remove speaker 1 after adding an empty speaker 2", async () => {
      renderSetting("Station:Ads:CastVoices", "af_nova");
      fireEvent.click(await screen.findByRole("button", { name: "Add a speaker" }));
      expect(screen.getByRole("button", { name: "Remove speaker 1" })).toBeDisabled();
    });

    it("keeps the first speaker's last voice non-clearable while the second has none", async () => {
      renderSetting("Station:Ads:CastVoices", "af_nova");
      fireEvent.click(await screen.findByRole("button", { name: "Add a speaker" }));
      const first = screen.getByRole("group", { name: "Speaker 1" });
      const kokoro = within(first).getByRole("combobox", { name: "Voice on kokoro" });
      expect(within(kokoro).queryByRole("option", { name: "None" })).toBeNull();
    });

    it("cannot clear both speakers: the second to hold a voice loses its blank option", async () => {
      renderSetting("Station:Ads:CastVoices", '["af_nova","af_alloy"]');
      const first = await screen.findByRole("group", { name: "Speaker 1" });
      fireEvent.change(within(first).getByRole("combobox", { name: "Voice on kokoro" }), { target: { value: "" } });
      const second = screen.getByRole("group", { name: "Speaker 2" });
      const kokoro = within(second).getByRole("combobox", { name: "Voice on kokoro" });
      expect(within(kokoro).queryByRole("option", { name: "None" })).toBeNull();
    });

    it("does not offer a voice another speaker already holds on that engine", async () => {
      renderSetting("Station:Ads:CastVoices", "af_nova,af_alloy");
      const first = await screen.findByRole("group", { name: "Speaker 1" });
      const kokoro = within(first).getByRole("combobox", { name: "Voice on kokoro" });
      expect(within(kokoro).queryByRole("option", { name: "af_alloy" })).toBeNull();
    });
  });

  describe("Scenario: several voice controls on one page", () => {
    it("asks for each engine's voice list once", async () => {
      renderSettings([
        ["Station:Voice", "af_nova"],
        ["Station:Ads:AnnouncerVoice", "af_nova"],
        ["Station:Ads:CastVoices", "af_nova,af_alloy"],
      ]);
      await waitFor(() => expect(screen.getAllByRole("combobox", { name: "Voice on chatterbox" })).toHaveLength(4));
      await waitFor(() =>
        expect(
          screen.getAllByRole("combobox", { name: "Voice on chatterbox" }).every((box) => !(box as HTMLSelectElement).disabled)
        ).toBe(true)
      );
      const listRequests = mockFetch.mock.calls.filter(([u]) => String(u).startsWith("/api/voices?engine="));
      expect(listRequests).toHaveLength(2);
    });
  });
});
