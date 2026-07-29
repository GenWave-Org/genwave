// @jest-environment jsdom
// gh-#144 — settings: redesign into tabs per area.
//
// The single settings page grew a tab strip: one tab per key AREA (the prefix before the first
// `:`, mirroring StationSettingsAllowlist's namespaces), Station pinned first, the rest
// alphabetical; the shipped section cards nest unchanged under each tab. Every panel stays
// MOUNTED and merely `hidden` while inactive (the SettingHelpFlyover precedent), because the
// save model is page-wide: one form, one values map, one changed-keys PUT across every tab.
// Staged-but-unsaved work on a non-visible tab is flagged by a dot on its tab; a 400 lands the
// operator on the first offending tab. `?tab=<id>` deep-links the landing tab.
//
// Runner: Jest (jsdom) + @testing-library/react — renderWithProviders style per
// settings-grouped-sections.spec.tsx.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeSetting(key: string, overrides: Partial<SettingDto> = {}): SettingDto {
  return {
    key,
    value: "1",
    source: "default",
    applyMode: "live",
    kind: "number",
    unit: "count",
    ...overrides,
  };
}

/**
 * One key per area across five prefixes plus a colon-less engine knob — enough to exercise
 * derivation, ordering, and the Station fold-in without hand-typing the whole allowlist.
 */
