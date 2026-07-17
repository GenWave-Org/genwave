// @jest-environment jsdom
// STORY-126 — Author personas from the console (SPEC F35.7)
//
// Drives PersonasClient via @testing-library/react with a fetch mock dispatched BY URL+METHOD
// (mirrors the CatalogTable/CatalogToolbar harness in catalog-selection-toolbar.spec.tsx) rather
// than a call-order sequence — this page's single render can trigger GET /api/voices (the reused
// R3 VoiceControl's mount fetch), POST/PATCH/DELETE /api/personas, PUT /api/settings, and both
// preview endpoints, in whatever order a given interaction drives them. useConfirm()/toast need
// their providers, so every render wraps in ConfirmDialogProvider and mounts Toaster (mirrors
// feedback-primitives.spec.tsx's harness pattern).
//
// Deviations from the STORY-126 it.todo wording (documented per PLAN.md T10):
//   - "lists personas from GET /api/personas" is split across two angles: the server component
//     (page.tsx) actually issuing the GET and handing the rows to PersonasClient as props (mirrors
//     SafeContentPage/SettingsPage — GET happens server-side, not from PersonasClient), and
//     PersonasClient rendering whatever rows it's given. Both are exercised below.
//   - "shows the generated copy text for a chosen track (F35.6)" drops "for a chosen track" — this
//     page never surfaces a track/kind picker (SPEC F35.6 legally allows a null-track, LeadIn-kind
//     preview, which is all this page ever sends); the scenario instead previews a saved persona.

jest.mock("next/headers", () => ({
  cookies: jest.fn().mockResolvedValue({ toString: () => "session=test-cookie" }),
}));

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactNode } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonasClientProps } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const REX: PersonaDto = {
  id: 1,
  name: "Radio Rex",
  backstory: "A grizzled late-night jock who has seen every format come and go.",
  style: "Warm, gravelly, brief.",
  voice: "af_alloy",
};

const NOVA: PersonaDto = {
  id: 2,
  name: "Nova",
  backstory: "An upbeat morning host.",
  style: "Bright and quick.",
  voice: "",
};

const VOICE_IDS = ["af_alloy", "af_aoede"];

// ---------------------------------------------------------------------------
// Fetch mock — dispatched by "METHOD url", relative wire calls only (matches how
// PersonasClient/VoiceControl/PersonaPreview all issue same-origin fetch()).
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
  blob?: Blob;
}

function routeKey(method: string, url: string): string {
  return `${method.toUpperCase()} ${url}`;
}

/** GET /api/voices defaults to an empty list (renders "Station default" only) unless a test
 * overrides it — most CRUD/activate/delete/preview scenarios don't care about the voice list. */
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
      blob: jest.fn<() => Promise<Blob>>().mockResolvedValue(spec.blob ?? new Blob(["wav-bytes"])),
      headers: new Headers(),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function renderClient(overrides: Partial<PersonasClientProps> = {}): ReturnType<typeof render> {
  const props: PersonasClientProps = {
    initialPersonas: [REX, NOVA],
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

/** Finds the persona's own `<tr>` via its dedicated name span (SPEC: the badge sits in the same
 * cell, so a raw `getByText(name)` on the cell itself would fail once a badge is present). */
function rowFor(name: string): HTMLElement {
  const nameNode = screen.getByTestId(`persona-name-${name}`);
  const row = nameNode.closest("tr");
  if (row === null) throw new Error(`No <tr> ancestor for "${name}"`);
  return row;
}

/** Confirms the currently-open useConfirm() dialog (mirrors catalog-library-actions.spec.tsx). */
async function confirmDialog(name = "Delete"): Promise<void> {
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name }));
    await Promise.resolve();
  });
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
// Feature: Author personas from the console
// ---------------------------------------------------------------------------

