// @jest-environment jsdom
// STORY-139 — Every tunable in the console (Epic V / SPEC F44.3 + F44.8, closes gitea-#197) — UI half.
// The allowlist half lives in Host.Tests/Specs/Story139_SettingsSurfaceCompletion.cs.
//
// Runner: Jest (jsdom) + @testing-library/react. Implemented V8 (2026-07-14) against
// settings-sections.ts and SettingsForm.
//
// T577 (SPEC F205.5, STORY-478 AC5-AC7): the "the new sections exist" scenario that used to live
// here (Station:Name/Voice under "Station", Library:* under "Library", Rotation keys sharing
// "Playout" with Cadence keys) pinned the key-PREFIX sectioning `sectionForKey` used to do — that
// mechanism is gone. Sections now come straight off each descriptor's own server-assigned `group`
// (never derived from the key here), so "does the right key land in the right section" is a
// server-catalog fact (`Host.Tests`), and "does the client honor a descriptor's `group`" is now
// covered generically by settings-descriptor-form.spec.tsx's AC5/AC6/unrecognized-group scenarios.
// The rest of this file — badges, help flyover, cross-section save, validation — never depended on
// that mechanism and is unchanged.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";
import { settingDto } from "./setting-fixture";

/** One setting per section this file cares about — station/playout/library, live + enrichment. */
function makeSettings(): SettingDto[] {
  return [
    settingDto({
      key: "Station:Name",
      value: "GenWave",
      source: "default",
      applyMode: "live",
      kind: "string",
      unit: "",
      help: "help for Station:Name",
    }),
    settingDto({ key: "Station:Voice", value: "af_heart", source: "default", applyMode: "live", kind: "string", unit: "" }),
    settingDto({
      key: "Station:Cadence:StationIdEveryNUnits",
      value: "4",
      source: "default",
      applyMode: "live",
      kind: "number",
      unit: "count",
    }),
    settingDto({
      key: "Station:Rotation:RecentWindow",
      value: "20",
      source: "default",
      applyMode: "live",
      kind: "number",
      unit: "tracks",
    }),
    settingDto({
      key: "Library:ScanIntervalSeconds",
      value: "60",
      source: "default",
      applyMode: "live",
      kind: "number",
      unit: "seconds",
    }),
    settingDto({
      key: "Library:EnrichmentConcurrency",
      value: "4",
      source: "default",
      applyMode: "live",
      kind: "number",
      unit: "workers",
    }),
    settingDto({
      key: "Library:CueDetection:MinSilenceDurationSec",
      value: "0.5",
      source: "default",
      applyMode: "enrichment",
      kind: "number",
      unit: "seconds",
    }),
    settingDto({
      key: "Library:Energy:WindowSeconds",
      value: "12",
      source: "default",
      applyMode: "enrichment",
      kind: "number",
      unit: "seconds",
    }),
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

describe("Feature: The settings page groups every tunable honestly", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: badges match apply-modes", () => {
    it("badges the enrichment-mode keys 'applies at next enrichment' — the third apply-mode (F44.3)", () => {
      renderWithProviders(<SettingsForm settings={makeSettings()} />);

      expect(screen.getAllByText("applies at next enrichment")).toHaveLength(2);
    });

    it("renders the Station:Name help flyover, which carries the Icecast engine-restart caveat in production (F44.5)", () => {
      // The shipped caveat wording itself is pinned against the real resx in
      // Story138_StationIdentityLive.cs (TestSettingCopy.Real().Help("Station:Name")) — this
      // fixture's help text is neutral on purpose so this spec can't drift into re-asserting its
      // own fixture (T576 round 3, R2-1).
      renderWithProviders(<SettingsForm settings={makeSettings()} />);

      expect(screen.getByTestId("setting-help-Station:Name")).toBeInTheDocument();
    });
  });

  describe("Scenario: the save model stays page-wide across sections", () => {
    // Re-homed from the now-deleted settings-area-tabs.spec.tsx (gh-#144's tab strip retired,
    // T576): the save model itself never depended on tabs — one form, one changed-keys PUT — so
    // this fact still holds now that sections (not tabs) are what separates Station from Library.
    //
    // A dedicated fixture, not the file's shared makeSettings(): that one carries Station:Voice,
    // whose registry-backed VoiceSettingControl fetches /api/voices on mount and would double-
    // count against the single shared fetch mock below — this scenario is about the PUT, not
    // about registry controls.
    function makeCrossSectionSettings(): SettingDto[] {
      return [
        settingDto({ key: "Station:Name", value: "GenWave", source: "default", applyMode: "live", kind: "string", unit: "" }),
        settingDto({
          key: "Library:EnrichmentConcurrency",
          value: "4",
          source: "default",
          applyMode: "live",
          kind: "number",
          unit: "workers",
        }),
      ];
    }

    it("one Save submits staged changes from several sections in a single PUT", async () => {
      const mockFetch = makeFetchMock(200);
      renderWithProviders(<SettingsForm settings={makeCrossSectionSettings()} />);

      fireEvent.change(screen.getByLabelText(/Station:Name/), { target: { value: "New Name" } });
      fireEvent.change(screen.getByLabelText(/Library:EnrichmentConcurrency/), {
        target: { value: "8" },
      });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toEqual([
        { key: "Station:Name", value: "New Name" },
        { key: "Library:EnrichmentConcurrency", value: "8" },
      ]);
    });
  });

  describe("Scenario (sad path): validation feedback stays inline", () => {
    it("surfaces a per-field inline error on a 400 rejection of a newly-added key (F19.5)", async () => {
      const validationProblem = {
        errors: { settings: ["Must be a positive integer number of seconds"] },
        title: "One or more settings values are invalid.",
        status: 400,
      };
      makeFetchMock(400, validationProblem);
      renderWithProviders(<SettingsForm settings={makeSettings()} />);

      const scanIntervalInput = screen.getByLabelText(/Library:ScanIntervalSeconds/);
      fireEvent.change(scanIntervalInput, { target: { value: "0" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("alert")).toHaveTextContent("Must be a positive integer number of seconds");
      });
    });
  });
});
