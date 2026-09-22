// @jest-environment jsdom
// STORY-475 — A stale cookie sends you to sign in (gh-#729 · SPEC F208 · PLAN T566)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T566). Each Given comment names the arrange the scenario needs.

import { describe, it } from "@jest/globals";

describe("Feature: A stale cookie sends you to sign in", () => {
  describe("Scenario: a 401 through apiFetch", () => {
    // Given: fetch mocked 401, useRouter mocked, document.cookie seeded
    it.todo("AC1 — the session cookie is cleared");
    it.todo("AC2 — the router navigates to /login?expired=1");
  });

  describe("Scenario: the login page with the flag", () => {
    // Given: /login?expired=1 rendered
    it.todo("AC3 — shows \"Your session ended. Sign in again.\"");
  });

  describe("Scenario: the former 401 sites", () => {
    // Given: admin-ui source scanned
    it.todo("AC4 — no `status === 401` branch outside api-fetch.ts");
  });

  describe("Scenario: the middleware", () => {
    // Given: middleware.ts read
    it.todo("AC5 — still checks cookie presence only");
  });

  // ---- sad path ----
  describe("Scenario: a 500 through apiFetch", () => {
    // Given: fetch mocked 500
    it.todo("AC6 — rejects with the response");
    it.todo("AC6 — does not navigate");
  });

});
