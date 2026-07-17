// @jest-environment jsdom
// STORY-086 — Feedback primitives: toasts, confirm dialogs, skeletons, empty states (Epic Q / SPEC F28.9–F28.10, F28.14)
//
// Runner: Jest (jsdom) + @testing-library/react. Toast auto-dismiss timing
// is driven with fake timers (sonner schedules its own removal via
// setTimeout/requestAnimationFrame, both of which Jest's modern fake-timer
// implementation intercepts). The confirm dialog is exercised through a
// tiny harness component since useConfirm() is only callable from inside
// ConfirmDialogProvider — this mirrors how a real page will call it.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import { useState, type ReactNode } from "react";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { toast, Toaster } from "@/components/ui/toast";
import { ConfirmDialogProvider, useConfirm, type ConfirmOptions } from "@/components/ui/confirm-dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";

const ROOT = path.resolve(__dirname, "..");

/** Recursively lists files under `dir` with one of `exts`, skipping build/dep dirs. */
function collectFiles(dir: string, exts: string[], out: string[] = []): string[] {
  const SKIP = new Set(["node_modules", ".next"]);
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (SKIP.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      collectFiles(full, exts, out);
    } else if (exts.some((ext) => entry.name.endsWith(ext))) {
      out.push(full);
    }
  }
  return out;
}

/** Harness: only useConfirm()'s caller-facing contract (Promise<boolean>) is under test. */
function ConfirmHarness({ options }: { options: ConfirmOptions }): ReactNode {
  const confirm = useConfirm();
  const [result, setResult] = useState<"pending" | "confirmed" | "cancelled">("pending");

  return (
    <div>
      <button
        type="button"
        onClick={() => {
          void confirm(options).then((ok) => setResult(ok ? "confirmed" : "cancelled"));
        }}
      >
        Open confirm
      </button>
      <p>result: {result}</p>
    </div>
  );
}

function renderConfirmHarness(options: ConfirmOptions) {
  return render(
    <ConfirmDialogProvider>
      <ConfirmHarness options={options} />
    </ConfirmDialogProvider>
  );
}

async function openDialog(): Promise<void> {
  const trigger = screen.getByRole("button", { name: "Open confirm" });
  trigger.focus();
  fireEvent.click(trigger);
  await screen.findByRole("dialog");
}

// ---------------------------------------------------------------------------
// Feature: Feedback primitives
// ---------------------------------------------------------------------------

