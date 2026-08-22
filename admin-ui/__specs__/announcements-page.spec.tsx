// STORY-361 — The Announcements page (SPEC F146 · PLAN T344)
// Pending todos — /build-loop fills these when T344 builds the page.
import { describe, it } from "@jest/globals";

describe("Feature: The Announcements page", () => {
  describe("Scenario: sending from the page", () => {
    it.todo(
      "posts the typed message with the verbatim toggle through the one announcements endpoint (T344, STORY-361 AC1)",
    );
    it.todo(
      "shows the new entry immediately as pending (T344, STORY-361 AC1)",
    );
  });

  describe("Scenario: the history is the visible-decline surface", () => {
    it.todo(
      "renders every reachable state with its decline reason where present (T344, STORY-361 AC2)",
    );
    it.todo(
      "renders collapse counts and aired timestamps (T344, STORY-361 AC2)",
    );
  });

  describe("Scenario: token management lives here", () => {
    it.todo(
      "reveals a generated token exactly once and shows last-used (T344, STORY-361 AC3)",
    );
  });

  describe("Scenario: public mode says so", () => {
    it.todo(
      "replaces the send with an explanation while SpectatorMode is on (T344, STORY-361 AC4)",
    );
  });
});
