// @jest-environment jsdom
// STORY-572 — The Talk rules page (SPEC F220.27 · PLAN T804)

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, waitFor, within, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { TalkRulesClient } from "../app/(authed)/talk-rules/TalkRulesClient";
import { NAV_GROUPS } from "../app/(authed)/_components/nav-items";
import type { TalkRuleDto, TalkRulesMode } from "../lib/talk-rules-api";

const RULES: TalkRuleDto[] = [
  {
    id: "no-countdowns",
    name: "No countdowns",
    whatItDoes: "Stops the DJ counting down out loud.",
    why: "It sounds stiff.",
    example: "In three, two, one.",
    canCover: ["dj-line", "chat"],
    switches: { "dj-line": false, chat: true },
    flags7d: { "dj-line": 3, chat: 1 },
    broken7d: { "dj-line": 1, chat: 1 },
  },
  {
    id: "ad-phone",
    name: "Ad phone numbers",
    whatItDoes: "Keeps phone numbers out of ads.",
    why: "",
    example: "",
    canCover: ["ad"],
    switches: { ad: true },
    flags7d: { ad: 2 },
    broken7d: {},
  },
];

type FetchFn = typeof fetch;
let fetchMock: jest.MockedFunction<FetchFn>;
const realFetch = global.fetch;

function json(body: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => body } as unknown as Response;
}

function renderPage(mode: TalkRulesMode | null = "shadow"): void {
  render(<TalkRulesClient initialRules={RULES} mode={mode} />);
}

function ruleItems(): HTMLElement[] {
  return screen.getAllByRole("heading", { level: 2 }).map((h) => h.closest("li")!);
}

/** The text of each rule's paragraph that starts with `prefix`, in page order. */
function paragraphsStartingWith(prefix: string): string[] {
  return ruleItems().map(
    (item) => Array.from(item.querySelectorAll("p")).find((p) => p.textContent?.startsWith(prefix))?.textContent ?? ""
  );
}

async function click(element: HTMLElement): Promise<void> {
  await act(async () => {
    fireEvent.click(element);
  });
}

function calls(method: string): string[] {
  return fetchMock.mock.calls
    .filter(([, init]) => (init?.method ?? "GET") === method)
    .map(([url]) => String(url));
}

beforeEach(() => {
  fetchMock = jest.fn<FetchFn>();
  global.fetch = fetchMock;
});

afterEach(() => {
  global.fetch = realFetch;
});

