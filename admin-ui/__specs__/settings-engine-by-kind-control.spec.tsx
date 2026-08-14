// @jest-environment jsdom
// gh-#146 — Tts:EngineByKind gets a structured editor: dropdowns for kind and engine.
//
// Both dimensions are closed sets the backend validates (SettingValidator.IsValidEngineByKindMap:
// SegmentKind enum names × "kokoro"/"piper"), yet the field was freeform text — a typo shipped
// silently as an ignored override. The Corrections-style rule editor makes an invalid pair
// inexpressible, stages every change until the page-wide Save settings, and rides the same
// isDirty dirty-pill pattern (gh-#139/gh-#140).
//
// The EXPECTED_* fixtures below are deliberately hand-typed copies of the backend's value sets —
// NOT imports from the control — so a one-sided edit (control or backend mirror) fails here, the
// same independent-authoring ethos as settings-help-keys.ts' parity guard.
//
// Runner: Jest (jsdom) + @testing-library/react — renderWithProviders/makeSequencedFetchMock
// style per settings-corrections-control.spec.tsx.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";

// ---------------------------------------------------------------------------
// Backend value-set fixtures (independently authored — see header)
// ---------------------------------------------------------------------------

/** Mirrors GenWave.Core.Domain.SegmentKind (src/GenWave.Abstractions/Domain/SegmentKind.cs). */
const EXPECTED_SEGMENT_KINDS = [
  "StationId",
  "LeadIn",
  "BackAnnounce",
  "TimeDate",
  "SignOff",
  "SignOn",
];

/**
 * Mirrors SettingValidator.IsValidEngineByKindMap's accepted engines
 * (src/GenWave.Host/Configuration/SettingValidator.cs) / GenWave.Tts.DependencyNames.
 */
const EXPECTED_ENGINES = ["kokoro", "piper"];

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const SAVED_MAP = JSON.stringify({ StationId: "piper", LeadIn: "kokoro" });

function makeEngineByKindSetting(overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key: "Tts:EngineByKind",
    value: SAVED_MAP,
    source: "override",
    applyMode: "live",
    kind: "string",
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

/** Collects every PUT /api/settings call in the mock's call list, in order. */
function putCalls(mockFetch: jest.MockedFunction<typeof fetch>): Array<[string, RequestInit]> {
  return mockFetch.mock.calls.filter(
    (call) => (call[1] as RequestInit | undefined)?.method === "PUT"
  ) as Array<[string, RequestInit]>;
}

function putBody(call: [string, RequestInit]): Array<{ key: string; value: string }> {
  return JSON.parse(call[1].body as string) as Array<{ key: string; value: string }>;
}

async function clickSave(): Promise<void> {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
    await Promise.resolve();
  });
}

function kindSelect(rowNumber: number): HTMLSelectElement {
  return screen.getByLabelText(`Speech kind for override ${rowNumber}`) as HTMLSelectElement;
}

function engineSelect(rowNumber: number): HTMLSelectElement {
  return screen.getByLabelText(`Engine for override ${rowNumber}`) as HTMLSelectElement;
}

function optionValues(select: HTMLSelectElement): string[] {
  return Array.from(select.options).map((option) => option.value);
}

function dirtyNotice(): HTMLElement | null {
  return screen.queryByTestId("engine-by-kind-dirty-notice");
}

// ---------------------------------------------------------------------------
// Feature: Tts:EngineByKind structured editor round-trips the stored value
// ---------------------------------------------------------------------------

