// @jest-environment jsdom
// STORY-474 — About page (gh-#16 · SPEC F207 · PLAN T561 T559)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T561). Each Given comment names the arrange the scenario needs.
//
// next/jest's SWC transform (unlike babel-jest) does not hoist jest.mock() calls above import
// statements (mirrors app-shell.spec.tsx's own header comment), so Sidebar and the page's own
// server component (app/(authed)/about/page.tsx) — both of which call a mocked next/navigation or
// next/headers export — are loaded via a dynamic `await import()` inside their tests or before* hooks.

jest.mock("next/navigation", () => ({
  usePathname: jest.fn(),
  redirect: jest.fn(),
}));

jest.mock("next/headers", () => ({
  cookies: jest.fn(),
}));

jest.mock("@/app/login/actions", () => ({
  logout: jest.fn(),
}));

import { describe, it, jest, expect, beforeAll, afterAll } from "@jest/globals";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { ReactNode } from "react";
import type { redirect, usePathname } from "next/navigation";
import type { cookies } from "next/headers";
import { AboutView } from "../app/(authed)/about/AboutView";
import type { AboutResponseDto } from "../lib/about-api";

const mockedNextNavigation = jest.requireMock<{
  usePathname: typeof usePathname;
  redirect: typeof redirect;
}>("next/navigation");
const mockedUsePathname = mockedNextNavigation.usePathname as jest.MockedFunction<typeof usePathname>;
const mockedRedirect = mockedNextNavigation.redirect as jest.MockedFunction<typeof redirect>;

const mockedCookies = jest
  .requireMock<{ cookies: typeof cookies }>("next/headers")
  .cookies as jest.MockedFunction<typeof cookies>;

// Given: a single fixture GET /api/about response — two attribution kinds, two packs, so AC4's
// "shows every attribution name" fact can't pass vacuously on a one-item list.
const ABOUT_FIXTURE: AboutResponseDto = {
  version: "5.11.0+abc123",
  stationName: "GWAV 108.8",
  tagline: "Your late-night companion",
  libraryCount: 4821,
  uptimeSeconds: 90_000, // 1 day, 1 hour
  attributions: {
    groups: [
      {
        kind: "jingle-pack",
        packs: [
          {
            slug: "gw-first-beds",
            name: "GW First Beds",
            attributions: [
              {
                title: "Night Drive",
                creator: "Jane Doe",
                sourceUrl: "https://example.com/night-drive",
                license: "CC-BY",
              },
            ],
          },
        ],
      },
      {
        kind: "font-pack",
        packs: [
          {
            slug: "about-font-pack",
            name: "About Grotesk",
            attributions: [
              {
                title: "Grotesk Typeface",
                creator: null,
                sourceUrl: "https://fonts.example.com/about-grotesk",
                license: "OFL-1.1",
              },
            ],
          },
        ],
      },
    ],
  },
};

