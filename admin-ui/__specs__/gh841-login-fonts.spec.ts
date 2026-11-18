// gh-#841 — signed out, every /fonts/*.woff2 request was redirected to /login, so the login page
// rendered without its fonts. Fonts now pass the middleware like favicon.ico and icon.png do.
//
// Runner: Jest (node environment — real NextRequest, as api-fetch-401.spec.ts).

import { describe, it, expect } from "@jest/globals";
import { NextRequest } from "next/server";
import { config, middleware } from "../middleware";

const fontPath = "/fonts/fraunces-variable-latin.woff2";

describe("Feature: login page fonts load signed out (gh-#841)", () => {
  describe("Scenario: a font request with no session cookie", () => {
    it("passes through instead of redirecting to /login", () => {
      const res = middleware(new NextRequest(`http://localhost:3000${fontPath}`));
      expect(res.headers.get("location")).toBeNull();
      expect(res.status).toBe(200);
    });

    it("is excluded by the matcher, so the middleware never runs for it", () => {
      const matcher = new RegExp(`^${config.matcher[0]}$`);
      expect(matcher.test(fontPath)).toBe(false);
      expect(matcher.test("/dashboard")).toBe(true);
    });
  });

  describe("Scenario: a page with no session cookie", () => {
    it("is still redirected to /login", () => {
      const res = middleware(new NextRequest("http://localhost:3000/dashboard"));
      expect(res.status).toBe(307);
    });
  });
});
