// @jest-environment jsdom
// STORY-091 — Settings: grouped sections with applyMode badges (Epic Q / SPEC F28.12, F28.9)
//
// Runner: Jest (jsdom) + @testing-library/react. SettingsForm calls useConfirm() unconditionally
// (the SafeScope-empty save opens the shared modal — window.confirm is gone), so every render is
// wrapped in ConfirmDialogProvider; Toaster is mounted alongside it so success/error mutation
// outcomes (F28.9) can be asserted as toast copy.

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
const SCOPE_KEY = "Station:Scope:LibraryIds";
const SAFE_SCOPE_EMPTY_CONSEQUENCE =
  "Saving an empty SafeScope silences the stream on drain — mksafe emits silence until re-pointed.";

/** One setting per canonical section (Loudness/Playout/Scope/Safe) — covers STORY-091's grouping. */
function makeGroupedSettings(): SettingDto[] {
  return [
    { key: "Loudness:TargetLufs", value: "-16", source: "default", applyMode: "live", kind: "number", unit: "LUFS" },
    { key: "Loudness:CeilingDbtp", value: "-1", source: "default", applyMode: "live", kind: "number", unit: "dBTP" },
    {
      key: "Station:Cadence:LeadInBeforeEachTrack",
      value: "true",
      source: "default",
      applyMode: "live",
      kind: "boolean",
      unit: "",
    },
    { key: "GW_XFADE_MAX", value: "8", source: "override", applyMode: "engine-restart", kind: "number", unit: "seconds" },
    { key: SCOPE_KEY, value: "[1]", source: "override", applyMode: "live", kind: "number-list", unit: "" },
    { key: SAFE_SCOPE_KEY, value: "[2]", source: "override", applyMode: "live", kind: "number-list", unit: "" },
  ];
}

function makeLibraries(): LibraryDto[] {
  return [
    { id: 1, name: "Lib Alpha", mediaCount: 10 },
    { id: 2, name: "Lib Beta", mediaCount: 5 },
  ];
}

