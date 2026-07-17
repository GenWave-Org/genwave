// @jest-environment jsdom
// STORY-090 — Track detail: edit, move, reanalyze in the new identity (Epic Q / SPEC F28.5, F28.9)
//
// Runner: Jest (jsdom) + @testing-library/react + mocked fetch. Mirrors catalog-selection-toolbar
// .spec.tsx's harness pattern: useConfirm()/toast need their providers, so every render wraps in
// ConfirmDialogProvider and mounts Toaster; router.refresh() is exercised via a mocked
// next/navigation useRouter (usePathname is mocked the same way for the Breadcrumbs scenario).

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
  usePathname: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ComponentProps, ReactElement } from "react";
import type { useRouter, usePathname } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { LibraryDto } from "@/lib/library";
import type { EditableTrackFields } from "../app/(authed)/catalog/[mediaId]/EditTrackForm";
import type { MoveToLibraryAction } from "../app/(authed)/catalog/[mediaId]/MoveToLibraryAction";
import { ReanalyzePanel } from "../app/(authed)/catalog/[mediaId]/ReanalyzePanel";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;
const mockedUsePathname = jest
  .requireMock<{ usePathname: typeof usePathname }>("next/navigation")
  .usePathname as jest.MockedFunction<typeof usePathname>;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const MEDIA_ID = "abc-123";
const ETAG = 'W/"abc-etag-1"';

const LIBRARIES: LibraryDto[] = [
  { id: 1, name: "In Rotation", mediaCount: 50 },
  { id: 2, name: "Archive", mediaCount: 20 },
];

function makeInitialValues(overrides: Partial<EditableTrackFields> = {}): EditableTrackFields {
  return {
    title: "Test Track",
    artist: "Test Artist",
    album: "Test Album",
    genre: "Rock",
    year: 2024,
    eligible: true,
    ...overrides,
  };
}