describe("Feature: The Talk rules page", () => {
  describe("Scenario: the rules are listed", () => {
    beforeEach(() => renderPage());

    it("shows each rule's name", () => {
      expect(screen.getAllByRole("heading", { level: 2 }).map((h) => h.textContent)).toEqual([
        "No countdowns",
        "Ad phone numbers",
      ]);
    });

    it("shows what each rule does", () => {
      expect(paragraphsStartingWith("")).toEqual([
        "Stops the DJ counting down out loud.",
        "Keeps phone numbers out of ads.",
      ]);
    });

    it("shows one switch per kind of talk the rule can cover", () => {
      expect(screen.getAllByRole("switch").map((s) => s.getAttribute("aria-label"))).toEqual([
        "No countdowns: DJ lines",
        "No countdowns: Chats",
        "Ad phone numbers: Ads",
      ]);
    });

    it("shows how many lines each rule flagged in the last 7 days", () => {
      expect(paragraphsStartingWith("Flagged")).toEqual([
        "Flagged 4 lines in the last 7 days (DJ lines 3, Chats 1).",
        "Flagged 2 lines in the last 7 days.",
      ]);
    });
  });

  describe("Scenario: a switch is clicked", () => {
    beforeEach(async () => {
      fetchMock.mockResolvedValue(json({ id: "no-countdowns", kind: "chat", enabled: false }));
      renderPage();
      await click(screen.getByRole("switch", { name: "No countdowns: Chats" }));
    });

    it("sends the PUT for that rule and kind", () => {
      expect(calls("PUT")).toEqual(["/api/talk-rules/no-countdowns/chat"]);
    });

    it("sends the new state in the PUT body", () => {
      const init = fetchMock.mock.calls.find(([, i]) => i?.method === "PUT")?.[1];

      expect(JSON.parse(String(init?.body))).toEqual({ enabled: false });
    });

    it("shows the switch in its new state", async () => {
      await waitFor(() => expect(screen.getByRole("switch", { name: "No countdowns: Chats" })).not.toBeChecked());
    });
  });

  describe("Scenario: a rule is opened", () => {
    beforeEach(async () => {
      fetchMock.mockResolvedValue(
        json([{ checkId: 7, when: "2026-11-20T10:00:00Z", dj: "Flip", kind: "dj-line", words: "In three two one", outcome: "re-asked" }])
      );
      renderPage();
      await click(screen.getAllByRole("button", { name: "Show flagged lines" })[0]!);
    });

    it("shows the DJ of each flagged line", async () => {
      expect(await screen.findByText(/Flip/)).toBeInTheDocument();
    });

    it("shows the words of each flagged line", async () => {
      expect(await screen.findByText("In three two one")).toBeInTheDocument();
    });

    it("shows the outcome of each flagged line", async () => {
      expect(await screen.findByText(/Asked again/)).toBeInTheDocument();
    });

    it("shows the kind of each flagged line", async () => {
      expect(await screen.findByText(/DJ lines ·/)).toBeInTheDocument();
    });
  });

  describe("Scenario: checking against last week", () => {
    it("shows each rule's replay count", async () => {
      fetchMock.mockResolvedValue(
        json({
          counts: [
            { ruleId: "no-countdowns", checked: 40, flagged: 9 },
            { ruleId: "ad-phone", checked: 25, flagged: 4 },
          ],
          changes: [],
          trimSavedLines: 0,
        })
      );
      renderPage();
      await click(screen.getByRole("button", { name: "Check against last week" }));

      await screen.findAllByText(/Checked against last week/);

      expect(paragraphsStartingWith("Checked against last week")).toEqual([
        "Checked against last week: 9 lines flagged.",
        "Checked against last week: 4 lines flagged.",
      ]);
    });
  });

  describe("Scenario: today's checks still decide", () => {
    const NOTE = "Today's checks still decide what airs. Switching a rule here does not change what airs yet.";

    it("says today's checks still decide what airs in shadow", () => {
      renderPage("shadow");

      expect(screen.getByText(NOTE)).toBeInTheDocument();
    });

    it("says today's checks still decide what airs in legacy", () => {
      renderPage("legacy");

      expect(screen.getByText(NOTE)).toBeInTheDocument();
    });

    it("shows no such note in mode rules", () => {
      renderPage("rules");

      expect(screen.queryByText(NOTE)).not.toBeInTheDocument();
    });
  });

  describe("Scenario: saving a switch fails", () => {
    beforeEach(async () => {
      fetchMock.mockResolvedValue(json({ detail: "boom" }, 500));
      renderPage();
      await click(screen.getByRole("switch", { name: "No countdowns: Chats" }));
    });

    it("returns the switch to its old state", async () => {
      await waitFor(() => expect(screen.getByRole("switch", { name: "No countdowns: Chats" })).toBeChecked());
    });

    it("shows an error", async () => {
      expect(await screen.findByRole("alert")).toHaveTextContent("Could not save that switch");
    });
  });

  describe("Scenario: a kind the rule can't cover", () => {
    it("shows no switch for that kind", () => {
      renderPage();
      const adRule = screen.getByRole("heading", { name: "Ad phone numbers" }).closest("li")!;

      expect(within(adRule).queryByRole("switch", { name: /DJ lines/ })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: a rule could not run (T804a)", () => {
    beforeEach(() => renderPage());

    it("shows how many times, only for a rule where N is above zero", () => {
      expect(paragraphsStartingWith("Could not run")).toEqual(["Could not run 2 times in the last 7 days.", ""]);
    });
  });

  describe("Scenario: a rule could not run once (T804a)", () => {
    beforeEach(() => {
      render(<TalkRulesClient initialRules={[{ ...RULES[0]!, broken7d: { chat: 1 } }]} mode="shadow" />);
    });

    it("says time, not times", () => {
      expect(paragraphsStartingWith("Could not run")).toEqual(["Could not run 1 time in the last 7 days."]);
    });
  });

  describe("Scenario: the nav entry", () => {
    it("lists Talk rules in the Station group", () => {
      const item = NAV_GROUPS.find((g) => g.id === "station")?.items.find((i) => i.label === "Talk rules");

      expect(item?.href).toBe("/talk-rules");
    });
  });
});
