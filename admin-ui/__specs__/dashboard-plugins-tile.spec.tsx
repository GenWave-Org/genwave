// @jest-environment jsdom
// SPEC F156.7, STORY-385/386 AC3, PLAN T394 — the dashboard's Plugins tile.
//
// Runner: Jest (jsdom) + @testing-library/react — the safe-scope-tile.spec.tsx/dashboard-gardener-tile.spec.tsx
// idiom: StatusTiles is a pure, prop-driven presentational component, rendered directly with a built
// `status` prop rather than standing up DashboardView's own fetch mock.
//
// T394 review MEDIUM-3: every sibling tile has a spec; this one was missing.

import { describe, it, expect, afterEach, jest } from "@jest/globals";
import { render, screen, cleanup } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { PluginStatusEntry, StatusResponse } from "@/lib/broadcast-api";

function makeStatus(plugins?: PluginStatusEntry[]): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 5 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    voice: { engine: "kokoro", degraded: false, reason: null, checkedAt: null },
    plugins,
  };
}

function queryPluginsTile(): HTMLElement | null {
  return screen.queryByRole("group", { name: "Plugins" });
}

afterEach(() => {
  cleanup();
  jest.restoreAllMocks();
});

describe("Feature: the Plugins tile on the dashboard (SPEC F156.7, STORY-385/386 AC3)", () => {
  describe("Scenario: the closed door shows nothing (AC3)", () => {
    it("renders no tile when plugins is an empty array", () => {
      render(<StatusTiles status={makeStatus([])} error={false} />);

      expect(queryPluginsTile()).not.toBeInTheDocument();
    });

    it("renders no tile when plugins is absent from the wire (pre-T394 fixture compatibility)", () => {
      render(<StatusTiles status={makeStatus(undefined)} error={false} />);

      expect(queryPluginsTile()).not.toBeInTheDocument();
    });

    it("renders nothing while the first poll is still in flight", () => {
      render(<StatusTiles status={null} error={false} />);

      expect(queryPluginsTile()).not.toBeInTheDocument();
    });
  });

  describe("Scenario: a loaded and a skipped plugin (AC2)", () => {
    function loadedAndSkipped(): PluginStatusEntry[] {
      return [
        { name: "Dice Roll Example Plugin", version: "1.0.0", contracts: ["IContextProvider"], state: "loaded" },
        {
          name: "Broken Plugin",
          version: null,
          contracts: [],
          state: "skipped",
          reason: "AssemblyFileMissing: assembly file \"Missing.dll\" does not exist",
        },
      ];
    }

    it("renders the tile once plugins is non-empty", () => {
      render(<StatusTiles status={makeStatus(loadedAndSkipped())} error={false} />);

      expect(queryPluginsTile()).toBeInTheDocument();
    });

    it("counts only loaded plugins in the headline — the skipped one never inflates it", () => {
      render(<StatusTiles status={makeStatus(loadedAndSkipped())} error={false} />);

      expect(queryPluginsTile()).toHaveTextContent("1 plugin loaded");
    });

    it("names the loaded plugin by its own name and version", () => {
      render(<StatusTiles status={makeStatus(loadedAndSkipped())} error={false} />);

      expect(queryPluginsTile()).toHaveTextContent("Dice Roll Example Plugin 1.0.0");
    });

    it("lists the skipped plugin's own reason on the tile — never hover-only", () => {
      render(<StatusTiles status={makeStatus(loadedAndSkipped())} error={false} />);

      const tile = queryPluginsTile();
      expect(tile).toHaveTextContent("skipped");
      expect(tile).toHaveTextContent("AssemblyFileMissing");
    });

    it("flags the tile warning-styled when any plugin is skipped", () => {
      render(<StatusTiles status={makeStatus(loadedAndSkipped())} error={false} />);

      expect(queryPluginsTile()?.className).toMatch(/\bborder-danger\b/);
    });
  });

  describe("Scenario: singular/plural caption (AC2)", () => {
    it("reads '1 plugin loaded' for exactly one loaded plugin", () => {
      render(
        <StatusTiles
          status={makeStatus([
            { name: "Solo Plugin", version: "1.0.0", contracts: ["IContextProvider"], state: "loaded" },
          ])}
          error={false}
        />
      );

      expect(queryPluginsTile()).toHaveTextContent("1 plugin loaded");
    });

    it("reads '2 plugins loaded' for more than one loaded plugin", () => {
      render(
        <StatusTiles
          status={makeStatus([
            { name: "Plugin A", version: "1.0.0", contracts: ["IContextProvider"], state: "loaded" },
            { name: "Plugin B", version: "2.0.0", contracts: ["IContextProvider"], state: "loaded" },
          ])}
          error={false}
        />
      );

      const tile = queryPluginsTile();
      expect(tile).toHaveTextContent("2 plugins loaded");
      expect(tile).not.toHaveTextContent("2 plugin loaded");
    });

    it("renders 'ok' (success) styling, never warning, when every plugin loaded cleanly", () => {
      render(
        <StatusTiles
          status={makeStatus([
            { name: "Plugin A", version: "1.0.0", contracts: ["IContextProvider"], state: "loaded" },
          ])}
          error={false}
        />
      );

      const tile = queryPluginsTile();
      expect(tile?.className).toMatch(/\bborder-success\b/);
      expect(tile?.className).not.toMatch(/\bborder-danger\b/);
    });
  });
});
