// @jest-environment jsdom
// STORY-059 — Admin UI SafeScope on the settings page
//
// Runner: Jest + jsdom + @testing-library/react. Extends the shipped W6 SettingsForm
// with a "Safe rotation library scope" library-picker multi-select whose options come
// from GET /api/libraries. Only CHANGED settings are submitted (W6 pattern). Clearing
// SafeScope to [] prompts a "This will silence the stream on drain" confirmation
// (SPEC F21.5). See docs/PLAN.md Epic K.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";
import type { LibraryDto } from "../lib/library";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SAFE_SCOPE_KEY = "Station:SafeScope:LibraryIds";
const SAFE_SCOPE_EMPTY_CONSEQUENCE =
  "Saving an empty SafeScope silences the stream on drain — mksafe emits silence until re-pointed.";

function makeSafeScopeSetting(override: Partial<SettingDto> = {}): SettingDto {
  return {
    key: SAFE_SCOPE_KEY,
    value: "[1]",
    source: "override",
    applyMode: "live",
    kind: "number-list",
    unit: "",
    ...override,
  };
}

function makeLibraries(): LibraryDto[] {
  return [
    { id: 1, name: "Lib Alpha", mediaCount: 10 },
    { id: 2, name: "Lib Beta", mediaCount: 5 },
  ];
}

/** A second setting alongside SafeScope — used to verify submit-only-changed. */
function makeNumericSetting(): SettingDto {
  return {
    key: "Loudness:TargetLufs",
    value: "-16",
    source: "default",
    applyMode: "live",
    kind: "number",
    unit: "LUFS",
  };
}

function makeFetchMock(
  status: number,
  body: unknown = {}
): jest.MockedFunction<typeof fetch> {
  const fn = jest
    .fn<typeof fetch>()
    .mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
      headers: new Headers({ "content-type": "application/json" }),
    } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/**
 * Change the selection state of a <select multiple> element in jsdom.
 * Sets the given option values as selected and fires a change event so
 * React's synthetic onChange sees the updated selectedOptions.
 */
function selectOptions(select: HTMLSelectElement, selectedValues: string[]): void {
  const selectedSet = new Set(selectedValues);
  Array.from(select.options).forEach((opt) => {
    opt.selected = selectedSet.has(opt.value);
  });
  fireEvent.change(select);
}

/**
 * SettingsForm calls useConfirm() unconditionally (SafeScope-empty save opens
 * the shared modal — SPEC F28.9, window.confirm is gone), so every render needs
 * a ConfirmDialogProvider ancestor. Toaster is mounted alongside it so tests can
 * assert on the toast copy that replaced the shipped inline "Settings saved."
 * status banner.
 */
function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/**
 * The SafeScope field mounts SafeScopeAvailabilityBadge (STORY-105, SPEC F31.4–F31.5),
 * which mount-fetches GET /api/status. Tests that don't otherwise await a fetch-driven
 * change still need to let that pending promise settle inside act() once, or React logs
 * an "update not wrapped in act(...)" warning for the eventual setState.
 */
