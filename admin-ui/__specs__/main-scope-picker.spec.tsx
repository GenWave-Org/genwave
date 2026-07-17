// @jest-environment jsdom
// STORY-063 — Main rotation scope joins the live settings surface (UI half)
//
// Runner: Jest + jsdom + @testing-library/react. The settings page renders
// Station:Scope:LibraryIds with the K5-style library multi-select (options from
// GET /api/libraries, applyMode="live" badge). Unlike SafeScope's empty-with-confirm,
// an EMPTY main-scope selection is blocked inline — no confirm-dialog override (an empty
// main scope is a silent station, SPEC F23.5). A 400 on the field re-enables the form
// (the K5 stuck-Saving regression class). The out-of-rotation browse badge
// (X-Out-Of-Scope on a named-library browse, SPEC F23.2) rides with this story's UI work.
//
// The settings-page scenarios render SettingsForm directly via @testing-library/react,
// mirroring safe-scope-picker.spec.tsx. The catalog-badge scenario calls the async
// CatalogPage server component directly and walks the returned element tree, mirroring
// catalog-pages.spec.ts (no DOM rendering needed for a server component's output).

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactElement, ReactNode } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";
import type { LibraryDto } from "../lib/library";

// ---------------------------------------------------------------------------
// Fixtures — settings-page scenarios
// ---------------------------------------------------------------------------

const MAIN_SCOPE_KEY = "Station:Scope:LibraryIds";

function makeMainScopeSetting(override: Partial<SettingDto> = {}): SettingDto {
  return {
    key: MAIN_SCOPE_KEY,
    value: "[1]",
    source: "override",
    applyMode: "live",
    kind: "number-list",
    unit: "",
    ...override,
  };
}

function makeLibraries(): LibraryDto[] {
  return [
    { id: 1, name: "Lib Alpha", mediaCount: 10 },
    { id: 2, name: "Lib Beta", mediaCount: 5 },
  ];
}

function makeSettingsFetchMock(
  status: number,
  body: unknown = {}
): jest.MockedFunction<typeof fetch> {
  const fn = jest
    .fn<typeof fetch>()
    .mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
      headers: new Headers({ "content-type": "application/json" }),
    } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/**
 * Change the selection state of a <select multiple> element in jsdom.
 * Sets the given option values as selected and fires a change event so
 * React's synthetic onChange sees the updated selectedOptions.
 */
function selectOptions(select: HTMLSelectElement, selectedValues: string[]): void {
  const selectedSet = new Set(selectedValues);
  Array.from(select.options).forEach((opt) => {
    opt.selected = selectedSet.has(opt.value);
  });
  fireEvent.change(select);
}

/**
 * SettingsForm calls useConfirm() unconditionally (SafeScope-empty save opens
 * the shared modal — SPEC F28.9, window.confirm is gone), so every render needs
 * a ConfirmDialogProvider ancestor. Toaster is mounted alongside it so tests can
 * assert on the toast copy that replaced the shipped inline "Settings saved."
 * status banner.
 */
function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

// ---------------------------------------------------------------------------
// Fixtures — catalog out-of-rotation badge scenario
// ---------------------------------------------------------------------------

jest.mock("next/headers", () => ({
  cookies: jest.fn().mockResolvedValue({
    toString: () => "session=test-cookie",
  }),
}));

function collectStrings(node: ReactNode, out: string[] = []): string[] {
  if (node === null || node === undefined || typeof node === "boolean") {
    return out;
  }
  if (typeof node === "string" || typeof node === "number") {
    out.push(String(node));
    return out;
  }
  if (Array.isArray(node)) {
    for (const child of node) collectStrings(child, out);
    return out;
  }
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props) {
    if (typeof el.props["href"] === "string") {
      out.push(el.props["href"] as string);
    }
    if (el.props["children"] !== undefined) {
      collectStrings(el.props["children"] as ReactNode, out);
    }
  }
  return out;
}

function treeContains(node: ReactNode, text: string): boolean {
  return collectStrings(node).some((s) => s.includes(text));
}

