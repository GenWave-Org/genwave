// @jest-environment jsdom
// STORY-246 — The roster replaces the switch (SPEC F94.1, PLAN T127)
//
// Section derivation and badge rendering are component-testable via the personas-page harness
// idiom (a fetch mock dispatched BY URL+METHOD, ConfirmDialogProvider/Toaster wrappers) — the
// "no activation control anywhere" sweep across the REAL browser and the sections' live-update
// behavior are T127's own browser acceptance (T92 precedent), not this file's job.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PersonasClient } from "../app/(authed)/personas/PersonasClient";
import type { PersonasClientProps } from "../app/(authed)/personas/PersonasClient";
import type { PersonaDto } from "../app/(authed)/personas/types";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";

// ---------------------------------------------------------------------------
// Fixtures — three personas so grouping and the on-air badge are both exercised
// without conflating "scheduled" with "on the air" (a scheduled persona need not
// be the one airing right now).
// ---------------------------------------------------------------------------

const REX: PersonaDto = {
  id: 1,
  name: "Radio Rex",
  backstory: "A grizzled late-night jock who has seen every format come and go.",
  style: "Warm, gravelly, brief.",
  voice: "af_alloy",
  slug: "radio-rex",
  importedFrom: null,
  importedAt: null,
  soul: "",
  quirks: [],
  lore: [],
};

const NOVA: PersonaDto = {
  id: 2,
  name: "Nova",
  backstory: "An upbeat morning host.",
  style: "Bright and quick.",
  voice: "",
  slug: "nova",
  importedFrom: null,
  importedAt: null,
  soul: "",
  quirks: [],
  lore: [],
};

const PROFESSOR: PersonaDto = {
  id: 3,
  name: "The Professor",
  backstory: "Dry wit, deep crates.",
  style: "Deadpan.",
  voice: "",
  slug: "the-professor",
  importedFrom: null,
  importedAt: null,
  soul: "",
  quirks: [],
  lore: [],
};

// ---------------------------------------------------------------------------
// Fetch mock — dispatched by "METHOD url" (mirrors personas-page.spec.tsx's own harness).
// PersonasClient's VoiceControl mounts GET /api/voices unconditionally, so every render needs
// at least the default route below even when a test never touches the voice field.
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

function routeKey(method: string, url: string): string {
  return `${method.toUpperCase()} ${url}`;
}

const DEFAULT_ROUTES: Record<string, RouteResponseSpec> = {
  "GET /api/voices": { status: 200, body: [] },
};

