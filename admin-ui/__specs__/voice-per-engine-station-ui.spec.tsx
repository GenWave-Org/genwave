// @jest-environment jsdom
// T753b (STORY-556, SPEC F219.8, F219.9) — the settings voice choice list shows a per-engine map value plainly,
// and the booth log names the voice-missing and handoff-dropped rows in plain words.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import { BoothLogView } from "../app/(authed)/booth-log/BoothLogView";
import { boothLogKindLabel } from "@/lib/booth-log-copy";
import { settingDto } from "./setting-fixture";

const VOICE_MAP = '{"kokoro":"af_nova","chatterbox":"nova"}';
const VOICE_MAP_LABEL = "One voice per engine (chatterbox: nova, kokoro: af_nova)";

function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

describe("Feature: the voice choice list shows a per-engine map plainly", () => {
  describe("Scenario: Station:Voice is saved as an engine map", () => {
    // Given the server appended the map to the list with its plain label.
    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "Station:Voice",
              value: VOICE_MAP,
              source: "override",
              applyMode: "live",
              kind: "choice",
              unit: "",
              choices: [
                { value: "af_nova", label: "af_nova" },
                { value: VOICE_MAP, label: VOICE_MAP_LABEL },
              ],
            }),
          ]}
        />
      );
    });

    it("selects the map option and shows its plain label", () => {
      const select = screen.getByLabelText(/Station:Voice/) as HTMLSelectElement;
      expect(select.selectedOptions[0]?.textContent).toBe(VOICE_MAP_LABEL);
    });

    it("never calls the value '(not found)' or '(current)'", () => {
      expect(screen.queryByText(/not found/)).not.toBeInTheDocument();
      expect(screen.queryByText(/\(current\)/)).not.toBeInTheDocument();
    });
  });
});

describe("Feature: the booth log names voice-missing and handoff-dropped rows", () => {
  beforeEach(() => {
    jest.useFakeTimers({ now: new Date("2026-01-01T12:00:00.000Z") });
  });

  afterEach(() => {
    jest.useRealTimers();
    jest.restoreAllMocks();
  });

  function installFetchMock(entries: unknown[]): void {
    const fn = jest.fn<typeof fetch>().mockImplementation((input) => {
      const body = String(input).includes("/api/personas") ? [] : { entries, nextBefore: null };
      return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(body) } as Response);
    });
    global.fetch = fn as unknown as typeof fetch;
  }

  async function renderFeed(kind: string, summary: string): Promise<void> {
    installFetchMock([{ occurredAt: "2026-01-01T10:04:00.000Z", kind, summary, id: 1, personaId: null }]);
    render(<BoothLogView timeZone="UTC" />);
    await act(async () => {
      await jest.advanceTimersByTimeAsync(0);
    });
  }

  it("labels a voice-missing row 'Voice missing'", async () => {
    await renderFeed("voice-missing", "Voice missing: Station:Ads:CastVoices has no voice for the active engine — talk dropped");
    expect(screen.getByText("Voice missing")).toBeInTheDocument();
  });

  it("labels a handoff-dropped row 'Handoff dropped'", async () => {
    await renderFeed("handoff-dropped", "Handoff intro dropped (render)");
    expect(screen.getByText("Handoff dropped")).toBeInTheDocument();
  });

  it("still shows an unknown kind raw", () => {
    expect(boothLogKindLabel("something-new")).toBe("something-new");
  });
});
