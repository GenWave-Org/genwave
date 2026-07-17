"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import type { LibraryDto } from "@/lib/library";

export type { LibraryDto };

interface LibrariesTabProps {
  initialLibraries: LibraryDto[];
}

type CreateResult = { ok: true; data: LibraryDto } | { ok: false; error: string };
type MutateResult = { ok: true } | { ok: false; error: string };

async function apiCreateLibrary(name: string): Promise<CreateResult> {
  const resp = await fetch("/api/libraries", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
  });
  if (resp.status === 201) {
    const data = (await resp.json()) as LibraryDto;
    return { ok: true, data };
  }
  if (resp.status === 409) {
    return { ok: false, error: "A library with that name already exists." };
  }
  if (resp.status === 400) {
    return { ok: false, error: "Invalid library name." };
  }
  return { ok: false, error: `Server error (${resp.status})` };
}

async function apiRenameLibrary(id: number, name: string): Promise<MutateResult> {
  const resp = await fetch(`/api/libraries/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
  });
  if (resp.ok) {
    return { ok: true };
  }
  if (resp.status === 409) {
    return { ok: false, error: "A library with that name already exists." };
  }
  if (resp.status === 400) {
    return { ok: false, error: "Invalid library name." };
  }
  return { ok: false, error: `Server error (${resp.status})` };
}

async function apiDeleteLibrary(id: number): Promise<MutateResult> {
  const resp = await fetch(`/api/libraries/${id}`, { method: "DELETE" });
  if (resp.status === 204) {
    return { ok: true };
  }
  if (resp.status === 409) {
    try {
      const body = (await resp.json()) as Record<string, unknown>;
      const count = typeof body["dependentMediaCount"] === "number" ? body["dependentMediaCount"] : null;
      if (count !== null) {
        return { ok: false, error: `Library has ${count} track${count === 1 ? "" : "s"} — reassign them first.` };
      }
    } catch {
      // malformed body — fall through
    }
    return { ok: false, error: "Cannot delete: library is not empty." };
  }
  return { ok: false, error: `Server error (${resp.status})` };
}

/**
 * The Libraries tab of the Catalog page (SPEC F28.11, STORY-089 AC4) — the CRUD content
 * migrated verbatim from the retired standalone `/libraries` page. The only behavior change
 * is the feedback layer: the browser's native confirm prompt on delete becomes `useConfirm()`,
 * and inline error paragraphs (including the 409 `dependentMediaCount` message) become toasts
 * (F28.9).
 */
export function LibrariesTab({ initialLibraries }: LibrariesTabProps): ReactNode {
  const confirm = useConfirm();
  const [libraries, setLibraries] = useState<LibraryDto[]>(initialLibraries);

  const [createName, setCreateName] = useState("");
  const [isCreating, setIsCreating] = useState(false);

  const [editingId, setEditingId] = useState<number | null>(null);
  const [editName, setEditName] = useState("");
  const [isSavingEdit, setIsSavingEdit] = useState(false);

  const [deletingId, setDeletingId] = useState<number | null>(null);

  const isCreateBlank = createName.trim() === "";

  async function handleCreate(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    if (isCreateBlank) return;

    const name = createName.trim();
    setIsCreating(true);
    const result = await apiCreateLibrary(name);
    if (result.ok) {
      setLibraries((prev) => [...prev, { ...result.data, mediaCount: 0 }]);
      setCreateName("");
      toast.success(`"${name}" created.`);
    } else {
      toast.error(result.error);
    }
    setIsCreating(false);
  }

  function startEdit(lib: LibraryDto): void {
    setEditingId(lib.id);
    setEditName(lib.name);
  }

  function cancelEdit(): void {
    setEditingId(null);
    setEditName("");
  }

  async function handleRename(id: number): Promise<void> {
    const name = editName.trim();
    if (name === "") return;

    setIsSavingEdit(true);
    const result = await apiRenameLibrary(id, name);
    if (result.ok) {
      setLibraries((prev) => prev.map((lib) => (lib.id === id ? { ...lib, name } : lib)));
      setEditingId(null);
      setEditName("");
      toast.success(`Renamed to "${name}".`);
    } else {
      toast.error(result.error);
    }
    setIsSavingEdit(false);
  }

  async function handleDelete(lib: LibraryDto): Promise<void> {
    const ok = await confirm({
      title: "Delete library",
      consequence: `Delete "${lib.name}"? This only succeeds while the library is empty — non-empty libraries are refused (with the track count) rather than deleted.`,
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!ok) return;

    setDeletingId(lib.id);
    const result = await apiDeleteLibrary(lib.id);
    if (result.ok) {
      setLibraries((prev) => prev.filter((l) => l.id !== lib.id));
      toast.success(`"${lib.name}" deleted.`);
    } else {
      toast.error(result.error);
    }
    setDeletingId(null);
  }

  return (
    <section aria-label="Libraries">
      {libraries.length === 0 ? (
        <p className="text-[0.85rem] text-mute">No libraries found.</p>
      ) : (
        <ul className="divide-y divide-line rounded-[6px] border border-line bg-surface">
          {libraries.map((lib) => (
            <li key={lib.id} data-library-id={lib.id} className="flex items-center justify-between gap-3 px-4 py-3">
              {editingId === lib.id ? (
                <>
                  <input
                    aria-label="Library name"
                    type="text"
                    value={editName}
                    onChange={(e) => setEditName(e.currentTarget.value)}
                    disabled={isSavingEdit}
                    className="h-9 flex-1 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink"
                  />
                  <div className="flex shrink-0 gap-2">
                    <Button
                      type="button"
                      onClick={() => {
                        void handleRename(lib.id);
                      }}
                      disabled={isSavingEdit || editName.trim() === ""}
                    >
                      Save name
                    </Button>
                    <Button type="button" variant="secondary" onClick={cancelEdit} disabled={isSavingEdit}>
                      Cancel
                    </Button>
                  </div>
                </>
              ) : (
                <>
                  <div>
                    <span data-testid="library-name" className="font-semibold text-ink">
                      {lib.name}
                    </span>{" "}
                    <span data-testid="library-media-count" className="text-[0.8rem] text-mute">
                      ({lib.mediaCount} track{lib.mediaCount === 1 ? "" : "s"})
                    </span>
                  </div>
                  <div className="flex shrink-0 gap-2">
                    <Button type="button" variant="secondary" onClick={() => startEdit(lib)}>
                      Edit
                    </Button>
                    <Button
                      type="button"
                      variant="secondary"
                      aria-label={`Delete ${lib.name}`}
                      onClick={() => {
                        void handleDelete(lib);
                      }}
                      disabled={deletingId === lib.id}
                    >
                      Delete
                    </Button>
                  </div>
                </>
              )}
            </li>
          ))}
        </ul>
      )}

      <form
        onSubmit={(e) => {
          void handleCreate(e);
        }}
        aria-label="Create library"
        className="mt-4 flex items-end gap-2"
      >
        <div className="flex flex-col gap-1.5">
          <label htmlFor="create-library-name" className="text-[0.82rem] font-semibold text-mute">
            New library name
          </label>
          <input
            id="create-library-name"
            type="text"
            value={createName}
            onChange={(e) => setCreateName(e.currentTarget.value)}
            disabled={isCreating}
            className="h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink"
          />
        </div>
        <Button type="submit" disabled={isCreateBlank || isCreating}>
          Create library
        </Button>
      </form>
    </section>
  );
}