function makeDispatchFetchMock(routes: Record<string, RouteResponseSpec> = {}): jest.MockedFunction<typeof fetch> {
  const allRoutes = { ...DEFAULT_ROUTES, ...routes };
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = init?.method ?? "GET";
    const url = String(input);
    const spec = allRoutes[routeKey(method, url)] ?? { status: 200, body: {} };
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

function renderRoster(overrides: Partial<PersonasClientProps> = {}): ReturnType<typeof render> {
  const props: PersonasClientProps = {
    initialPersonas: [REX, NOVA, PROFESSOR],
    scheduledPersonaIds: [],
    onAirPersonaName: null,
    ...overrides,
  };
  return render(
    <ConfirmDialogProvider>
      <PersonasClient {...props} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/** Finds a persona's own `<tr>` via its dedicated name span (mirrors personas-page.spec.tsx's own
 * `rowFor` — the badge sits in the same cell as the name, so a raw `getByText` would miss). */
function rowFor(name: string): HTMLElement {
  const nameNode = screen.getByTestId(`persona-name-${name}`);
  const row = nameNode.closest("tr");
  if (row === null) throw new Error(`No <tr> ancestor for "${name}"`);
  return row;
}

/** Finds one roster section (Scheduled or Bench) by its own heading — the heading and its rows
 * share the same wrapping `<div>` (PersonasClient's own render, no `data-testid` needed for a
 * structure this shallow). */
function sectionFor(heading: "Scheduled" | "Bench"): HTMLElement {
  const headingNode = screen.getByRole("heading", { name: heading });
  const section = headingNode.closest("div");
  if (section === null) throw new Error(`No wrapping <div> for the "${heading}" heading`);
  return section;
}

function makeSetting(overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key: "Station:Name",
    value: "GenWave",
    source: "default",
    applyMode: "live",
    kind: "string",
    unit: "",
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// Feature: The roster replaces the switch
// ---------------------------------------------------------------------------

describe("Feature: The roster replaces the switch", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: scheduled vs bench, derived from schedule data", () => {
    it("groups personas with schedule rows under Scheduled", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [REX.id, NOVA.id] });

      const scheduled = sectionFor("Scheduled");
      expect(within(scheduled).getByTestId("persona-name-Radio Rex")).toBeInTheDocument();
      expect(within(scheduled).getByTestId("persona-name-Nova")).toBeInTheDocument();
    });

    it("groups personas without schedule rows under Bench", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [REX.id, NOVA.id] });

      const bench = sectionFor("Bench");
      expect(within(bench).getByTestId("persona-name-The Professor")).toBeInTheDocument();
      // Not duplicated into the other section.
      expect(within(bench).queryByTestId("persona-name-Radio Rex")).not.toBeInTheDocument();
      const scheduled = sectionFor("Scheduled");
      expect(within(scheduled).queryByTestId("persona-name-The Professor")).not.toBeInTheDocument();
    });

    it("shows the On The Air badge on the current DJ only", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [REX.id, NOVA.id], onAirPersonaName: "Radio Rex" });

      expect(within(rowFor("Radio Rex")).getByText("On the air")).toBeInTheDocument();
      expect(within(rowFor("Nova")).queryByText("On the air")).not.toBeInTheDocument();
      expect(within(rowFor("The Professor")).queryByText("On the air")).not.toBeInTheDocument();
    });

    it("puts every persona under Bench when the week has no segments at all (SPEC F91.4)", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [] });

      // Scheduled is empty — its own empty-section copy renders...
      expect(screen.getByText("No personas are scheduled yet.")).toBeInTheDocument();
      // ...and Bench is NOT empty (every persona fell through to it), so ITS empty-section copy
      // must not render alongside it — both of the two distinct strings are exercised here.
      expect(screen.queryByText("Every persona is scheduled.")).not.toBeInTheDocument();

      const bench = sectionFor("Bench");
      expect(within(bench).getByTestId("persona-name-Radio Rex")).toBeInTheDocument();
      expect(within(bench).getByTestId("persona-name-Nova")).toBeInTheDocument();
      expect(within(bench).getByTestId("persona-name-The Professor")).toBeInTheDocument();
    });
  });

  describe("Scenario: the switch is gone", () => {
    it("renders no Activate/Deactivate control on the Roster page", () => {
      makeDispatchFetchMock();
      renderRoster({ scheduledPersonaIds: [REX.id], onAirPersonaName: "Radio Rex" });

      // /activate/i also catches "Deactivate" — one sweep covers both retired labels.
      expect(screen.queryByRole("button", { name: /activate/i })).not.toBeInTheDocument();
    });

    it("renders no persona activation control on the Settings page", () => {
      render(
        <ConfirmDialogProvider>
          <SettingsForm
            settings={[makeSetting({ key: "Station:Persona:ActiveId", kind: "number", value: "0" })]}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );

      // Feeding the retired key straight back through SettingsForm gives this assertion teeth: if
      // SETTING_CONTROL_REGISTRY still mapped it to PersonaSettingControl, this would render a
      // persona-name <select> instead. It doesn't (PLAN T120/T127, SPEC F91.5) — the shipped
      // kind-based number input renders instead, never PersonaSettingControl's dropdown or its
      // distinctive "None — persona-less patter" copy.
      const field = screen.getByLabelText(/Station:Persona:ActiveId/) as HTMLInputElement;
      expect(field.tagName).toBe("INPUT");
      expect(field.type).toBe("number");
      expect(screen.queryByText(/persona-less patter/i)).not.toBeInTheDocument();
    });
    // Full-app sweep for activation controls = T127 browser acceptance.
  });
});
