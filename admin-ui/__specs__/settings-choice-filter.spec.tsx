// @jest-environment jsdom
// gh-#260 — a long choice list (Station:Timezone carries every zone the host knows) gets a filter
// box above the select. Renders ChoiceSettingControl directly: the claim is the control's own.

import { describe, it, expect, jest } from "@jest/globals";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ChoiceSettingControl, CHOICE_FILTER_THRESHOLD } from "../app/(authed)/settings/ChoiceSettingControl";

const ZONES = Array.from({ length: CHOICE_FILTER_THRESHOLD + 1 }, (_, i) => ({
  value: `Zone/${String(i).padStart(2, "0")}`,
  label: `zone-label-${i}`,
}));
const LONG_CHOICES = [
  { value: "", label: "fixture-default", isDefault: true },
  { value: "America/Edmonton", label: "edmonton-label" },
  { value: "Europe/London", label: "london-label" },
  ...ZONES,
];

function renderControl(value: string, choices = LONG_CHOICES) {
  render(
    <ChoiceSettingControl controlId="tz" value={value} onChange={jest.fn()} disabled={false} choices={choices} />,
  );
  return screen.getByRole("combobox") as HTMLSelectElement;
}

function optionValues(select: HTMLSelectElement): string[] {
  return Array.from(select.options).map((o) => o.value);
}

describe("Feature: filtering a long choice list (gh-#260)", () => {
  describe("Scenario: a list longer than the threshold", () => {
    it("shows a filter box", () => {
      renderControl("");
      expect(screen.getByRole("searchbox")).toBeInTheDocument();
    });

    it("narrows the options by value, case-insensitive", () => {
      const select = renderControl("");
      fireEvent.change(screen.getByRole("searchbox"), { target: { value: "edmon" } });
      expect(optionValues(select)).toEqual(["", "America/Edmonton"]);
    });

    it("narrows the options by label", () => {
      const select = renderControl("");
      fireEvent.change(screen.getByRole("searchbox"), { target: { value: "LONDON-LAB" } });
      expect(optionValues(select)).toEqual(["", "Europe/London"]);
    });

    it("keeps the current value's option while filtering", () => {
      const select = renderControl("Europe/London");
      fireEvent.change(screen.getByRole("searchbox"), { target: { value: "edmon" } });
      expect(optionValues(select)).toEqual(["America/Edmonton", "Europe/London"]);
      expect(select.value).toBe("Europe/London");
    });

    it("shows every option when the box is cleared", () => {
      const select = renderControl("");
      const box = screen.getByRole("searchbox");
      fireEvent.change(box, { target: { value: "edmon" } });
      fireEvent.change(box, { target: { value: "" } });
      expect(optionValues(select)).toEqual(LONG_CHOICES.map((c) => c.value));
    });
  });

  describe("Scenario: a short list (sad path)", () => {
    it("shows no filter box", () => {
      renderControl("", LONG_CHOICES.slice(0, 3));
      expect(screen.queryByRole("searchbox")).not.toBeInTheDocument();
    });
  });
});