describe("Feature: About page", () => {
  describe("Scenario: the page with a response", () => {
    // Given: /about rendered with version, stationName, libraryCount, uptimeSeconds, attributions
    it("AC4 — shows the version", () => {
      render(<AboutView about={ABOUT_FIXTURE} />);
      expect(screen.getByText(/5\.11\.0\+abc123/)).toBeInTheDocument();
    });

    it("AC4 — shows the station name", () => {
      render(<AboutView about={ABOUT_FIXTURE} />);
      expect(screen.getByText("GWAV 108.8")).toBeInTheDocument();
    });

    it("AC4 — shows the library count", () => {
      render(<AboutView about={ABOUT_FIXTURE} />);
      expect(screen.getByText(/4821/)).toBeInTheDocument();
    });

    it("AC4 — shows the uptime", () => {
      render(<AboutView about={ABOUT_FIXTURE} />);
      expect(screen.getByText(/1 day, 1 hour/)).toBeInTheDocument();
    });

    // One assertion: the rendered pack-name elements' own text, collected in document order,
    // against the expected list — catches a missing OR an extra pack, not just "Night Drive is
    // somewhere" (that string is also an attribution-line title, so a getByText check on it
    // wasn't even proving the PACK name rendered at all).
    it("AC4 — shows every attribution name", () => {
      render(<AboutView about={ABOUT_FIXTURE} />);
      expect(screen.getAllByTestId("attribution-pack-name").map((el) => el.textContent)).toEqual([
        "GW First Beds",
        "About Grotesk",
      ]);
    });
  });

  describe("Scenario: the tagline", () => {
    // Given: Station:Tagline is set (Dean's ruling 2026-09-23)
    it("renders the tagline when set", () => {
      render(<AboutView about={ABOUT_FIXTURE} />);
      expect(screen.getByText("Your late-night companion")).toBeInTheDocument();
    });

    // Given: Station:Tagline is unset ("" on the wire)
    it("is absent when the tagline is blank", () => {
      render(<AboutView about={{ ...ABOUT_FIXTURE, tagline: "" }} />);
      expect(screen.queryByText("Your late-night companion")).not.toBeInTheDocument();
    });

    // Given: Station:Tagline is whitespace-only (a settings-form edge case, not merely "")
    it("is absent when the tagline is whitespace-only", () => {
      render(<AboutView about={{ ...ABOUT_FIXTURE, tagline: "   " }} />);
      expect(screen.queryByTestId("station-tagline")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: attribution source links", () => {
    // Given: an attribution line whose sourceUrl is http(s) — a safe link target
    it("links the credit when sourceUrl is http(s)", () => {
      render(<AboutView about={ABOUT_FIXTURE} />);
      expect(screen.getByRole("link", { name: "Night Drive" })).toHaveAttribute(
        "href",
        "https://example.com/night-drive",
      );
    });

    // Given: an attribution line whose sourceUrl is NOT http(s) — untrusted pack-manifest data,
    // never pointed at from an href (security-web)
    it("renders no link when sourceUrl is not http(s)", () => {
      const unsafeSourceFixture: AboutResponseDto = {
        ...ABOUT_FIXTURE,
        attributions: {
          groups: [
            {
              kind: "jingle-pack",
              packs: [
                {
                  slug: "gw-first-beds",
                  name: "GW First Beds",
                  attributions: [
                    { title: "Night Drive", creator: "Jane Doe", sourceUrl: "javascript:alert(1)", license: "CC-BY" },
                  ],
                },
              ],
            },
          ],
        },
      };

      render(<AboutView about={unsafeSourceFixture} />);

      expect(screen.queryByRole("link", { name: "Night Drive" })).not.toBeInTheDocument();
    });
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

  // The page's own 401/403 handling (SPEC F208.1, STORY-475, PLAN T566) — AboutView's fixture
  // scenarios above don't exercise the server component (app/(authed)/about/page.tsx) itself, so
  // GET /api/about's failure statuses need their own arrange. next/headers' cookies() is loaded
  // via the mock registered above; the page module is loaded via a dynamic `await import()`
  // (same reason as the Sidebar scenario: next/jest's SWC transform does not hoist jest.mock()
  // above import statements).
  describe("Scenario: a stale session (401)", () => {
    let originalFetch: typeof fetch;

    beforeAll(async () => {
      originalFetch = global.fetch;
      mockedCookies.mockResolvedValue(
        { toString: () => "genwave-auth=stale" } as unknown as Awaited<ReturnType<typeof cookies>>
      );
      global.fetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue({
          ok: false,
          status: 401,
          json: () => Promise.resolve({}),
        } as unknown as Response) as unknown as typeof fetch;

      const { default: AboutPage } = await import("../app/(authed)/about/page");
      await AboutPage();
    });

    afterAll(() => {
      global.fetch = originalFetch;
    });

    it("apiGet 401 redirects to /session-expired", () => {
      expect(mockedRedirect).toHaveBeenCalledWith("/session-expired");
    });
  });

  describe("Scenario: a permission error (403)", () => {
    let originalFetch: typeof fetch;
    let node: ReactNode;

    beforeAll(async () => {
      originalFetch = global.fetch;
      mockedCookies.mockResolvedValue(
        { toString: () => "genwave-auth=ok" } as unknown as Awaited<ReturnType<typeof cookies>>
      );
      global.fetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue({
          ok: false,
          status: 403,
          json: () => Promise.resolve({}),
        } as unknown as Response) as unknown as typeof fetch;

      const { default: AboutPage } = await import("../app/(authed)/about/page");
      node = await AboutPage();
    });

    afterAll(() => {
      global.fetch = originalFetch;
    });

    it("apiGet 403 still renders the permission copy", () => {
      render(<>{node}</>);
      expect(screen.getByText("You do not have permission to view this page.")).toBeInTheDocument();
    });
  });

});