function makeCrossAreaSettings(): SettingDto[] {
  return [
    makeSetting("Loudness:TargetLufs", { unit: "LUFS" }),
    makeSetting("Tts:Endpoint", { kind: "string", unit: "", value: "http://kokoro:8880" }),
    makeSetting("Station:Cadence:StationIdEveryNUnits"),
    makeSetting("GW_XFADE_MIN", { applyMode: "engine-restart", unit: "seconds" }),
    makeSetting("Llm:TimeoutSeconds", { unit: "seconds" }),
    makeSetting("Admin:PlayHistoryCapacity"),
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

/** SettingsForm calls useConfirm() unconditionally; Toaster renders mutation-outcome toasts. */
function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function tabNamed(name: RegExp): HTMLElement {
  return screen.getByRole("tab", { name });
}

/** A tab's panel via its own aria-controls — the wiring the a11y scenario also asserts. */
function panelOf(tab: HTMLElement): HTMLElement {
  const panelId = tab.getAttribute("aria-controls");
  if (panelId === null) throw new Error("tab lacks aria-controls");
  const panel = document.getElementById(panelId);
  if (panel === null) throw new Error(`no element with id "${panelId}"`);
  return panel;
}

// ---------------------------------------------------------------------------
// Feature: settings areas render as tabs (gh-#144)
// ---------------------------------------------------------------------------

describe("Feature: settings areas render as tabs", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    // Deep-link state is read from the real jsdom location — pin it per test so a prior
    // test's ?tab= (written by clicking a tab) can never leak into the next render.
    window.history.replaceState(null, "", "/settings");
  });

  afterEach(() => {
    global.fetch = originalFetch;
    window.history.replaceState(null, "", "/settings");
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: tab derivation from key prefixes", () => {
    it("renders one tab per key area — Station first, the rest alphabetical", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      const tabs = screen.getAllByRole("tab");
      expect(tabs.map((t) => t.textContent)).toEqual([
        "Station",
        "Admin",
        "LLM",
        "Loudness",
        "TTS",
      ]);
    });

    it("folds the colon-less GW_* engine knobs into the Station tab instead of minting their own", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      expect(screen.queryByRole("tab", { name: /GW/ })).not.toBeInTheDocument();
      const stationPanel = panelOf(tabNamed(/^Station/));
      expect(within(stationPanel).getByLabelText(/GW_XFADE_MIN/)).toBeInTheDocument();
    });

    it("nests the existing section cards under each tab unchanged", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      // Station's panel keeps the shipped Playout section card (cadence + GW_XFADE siblings).
      const playout = screen.getByRole("heading", { name: "Playout" });
      const playoutSection = within(playout.closest("section")!);
      expect(playoutSection.getByLabelText(/Station:Cadence:StationIdEveryNUnits/)).toBeInTheDocument();
      expect(playoutSection.getByLabelText(/GW_XFADE_MIN/)).toBeInTheDocument();
    });

    it("keeps every panel mounted while inactive — hidden, never unmounted", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      // Station is active; the TTS field is still in the document (page-wide form), just hidden.
      const ttsInput = screen.getByLabelText(/Tts:Endpoint/);
      expect(ttsInput).toBeInTheDocument();
      expect(ttsInput).not.toBeVisible();
      expect(panelOf(tabNamed(/^TTS/))).toHaveAttribute("hidden");
      expect(panelOf(tabNamed(/^Station/))).not.toHaveAttribute("hidden");
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: switching tabs", () => {
    it("clicking a tab shows its panel, hides the rest, and moves aria-selected", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      fireEvent.click(tabNamed(/^TTS/));

      expect(tabNamed(/^TTS/)).toHaveAttribute("aria-selected", "true");
      expect(tabNamed(/^Station/)).toHaveAttribute("aria-selected", "false");
      expect(screen.getByLabelText(/Tts:Endpoint/)).toBeVisible();
      expect(panelOf(tabNamed(/^Station/))).toHaveAttribute("hidden");
    });

    it("activating a tab writes ?tab=<id> so the view is shareable", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      fireEvent.click(tabNamed(/^TTS/));

      expect(window.location.search).toBe("?tab=tts");
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: deep-linking the active tab", () => {
    it("?tab=tts lands on the TTS tab", () => {
      window.history.replaceState(null, "", "/settings?tab=tts");
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      expect(tabNamed(/^TTS/)).toHaveAttribute("aria-selected", "true");
      expect(screen.getByLabelText(/Tts:Endpoint/)).toBeVisible();
    });

    it("an unknown ?tab value falls back to the first tab", () => {
      window.history.replaceState(null, "", "/settings?tab=bogus");
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      expect(tabNamed(/^Station/)).toHaveAttribute("aria-selected", "true");
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: the save model stays page-wide", () => {
    it("one Save submits staged changes from several tabs in a single PUT", async () => {
      const mockFetch = makeFetchMock(200);
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      // Stage a change on the hidden TTS panel and another on the active Station panel.
      fireEvent.change(screen.getByLabelText(/Tts:Endpoint/), {
        target: { value: "http://kokoro-2:8880" },
      });
      fireEvent.change(screen.getByLabelText(/Station:Cadence:StationIdEveryNUnits/), {
        target: { value: "6" },
      });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/settings");
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toHaveLength(2);
      expect(body).toContainEqual({ key: "Tts:Endpoint", value: "http://kokoro-2:8880" });
      expect(body).toContainEqual({ key: "Station:Cadence:StationIdEveryNUnits", value: "6" });
    });

    it("the Save button renders outside every tabpanel, so it shows on any active tab", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      fireEvent.click(tabNamed(/^Loudness/));

      const save = screen.getByRole("button", { name: /save settings/i });
      expect(save).toBeVisible();
      expect(save.closest("[role='tabpanel']")).toBeNull();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: staged changes on a non-visible tab are never silent", () => {
    it("flags a tab with unsaved changes while another tab is active", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      fireEvent.change(screen.getByLabelText(/Tts:Endpoint/), {
        target: { value: "http://kokoro-2:8880" },
      });

      expect(screen.getByRole("tab", { name: /TTS.*unsaved changes/ })).toBeInTheDocument();
      // Tabs with nothing staged stay unflagged.
      expect(screen.queryByRole("tab", { name: /Loudness.*unsaved changes/ })).not.toBeInTheDocument();
    });

    it("clears the flag once the change is saved (re-baselined diff, gh-#140)", async () => {
      makeFetchMock(200);
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      fireEvent.change(screen.getByLabelText(/Tts:Endpoint/), {
        target: { value: "http://kokoro-2:8880" },
      });
      expect(screen.getByRole("tab", { name: /TTS.*unsaved changes/ })).toBeInTheDocument();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.queryByRole("tab", { name: /TTS.*unsaved changes/ })).not.toBeInTheDocument();
      });
    });

    it("clears the flag when the operator reverts the staged value by hand", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      const input = screen.getByLabelText(/Tts:Endpoint/);
      fireEvent.change(input, { target: { value: "http://kokoro-2:8880" } });
      expect(screen.getByRole("tab", { name: /TTS.*unsaved changes/ })).toBeInTheDocument();

      fireEvent.change(input, { target: { value: "http://kokoro:8880" } });
      expect(screen.queryByRole("tab", { name: /TTS.*unsaved changes/ })).not.toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario (sad path): a rejected key on a non-visible tab is surfaced", () => {
    it("a 400 auto-switches to the first offending tab and shows the inline error", async () => {
      const validationProblem = {
        errors: { settings: ["Must be a non-empty absolute http/https URL"] },
        title: "One or more settings values are invalid.",
        status: 400,
      };
      makeFetchMock(400, validationProblem);
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      // Stage a change on the hidden TTS panel while Station stays active, then save.
      fireEvent.change(screen.getByLabelText(/Tts:Endpoint/), { target: { value: "not-a-url" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(tabNamed(/^TTS/)).toHaveAttribute("aria-selected", "true");
        expect(screen.getByRole("alert")).toHaveTextContent(
          "Must be a non-empty absolute http/https URL"
        );
      });
      expect(screen.getByRole("alert")).toBeVisible();
    });

    it("marks the offending tab with a validation-error flag", async () => {
      const validationProblem = {
        errors: { settings: ["Must be a non-empty absolute http/https URL"] },
        status: 400,
      };
      makeFetchMock(400, validationProblem);
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      fireEvent.change(screen.getByLabelText(/Tts:Endpoint/), { target: { value: "not-a-url" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("tab", { name: /TTS.*validation error/ })).toBeInTheDocument();
      });
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: keyboard and assistive-tech semantics", () => {
    it("renders a labelled tablist whose tabs control their aria-labelled panels", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      const tablist = screen.getByRole("tablist", { name: /settings areas/i });
      expect(within(tablist).getAllByRole("tab").length).toBeGreaterThan(1);

      for (const tab of within(tablist).getAllByRole("tab")) {
        const panel = panelOf(tab);
        expect(panel).toHaveAttribute("role", "tabpanel");
        expect(panel).toHaveAttribute("aria-labelledby", tab.id);
      }
    });

    it("uses a roving tabindex — only the active tab is in the tab order", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      expect(tabNamed(/^Station/)).toHaveAttribute("tabindex", "0");
      expect(tabNamed(/^TTS/)).toHaveAttribute("tabindex", "-1");
      expect(tabNamed(/^Loudness/)).toHaveAttribute("tabindex", "-1");
    });

    it("ArrowRight moves selection and focus to the next tab, wrapping at the end", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      const station = tabNamed(/^Station/);
      station.focus();
      fireEvent.keyDown(station, { key: "ArrowRight" });

      const admin = tabNamed(/^Admin/);
      expect(admin).toHaveAttribute("aria-selected", "true");
      expect(admin).toHaveFocus();

      // Wrap: ArrowLeft from the first tab lands on the last (TTS).
      fireEvent.keyDown(admin, { key: "ArrowLeft" });
      fireEvent.keyDown(tabNamed(/^Station/), { key: "ArrowLeft" });
      expect(tabNamed(/^TTS/)).toHaveAttribute("aria-selected", "true");
      expect(tabNamed(/^TTS/)).toHaveFocus();
    });

    it("Home and End jump to the first and last tab", () => {
      renderWithProviders(<SettingsForm settings={makeCrossAreaSettings()} />);

      const station = tabNamed(/^Station/);
      station.focus();
      fireEvent.keyDown(station, { key: "End" });
      expect(tabNamed(/^TTS/)).toHaveAttribute("aria-selected", "true");
      expect(tabNamed(/^TTS/)).toHaveFocus();

      fireEvent.keyDown(tabNamed(/^TTS/), { key: "Home" });
      expect(tabNamed(/^Station/)).toHaveAttribute("aria-selected", "true");
      expect(tabNamed(/^Station/)).toHaveFocus();
    });
  });
});
