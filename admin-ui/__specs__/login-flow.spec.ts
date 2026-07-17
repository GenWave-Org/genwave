// STORY-043 — Next.js scaffold: login flow + middleware
//
// Runner: Jest. The admin-ui/ project does not exist yet — T033 scaffolds
// it. These specs document the expected behavior; they will fail (and the
// suite won't even compile) until T033 lands the Next.js scaffold + Jest
// config + a real implementation to drive.

import { describe, it, expect } from "@jest/globals";

describe("Feature: Login flow + middleware (admin UI)", () => {
  describe("Scenario: scaffolded project shape", () => {
    it.todo("admin-ui/package.json declares Next.js 14+ with App Router");
    it.todo("admin-ui/tsconfig.json has strict: true and noUncheckedIndexedAccess: true");
  });

  describe("Scenario: middleware redirects unauthenticated requests", () => {
    it.todo("GET /dashboard without cookie returns 307 to /login?return=/dashboard");
  });

  describe("Scenario: /login renders a form posting to /api/auth/login", () => {
    it.todo("GET /login returns 200 with an HTML form action=/api/auth/login method=POST");
  });

  describe("Scenario: successful login redirects to returnTo or /dashboard", () => {
    it.todo("after 204 from /api/auth/login the browser is at /dashboard");
    it.todo("the session cookie is set on the browser via the proxy");
  });

  describe("Scenario: logged-in user hitting /login is redirected to /dashboard", () => {
    it.todo("GET /login with session cookie returns a redirect to /dashboard");
  });

  describe("Scenario: secrets are not leaked into the client bundle", () => {
    it.todo("built bundle contains no NEXT_PUBLIC_ env values besides the cookie name");
  });

  // -------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------

  describe("Scenario: login failure renders a generic error", () => {
    it.todo("401 from /api/auth/login renders 'Invalid credentials' (no enumeration oracle)");
  });

  describe("Scenario: logout clears the cookie and lands on /login", () => {
    it.todo("POST /api/auth/logout clears the cookie and redirects to /login");
  });
});
