// gh-#428 — Wardrobe uninstall's 409 copy. `FontPackController.Uninstall`'s `ReferencedProblem`
// carries no structured `themeSlugs` field (unlike `LibrariesController`'s `dependentMediaCount`
// extension) — only a prose `detail` sentence. These specs pin the parser against that EXACT
// sentence shape (`FontPackController.cs`'s own `ReferencedProblem`), so a future prose change on
// the Host side shows up here instead of silently degrading to the generic-message fallback.
//
// Runner: Jest (node) — pure string parsing, no DOM needed.

import { describe, it, expect } from "@jest/globals";
import { formatReferencedThemesMessage, parseReferencedThemeSlugs } from "../app/(authed)/wardrobe/referenced-themes";

describe("Feature: Wardrobe uninstall names the referencing themes", () => {
  describe("Scenario: the 409 detail names two referencing themes", () => {
    const detail =
      '"midnight-drive" is still referenced by theme(s) "midnight-drive", "sunday-static" and cannot be uninstalled — remove or edit those themes first.';

    it("extracts every referenced theme slug, in order", () => {
      expect(parseReferencedThemeSlugs(detail)).toEqual(["midnight-drive", "sunday-static"]);
    });

    it("formats the toast copy as 'In use by: <themes>'", () => {
      expect(formatReferencedThemesMessage(detail)).toBe("In use by: midnight-drive, sunday-static");
    });
  });

  describe("Scenario: the 409 detail names exactly one referencing theme", () => {
    const detail =
      '"space-grotesk" is still referenced by theme(s) "midnight-drive" and cannot be uninstalled — remove or edit those themes first.';

    it("extracts the single referenced theme slug", () => {
      expect(parseReferencedThemeSlugs(detail)).toEqual(["midnight-drive"]);
    });

    it("formats the toast copy with just that one theme", () => {
      expect(formatReferencedThemesMessage(detail)).toBe("In use by: midnight-drive");
    });
  });

  describe("Scenario: the rare empty-race sentence names no theme at all", () => {
    const detail = '"space-grotesk" is still referenced by a theme and cannot be uninstalled.';

    it("parses to an empty list rather than throwing", () => {
      expect(parseReferencedThemeSlugs(detail)).toEqual([]);
    });

    it("falls back to the raw detail sentence instead of an empty 'In use by:'", () => {
      expect(formatReferencedThemesMessage(detail)).toBe(detail);
    });
  });

  describe("Scenario: an unrelated detail sentence", () => {
    it("parses to an empty list on any shape it doesn't recognize", () => {
      expect(parseReferencedThemeSlugs("No installed font pack with slug \"unknown\" exists.")).toEqual([]);
    });
  });
});