describe("Feature: Feedback primitives", () => {
  describe("Scenario: toast helper renders variants", () => {
    beforeEach(() => {
      jest.useFakeTimers();
    });

    afterEach(() => {
      act(() => {
        jest.runOnlyPendingTimers();
      });
      jest.useRealTimers();
    });

    it("renders a success toast that auto-dismisses", async () => {
      render(<Toaster />);

      act(() => {
        toast.success("Track deleted");
      });
      act(() => {
        jest.advanceTimersByTime(50); // flush sonner's mount rAF
      });

      expect(screen.getByText("Track deleted")).toBeInTheDocument();

      act(() => {
        jest.advanceTimersByTime(4300); // success duration (4000ms) + removal grace
      });

      expect(screen.queryByText("Track deleted")).not.toBeInTheDocument();
    });

    it("renders an error toast that persists longer than success", async () => {
      render(<Toaster />);

      act(() => {
        toast.error("Save failed");
      });
      act(() => {
        jest.advanceTimersByTime(50);
      });

      expect(screen.getByText("Save failed")).toBeInTheDocument();

      // Past the success duration (4000ms) the error toast is still up —
      // proving it persists longer, not just that it has *a* duration.
      act(() => {
        jest.advanceTimersByTime(4300);
      });
      expect(screen.getByText("Save failed")).toBeInTheDocument();

      act(() => {
        jest.advanceTimersByTime(4000); // past the error duration (8000ms total) + grace
      });
      expect(screen.queryByText("Save failed")).not.toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: confirm dialog resolves the caller", () => {
    it("renders title and consequence copy in a modal", async () => {
      renderConfirmHarness({
        title: "Delete library",
        consequence: "This deletes the library. 14 tracks keep playing from the catalog until removed.",
      });

      await openDialog();

      const dialog = screen.getByRole("dialog");
      expect(dialog).toHaveTextContent("Delete library");
      expect(dialog).toHaveTextContent("This deletes the library. 14 tracks keep playing from the catalog until removed.");

      // Accessible name comes from the title, wired via aria-labelledby.
      const title = screen.getByText("Delete library");
      expect(dialog.getAttribute("aria-labelledby")).toBe(title.id);
    });

    it("resolves true on confirm and false on cancel", async () => {
      const view = renderConfirmHarness({ title: "Confirm", consequence: "Are you sure?" });

      await openDialog();
      fireEvent.click(screen.getByRole("button", { name: "Confirm" }));
      expect(await screen.findByText("result: confirmed")).toBeInTheDocument();

      view.unmount();
      renderConfirmHarness({ title: "Confirm", consequence: "Are you sure?" });

      await openDialog();
      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
      expect(await screen.findByText("result: cancelled")).toBeInTheDocument();
    });

    it("cancels on Escape", async () => {
      renderConfirmHarness({ title: "Confirm", consequence: "Are you sure?" });

      await openDialog();
      fireEvent.keyDown(document, { key: "Escape", code: "Escape" });

      expect(await screen.findByText("result: cancelled")).toBeInTheDocument();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    it("traps focus while open and restores it on close", async () => {
      renderConfirmHarness({ title: "Confirm", consequence: "Are you sure?" });

      const trigger = screen.getByRole("button", { name: "Open confirm" });
      await openDialog();

      const dialog = screen.getByRole("dialog");
      expect(dialog).toContainElement(document.activeElement as HTMLElement);

      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
      await screen.findByText("result: cancelled");
      // Radix's FocusScope restores focus in a setTimeout(0) on unmount.
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 0));
      });

      expect(document.activeElement).toBe(trigger);
    });

    it("styles the confirm button destructively when flagged", async () => {
      renderConfirmHarness({
        title: "Delete library",
        consequence: "This deletes the library permanently.",
        destructive: true,
      });

      await openDialog();

      const confirmButton = screen.getByRole("button", { name: "Confirm" });
      expect(confirmButton.className).toMatch(/bg-danger\b/);
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: skeleton and empty state exist", () => {
    it("renders the Skeleton placeholder during fetch", () => {
      render(<Skeleton className="h-4 w-32" />);

      const placeholder = screen.getByRole("status", { name: "Loading" });
      expect(placeholder).toBeInTheDocument();
      expect(placeholder.className).toMatch(/animate-pulse/);
      expect(placeholder.className).toMatch(/motion-reduce:animate-none/);
    });

    it("renders EmptyState with a reason line", () => {
      render(<EmptyState title="Nothing here yet" reason="No tracks match the current filters." />);

      expect(screen.getByText("Nothing here yet")).toBeInTheDocument();
      expect(screen.getByText("No tracks match the current filters.")).toBeInTheDocument();
      expect(screen.queryByRole("button")).not.toBeInTheDocument();
      expect(screen.queryByRole("link")).not.toBeInTheDocument();
    });

    it("renders EmptyState's CTA button when an action is provided", () => {
      const onGenerate = jest.fn();
      render(
        <EmptyState
          title="Safe library is empty"
          reason="Nothing in the safe library yet."
          cta={{ label: "Generate announcement", onClick: onGenerate }}
        />
      );

      fireEvent.click(screen.getByRole("button", { name: "Generate announcement" }));
      expect(onGenerate).toHaveBeenCalledTimes(1);
    });
  });

  // ---------------------------------------------------------------------------
  // SAD PATH
  // ---------------------------------------------------------------------------

  describe("Scenario: rejecting window.confirm (sad path)", () => {
    it("keeps zero window.confirm call sites in the new feedback primitives (app/ call sites are Q6–Q10's job, re-asserted at the Q12 gate)", () => {
      const files = collectFiles(path.join(ROOT, "components", "ui"), [".tsx", ".ts"]);
      const offenders = files
        .filter((f) => readFileSync(f, "utf-8").includes("window.confirm"))
        .map((f) => path.relative(ROOT, f));

      expect(offenders).toEqual([]);
    });
  });
});
