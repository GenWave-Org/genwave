// @jest-environment jsdom
// STORY-474 — About page (gh-#16 · SPEC F207 · PLAN T561 T559)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T561). Each Given comment names the arrange the scenario needs.
//
// next/jest's SWC transform (unlike babel-jest) does not hoist jest.mock() calls above import
// statements (mirrors app-shell.spec.tsx's own header comment), so Sidebar — which calls the
// mocked next/navigation hook — is loaded via a dynamic `await import()` inside the test.

jest.mock("next/navigation", () => ({
  usePathname: jest.fn(),
}));

jest.mock("@/app/login/actions", () => ({
  logout: jest.fn(),
}));

import { describe, it, jest, expect } from "@jest/globals";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { usePathname } from "next/navigation";

const mockedUsePathname = jest
  .requireMock<{ usePathname: typeof usePathname }>("next/navigation")
  .usePathname as jest.MockedFunction<typeof usePathname>;

describe("Feature: About page", () => {
  describe("Scenario: the page with a response", () => {
    // Given: /about rendered with version, stationName, libraryCount, uptimeSeconds, attributions
    it.todo("AC4 — shows the version");
    it.todo("AC4 — shows the station name");
    it.todo("AC4 — shows the library count");
    it.todo("AC4 — shows the uptime");
    it.todo("AC4 — shows every attribution name");
  });

  describe("Scenario: the sidebar footer", () => {
    // Given: Sidebar rendered
    it("AC5 — About sits in the footer beside Sign out", async () => {
      mockedUsePathname.mockReturnValue("/dashboard");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      render(<Sidebar />);

      const about = screen.getByRole("link", { name: "About" });
      const signOut = screen.getByRole("button", { name: /sign out/i });
      expect(about.closest("footer")).toContainElement(signOut);
    });
  });

});
