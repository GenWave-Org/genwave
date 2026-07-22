// @jest-environment jsdom
// STORY-210 — Voice is picked from a list, not typed from memory (SPEC F79.4, F79.5)
//
// Runner: Jest (jsdom) + @testing-library/react. Drives PersonasClient (the persona form + list)
// with a fetch mock dispatched by "METHOD url" (mirrors personas-page.spec.tsx's harness) rather
// than a call-order sequence, since a single render can trigger GET /api/voices (the reused R3
// VoiceControl's mount fetch, now shared via useVoiceList — SPEC F79.5's "one voice-listing path")
// alongside the import-warning derivation's own GET /api/personas/{slug}/export. useConfirm()/
// toast need their providers, so every render wraps in ConfirmDialogProvider and mounts Toaster.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonasClientProps } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const VOICE_IDS = ["af_alloy", "af_aoede"];

/** A persona whose legacy voice is the station-default sentinel (SPEC F35.1) — the only way this
 * shape occurs post-import-with-a-warning (SPEC F79.4); the ordinary create/edit form always
 * keeps `voice` and the card's `voice.voiceId` in sync. */
const NOVA: PersonaDto = {
  id: 2,
  name: "Nova",
  backstory: "An upbeat morning host.",
  style: "Bright and quick.",
  voice: "",
};

/** A card naming a voice this station doesn't have — what `GET /api/personas/nova/export` would
 * return after an import degraded Nova's legacy voice to "" (SPEC F79.4's own persisted shape:
 * LegacyVoice="", card `voice.voiceId` unchanged). */
function unresolvedVoiceCardJson(): string {
  return JSON.stringify({
    schemaVersion: 1,
    name: "Nova",
    tagline: "",
    soul: "",
    quirks: [],
    voice: { engine: "", voiceId: "af_unknown", pace: 1.0, language: "en" },
    energyDisposition: 0,
    lore: [],
    corrections: [],
  });
}

// ---------------------------------------------------------------------------
// Fetch mock — dispatched by "METHOD url" (matches personas-page.spec.tsx's own harness).
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
  text?: string;
}

function routeKey(method: string, url: string): string {
  return `${method.toUpperCase()} ${url}`;
}

const DEFAULT_ROUTES: Record<string, RouteResponseSpec> = {
  "GET /api/voices": { status: 200, body: [] },
};