function makeCatalogFetchMock(
  body: unknown,
  status = 200,
  extraHeaders: Record<string, string> = {}
): jest.MockedFunction<typeof fetch> {
  const headers = new Headers({ "content-type": "application/json", ...extraHeaders });
  const fn = jest
    .fn<typeof fetch>()
    .mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
      headers,
    } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

// ---------------------------------------------------------------------------
// Feature: Main rotation scope on the settings page
// ---------------------------------------------------------------------------

describe("Feature: Main rotation scope on the settings page", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    jest.resetModules();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: rendering the scope picker", () => {
    it("renders Station:Scope:LibraryIds as a library multi-select with options from GET /api/libraries", () => {
      renderWithProviders(
        <SettingsForm
          settings={[makeMainScopeSetting()]}
          libraries={makeLibraries()}
        />
      );

      // Labeled field, associated via htmlFor, referencing the configuration key
      expect(screen.getByLabelText(new RegExp(MAIN_SCOPE_KEY))).toBeInTheDocument();

      // Options come from GET /api/libraries — human-readable names, not raw ids
      expect(screen.getByRole("option", { name: "Lib Alpha" })).toBeInTheDocument();
      expect(screen.getByRole("option", { name: "Lib Beta" })).toBeInTheDocument();

      // A multi-select, not a plain text/number input
      const listbox = screen.getByRole("listbox") as HTMLSelectElement;
      expect(listbox.multiple).toBe(true);
    });

    it("shows the applyMode=live badge on the scope field", () => {
      renderWithProviders(
        <SettingsForm
          settings={[makeMainScopeSetting({ applyMode: "live" })]}
          libraries={makeLibraries()}
        />
      );

      expect(screen.getByText(/live/)).toBeInTheDocument();
    });

    it("pre-selects the libraries in the current effective value", () => {
      renderWithProviders(
        <SettingsForm
          settings={[makeMainScopeSetting({ value: "[1]" })]}
          libraries={makeLibraries()}
        />
      );

      const alpha = screen.getByRole("option", { name: "Lib Alpha" }) as HTMLOptionElement;
      expect(alpha.selected).toBe(true);

      const beta = screen.getByRole("option", { name: "Lib Beta" }) as HTMLOptionElement;
      expect(beta.selected).toBe(false);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: submitting a scope change", () => {
    it("submits only the changed setting via PUT /api/settings (W6 changed-fields pattern)", async () => {
      const mockFetch = makeSettingsFetchMock(200);
      const untouchedSetting: SettingDto = {
        key: "Loudness:TargetLufs",
        value: "-16",
        source: "default",
        applyMode: "live",
        kind: "number",
        unit: "LUFS",
      };

      renderWithProviders(
        <SettingsForm
          settings={[makeMainScopeSetting({ value: "[1]" }), untouchedSetting]}
          libraries={makeLibraries()}
        />
      );

      const select = screen.getByRole("listbox") as HTMLSelectElement;
      selectOptions(select, ["1", "2"]);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalledTimes(1);
      });

      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/settings");
      expect(init.method).toBe("PUT");

      const body = JSON.parse(init.body as string) as Array<{ key: string; value: string }>;
      expect(body).toHaveLength(1);
      expect(body[0]).toEqual({ key: MAIN_SCOPE_KEY, value: "[1,2]" });
      expect(body.some((e) => e.key === "Loudness:TargetLufs")).toBe(false);
    });

    it("toasts 'Settings saved.' on success (SPEC F28.9)", async () => {
      makeSettingsFetchMock(200);

      renderWithProviders(
        <SettingsForm
          settings={[makeMainScopeSetting({ value: "[1]" })]}
          libraries={makeLibraries()}
        />
      );

      const select = screen.getByRole("listbox") as HTMLSelectElement;
      selectOptions(select, ["2"]);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Settings saved.")).toBeInTheDocument();
      });
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: rejecting an empty selection", () => {
    it("blocks save with an inline field error when every library is deselected", async () => {
      const mockFetch = makeSettingsFetchMock(200);

      renderWithProviders(
        <SettingsForm
          settings={[makeMainScopeSetting({ value: "[1]" })]}
          libraries={makeLibraries()}
        />
      );

      const select = screen.getByRole("listbox") as HTMLSelectElement;
      selectOptions(select, []);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      // Blocked client-side: no PUT was ever sent
      expect(mockFetch).not.toHaveBeenCalled();

      // An inline field error explains why
      expect(screen.getByRole("alert")).toHaveTextContent(/cannot be empty/i);

      // No success confirmation
      expect(screen.queryByRole("status")).toBeNull();
    });

    it("does NOT offer a confirm-dialog override (unlike SafeScope's empty-with-confirm)", async () => {
      makeSettingsFetchMock(200);

      renderWithProviders(
        <SettingsForm
          settings={[makeMainScopeSetting({ value: "[1]" })]}
          libraries={makeLibraries()}
        />
      );

      const select = screen.getByRole("listbox") as HTMLSelectElement;
      selectOptions(select, []);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      // Unlike SafeScope's useConfirm() modal, main scope never prompts — it just blocks.
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: surviving a save failure", () => {
    it("renders a 400 on the scope field as an inline error", async () => {
      const validationProblem = {
        errors: { settings: ["Station:Scope:LibraryIds must be non-empty"] },
        title: "One or more settings values are invalid.",
        status: 400,
      };
      makeSettingsFetchMock(400, validationProblem);

      renderWithProviders(
        <SettingsForm
          settings={[makeMainScopeSetting({ value: "[1]" })]}
          libraries={makeLibraries()}
        />
      );

      const select = screen.getByRole("listbox") as HTMLSelectElement;
      selectOptions(select, ["2"]);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("alert")).toBeInTheDocument();
        expect(
          screen.getByText("Station:Scope:LibraryIds must be non-empty")
        ).toBeInTheDocument();
      });

      expect(screen.queryByRole("status")).toBeNull();
    });

    it("re-enables the picker and Save button after the 400 (K5 stuck-Saving regression class)", async () => {
      const validationProblem = {
        errors: { settings: ["Station:Scope:LibraryIds must be non-empty"] },
        title: "One or more settings values are invalid.",
        status: 400,
      };
      makeSettingsFetchMock(400, validationProblem);

      renderWithProviders(
        <SettingsForm
          settings={[makeMainScopeSetting({ value: "[1]" })]}
          libraries={makeLibraries()}
        />
      );

      const select = screen.getByRole("listbox") as HTMLSelectElement;
      selectOptions(select, ["2"]);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save settings/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("alert")).toBeInTheDocument();
      });

      // The stuck-"Saving…" regression: after a 400 the form must re-enable so the
      // operator can correct and retry without a full page reload.
      expect(select).not.toBeDisabled();
      const saveButton = screen.getByRole("button", { name: /save settings/i });
      expect(saveButton).not.toBeDisabled();
      expect(saveButton).toHaveTextContent(/save settings/i);
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: out-of-rotation browse badge", () => {
    it("shows an 'out of rotation' badge when a named-library browse returns X-Out-Of-Scope: true", async () => {
      makeCatalogFetchMock([], 200, {
        "x-pagination": "total=0,pages=1,page=1,limit=50",
        "x-out-of-scope": "true",
      });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({
        searchParams: Promise.resolve({ "library-id": "2" }),
      });

      expect(treeContains(node, "Out of rotation")).toBe(true);
    });

    it("shows no badge on an in-scope named-library browse", async () => {
      makeCatalogFetchMock([], 200, {
        "x-pagination": "total=0,pages=1,page=1,limit=50",
      });

      const { default: CatalogPage } = await import("../app/(authed)/catalog/page");
      const node = await CatalogPage({
        searchParams: Promise.resolve({ "library-id": "1" }),
      });

      expect(treeContains(node, "Out of rotation")).toBe(false);
    });
  });
});
