// @jest-environment jsdom
// gh-#139 / gh-#140 — Tts:Corrections editor: edits persist, staging is unmistakable.
//
// gh-#140's data loss (reproduced live against a real browser + stub backend): SettingsForm froze
// its save-diff baseline at mount, so a SECOND save in the same pageview whose staged value landed
// back on a page-load value produced NO PUT — "No changes to save." while the server held the
// earlier save. The "save twice in one pageview" scenario below pins the fix (re-baseline after
// every successful PUT); the staging scenarios pin gh-#139's unmistakable-dirty UX.
//
// Runner: Jest (jsdom) + @testing-library/react. Drives SettingsForm via a mocked fetch —
// mirrors settings-semantic-controls.spec.tsx in style (renderWithProviders,
// makeSequencedFetchMock).

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const SAVED_RULES = JSON.stringify([
  { from: "MacLeod", to: "Muh-cloud" },
  { from: "GenWave", to: "Jen Wave" },
]);

function makeCorrectionsSetting(overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key: "Tts:Corrections",
    value: SAVED_RULES,
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
  networkError?: boolean;
}

/** A fetch mock that replays one response per call, in order (last spec repeats if exhausted). */
function makeSequencedFetchMock(specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  let callIndex = 0;
  const fn = jest.fn<typeof fetch>().mockImplementation(async () => {
    const spec = specs[callIndex] ?? specs[specs.length - 1]!;
    callIndex += 1;
    if (spec.networkError === true) {
      throw new Error("network error");
    }
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

/** Retypes an input's full value and clicks Save settings, flushing the async submit. */
async function editAndSave(field: HTMLInputElement, newValue: string): Promise<void> {
  fireEvent.change(field, { target: { value: newValue } });
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: Tts:Corrections editor persistence (gh-#140)
// ---------------------------------------------------------------------------

describe("Feature: Tts:Corrections editor persistence", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: editing an existing rule then saving", () => {
    it("the PUT payload contains the edited rule (gh-#140)", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 200, body: [] }, // GET /api/tts/corrections-stats
        { status: 200 }, // PUT /api/settings
      ]);
      renderWithProviders(<SettingsForm settings={[makeCorrectionsSetting()]} />);

      const toField = screen.getByLabelText("To text for rule 1") as HTMLInputElement;
      expect(toField.value).toBe("Muh-cloud");
      await editAndSave(toField, "Mick-loud");

      const puts = putCalls(mockFetch);
      expect(puts).toHaveLength(1);
      expect(putBody(puts[0]!)).toEqual([
        {
          key: "Tts:Corrections",
          value: JSON.stringify([
            { from: "MacLeod", to: "Mick-loud" },
            { from: "GenWave", to: "Jen Wave" },
          ]),
        },
      ]);
    });
  });

  describe("Scenario: saving twice in one pageview (the gh-#140 data loss)", () => {
    it("a second save that reverts a rule to its page-load value still PUTs — the diff baseline follows the last save, not the mount", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 200, body: [] }, // GET /api/tts/corrections-stats
        { status: 200 }, // PUT #1
        { status: 200 }, // PUT #2
      ]);
      renderWithProviders(<SettingsForm settings={[makeCorrectionsSetting()]} />);
      const toField = screen.getByLabelText("To text for rule 1") as HTMLInputElement;

      // Save #1: tweak the pronunciation. Server now holds "Mick-loud".
      await editAndSave(toField, "Mick-loud");
      // Save #2: the operator hears it, edits BACK to the page-load spelling. Pre-fix, this
      // produced NO second PUT ("No changes to save.") — UI showing Muh-cloud, server holding
      // Mick-loud: silent data loss.
      await editAndSave(toField, "Muh-cloud");

      const puts = putCalls(mockFetch);
      expect(puts).toHaveLength(2);
      expect(putBody(puts[1]!)).toEqual([
        { key: "Tts:Corrections", value: SAVED_RULES },
      ]);
      expect(screen.queryByText("No changes to save.")).not.toBeInTheDocument();
    });

    it("a save with nothing further staged reports no changes rather than re-sending the batch", async () => {
      makeSequencedFetchMock([
        { status: 200, body: [] }, // GET /api/tts/corrections-stats
        { status: 200 }, // PUT #1
      ]);
      renderWithProviders(<SettingsForm settings={[makeCorrectionsSetting()]} />);
      const toField = screen.getByLabelText("To text for rule 1") as HTMLInputElement;

      await editAndSave(toField, "Mick-loud");
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      expect(screen.getByText("No changes to save.")).toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: Tts:Corrections staging is unmistakable (gh-#139)
// ---------------------------------------------------------------------------

describe("Feature: Tts:Corrections staging is unmistakable", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    makeSequencedFetchMock([
      { status: 200, body: [] }, // GET /api/tts/corrections-stats
      { status: 200 }, // any PUT
    ]);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  function dirtyNotice(): HTMLElement | null {
    return screen.queryByTestId("corrections-dirty-notice");
  }

  describe("Scenario: the unsaved-changes badge tracks the staged rules", () => {
    it("is absent while staged rules match the saved rules", () => {
      renderWithProviders(<SettingsForm settings={[makeCorrectionsSetting()]} />);
      expect(dirtyNotice()).not.toBeInTheDocument();
    });

    it("appears when a rule is added via Add rule", () => {
      renderWithProviders(<SettingsForm settings={[makeCorrectionsSetting()]} />);
      fireEvent.change(screen.getByLabelText("From", { selector: "input" }), {
        target: { value: "Liquidsoap" },
      });
      fireEvent.click(screen.getByRole("button", { name: "Add rule" }));

      expect(screen.getByLabelText("From text for rule 3")).toBeInTheDocument();
      expect(dirtyNotice()).toHaveTextContent(/unsaved changes/i);
    });

    it("appears when an existing rule is edited", () => {
      renderWithProviders(<SettingsForm settings={[makeCorrectionsSetting()]} />);
      fireEvent.change(screen.getByLabelText("To text for rule 1"), {
        target: { value: "Mick-loud" },
      });
      expect(dirtyNotice()).toHaveTextContent(/unsaved changes/i);
    });

    it("appears when a rule is deleted", () => {
      renderWithProviders(<SettingsForm settings={[makeCorrectionsSetting()]} />);
      fireEvent.click(screen.getByRole("button", { name: "Delete rule 2" }));
      expect(dirtyNotice()).toHaveTextContent(/unsaved changes/i);
    });

    it("clears after a successful Save settings", async () => {
      renderWithProviders(<SettingsForm settings={[makeCorrectionsSetting()]} />);
      const toField = screen.getByLabelText("To text for rule 1") as HTMLInputElement;
      await editAndSave(toField, "Mick-loud");
      expect(dirtyNotice()).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the Preview note names which rules preview uses", () => {
    it("reads as a quiet saved-rules note while clean, and never shows the old save-first apology", () => {
      renderWithProviders(<SettingsForm settings={[makeCorrectionsSetting()]} />);
      expect(screen.getByTestId("corrections-preview-note")).toHaveTextContent(
        "Previews with your saved rules."
      );
      expect(
        screen.queryByText(/save changes above first to preview them/i)
      ).not.toBeInTheDocument();
    });

    it("turns into a prominent unsaved-rules warning while dirty", () => {
      renderWithProviders(<SettingsForm settings={[makeCorrectionsSetting()]} />);
      fireEvent.change(screen.getByLabelText("To text for rule 1"), {
        target: { value: "Mick-loud" },
      });
      const note = screen.getByTestId("corrections-preview-note");
      expect(note).toHaveTextContent(/last-saved rules/i);
      expect(note).toHaveTextContent(/not included until you Save settings/i);
    });
  });
});