describe("Feature: Author personas from the console", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    // jsdom ships no Blob-URL implementation at all (not even a stub) — PersonaPreview's
    // playback path needs both mocked so it can hand the <audio> element a src.
    URL.createObjectURL = jest.fn<(obj: Blob | MediaSource) => string>(() => "blob:mock-url");
    URL.revokeObjectURL = jest.fn<(url: string) => void>();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: CRUD from the page", () => {
    it("lists personas from GET /api/personas", async () => {
      // GET /api/personas happens server-side (page.tsx), mirroring SafeContentPage/SettingsPage
      // — verify the server component reaches the endpoint and hands PersonasClient exactly the
      // rows it returned, then verify PersonasClient renders whatever rows it's given.
      const mockFetch = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url.includes("/api/personas")) {
          return {
            ok: true,
            status: 200,
            json: jest.fn<() => Promise<unknown>>().mockResolvedValue([REX, NOVA]),
            headers: new Headers(),
          } as unknown as Response;
        }
        return {
          ok: true,
          status: 200,
          json: jest.fn<() => Promise<unknown>>().mockResolvedValue([]),
          headers: new Headers(),
        } as unknown as Response;
      });
      global.fetch = mockFetch as unknown as typeof fetch;

      const { default: PersonasPage } = await import("../app/(authed)/personas/page");
      const tree = await PersonasPage();

      function findPropsWithKey(node: ReactNode, key: string): Record<string, unknown> | null {
        if (node === null || node === undefined || typeof node !== "object") return null;
        if (Array.isArray(node)) {
          for (const child of node) {
            const found = findPropsWithKey(child, key);
            if (found !== null) return found;
          }
          return null;
        }
        const el = node as { props?: Record<string, unknown> };
        if (el.props && key in el.props) return el.props;
        if (el.props?.["children"] !== undefined) {
          return findPropsWithKey(el.props["children"] as ReactNode, key);
        }
        return null;
      }

      const clientProps = findPropsWithKey(tree, "initialPersonas");
      expect(clientProps?.["initialPersonas"]).toEqual([REX, NOVA]);

      const urls = mockFetch.mock.calls.map(([url]) => String(url));
      expect(urls.some((u) => u.includes("/api/personas"))).toBe(true);

      // PersonasClient itself, given those same rows, renders them.
      makeDispatchFetchMock({});
      renderClient({ initialPersonas: [REX, NOVA] });
      await waitFor(() => {
        expect(screen.getByTestId("persona-name-Radio Rex")).toHaveTextContent("Radio Rex");
        expect(screen.getByTestId("persona-name-Nova")).toHaveTextContent("Nova");
      });
    });

    it("creates a persona and shows it in the list", async () => {
      const created: PersonaDto = {
        id: 3,
        name: "The Professor",
        backstory: "Dry wit, deep crates.",
        style: "Deadpan.",
        voice: "",
      };
      makeDispatchFetchMock({ "POST /api/personas": { status: 201, body: created } });
      renderClient({ initialPersonas: [] });

      fireEvent.change(screen.getByLabelText("Name"), { target: { value: "The Professor" } });
      fireEvent.change(screen.getByLabelText("Backstory"), { target: { value: "Dry wit, deep crates." } });
      fireEvent.change(screen.getByLabelText("Style"), { target: { value: "Deadpan." } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Create persona" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByTestId("persona-name-The Professor")).toBeInTheDocument();
      });
    });

    it("edits a persona in place", async () => {
      const updated: PersonaDto = { ...REX, style: "Now smoother." };
      const mockFetch = makeDispatchFetchMock({ "PATCH /api/personas/1": { status: 200, body: updated } });
      renderClient({ initialPersonas: [REX, NOVA], initialActiveId: 0 });

      fireEvent.click(screen.getByRole("button", { name: "Edit Radio Rex" }));

      const styleField = screen.getByLabelText("Style") as HTMLTextAreaElement;
      expect(styleField.value).toBe(REX.style);
      fireEvent.change(styleField, { target: { value: "Now smoother." } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save changes" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Now smoother.")).toBeInTheDocument();
      });

      expect(findCall(mockFetch, "PATCH", "/api/personas/1")).toBeDefined();
    });

    it("deletes a persona after a confirm", async () => {
      const mockFetch = makeDispatchFetchMock({ "DELETE /api/personas/2": { status: 204 } });
      renderClient({ initialPersonas: [REX, NOVA], initialActiveId: 1 });

      fireEvent.click(screen.getByRole("button", { name: "Delete Nova" }));
      await confirmDialog("Delete");

      await waitFor(() => {
        expect(screen.queryByTestId("persona-name-Nova")).not.toBeInTheDocument();
      });

      expect(findCall(mockFetch, "DELETE", "/api/personas/2")).toBeDefined();
    });

    it("confirms before deleting the ACTIVE persona (it silences the persona, not the blurbs)", async () => {
      makeDispatchFetchMock({});
      renderClient({ initialPersonas: [REX, NOVA], initialActiveId: REX.id });

      fireEvent.click(screen.getByRole("button", { name: "Delete Radio Rex" }));

      const dialog = await screen.findByRole("dialog");
      expect(within(dialog).getByText(/deactivates the DJ/i)).toBeInTheDocument();
      expect(within(dialog).getByText(/neutral house style/i)).toBeInTheDocument();
    });

    it("toasts failures naming the outcome (F31.3)", async () => {
      makeDispatchFetchMock({
        "POST /api/personas": {
          status: 409,
          body: { detail: 'A persona named "Radio Rex" already exists.' },
        },
      });
      renderClient({ initialPersonas: [] });

      fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Radio Rex" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Create persona" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText('A persona named "Radio Rex" already exists.')).toBeInTheDocument();
      });
    });
  });

  describe("Scenario: voice control reuses the voices dropdown", () => {
    it("renders a dropdown with 'Station default' first when /api/voices succeeds", async () => {
      makeDispatchFetchMock({ "GET /api/voices": { status: 200, body: VOICE_IDS } });
      renderClient({ initialPersonas: [] });

      await waitFor(() => {
        const select = screen.getByLabelText("Voice") as HTMLSelectElement;
        expect(select.tagName).toBe("SELECT");
        expect(select.options[0]?.textContent).toBe("Station default");
        expect(select.options[0]?.value).toBe("");
      });
    });

    it("submits no voice field when 'Station default' is chosen", async () => {
      const created: PersonaDto = { ...NOVA, id: 5 };
      const mockFetch = makeDispatchFetchMock({
        "GET /api/voices": { status: 200, body: VOICE_IDS },
        "POST /api/personas": { status: 201, body: created },
      });
      renderClient({ initialPersonas: [] });

      await waitFor(() => {
        expect(screen.getByLabelText("Voice").tagName).toBe("SELECT");
      });

      fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Nova" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Create persona" }));
        await Promise.resolve();
      });

      const call = findCall(mockFetch, "POST", "/api/personas");
      expect(call).toBeDefined();
      const [, init] = call as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body["voice"]).toBeUndefined();
    });

    it("falls back to free-text with a visible notice when the listing fails (F29.5)", async () => {
      makeDispatchFetchMock({
        "GET /api/voices": { status: 502, body: { detail: "Kokoro unreachable" } },
      });
      renderClient({ initialPersonas: [] });

      await waitFor(() => {
        const voiceField = screen.getByLabelText("Voice") as HTMLInputElement;
        expect(voiceField.tagName).toBe("INPUT");
        expect(voiceField.type).toBe("text");
      });
      expect(screen.getByText(/voice list unavailable/i)).toBeInTheDocument();
    });
  });

  describe("Scenario: activate control", () => {
    it("writes Station:Persona:ActiveId via the settings surface on activate (F35.2)", async () => {
      const mockFetch = makeDispatchFetchMock({ "PUT /api/settings": { status: 200, body: [] } });
      renderClient({ initialPersonas: [REX, NOVA], initialActiveId: 0 });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Activate Nova" }));
        await Promise.resolve();
      });

      const call = await waitFor(() => {
        const found = findCall(mockFetch, "PUT", "/api/settings");
        expect(found).toBeDefined();
        return found as [string, RequestInit];
      });

      const [, init] = call;
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Station:Persona:ActiveId", value: "2" }]);
    });

    it("moves the active badge to the newly activated persona", async () => {
      makeDispatchFetchMock({ "PUT /api/settings": { status: 200, body: [] } });
      renderClient({ initialPersonas: [REX, NOVA], initialActiveId: REX.id });

      expect(within(rowFor("Radio Rex")).getByText("Active")).toBeInTheDocument();
      expect(within(rowFor("Nova")).queryByText("Active")).not.toBeInTheDocument();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Activate Nova" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(within(rowFor("Nova")).getByText("Active")).toBeInTheDocument();
      });
      expect(within(rowFor("Radio Rex")).queryByText("Active")).not.toBeInTheDocument();
    });

    it("supports deactivating to none (ActiveId 0)", async () => {
      const mockFetch = makeDispatchFetchMock({ "PUT /api/settings": { status: 200, body: [] } });
      renderClient({ initialPersonas: [REX, NOVA], initialActiveId: REX.id });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Deactivate Radio Rex" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.queryByText("Active")).not.toBeInTheDocument();
      });

      const call = findCall(mockFetch, "PUT", "/api/settings") as [string, RequestInit];
      const body = JSON.parse(call[1].body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Station:Persona:ActiveId", value: "0" }]);
    });
  });

  describe("Scenario: preview", () => {
    it("shows the generated copy text for a saved persona (F35.6)", async () => {
      makeDispatchFetchMock({
        "POST /api/personas/preview": { status: 200, body: { text: "Coming up next on GenWave…" } },
      });
      renderClient({ initialPersonas: [REX, NOVA], initialActiveId: 0 });

      const row = rowFor("Radio Rex");
      await act(async () => {
        fireEvent.click(within(row).getByRole("button", { name: "Preview" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(within(row).getByText("Coming up next on GenWave…")).toBeInTheDocument();
      });
    });

    it("plays the preview wav in-page", async () => {
      makeDispatchFetchMock({
        "POST /api/personas/preview": { status: 200, body: { text: "Coming up next…" } },
        "POST /api/tts/preview": { status: 200, blob: new Blob(["wav-bytes"], { type: "audio/wav" }) },
      });
      renderClient({ initialPersonas: [REX, NOVA], initialActiveId: 0 });

      const row = rowFor("Radio Rex");
      await act(async () => {
        fireEvent.click(within(row).getByRole("button", { name: "Preview" }));
        await Promise.resolve();
      });
      await waitFor(() => {
        expect(within(row).getByText("Coming up next…")).toBeInTheDocument();
      });

      await act(async () => {
        fireEvent.click(within(row).getByRole("button", { name: "Play" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        const audio = within(row).getByLabelText("Persona preview audio") as HTMLAudioElement;
        expect(audio.tagName).toBe("AUDIO");
        expect(audio).toHaveAttribute("src", "blob:mock-url");
      });
    });

    it("previews draft (unsaved) persona fields", async () => {
      const mockFetch = makeDispatchFetchMock({
        "POST /api/personas/preview": { status: 200, body: { text: "Draft copy preview." } },
      });
      renderClient({ initialPersonas: [] });

      const form = screen.getByRole("region", { name: "Create persona" });
      fireEvent.change(within(form).getByLabelText("Name"), { target: { value: "Test DJ" } });
      fireEvent.change(within(form).getByLabelText("Backstory"), { target: { value: "Unsaved so far." } });
      fireEvent.change(within(form).getByLabelText("Style"), { target: { value: "Casual." } });

      await act(async () => {
        fireEvent.click(within(form).getByRole("button", { name: "Preview" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(within(form).getByText("Draft copy preview.")).toBeInTheDocument();
      });

      const call = findCall(mockFetch, "POST", "/api/personas/preview");
      expect(call).toBeDefined();
      const [, init] = call as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body["personaId"]).toBeUndefined();
      expect(body["name"]).toBe("Test DJ");
      expect(body["backstory"]).toBe("Unsaved so far.");
      expect(body["style"]).toBe("Casual.");
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: preview failure is honest (sad path)", () => {
    it("toasts the 502 when the LLM is unreachable — never shows template text as the persona's (F35.6)", async () => {
      makeDispatchFetchMock({
        "POST /api/personas/preview": {
          status: 502,
          body: { title: "Persona preview failed.", detail: "The LLM endpoint is unreachable." },
        },
      });
      renderClient({ initialPersonas: [REX, NOVA], initialActiveId: 0 });

      const row = rowFor("Radio Rex");
      await act(async () => {
        fireEvent.click(within(row).getByRole("button", { name: "Preview" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Persona preview failed.")).toBeInTheDocument();
      });

      // No copy panel rendered at all — a 502 must never render template text as if it were
      // this persona's own words (the "Play" action only appears once real copy has loaded).
      expect(within(row).queryByRole("button", { name: "Play" })).not.toBeInTheDocument();
    });
  });
});
