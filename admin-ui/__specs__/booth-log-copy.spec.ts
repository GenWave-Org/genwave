import { describe, it, expect } from "@jest/globals";
import { repeatCaughtSummary } from "../lib/booth-log-copy";

describe("repeatCaughtSummary", () => {
  it("names the DJ and says cut short for a trimmed outcome", () => {
    expect(repeatCaughtSummary('Repeat caught: "banger, log it" — trimmed', "Flip")).toBe(
      'Flip wrote "banger, log it" again — cut short'
    );
  });

  it("says swapped for a stock line for a template outcome", () => {
    expect(repeatCaughtSummary('Repeat caught: "oh one" — template', "Flip")).toBe(
      'Flip wrote "oh one" again — swapped for a stock line'
    );
  });

  it("keeps the stored lead-in when no DJ name is known, still rewording the outcome", () => {
    expect(repeatCaughtSummary('Repeat caught: "x" — trimmed', null)).toBe('Repeat caught: "x" — cut short');
  });

  it("leaves outcomes it has no plain wording for untouched", () => {
    expect(repeatCaughtSummary('Repeat caught: "x" — re-asked', "Flip")).toBe('Flip wrote "x" again — re-asked');
    expect(repeatCaughtSummary('Repeat caught: "x" — dropped', null)).toBe('Repeat caught: "x" — dropped');
  });

  it("only rewrites the trailing outcome when the phrase contains a dash", () => {
    expect(repeatCaughtSummary('Repeat caught: "a — trimmed — b" — trimmed', null)).toBe(
      'Repeat caught: "a — trimmed — b" — cut short'
    );
  });

  it("returns a summary of an unexpected shape unchanged", () => {
    expect(repeatCaughtSummary("Something else", "Flip")).toBe("Something else");
  });

  it("leaves a phrase containing a dash but no outcome unchanged", () => {
    const summary = 'Repeat caught: "a — b"';
    expect(repeatCaughtSummary(summary, "Flip")).toBe(summary);
  });
});
