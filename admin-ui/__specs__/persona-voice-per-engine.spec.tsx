// @jest-environment jsdom
// PLAN T754 — a voice picker per engine on the DJ page (SPEC F219.10, STORY-556 AC5, AC6, AC8)
// Runner: Jest (jsdom). Drives PersonasClient with a fetch mock dispatched by "METHOD url".

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, within, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";

// The registry lists chatterbox before kokoro, but kokoro is the active engine.
const ENGINES = {
  active: "kokoro",
  engines: [
    { name: "chatterbox", profile: "gpu", renderBudgetSeconds: 60, configRenderBudgetSeconds: 60 },
    { name: "kokoro", profile: "cpu", renderBudgetSeconds: 30, configRenderBudgetSeconds: 30 },
  ],
};

const NOVA: PersonaDto = {
  id: 2,
  name: "Nova",
  backstory: "An upbeat morning host.",
  style: "Bright and quick.",
  voice: "af_nova",
  voices: { kokoro: "af_nova", chatterbox: "nova" },
  voiceEngine: "kokoro",
  slug: "nova",
  importedFrom: null,
  importedAt: null,
  soul: "",
  quirks: [],
  lore: [],
};

/** A DJ with a voice on kokoro only: chatterbox has none. */
const KOKORO_ONLY: PersonaDto = { ...NOVA, voices: { kokoro: "af_nova" } };

const ROUTES: Record<string, unknown> = {
  "GET /api/voices/engines": ENGINES,
  "GET /api/voices?engine=kokoro": ["af_alloy", "af_nova"],
  "GET /api/voices?engine=chatterbox": ["nova", "ada"],
};

let mockFetch: jest.MockedFunction<typeof fetch>;

function installFetch(persona: PersonaDto): void {
  mockFetch = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const key = `${(init?.method ?? "GET").toUpperCase()} ${String(input)}`;
    const body = key.startsWith("PATCH ")
      ? persona
      : key === "POST /api/tts/preview"
        ? null
        : (ROUTES[key] ?? []);
    return {
      ok: true,
      status: 200,
      json: () => Promise.resolve(body),
      text: () => Promise.resolve(JSON.stringify(body)),
      blob: () => Promise.resolve(new Blob(["RIFF"], { type: "audio/wav" })),
      headers: new Headers(),
    } as unknown as Response;
  });
  global.fetch = mockFetch as unknown as typeof fetch;
}

async function openEditor(persona: PersonaDto): Promise<void> {
  installFetch(persona);
  render(
    <ConfirmDialogProvider>
      <PersonasClient initialPersonas={[persona]} />
      <Toaster />
    </ConfirmDialogProvider>
  );
  fireEvent.click(screen.getByRole("button", { name: `Edit ${persona.name}` }));
  await screen.findByRole("group", { name: "Voice on kokoro" });
  // Each picker's voice list has arrived, so the saved voices are selectable.
  await waitFor(() => {
    expect(screen.getByRole("combobox", { name: "Voice on chatterbox" })).not.toBeDisabled();
    expect(screen.getByRole("combobox", { name: "Voice on kokoro" })).not.toBeDisabled();
  });
}

function lastBody(method: string, url: string): Record<string, unknown> {
  const call = mockFetch.mock.calls.filter(
    ([u, init]) => String(u) === url && (init?.method ?? "GET") === method
  ).at(-1);
  return JSON.parse(String(call?.[1]?.body)) as Record<string, unknown>;
}

describe("Feature: a voice per engine on the DJ page", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    URL.createObjectURL = jest.fn<(obj: Blob | MediaSource) => string>(() => "blob:sample");
    URL.revokeObjectURL = jest.fn<(url: string) => void>();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: two engines", () => {
    beforeEach(async () => {
      // Given two voice engines, kokoro active, and a DJ with a voice on each.
      await openEditor(NOVA);
    });

    it("shows one picker per engine", () => {
      expect(screen.getAllByRole("group", { name: /^Voice on / })).toHaveLength(2);
    });

    it("puts the active engine's picker first", () => {
      const groups = screen.getAllByRole("group", { name: /^Voice on / });
      expect(groups[0]).toHaveAccessibleName("Voice on kokoro");
    });

    it("marks the active engine", () => {
      const marked = screen.getAllByText("In use");
      expect(marked.map((el) => el.closest("[role=group]")?.getAttribute("aria-label"))).toEqual([
        "Voice on kokoro",
      ]);
    });

    it("saves the map with both voices", async () => {
      fireEvent.change(screen.getByRole("combobox", { name: "Voice on chatterbox" }), { target: { value: "ada" } });
      fireEvent.click(screen.getByRole("button", { name: "Save changes" }));
      await waitFor(() => expect(mockFetch).toHaveBeenCalledWith("/api/personas/2", expect.anything()));
      expect(lastBody("PATCH", "/api/personas/2")).toMatchObject({
        voice: "af_nova",
        voices: { kokoro: "af_nova", chatterbox: "ada" },
      });
    });
  });

  describe("Scenario: clearing an engine's voice", () => {
    it("sends a blank id for that engine", async () => {
      await openEditor(NOVA);
      fireEvent.change(screen.getByRole("combobox", { name: "Voice on chatterbox" }), { target: { value: "" } });
      fireEvent.click(screen.getByRole("button", { name: "Save changes" }));
      await waitFor(() => expect(mockFetch).toHaveBeenCalledWith("/api/personas/2", expect.anything()));
      expect(lastBody("PATCH", "/api/personas/2")).toMatchObject({ voices: { kokoro: "af_nova", chatterbox: "" } });
    });
  });

  describe("Scenario: a DJ whose own engine is not kokoro", () => {
    it("sends the voice of the engine the server names", async () => {
      await openEditor({ ...NOVA, voice: "nova", voiceEngine: "chatterbox" });
      fireEvent.change(screen.getByRole("combobox", { name: "Voice on chatterbox" }), { target: { value: "ada" } });
      fireEvent.click(screen.getByRole("button", { name: "Save changes" }));
      await waitFor(() => expect(mockFetch).toHaveBeenCalledWith("/api/personas/2", expect.anything()));
      expect(lastBody("PATCH", "/api/personas/2")).toMatchObject({ voice: "ada" });
    });
  });

  describe("Scenario: Hear it", () => {
    it("requests a sample in that picker's voice and engine", async () => {
      // Given a DJ with a voice on each engine.
      await openEditor(NOVA);
      // When Hear it is pressed on the chatterbox picker.
      fireEvent.click(screen.getByRole("button", { name: "Hear it on chatterbox" }));
      // Then the sample is requested with that voice on that engine.
      await waitFor(() => expect(mockFetch).toHaveBeenCalledWith("/api/tts/preview", expect.anything()));
      expect(lastBody("POST", "/api/tts/preview")).toMatchObject({ voice: "nova", engine: "chatterbox" });
    });
  });

  describe("Scenario: an engine with no voice chosen", () => {
    it("says this DJ stays quiet on that engine", async () => {
      // Given a DJ with a voice on kokoro and none on chatterbox.
      await openEditor(KOKORO_ONLY);
      // Then the chatterbox picker says so.
      const picker = screen.getByRole("group", { name: "Voice on chatterbox" });
      expect(within(picker).getByText("No voice — this DJ stays quiet on chatterbox.")).toBeInTheDocument();
    });
  });
});
