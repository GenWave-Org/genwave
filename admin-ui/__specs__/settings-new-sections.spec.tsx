// @jest-environment jsdom
// STORY-139 — Every tunable in the console (Epic V / SPEC F44.3 + F44.8, closes gitea-#197) — UI half.
// The allowlist half lives in Host.Tests/Specs/Story139_SettingsSurfaceCompletion.cs.
//
// Runner: Jest (jsdom) + @testing-library/react. Implemented V8 (2026-07-14) against
// settings-sections.ts and SettingsForm.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";

/** One setting per section this file cares about — station/playout/library, live + enrichment. */
function makeSettings(): SettingDto[] {
  return [
    { key: "Station:Name", value: "GenWave", source: "default", applyMode: "live", kind: "string", unit: "" },
    { key: "Station:Voice", value: "af_heart", source: "default", applyMode: "live", kind: "string", unit: "" },
    { key: "Station:Persona:ActiveId", value: "0", source: "default", applyMode: "live", kind: "number", unit: "" },
    {
      key: "Station:Cadence:StationIdEveryNUnits",
      value: "4",
      source: "default",
      applyMode: "live",
      kind: "number",
      unit: "count",
    },
    {
      key: "Station:Rotation:RecentWindow",
      value: "20",
      source: "default",
      applyMode: "live",
      kind: "number",
      unit: "tracks",
    },
    {
      key: "Library:ScanIntervalSeconds",
      value: "60",
      source: "default",
      applyMode: "live",
      kind: "number",
      unit: "seconds",
    },
    {
      key: "Library:EnrichmentConcurrency",
      value: "4",
      source: "default",
      applyMode: "live",
      kind: "number",
      unit: "workers",
    },
    {
      key: "Library:CueDetection:MinSilenceDurationSec",
      value: "0.5",
      source: "default",
      applyMode: "enrichment",
      kind: "number",
      unit: "seconds",
    },
    {
      key: "Library:Energy:WindowSeconds",
      value: "12",
      source: "default",
      applyMode: "enrichment",
      kind: "number",
      unit: "seconds",
    },
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

  describe("Scenario: the new sections exist", () => {
    it("renders a station section holding Station:Name, Station:Voice, and Station:Persona:ActiveId (F44.8)", () => {
      renderWithProviders(<SettingsForm settings={makeSettings()} />);

      const station = screen.getByRole("heading", { name: "Station" });
      const stationSection = within(station.closest("section")!);

      expect(stationSection.getByLabelText(/Station:Name/)).toBeInTheDocument();
      expect(stationSection.getByLabelText(/Station:Voice/)).toBeInTheDocument();
      expect(stationSection.getByLabelText(/Station:Persona:ActiveId/)).toBeInTheDocument();
    });

    it("renders a library section holding the Library:* keys (F44.8)", () => {
      renderWithProviders(<SettingsForm settings={makeSettings()} />);

      const library = screen.getByRole("heading", { name: "Library" });
      const librarySection = within(library.closest("section")!);

      expect(librarySection.getByLabelText(/Library:ScanIntervalSeconds/)).toBeInTheDocument();
      expect(librarySection.getByLabelText(/Library:EnrichmentConcurrency/)).toBeInTheDocument();
      expect(librarySection.getByLabelText(/Library:CueDetection:MinSilenceDurationSec/)).toBeInTheDocument();
      expect(librarySection.getByLabelText(/Library:Energy:WindowSeconds/)).toBeInTheDocument();
    });

    it("places the Station:Rotation:* keys under playout (F44.8)", () => {
      renderWithProviders(<SettingsForm settings={makeSettings()} />);

      const playout = screen.getByRole("heading", { name: "Playout" });
      const playoutSection = within(playout.closest("section")!);

      expect(playoutSection.getByLabelText(/Station:Rotation:RecentWindow/)).toBeInTheDocument();
      // Rotation keys share the group with the pre-existing cadence key — never their own section.
      expect(playoutSection.getByLabelText(/Station:Cadence:StationIdEveryNUnits/)).toBeInTheDocument();
    });
  });

  describe("Scenario: badges match apply-modes", () => {
    it("badges the enrichment-mode keys 'applies at next enrichment' — the third apply-mode (F44.3)", () => {
      renderWithProviders(<SettingsForm settings={makeSettings()} />);

      expect(screen.getAllByText("applies at next enrichment")).toHaveLength(2);
    });

    it("badges Station:Name live with the icy-name engine-restart caveat copy (F44.5)", () => {
      renderWithProviders(<SettingsForm settings={makeSettings()} />);

      const station = screen.getByRole("heading", { name: "Station" });
      const stationSection = within(station.closest("section")!);

      expect(stationSection.getByLabelText(/Station:Name/).closest("div")).toHaveTextContent("Station:Name");
      // The field badges "live" (not "applies at next enrichment"/"applies after engine restart") …
      expect(screen.getAllByText("live").length).toBeGreaterThan(0);
      // … with the Icecast-name caveat copy rendered alongside it (SPEC F44.5, shipped V7).
      expect(screen.getByText(/Icecast stream\/directory name updates on the next engine restart/i))
        .toBeInTheDocument();
    });

    it("states '0 disables' on the StationIdEveryNUnits field (F42.2)", () => {
      renderWithProviders(<SettingsForm settings={makeSettings()} />);

      expect(screen.getByText(/0 disables station IDs/i)).toBeInTheDocument();
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