async function flushMountFetch(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: Safe-rotation library-scope picker on the settings page
// ---------------------------------------------------------------------------

describe("Feature: Safe-rotation library-scope picker on the settings page", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: The picker renders alongside the shipped live settings", () => {
    it(
      "AC1 — a labeled 'Safe rotation library scope' field is present, populated from GET /api/settings, and rendered with applyMode='live' badge",
      async () => {
        renderWithProviders(
          <SettingsForm
            settings={[makeSafeScopeSetting()]}
            libraries={makeLibraries()}
          />
        );

        // The label references the configuration key; the select is associated via htmlFor
        expect(screen.getByLabelText(new RegExp(SAFE_SCOPE_KEY))).toBeInTheDocument();

        // The applyMode="live" badge must be present (same rendering as W6 live settings)
        expect(screen.getByText(/live/)).toBeInTheDocument();

        await flushMountFetch();
      }
    );

    it(
      "AC2 — the field is a library-picker multi-select showing human-readable names from GET /api/libraries, not raw ids",
      async () => {
        renderWithProviders(
          <SettingsForm
            settings={[makeSafeScopeSetting({ value: "[1]" })]}
            libraries={makeLibraries()}
          />
        );

        // Options should show library names, not raw numeric IDs
        expect(screen.getByRole("option", { name: "Lib Alpha" })).toBeInTheDocument();
        expect(screen.getByRole("option", { name: "Lib Beta" })).toBeInTheDocument();

        // The initially-selected library (id=1 → "Lib Alpha") should be selected
        const alpha = screen.getByRole("option", { name: "Lib Alpha" }) as HTMLOptionElement;
        expect(alpha.selected).toBe(true);

        // "Lib Beta" (id=2) is NOT in the initial value "[1]" → must not be selected
        const beta = screen.getByRole("option", { name: "Lib Beta" }) as HTMLOptionElement;
        expect(beta.selected).toBe(false);

        // The control is a multi-select (not a plain text or number input)
        const listbox = screen.getByRole("listbox") as HTMLSelectElement;
        expect(listbox.multiple).toBe(true);

        await flushMountFetch();
      }
    );

    it(
      "AC4 — after a successful 200 from PUT, the page toasts 'Settings saved.' (SPEC F28.9)",
      async () => {
        makeFetchMock(200);

        renderWithProviders(
          <SettingsForm
            settings={[makeSafeScopeSetting({ value: "[1]" })]}
            libraries={makeLibraries()}
          />
        );

        // Change selection from lib 1 to lib 2
        const select = screen.getByRole("listbox") as HTMLSelectElement;
        selectOptions(select, ["2"]);

        await act(async () => {
          fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
          await Promise.resolve();
        });

        await waitFor(() => {
          expect(screen.getByText("Settings saved.")).toBeInTheDocument();
        });
      }
    );
  });

  // -------------------------------------------------------------------------
  describe("Scenario: Saving submits only the changed fields", () => {
    it(
      "AC3 — selecting two libraries and clicking Save sends ONE PUT /api/settings with the SafeScope key and no other keys (matches the shipped W6 submit-only-changed pattern)",
      async () => {
        const mockFetch = makeFetchMock(200);

        // Render with a second setting alongside SafeScope — that setting must NOT be in the PUT body
        renderWithProviders(
          <SettingsForm
            settings={[makeSafeScopeSetting({ value: "[1]" }), makeNumericSetting()]}
            libraries={makeLibraries()}
          />
        );

        // Change selection to both libraries (previously only lib 1 was selected)
        const select = screen.getByRole("listbox") as HTMLSelectElement;
        selectOptions(select, ["1", "2"]);

        await act(async () => {
          fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
          await Promise.resolve();
        });

        await waitFor(() => {
          // The SafeScope field mounts SafeScopeAvailabilityBadge (STORY-105, SPEC F31.4–F31.5),
          // which mount-fetches GET /api/status on render, ahead of the scenario-triggered PUT.
          expect(mockFetch).toHaveBeenCalledTimes(2);
        });

        const [url, init] = mockFetch.mock.calls[1] as [string, RequestInit];
        expect(url).toBe("/api/settings");
        expect(init.method).toBe("PUT");

        const headers = init.headers as Record<string, string>;
        expect(headers["Content-Type"]).toBe("application/json");

        const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;

        // Only the changed SafeScope key is in the body — Loudness:TargetLufs was not touched
        expect(body).toHaveLength(1);
        expect(body[0]).toEqual({
          key: SAFE_SCOPE_KEY,
          value: "[1,2]",
        });
        expect(body.some((e) => e.key === "Loudness:TargetLufs")).toBe(false);
      }
    );
  });

  // -------------------------------------------------------------------------
  describe("Scenario: rejecting invalid input / degraded-mode confirmation", () => {
    it(
      "AC5 — a 400 ProblemDetails on the SafeScope key surfaces as an inline field-level error next to the picker (not a toast on a different page)",
      async () => {
        const validationProblem = {
          errors: { settings: ["Library ID 99 does not exist"] },
          title: "One or more settings values are invalid.",
          status: 400,
        };
        makeFetchMock(400, validationProblem);

        renderWithProviders(
          <SettingsForm
            settings={[makeSafeScopeSetting({ value: "[1]" })]}
            libraries={makeLibraries()}
          />
        );

        // Change selection to trigger a PUT
        const select = screen.getByRole("listbox") as HTMLSelectElement;
        selectOptions(select, ["2"]);

        await act(async () => {
          fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
          await Promise.resolve();
        });

        await waitFor(() => {
          // The error message must appear in an alert region (inline, next to the picker)
          expect(screen.getByRole("alert")).toBeInTheDocument();
          expect(screen.getByText("Library ID 99 does not exist")).toBeInTheDocument();
        });

        // "Settings saved." must NOT appear
        expect(screen.queryByRole("status")).toBeNull();

        // After a 400 the form must re-enable so the operator can correct and retry
        // without a full page reload. Both the picker and Save button must NOT be disabled.
        expect(select).not.toBeDisabled();
        expect(screen.getByRole("button", { name: /save settings/i })).not.toBeDisabled();
      }
    );

    it(
      "AC6 — clearing every library from the picker opens a useConfirm() modal with consequence copy BEFORE the PUT submits [] (SPEC F21.5/F28.9 explicit-consent for degraded mode)",
      async () => {
        const mockFetch = makeFetchMock(200);

        renderWithProviders(
          <SettingsForm
            settings={[makeSafeScopeSetting({ value: "[1]" })]}
            libraries={makeLibraries()}
          />
        );

        // Clear all selections — deselect every option
        const select = screen.getByRole("listbox") as HTMLSelectElement;
        selectOptions(select, []);

        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));

        // The confirm modal (window.confirm is gone) must appear before any PUT. The SafeScope
        // field's SafeScopeAvailabilityBadge (STORY-105) already mount-fetched GET /api/status
        // by this point, so one call is expected here — none of them are the PUT.
        const dialog = await screen.findByRole("dialog");
        expect(dialog).toHaveTextContent(SAFE_SCOPE_EMPTY_CONSEQUENCE);
        expect(mockFetch).toHaveBeenCalledTimes(1);

        await act(async () => {
          fireEvent.click(within(dialog).getByRole("button", { name: "Save" }));
          await Promise.resolve();
        });

        await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
        const [, init] = mockFetch.mock.calls[1] as [string, RequestInit];
        const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
        expect(body).toEqual([{ key: SAFE_SCOPE_KEY, value: "[]" }]);
      }
    );

    it(
      "AC6b — if the operator cancels the empty-scope confirm modal, the PUT is NOT sent",
      async () => {
        const mockFetch = makeFetchMock(200);

        renderWithProviders(
          <SettingsForm
            settings={[makeSafeScopeSetting({ value: "[1]" })]}
            libraries={makeLibraries()}
          />
        );

        const select = screen.getByRole("listbox") as HTMLSelectElement;
        selectOptions(select, []);

        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        const dialog = await screen.findByRole("dialog");

        await act(async () => {
          fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
          await Promise.resolve();
        });

        await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

        // PUT must NOT have been issued — only the SafeScopeAvailabilityBadge mount fetch
        // of GET /api/status (STORY-105) happened.
        expect(mockFetch).toHaveBeenCalledTimes(1);
        const [url] = mockFetch.mock.calls[0] as [string, RequestInit];
        expect(url).toBe("/api/status");
      }
    );
  });
});