function makeDispatchFetchMock(
  routes: Record<string, RouteResponseSpec>
): jest.MockedFunction<typeof fetch> {
  const allRoutes = { ...DEFAULT_ROUTES, ...routes };
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = init?.method ?? "GET";
    const url = String(input);
    const spec = allRoutes[routeKey(method, url)] ?? { status: 200, body: {} };
    return {
      ok: spec.status >= 200 && spec.status < 300,
      status: spec.status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
      text: jest.fn<() => Promise<string>>().mockResolvedValue(spec.text ?? JSON.stringify(spec.body ?? {})),
      headers: new Headers(),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function renderClient(overrides: Partial<PersonasClientProps> = {}): ReturnType<typeof render> {
  const props: PersonasClientProps = {
    initialPersonas: [NOVA],
    initialActiveId: 0,
    ...overrides,
  };
  return render(
    <ConfirmDialogProvider>
      <PersonasClient {...props} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function findCall(
  mockFetch: jest.MockedFunction<typeof fetch>,
  method: string,
  url: string
): [string, RequestInit] | undefined {
  return mockFetch.mock.calls.find(
    ([callUrl, init]) => String(callUrl) === url && ((init as RequestInit | undefined)?.method ?? "GET") === method
  ) as [string, RequestInit] | undefined;
}

// ---------------------------------------------------------------------------
// Feature: Persona voice picker
// ---------------------------------------------------------------------------

describe("Feature: Persona voice picker", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: live voices listed", () => {
    it("offers the engine's voice list when the field opens", async () => {
      makeDispatchFetchMock({ "GET /api/voices": { status: 200, body: VOICE_IDS } });
      renderClient({ initialPersonas: [] });

      await waitFor(() => {
        const select = screen.getByLabelText("Voice") as HTMLSelectElement;
        expect(select.tagName).toBe("SELECT");
        const values = Array.from(select.options).map((o) => o.value);
        expect(values).toEqual(["", ...VOICE_IDS]);
      });
    });

    it("submits the selected voiceId unchanged", async () => {
      const created: PersonaDto = { ...NOVA, id: 9 };
      const mockFetch = makeDispatchFetchMock({
        "GET /api/voices": { status: 200, body: VOICE_IDS },
        "POST /api/personas": { status: 201, body: created },
      });
      renderClient({ initialPersonas: [] });

      const select = (await screen.findByLabelText("Voice")) as HTMLSelectElement;
      await waitFor(() => {
        expect(Array.from(select.options).map((o) => o.value)).toContain("af_aoede");
      });
      fireEvent.change(select, { target: { value: "af_aoede" } });
      fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Nova" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Create persona" }));
        await Promise.resolve();
      });

      const call = findCall(mockFetch, "POST", "/api/personas");
      expect(call).toBeDefined();
      const [, init] = call as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body["voice"]).toBe("af_aoede");
    });
  });

  describe("Scenario: import warning leads here", () => {
    it("names the unresolved voice in the warning", async () => {
      makeDispatchFetchMock({
        "GET /api/voices": { status: 200, body: VOICE_IDS },
        "GET /api/personas/nova/export": { status: 200, text: unresolvedVoiceCardJson() },
      });
      renderClient({ initialPersonas: [NOVA] });

      fireEvent.click(screen.getByRole("button", { name: "Edit Nova" }));

      await waitFor(() => {
        expect(screen.getByRole("alert")).toHaveTextContent(/af_unknown/i);
      });
    });

    it("links the warning to the voice picker", async () => {
      makeDispatchFetchMock({
        "GET /api/voices": { status: 200, body: VOICE_IDS },
        "GET /api/personas/nova/export": { status: 200, text: unresolvedVoiceCardJson() },
      });
      renderClient({ initialPersonas: [NOVA] });

      fireEvent.click(screen.getByRole("button", { name: "Edit Nova" }));

      const link = await screen.findByRole("link", { name: /pick a voice/i });
      expect(link).toHaveAttribute("href", "#persona-voice");
      expect(screen.getByLabelText("Voice")).toHaveAttribute("id", "persona-voice");
    });
  });

  describe("Scenario: engine down (sad path)", () => {
    it("degrades to a free-text voice field", async () => {
      makeDispatchFetchMock({
        "GET /api/voices": { status: 502, body: { detail: "Kokoro unreachable" } },
      });
      renderClient({ initialPersonas: [] });

      await waitFor(() => {
        const voiceField = screen.getByLabelText("Voice") as HTMLInputElement;
        expect(voiceField.tagName).toBe("INPUT");
        expect(voiceField.type).toBe("text");
      });
    });

    it("keeps the form submittable", async () => {
      const created: PersonaDto = { ...NOVA, id: 9, voice: "custom-voice" };
      const mockFetch = makeDispatchFetchMock({
        "GET /api/voices": { status: 502, body: { detail: "Kokoro unreachable" } },
        "POST /api/personas": { status: 201, body: created },
      });
      renderClient({ initialPersonas: [] });

      const voiceField = await waitFor(() => {
        const field = screen.getByLabelText("Voice") as HTMLInputElement;
        expect(field.tagName).toBe("INPUT");
        return field;
      });

      fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Nova" } });
      fireEvent.change(voiceField, { target: { value: "custom-voice" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Create persona" }));
        await Promise.resolve();
      });

      const call = findCall(mockFetch, "POST", "/api/personas");
      expect(call).toBeDefined();
      const [, init] = call as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body["voice"]).toBe("custom-voice");
    });
  });
});