describe("Feature: Tts:EngineByKind structured editor round-trips the stored value", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the saved map renders as rows", () => {
    it("one row per entry, in stored order, with the saved kind and engine selected", () => {
      makeSequencedFetchMock([{ status: 200 }]);
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);

      expect(kindSelect(1).value).toBe("StationId");
      expect(engineSelect(1).value).toBe("piper");
      expect(kindSelect(2).value).toBe("LeadIn");
      expect(engineSelect(2).value).toBe("kokoro");
    });

    it("kind and engine casing is canonicalized on render, matching the backend's case-insensitive parse", () => {
      makeSequencedFetchMock([{ status: 200 }]);
      renderWithProviders(
        <SettingsForm
          settings={[
            makeEngineByKindSetting({ value: JSON.stringify({ stationid: "PIPER" }) }),
          ]}
        />
      );

      expect(kindSelect(1).value).toBe("StationId");
      expect(engineSelect(1).value).toBe("piper");
    });

    it("an untouched control stages nothing — Save reports no changes and sends no PUT", async () => {
      const mockFetch = makeSequencedFetchMock([{ status: 200 }]);
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);

      await clickSave();

      expect(putCalls(mockFetch)).toHaveLength(0);
      expect(screen.getByText("No changes to save.")).toBeInTheDocument();
    });
  });

  describe("Scenario: editing a row's engine then saving", () => {
    it("the PUT payload carries the exact backend wire shape — a compact JSON object of canonical names", async () => {
      const mockFetch = makeSequencedFetchMock([{ status: 200 }]);
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);

      fireEvent.change(engineSelect(1), { target: { value: "kokoro" } });
      await clickSave();

      const puts = putCalls(mockFetch);
      expect(puts).toHaveLength(1);
      expect(putBody(puts[0]!)).toEqual([
        {
          key: "Tts:EngineByKind",
          value: JSON.stringify({ StationId: "kokoro", LeadIn: "kokoro" }),
        },
      ]);
    });
  });

  describe("Scenario: adding an override", () => {
    it("appends the (kind, engine) pair to the serialized map on Save", async () => {
      const mockFetch = makeSequencedFetchMock([{ status: 200 }]);
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);

      fireEvent.change(screen.getByLabelText("Speech kind", { selector: "select" }), {
        target: { value: "TimeDate" },
      });
      fireEvent.change(screen.getByLabelText("Engine", { selector: "select" }), {
        target: { value: "piper" },
      });
      fireEvent.click(screen.getByRole("button", { name: "Add override" }));
      await clickSave();

      const puts = putCalls(mockFetch);
      expect(puts).toHaveLength(1);
      expect(putBody(puts[0]!)).toEqual([
        {
          key: "Tts:EngineByKind",
          value: JSON.stringify({ StationId: "piper", LeadIn: "kokoro", TimeDate: "piper" }),
        },
      ]);
    });
  });

  describe("Scenario: deleting every override", () => {
    it("serializes back to the empty seeded default \"\" — the backend's no-overrides state", async () => {
      const mockFetch = makeSequencedFetchMock([{ status: 200 }]);
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);

      fireEvent.click(screen.getByRole("button", { name: "Delete override 2" }));
      fireEvent.click(screen.getByRole("button", { name: "Delete override 1" }));
      await clickSave();

      const puts = putCalls(mockFetch);
      expect(puts).toHaveLength(1);
      expect(putBody(puts[0]!)).toEqual([{ key: "Tts:EngineByKind", value: "" }]);
      expect(
        screen.getByText(/No overrides — every kind uses the normal Kokoro-first/i)
      ).toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: Tts:EngineByKind staging rides the dirty-pill pattern (gh-#139)
// ---------------------------------------------------------------------------

describe("Feature: Tts:EngineByKind staging rides the dirty-pill pattern", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    makeSequencedFetchMock([{ status: 200 }]);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the unsaved-changes badge tracks the staged map", () => {
    it("is absent while the staged map matches the saved map", () => {
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);
      expect(dirtyNotice()).not.toBeInTheDocument();
    });

    it("appears when an override is added", () => {
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);
      fireEvent.change(screen.getByLabelText("Speech kind", { selector: "select" }), {
        target: { value: "SignOff" },
      });
      fireEvent.click(screen.getByRole("button", { name: "Add override" }));

      expect(kindSelect(3).value).toBe("SignOff");
      expect(dirtyNotice()).toHaveTextContent(/unsaved changes/i);
    });

    it("appears when a row's engine is re-pinned", () => {
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);
      fireEvent.change(engineSelect(1), { target: { value: "kokoro" } });
      expect(dirtyNotice()).toHaveTextContent(/unsaved changes/i);
    });

    it("appears when an override is deleted", () => {
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);
      fireEvent.click(screen.getByRole("button", { name: "Delete override 2" }));
      expect(dirtyNotice()).toHaveTextContent(/unsaved changes/i);
    });

    it("clears after a successful Save settings", async () => {
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);
      fireEvent.change(engineSelect(1), { target: { value: "kokoro" } });
      await clickSave();
      expect(dirtyNotice()).not.toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: the dropdowns offer exactly the backend's value sets
// ---------------------------------------------------------------------------

describe("Feature: the dropdowns offer exactly the backend's value sets", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    makeSequencedFetchMock([{ status: 200 }]);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: kind options mirror SegmentKind", () => {
    it("the add-row kind dropdown offers every SegmentKind name (plus the placeholder) when nothing is pinned", () => {
      renderWithProviders(
        <SettingsForm settings={[makeEngineByKindSetting({ value: "" })]} />
      );

      const select = screen.getByLabelText("Speech kind", {
        selector: "select",
      }) as HTMLSelectElement;
      expect(optionValues(select)).toEqual(["", ...EXPECTED_SEGMENT_KINDS]);
    });

    it("a row's kind dropdown never offers a kind another row already pins — duplicates are inexpressible", () => {
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);

      // Row 2 (LeadIn): its own kind plus the unpinned kinds — never row 1's StationId.
      expect(optionValues(kindSelect(2))).toEqual(
        EXPECTED_SEGMENT_KINDS.filter((kind) => kind !== "StationId")
      );
      // Same rule in the add row: both pinned kinds are absent.
      const addKind = screen.getByLabelText("Speech kind", {
        selector: "select",
      }) as HTMLSelectElement;
      expect(optionValues(addKind)).toEqual([
        "",
        ...EXPECTED_SEGMENT_KINDS.filter((kind) => kind !== "StationId" && kind !== "LeadIn"),
      ]);
    });
  });

  describe("Scenario: engine options mirror the validator's accepted engines", () => {
    it("every engine dropdown offers exactly kokoro and piper, lowercase wire values", () => {
      renderWithProviders(<SettingsForm settings={[makeEngineByKindSetting()]} />);

      expect(optionValues(engineSelect(1))).toEqual(EXPECTED_ENGINES);
      const addEngine = screen.getByLabelText("Engine", {
        selector: "select",
      }) as HTMLSelectElement;
      expect(optionValues(addEngine)).toEqual(EXPECTED_ENGINES);
    });
  });
});
