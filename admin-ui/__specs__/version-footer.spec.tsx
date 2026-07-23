// @jest-environment jsdom
// gh-#7 — the version/edition footer stamped on every page from the root layout.
//
// NEXT_PUBLIC_GW_VERSION is inlined at build time; under jest it is unset, so the component
// renders its 0.0.0-dev sentinel — asserting that pins both the fallback and the
// "GenWave Home v…" wording without pretending to know a real build's version.

import { describe, it, expect } from "@jest/globals";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { VersionFooter } from "../components/VersionFooter";

describe("Feature: version footer", () => {
  describe("Scenario: rendering the build stamp", () => {
    it("shows the edition and the dev-sentinel version when no build stamp was inlined", () => {
      render(<VersionFooter />);
      expect(screen.getByText("GenWave Home v0.0.0-dev")).toBeInTheDocument();
    });

    it("is decorative chrome — out of the a11y tree and never clickable", () => {
      const { container } = render(<VersionFooter />);
      const footer = container.firstElementChild;
      expect(footer).toHaveAttribute("aria-hidden", "true");
      expect(footer).toHaveClass("pointer-events-none");
    });
  });
});
