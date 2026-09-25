// @jest-environment jsdom
// STORY-479 — Live model and voice lists in the form (gh-#778 · SPEC F205.7g · PLAN T581)
//
// BDD specification — Jest. Was RED at plan time (every scenario `it.todo`); T581 turned each one
// into a real `it`. Each Given comment names the arrange the scenario needs.
//
// Runner: Jest (jsdom) + @testing-library/react, mirroring settings-choice-control.spec.tsx's
// house pattern (renderWithProviders, settingDto from __specs__/setting-fixture.ts) —
// SettingsForm calls useConfirm() unconditionally, so every render needs a ConfirmDialogProvider
// ancestor. Driven through SettingsForm, the real entry point, never ChoiceSettingControl
// directly.
//
// AC19's registry fact: SETTING_CONTROL_REGISTRY is module-private in SettingsForm.tsx (no house
// idiom exports a registry just for a test — grepped, there is exactly one registry in this
// codebase). "Station:Voice has no entry" is proven behaviourally instead: a Station:Voice choice
// DTO never triggers a fetch (the old VoiceSettingControl's own /api/voices call is what a
// registry entry would have wired back in).

import { existsSync } from "node:fs";
import path from "node:path";
import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import { settingDto } from "./setting-fixture";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const MODEL_CHOICES = [
  { value: "llama3", label: "llama3" },
  { value: "phi4", label: "phi4" },
];

function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

// ---------------------------------------------------------------------------
// Feature: Live model and voice lists in the form
// ---------------------------------------------------------------------------

describe("Feature: Live model and voice lists in the form", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: a stale list", () => {
    // Given: a choice DTO with choicesStale = true, value "phi4", choices [llama3, phi4]
    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "Llm:Model",
              value: "phi4",
              source: "default",
              applyMode: "live",
              kind: "choice",
              unit: "",
              choices: MODEL_CHOICES,
              choicesStale: true,
            }),
          ]}
        />
      );
    });

    it("AC9 — the select renders", () => {
      const select = screen.getByLabelText(/Llm:Model/) as HTMLSelectElement;
      expect(select.tagName).toBe("SELECT");
    });

    it('AC9 — the note "This list may be out of date." renders', () => {
      expect(screen.getByText("This list may be out of date.")).toBeInTheDocument();
    });
  });

  describe("Scenario: a blank value", () => {
    // Given: a choice DTO with value "" and choices [llama3, phi4]
    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "Llm:Model",
              value: "",
              source: "default",
              applyMode: "live",
              kind: "choice",
              unit: "",
              choices: MODEL_CHOICES,
            }),
          ]}
        />
      );
    });

    it('AC10 — the select shows the "Choose…" placeholder', () => {
      const select = screen.getByLabelText(/Llm:Model/) as HTMLSelectElement;
      expect(select.options[0]).toHaveTextContent("Choose…");
    });

    it("AC10 — no blank option is offered", () => {
      const select = screen.getByLabelText(/Llm:Model/) as HTMLSelectElement;
      const enabledBlankOptions = Array.from(select.options).filter(
        (option) => option.value === "" && !option.disabled
      );
      expect(enabledBlankOptions).toHaveLength(0);
    });
  });

  describe("Scenario: the settings control registry", () => {
    let fetchMock: jest.MockedFunction<typeof fetch>;

    // Given: a Station:Voice choice DTO rendered through SettingsForm, with fetch mocked to catch
    // any call a registry entry would have wired back in (the old VoiceSettingControl's own
    // /api/voices fetch)
    beforeEach(() => {
      fetchMock = jest.fn<typeof fetch>();
      global.fetch = fetchMock;

      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "Station:Voice",
              value: "af_alloy",
              source: "default",
              applyMode: "live",
              kind: "choice",
              unit: "",
              choices: [{ value: "af_alloy", label: "af_alloy" }],
            }),
          ]}
        />
      );
    });

    it("AC19 — Station:Voice has no registry entry", () => {
      expect(fetchMock).not.toHaveBeenCalled();
    });

    it("AC19 — VoiceSettingControl.tsx does not exist", () => {
      const settingsDir = path.resolve(__dirname, "../app/(authed)/settings");
      expect(existsSync(path.join(settingsDir, "VoiceSettingControl.tsx"))).toBe(false);
    });
  });

  // ---- sad path ----
  describe("Scenario: a list that never loaded", () => {
    // Given: a choice DTO with choicesFailed = true, value "phi4", choices [("phi4", "phi4 (not found)")]
    beforeEach(() => {
      renderWithProviders(
        <SettingsForm
          settings={[
            settingDto({
              key: "Llm:Model",
              value: "phi4",
              source: "default",
              applyMode: "live",
              kind: "choice",
              unit: "",
              help: "The model the LLM engine loads for patter generation.",
              choices: [{ value: "phi4", label: "phi4 (not found)" }],
              choicesFailed: true,
            }),
          ]}
        />
      );
    });

    it("AC17 — the select is disabled", () => {
      const select = screen.getByLabelText(/Llm:Model/) as HTMLSelectElement;
      expect(select).toBeDisabled();
    });

    it('AC17 — the select holds "phi4"', () => {
      const select = screen.getByLabelText(/Llm:Model/) as HTMLSelectElement;
      expect(select.value).toBe("phi4");
    });

    it("AC17 — the help text renders", () => {
      expect(screen.getByTestId("setting-help-Llm:Model")).toBeInTheDocument();
    });

    it('AC17 — the note "Couldn\'t load the list." renders', () => {
      expect(screen.getByText("Couldn't load the list.")).toBeInTheDocument();
    });

    it("AC18 — no text input renders for the key", () => {
      const control = screen.getByLabelText(/Llm:Model/);
      expect(control.tagName).not.toBe("INPUT");
    });
  });
});
