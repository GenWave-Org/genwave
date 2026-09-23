// @jest-environment jsdom
// STORY-473 — Toasts mount on the built image (gh-#765 · SPEC F206 · PLAN T568 T569)
//
// BDD specification — Jest. RED at plan time: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T568–T569). Each Given comment names the arrange the scenario needs.

import { describe, it, expect, beforeAll, beforeEach } from "@jest/globals";
import { render, screen, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { Toaster, toast } from "@/components/ui/toast";

const ROOT = path.resolve(__dirname, "..");

/** Recursively lists files under `dir`, skipping build/dep/spec dirs (adapted from
 * design-system-foundation.spec.ts / feedback-primitives.spec.tsx's own walker). */
function collectFiles(dir: string, out: string[] = []): string[] {
  const SKIP = new Set(["node_modules", ".next", "__specs__", ".git"]);
  const SOURCE_EXTENSIONS = [".ts", ".tsx", ".js", ".jsx", ".mjs"];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (SKIP.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      collectFiles(full, out);
    } else if (SOURCE_EXTENSIONS.some((extension) => entry.name.endsWith(extension))) {
      out.push(full);
    }
  }
  return out;
}

// Catches `from "sonner"`, side-effect `import "sonner"`, dynamic `import("sonner")`,
// `require("sonner")`, and subpaths like `"sonner/x"`.
const SONNER_IMPORT_PATTERN = /(?:\bfrom\s*|\bimport\s*\(?\s*|\brequire\s*\(\s*)["']sonner(?:\/[^"']*)?["']/;

describe("Feature: Toasts mount on the built image", () => {
  describe("Scenario: the built bundle", () => {
    // Given: package-lock.json's dependency graph, and every source file under app/, lib/,
    // components/, middleware.ts — read once, hermetically (no `next build` involved: a duplicate
    // resolution is a lockfile fact, not a bundler-output fact).
    let sonnerPackageKeys: string[];
    let sonnerImporters: string[];

    beforeAll(() => {
      const lockPath = path.join(ROOT, "package-lock.json");
      const lock = JSON.parse(readFileSync(lockPath, "utf-8")) as { packages: Record<string, unknown> };
      sonnerPackageKeys = Object.keys(lock.packages).filter((key) => key.endsWith("node_modules/sonner"));

      const sourceFiles = [
        ...collectFiles(path.join(ROOT, "app")),
        ...collectFiles(path.join(ROOT, "lib")),
        ...collectFiles(path.join(ROOT, "components")),
        path.join(ROOT, "middleware.ts"),
      ];
      sonnerImporters = sourceFiles
        .filter((file) => SONNER_IMPORT_PATTERN.test(readFileSync(file, "utf-8")))
        .map((file) => path.relative(ROOT, file));
    });

    it("AC2 — exactly one copy of the sonner module is present", () => {
      expect(sonnerPackageKeys).toHaveLength(1);
    });

    it("AC2 — the wrapper is the only caller, so every mount shares its one sonner instance", () => {
      expect(sonnerImporters).toEqual(["components/ui/toast.tsx"]);
    });
  });

  describe("Scenario: a jsdom mount", () => {
    // Given: <Toaster/> rendered through the production wrapper, toast.success("saved") called.
    let savedText: HTMLElement;

    beforeEach(async () => {
      render(<Toaster />);
      await act(async () => {
        toast.success("saved");
      });
      savedText = await screen.findByText("saved");
    });

    it('AC4 — "saved" is in the document', () => {
      expect(savedText).toBeInTheDocument();
    });

    it("AC4 — the toast node carries data-sonner-toast, the Playwright probe's selector", () => {
      expect(savedText.closest("li")).toHaveAttribute("data-sonner-toast");
    });
  });

  describe("Scenario: the production image (Playwright, recorded in T568/T569)", () => {
    // Recorded in docs/PLAN.md T568/T569 — Playwright against the built image, not jest.
    it.todo("AC1 — the repro report records [data-sonner-toaster] presence and the console");
    // Recorded in docs/PLAN.md T568/T569 — Playwright against the built image, not jest.
    it.todo("AC3 — after the fix the toast node is found on the rebuilt image");
  });

});
