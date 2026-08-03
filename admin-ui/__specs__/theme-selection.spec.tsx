// STORY-267 — Admin UI theme selection (SPEC F102.12, F102.13, F102.16)
//
// Runner: Jest. The Admin UI already owns a real theme mechanism — a `genwave-theme`
// cookie driving `:root[data-theme="dark"]`, with `:root:not([data-theme])` as the
// system-dark fallback. This story widens that from a binary light/dark toggle to theme
// selection, keeping the two axes separate: the THEME is chosen, the MODE within it still
// follows an explicit choice or, absent one, prefers-color-scheme.
//
// F102.16 is the quiet win here. Today `wwwroot/spectator/styles.css` claims it "Mirrors
// admin-ui/app/globals.css's token values 1:1" and NOTHING enforces that — there is no
// cross-surface parity spec anywhere in the repo. Once both surfaces read the composed
// stylesheet, the drift is not merely tested against, it is structurally impossible: there
// is no second place a token value could be edited.
//
// Specs are it.todo pending T167/T168/T170 — the house pattern (see
// safe-scope-empty-badge.spec.tsx). Un-pin against the rendered component as each lands.

import { describe, it } from "@jest/globals";

describe("Feature: Admin UI theme selection", () => {
  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: the console offers the same themes as the public page", () => {
    it.todo(
      "renders one selectable option per shipped theme (T167, AC1)",
    );
  });

  describe("Scenario: it replaces the binary toggle", () => {
    it.todo(
      "no longer renders today's light/dark-only toggle (T167, AC2)",
    );

    it.todo(
      "renders theme selection in its place (T167, AC2)",
    );
  });

  describe("Scenario: an explicit choice outranks OS preference", () => {
    it.todo(
      "applies the explicitly chosen theme when the OS preference disagrees (T167, AC3)",
    );

    it.todo(
      "applies the explicitly chosen mode when the OS preference disagrees (T167, AC3)",
    );
  });

  describe("Scenario: OS preference still picks the mode", () => {
    // The two axes stay separate: with no explicit choice, prefers-color-scheme selects
    // the MODE WITHIN the station's theme — it does not select a different theme.
    it.todo(
      "applies the station theme's dark mode when the OS prefers dark and no explicit choice exists (T167, AC4)",
    );

    it.todo(
      "applies the station theme's light mode when the OS prefers light and no explicit choice exists (T167, AC4)",
    );
  });

  describe("Scenario: an OS-dark visitor is never served a light palette", () => {
    // This is why flat one-look themes were rejected at design — they would strand an
    // OS-dark visitor in whichever palette the station happened to pick.
    it.todo(
      "resolves the active theme's dark mode with no explicit choice anywhere (T167, AC5)",
    );
  });

  describe("Scenario: both surfaces resolve from one source", () => {
    it.todo(
      "resolves token values identical to the spectator surface for the same theme and mode (T170, AC6)",
    );
  });

  describe("Scenario: the duplicated token blocks are gone", () => {
    it.todo(
      "globals.css carries only the shipped default's fallback tokens, not a full hand-mirrored copy (T168, AC7)",
    );

    it.todo(
      "spectator/styles.css carries only the shipped default's fallback tokens (T168, AC7)",
    );
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: drift cannot be reintroduced by editing one surface", () => {
    // The assertion that matters: after T168 there is no second place to edit. A spec that
    // merely compared two files would still permit drift and then report it; this one
    // asserts the second copy does not exist.
    it.todo(
      "changing a manifest token value changes both surfaces, with no second location holding that value (T168, AC8)",
    );
  });
});