function makeFetchMock(status: number, body: unknown = {}): jest.MockedFunction<typeof fetch> {
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
 * SettingsForm calls useConfirm() unconditionally, so every render needs a
 * ConfirmDialogProvider ancestor; Toaster renders the success/error toasts
 * that replaced the shipped inline status banner (SPEC F28.9).
 */
function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/** Change the selection state of a <select multiple> and fire its change event. */
function selectOptions(select: HTMLSelectElement, selectedValues: string[]): void {
  const selectedSet = new Set(selectedValues);
  Array.from(select.options).forEach((opt) => {
    opt.selected = selectedSet.has(opt.value);
  });
  fireEvent.change(select);
}

/**
 * The SafeScope field mounts SafeScopeAvailabilityBadge (STORY-105, SPEC F31.4–F31.5),
 * which mount-fetches GET /api/status. Tests that render a SafeScope field but don't
 * otherwise await a fetch-driven change still need to let that pending promise settle
 * inside act() once, or React logs an "update not wrapped in act(...)" warning.
 */
async function flushMountFetch(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: Settings grouped sections
// ---------------------------------------------------------------------------

describe("Feature: Settings grouped sections", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: grouped rendering", () => {
    it("renders fields under Loudness / Playout / Scope / Safe headings", async () => {
      renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );

      const loudness = screen.getByRole("heading", { name: "Loudness" });
      const playout = screen.getByRole("heading", { name: "Playout" });
      const scope = screen.getByRole("heading", { name: "Scope" });
      const safe = screen.getByRole("heading", { name: "Safe" });

      // Each heading's section contains the fields the authoritative allowlist maps to it.
      expect(within(loudness.closest("section")!).getByLabelText(/Loudness:TargetLufs/)).toBeInTheDocument();
      expect(within(playout.closest("section")!).getByLabelText(/GW_XFADE_MAX/)).toBeInTheDocument();
      expect(within(scope.closest("section")!).getByLabelText(new RegExp(SCOPE_KEY))).toBeInTheDocument();
      expect(within(safe.closest("section")!).getByLabelText(new RegExp(SAFE_SCOPE_KEY))).toBeInTheDocument();

      await flushMountFetch();
    });

    // Q9 smoke finding, folded into Q11 (STORY-093): a field with no unit
    // must render its bare key ("Loudness:TargetLufs"), never an
    // empty-parens artifact ("Loudness:TargetLufs()") — the parens render at
    // all only when the field carries a real unit.
    it("renders a clean label with no empty-parens artifact when a field has no unit, and shows the unit when it has one", async () => {
      const { container } = renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );
      const labelFor = (key: string): HTMLLabelElement => {
        const label = Array.from(container.querySelectorAll("label")).find((el) =>
          (el.textContent ?? "").startsWith(key)
        );
        if (!label) throw new Error(`no <label> found starting with "${key}"`);
        return label;
      };

      const unitlessLabel = labelFor("Station:Cadence:LeadInBeforeEachTrack");
      expect(unitlessLabel).toHaveTextContent("Station:Cadence:LeadInBeforeEachTrack");
      expect(unitlessLabel.textContent).not.toMatch(/\(\s*\)/);

      const unitLabel = labelFor("Loudness:TargetLufs");
      expect(unitLabel).toHaveTextContent("Loudness:TargetLufs(LUFS)");

      await flushMountFetch();
    });

    it("badges every field with its applyMode", async () => {
      renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );

      // 5 live fields (both Loudness knobs, cadence lead-in, Scope, SafeScope) + 1 restart field.
      expect(screen.getAllByText("live")).toHaveLength(5);
      expect(screen.getByText("applies after engine restart")).toBeInTheDocument();

      await flushMountFetch();
    });

    // STORY-100 (SPEC F29.8) — GW_SAFE_GAP_SECONDS joins the F19 allowlist as an
    // engine-restart key and renders in the Safe group, mirroring GW_XFADE_*'s Playout
    // placement. Additive fixture (not merged into makeGroupedSettings()) so the shared
    // fixture's field/badge counts used by the other specs in this file are untouched.
    it("places GW_SAFE_GAP_SECONDS in the Safe group with the engine-restart badge", async () => {
      const settings: SettingDto[] = [
        ...makeGroupedSettings(),
        {
          key: "GW_SAFE_GAP_SECONDS",
          value: "7",
          source: "default",
          applyMode: "engine-restart",
          kind: "number",
          unit: "seconds",
        },
      ];
      renderWithProviders(
        <SettingsForm settings={settings} libraries={makeLibraries()} />
      );

      const safe = screen.getByRole("heading", { name: "Safe" });
      const safeSection = within(safe.closest("section")!);

      expect(safeSection.getByLabelText(/GW_SAFE_GAP_SECONDS/)).toBeInTheDocument();
      expect(safeSection.getByText("applies after engine restart")).toBeInTheDocument();

      await flushMountFetch();
    });

    it("lands an unrecognized/future key in a fallback section instead of dropping it", () => {
      const futureSetting: SettingDto = {
        key: "Station:Future:Knob",
        value: "1",
        source: "default",
        applyMode: "live",
        kind: "number",
        unit: "",
      };
      renderWithProviders(<SettingsForm settings={[futureSetting]} libraries={[]} />);

      expect(screen.getByRole("heading", { name: "Other" })).toBeInTheDocument();
      expect(screen.getByLabelText(/Station:Future:Knob/)).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: save round-trips with a toast", () => {
    it("PUTs the shipped body shape unchanged", async () => {
      const mockFetch = makeFetchMock(200);
      renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );

      const lufsInput = screen.getByLabelText(/Loudness:TargetLufs/);
      fireEvent.change(lufsInput, { target: { value: "-18" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      // makeGroupedSettings() includes the SafeScope key, so its SafeScopeAvailabilityBadge
      // (STORY-105, SPEC F31.4–F31.5) mount-fetches GET /api/status ahead of the PUT below.
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      const [url, init] = mockFetch.mock.calls[1] as [string, RequestInit];
      expect(url).toBe("/api/settings");
      expect(init.method).toBe("PUT");
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: "Loudness:TargetLufs", value: "-18" }]);
    });

    it("toasts success", async () => {
      makeFetchMock(200);
      renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );

      const lufsInput = screen.getByLabelText(/Loudness:TargetLufs/);
      fireEvent.change(lufsInput, { target: { value: "-18" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Settings saved.")).toBeInTheDocument();
      });
    });

    it("keeps the shipped restart-pending indication on engine-side keys", async () => {
      makeFetchMock(200);
      renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );

      const xfadeInput = screen.getByLabelText(/GW_XFADE_MAX/);
      expect(screen.getByText("applies after engine restart")).toBeInTheDocument();

      fireEvent.change(xfadeInput, { target: { value: "9" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Settings saved.")).toBeInTheDocument();
      });

      // The engine-restart badge for GW_XFADE_MAX still renders after a successful save —
      // the operator still needs to restart the engine for the write to take effect.
      expect(screen.getByText("applies after engine restart")).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: silent-on-drain badge survives", () => {
    it("renders the F25.4 badge at the SafeScope field when the effective value is empty", async () => {
      const settings = makeGroupedSettings().map((s) =>
        s.key === SAFE_SCOPE_KEY ? { ...s, value: "[]" } : s
      );
      renderWithProviders(<SettingsForm settings={settings} libraries={makeLibraries()} />);

      expect(screen.getByText(/silent on drain/i)).toBeInTheDocument();

      await flushMountFetch();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: rejecting SafeScope-empty without a modal confirm (sad path)", () => {
    it("renders the K5 empty-SafeScope confirm as a modal with consequence copy", async () => {
      renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );

      const select = screen.getByLabelText(new RegExp(SAFE_SCOPE_KEY)) as HTMLSelectElement;
      selectOptions(select, []);

      fireEvent.click(screen.getByRole("button", { name: /save settings/i }));

      const dialog = await screen.findByRole("dialog");
      expect(dialog).toHaveTextContent(SAFE_SCOPE_EMPTY_CONSEQUENCE);
    });

    it("keeps the staged value unsaved on cancel", async () => {
      const mockFetch = makeFetchMock(200);
      renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );

      const select = screen.getByLabelText(new RegExp(SAFE_SCOPE_KEY)) as HTMLSelectElement;
      selectOptions(select, []);

      fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
      const dialog = await screen.findByRole("dialog");

      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
      // PUT must NOT have been issued — only the SafeScope field's SafeScopeAvailabilityBadge
      // mount fetch of GET /api/status (STORY-105, SPEC F31.4–F31.5) happened.
      expect(mockFetch).toHaveBeenCalledTimes(1);
      const [url] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/status");
      // Staged selection is left exactly as the operator set it — still empty, not reverted.
      expect(Array.from(select.selectedOptions)).toHaveLength(0);
    });

    it("submits [] on confirm", async () => {
      const mockFetch = makeFetchMock(200);
      renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );

      const select = screen.getByLabelText(new RegExp(SAFE_SCOPE_KEY)) as HTMLSelectElement;
      selectOptions(select, []);

      fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
      const dialog = await screen.findByRole("dialog");

      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      // The SafeScopeAvailabilityBadge mount fetch (STORY-105) precedes the PUT here too.
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(2));
      const [, init] = mockFetch.mock.calls[1] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([{ key: SAFE_SCOPE_KEY, value: "[]" }]);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: rejecting invalid values at the field (sad path)", () => {
    it("renders 400 field errors inline at the fields", async () => {
      const validationProblem = {
        errors: { settings: ["Must be between -40 and 0"] },
        title: "One or more settings values are invalid.",
        status: 400,
      };
      makeFetchMock(400, validationProblem);
      renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );

      const lufsInput = screen.getByLabelText(/Loudness:TargetLufs/);
      fireEvent.change(lufsInput, { target: { value: "-99" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("alert")).toHaveTextContent("Must be between -40 and 0");
      });
    });

    it("re-enables the form after a validation failure", async () => {
      const validationProblem = {
        errors: { settings: ["Must be between -40 and 0"] },
        title: "One or more settings values are invalid.",
        status: 400,
      };
      makeFetchMock(400, validationProblem);
      renderWithProviders(
        <SettingsForm settings={makeGroupedSettings()} libraries={makeLibraries()} />
      );

      const lufsInput = screen.getByLabelText(/Loudness:TargetLufs/);
      fireEvent.change(lufsInput, { target: { value: "-99" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("alert")).toBeInTheDocument();
      });

      const saveButton = screen.getByRole("button", { name: /save settings/i });
      expect(saveButton).not.toBeDisabled();
      expect(saveButton).toHaveTextContent(/save settings/i);
      expect(lufsInput).not.toBeDisabled();
    });
  });
});