function makeFetchMock(status: number, body: unknown = {}): jest.MockedFunction<typeof fetch> {
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

function renderWithProviders(node: ReactElement) {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/**
 * Dynamically imports EditTrackForm and Breadcrumbs/BreadcrumbTitle — next/jest's SWC transform
 * hoists every static ES import to the top of the module ahead of the jest.mock() call above, so a
 * static import of a component that itself imports next/navigation would bind to the real
 * (unmocked) module (mirrors edit-track.spec.tsx / app-shell.spec.tsx's pattern). MoveToLibraryAction
 * and ReanalyzePanel don't touch next/navigation, so it stays statically imported above.
 * MoveToLibraryAction now calls useRouter() too (R9, SPEC F31.3 — refresh the row on a conflict),
 * so it gets the same dynamic-import treatment via renderMoveToLibraryAction below.
 */
async function renderEditTrackForm(props: {
  mediaId: string;
  initialValues: EditableTrackFields;
  etag: string;
}) {
  const { EditTrackForm } = await import("../app/(authed)/catalog/[mediaId]/EditTrackForm");
  return renderWithProviders(<EditTrackForm {...props} />);
}

async function renderMoveToLibraryAction(props: ComponentProps<typeof MoveToLibraryAction>) {
  const { MoveToLibraryAction: Component } = await import(
    "../app/(authed)/catalog/[mediaId]/MoveToLibraryAction"
  );
  return renderWithProviders(<Component {...props} />);
}

async function renderBreadcrumbTrail(title?: string) {
  const { Breadcrumbs } = await import("../app/(authed)/_components/Breadcrumbs");
  const { BreadcrumbTitle, BreadcrumbTitleProvider } = await import(
    "../app/(authed)/_components/BreadcrumbTitle"
  );
  return render(
    <BreadcrumbTitleProvider>
      <Breadcrumbs />
      {title !== undefined && <BreadcrumbTitle title={title} />}
    </BreadcrumbTitleProvider>
  );
}

// ---------------------------------------------------------------------------
// Feature: Track detail redesign
// ---------------------------------------------------------------------------

describe("Feature: Track detail redesign", () => {
  let originalFetch: typeof fetch;
  let refreshMock: jest.Mock;

  beforeEach(() => {
    originalFetch = global.fetch;
    refreshMock = jest.fn();
    mockedUseRouter.mockReturnValue({ refresh: refreshMock } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: edit form saves with a toast", () => {
    it("PATCHes with If-Match as shipped", async () => {
      const mockFetch = makeFetchMock(204);
      await renderEditTrackForm({ mediaId: MEDIA_ID, initialValues: makeInitialValues(), etag: ETAG });

      fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Updated Title" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe(`/api/media/${MEDIA_ID}`);
      expect(init.method).toBe("PATCH");
      const headers = init.headers as Record<string, string>;
      expect(headers["If-Match"]).toBe(ETAG);
      expect(JSON.parse(init.body as string)).toMatchObject({ title: "Updated Title" });
    });

    it("toasts success and reflects the new ETag (PATCH returns none, so the page is refreshed)", async () => {
      makeFetchMock(204);
      await renderEditTrackForm({ mediaId: MEDIA_ID, initialValues: makeInitialValues(), etag: ETAG });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Saved.")).toBeInTheDocument();
      });
      // PATCH never returns an ETag — router.refresh() re-fetches the server component so the
      // next save carries the row's actual current ETag.
      expect(refreshMock).toHaveBeenCalledTimes(1);
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: cross-scope move confirms with the warning", () => {
    it("renders the X-Out-Of-Scope allow-and-warn as a dialog/toast pair", async () => {
      makeFetchMock(200, { outOfScope: true });

      await renderMoveToLibraryAction({
        mediaId: MEDIA_ID,
        etag: ETAG,
        currentLibraryId: 1,
        libraries: LIBRARIES,
        scopeLibraryIds: [1], // only lib 1 is in scope — lib 2 triggers the pre-submit dialog
      });

      fireEvent.change(screen.getByLabelText("Destination library"), { target: { value: "2" } });
      fireEvent.click(screen.getByRole("button", { name: /move/i }));

      // Pre-submit confirm dialog (useConfirm — window.confirm is gone).
      const dialog = await screen.findByRole("dialog");
      expect(dialog).toHaveTextContent("not in rotation");

      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Move" }));
        await Promise.resolve();
      });

      // Response-driven allow-and-warn notice lands as a toast, not inline text.
      await waitFor(() => {
        expect(screen.getByText("Library updated. This track has left rotation.")).toBeInTheDocument();
      });
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: reanalyze panel toasts the 202", () => {
    it("toasts the accepted re-enrichment", async () => {
      makeFetchMock(202, null);
      renderWithProviders(<ReanalyzePanel mediaId={MEDIA_ID} tagsEditedAt={null} />);

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /re-analyze/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Re-analysis scheduled — will complete in a few ticks.")).toBeInTheDocument();
      });
    });

    it("disables the panel briefly after triggering", async () => {
      makeFetchMock(202, null);
      renderWithProviders(<ReanalyzePanel mediaId={MEDIA_ID} tagsEditedAt={null} />);

      const button = screen.getByRole("button", { name: /re-analyze/i });
      await act(async () => {
        fireEvent.click(button);
        await Promise.resolve();
      });

      await waitFor(() => expect(button).toBeDisabled());

      await waitFor(() => expect(button).not.toBeDisabled(), { timeout: 3000 });
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: breadcrumb context", () => {
    it("renders the shell breadcrumb as Catalog → <track title>", async () => {
      mockedUsePathname.mockReturnValue(`/catalog/${MEDIA_ID}`);

      await renderBreadcrumbTrail("My Favorite Track");

      const trail = screen.getByRole("navigation", { name: /breadcrumb/i });
      expect(trail).toHaveTextContent("Catalog");
      expect(trail).toHaveTextContent("My Favorite Track");
      expect(trail).not.toHaveTextContent(MEDIA_ID);
    });

    it("falls back to the raw mediaId when no title is claimed", async () => {
      mockedUsePathname.mockReturnValue(`/catalog/${MEDIA_ID}`);

      await renderBreadcrumbTrail();

      expect(screen.getByRole("navigation", { name: /breadcrumb/i })).toHaveTextContent(MEDIA_ID);
    });
  });

  // ---------------------------------------------------------------------------
  // SAD PATH
  // ---------------------------------------------------------------------------

  describe("Scenario: rejecting a stale edit (sad path)", () => {
    it("explains the 409 conflict in an error toast with a reload offer", async () => {
      makeFetchMock(409);
      await renderEditTrackForm({ mediaId: MEDIA_ID, initialValues: makeInitialValues(), etag: ETAG });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("This track changed elsewhere — your edits are unsaved.")).toBeInTheDocument();
      });
      // The reload offer is an adjacent, persistent affordance (the toast itself auto-dismisses).
      expect(screen.getByRole("alert")).toHaveTextContent("reload");
      expect(screen.getByRole("button", { name: "Reload" })).toBeInTheDocument();
    });

    it("does not silently drop the operator's edits", async () => {
      makeFetchMock(409);
      await renderEditTrackForm({ mediaId: MEDIA_ID, initialValues: makeInitialValues(), etag: ETAG });

      const titleInput = screen.getByLabelText("Title");
      fireEvent.change(titleInput, { target: { value: "Unsaved Edit" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByRole("button", { name: "Reload" })).toBeInTheDocument();
      });

      // The typed value is still in the field — the conflict never reset the form.
      expect(screen.getByLabelText("Title")).toHaveValue("Unsaved Edit");
    });
  });
});
