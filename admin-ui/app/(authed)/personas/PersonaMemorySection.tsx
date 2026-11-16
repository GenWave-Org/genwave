"use client";

import { useEffect, useId, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { toast } from "@/components/ui/toast";
import { formatUpSince } from "@/lib/format-clock";
import {
  MEMORY_TEXT_MAX_CHARS,
  MemoryApiError,
  addLore,
  changeMemoryRow,
  deleteMemoryRow,
  fetchPersonaMemory,
  suggestLore,
  type MemoryKind,
  type MemorySource,
  type PersonaMemoryChange,
  type PersonaMemoryRow,
} from "@/lib/persona-memory-api";
import { cn } from "@/lib/utils";

interface PersonaMemorySectionProps {
  personaId: number;
  personaName: string;
}

interface MemoryKindView {
  kind: MemoryKind;
  label: string;
  blurb: string;
  empty: string;
}

const LORE_VIEW: MemoryKindView = {
  kind: "lore",
  label: "Lore",
  blurb: "Things this DJ knows about themselves. They bring these up now and then.",
  empty: "No lore yet. Add a line, or ask for suggestions.",
};

const KIND_VIEWS: readonly MemoryKindView[] = [
  LORE_VIEW,
  {
    kind: "episode",
    label: "Lately",
    blurb: "What this DJ has said on air recently, so they do not repeat themselves.",
    empty: "Nothing here yet. This fills up as the DJ talks on air.",
  },
  {
    kind: "life",
    label: "Life",
    blurb: "Details of the DJ's life that came up on air. Forget one to stop it coming up again.",
    empty: "Nothing here yet. This fills up as the DJ talks on air.",
  },
];

const SOURCE_LABELS: Record<MemorySource, string> = {
  authored: "You wrote this",
  drafted: "Suggested",
  extracted: "Picked up on air",
  accrued: "From the log",
};

type LoadStatus =
  | { kind: "loading" }
  | { kind: "loaded"; rows: PersonaMemoryRow[] }
  | { kind: "error" };

function errorText(err: unknown): string {
  return err instanceof MemoryApiError ? err.message : "Something went wrong. Try again.";
}

function airedText(row: PersonaMemoryRow): string {
  if (row.airedCount === 0) return "Not aired yet";
  const times = row.airedCount === 1 ? "Aired once" : `Aired ${row.airedCount} times`;
  return row.lastAiredAt === null ? times : `${times}, last ${formatUpSince(row.lastAiredAt)}`;
}

interface MemoryRowItemProps {
  row: PersonaMemoryRow;
  busy: boolean;
  onChange: (row: PersonaMemoryRow, change: PersonaMemoryChange) => Promise<boolean>;
  onDelete: (row: PersonaMemoryRow) => Promise<void>;
}

/** One memory row. The text renders as plain text (React escaping) — it is model-written. */
function MemoryRowItem({ row, busy, onChange, onDelete }: MemoryRowItemProps): ReactNode {
  const [draft, setDraft] = useState<string | null>(null);
  const editing = draft !== null;

  async function saveEdit(): Promise<void> {
    if (draft === null) return;
    if (await onChange(row, { text: draft.trim() })) setDraft(null);
  }

  return (
    <li
      data-testid={`memory-row-${row.id}`}
      className={cn("flex flex-col gap-2 border-b border-line py-2 last:border-b-0", row.forgotten && "opacity-70")}
    >
      {editing ? (
        <div className="flex flex-col gap-2">
          <textarea
            aria-label="Memory text"
            className="min-h-16 w-full rounded-[6px] border border-line bg-surface p-2 text-[0.85rem] text-ink"
            maxLength={MEMORY_TEXT_MAX_CHARS}
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
          />
          <span className="text-[0.72rem] tabular-nums text-mute">
            {`${draft.length} / ${MEMORY_TEXT_MAX_CHARS}`}
          </span>
        </div>
      ) : (
        <p className="text-[0.85rem] text-ink">{row.text}</p>
      )}

      <div className="flex flex-wrap items-center gap-2 text-[0.72rem] text-mute">
        <Chip>{SOURCE_LABELS[row.source]}</Chip>
        {row.pending && <Chip className="border-accent-2 text-ink">Waiting for your OK</Chip>}
        {row.forgotten && <Chip>Forgotten</Chip>}
        <span>{airedText(row)}</span>
      </div>

      <div className="flex flex-wrap gap-2">
        {editing ? (
          <>
            <Button type="button" disabled={busy || draft.trim() === ""} onClick={() => void saveEdit()}>
              Save
            </Button>
            <Button type="button" variant="secondary" disabled={busy} onClick={() => setDraft(null)}>
              Cancel
            </Button>
          </>
        ) : (
          <>
            {row.pending && (
              <Button type="button" disabled={busy} onClick={() => void onChange(row, { approved: true })}>
                Approve
              </Button>
            )}
            <Button type="button" variant="secondary" disabled={busy} onClick={() => setDraft(row.text)}>
              Edit
            </Button>
            <Button
              type="button"
              variant="secondary"
              disabled={busy}
              onClick={() => void onChange(row, { forgotten: !row.forgotten })}
            >
              {row.forgotten ? "Remember again" : "Forget"}
            </Button>
            <Button type="button" variant="secondary" disabled={busy} onClick={() => void onDelete(row)}>
              Delete
            </Button>
          </>
        )}
      </div>
    </li>
  );
}

function AddLoreForm({ busy, onAdd }: { busy: boolean; onAdd: (text: string) => Promise<boolean> }): ReactNode {
  const [text, setText] = useState("");
  const textId = useId();

  async function submit(): Promise<void> {
    if (await onAdd(text.trim())) setText("");
  }

  return (
    <div className="flex flex-col gap-2">
      <label className="text-[0.78rem] text-mute" htmlFor={textId}>
        Add a line of lore
      </label>
      <textarea
        id={textId}
        className="min-h-16 w-full rounded-[6px] border border-line bg-surface p-2 text-[0.85rem] text-ink"
        maxLength={MEMORY_TEXT_MAX_CHARS}
        value={text}
        onChange={(event) => setText(event.target.value)}
      />
      <div className="flex items-center gap-3">
        <Button type="button" disabled={busy || text.trim() === ""} onClick={() => void submit()}>
          Add
        </Button>
        <span className="text-[0.72rem] tabular-nums text-mute">{`${text.length} / ${MEMORY_TEXT_MAX_CHARS}`}</span>
      </div>
    </div>
  );
}

/**
 * Per-DJ Memory section (SPEC F218.25, STORY-540 AC3/AC4, PLAN T707): a kind switcher (Lore / Lately / Life),
 * pending rows flagged, forgotten rows hidden behind "Show forgotten", and the row buttons
 * (Approve, Edit, Forget / Remember again, Delete; Add and Suggest on Lore only). One lazy GET on
 * mount lists every kind; each button sends its request and swaps the row from the response, with
 * every button disabled while any request is in flight.
 */
export function PersonaMemorySection({ personaId, personaName }: PersonaMemorySectionProps): ReactNode {
  const confirm = useConfirm();
  const [status, setStatus] = useState<LoadStatus>({ kind: "loading" });
  const [activeKind, setActiveKind] = useState<MemoryKind>("lore");
  const [showForgotten, setShowForgotten] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    async function load(): Promise<void> {
      try {
        const rows = await fetchPersonaMemory(personaId);
        if (!cancelled) setStatus({ kind: "loaded", rows });
      } catch (err) {
        if (!cancelled) {
          setStatus({ kind: "error" });
          toast.error(`Unable to load memory for "${personaName}". ${errorText(err)}`);
        }
      }
    }
    void load();
    return () => {
      cancelled = true;
    };
  }, [personaId, personaName]);

  function updateRows(update: (rows: PersonaMemoryRow[]) => PersonaMemoryRow[]): void {
    setStatus((prev) => (prev.kind === "loaded" ? { kind: "loaded", rows: update(prev.rows) } : prev));
  }

  /** Runs one request with the busy flag held; resolves true when it succeeded. */
  async function run(action: () => Promise<void>): Promise<boolean> {
    setBusy(true);
    try {
      await action();
      return true;
    } catch (err) {
      toast.error(errorText(err));
      return false;
    } finally {
      setBusy(false);
    }
  }

  function handleChange(row: PersonaMemoryRow, change: PersonaMemoryChange): Promise<boolean> {
    return run(async () => {
      const saved = await changeMemoryRow(personaId, row.id, change);
      updateRows((rows) => rows.map((r) => (r.id === saved.id ? saved : r)));
    });
  }

  async function handleDelete(row: PersonaMemoryRow): Promise<void> {
    const ok = await confirm({
      title: "Delete this memory?",
      consequence: "It is removed for good. To keep it but stop it coming up, use Forget instead.",
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!ok) return;
    await run(async () => {
      await deleteMemoryRow(personaId, row.id);
      updateRows((rows) => rows.filter((r) => r.id !== row.id));
    });
  }

  function handleAdd(text: string): Promise<boolean> {
    return run(async () => {
      const added = await addLore(personaId, text);
      updateRows((rows) => [added, ...rows]);
    });
  }

  async function handleSuggest(): Promise<void> {
    await run(async () => {
      const drafted = await suggestLore(personaId);
      updateRows((rows) => [...drafted, ...rows]);
      const count = drafted.length === 1 ? "1 line" : `${drafted.length} lines`;
      toast.success(`${count} suggested. Approve the ones you like.`);
    });
  }

  const view = KIND_VIEWS.find((v) => v.kind === activeKind) ?? LORE_VIEW;
  const loadedRows = status.kind === "loaded" ? status.rows : [];
  const viewRows = loadedRows.filter((row) => row.kind === view.kind);
  const visibleRows = showForgotten ? viewRows : viewRows.filter((row) => !row.forgotten);
  const forgottenCount = viewRows.filter((row) => row.forgotten).length;

  return (
    <div role="region" aria-label={`Memory for ${personaName}`} className="flex flex-col gap-3">
      <div role="group" aria-label="Memory kind" className="flex gap-1 border-b-2 border-line">
        {KIND_VIEWS.map((v) => {
          const active = v.kind === activeKind;
          return (
            <button
              key={v.kind}
              type="button"
              aria-pressed={active}
              onClick={() => setActiveKind(v.kind)}
              className={cn(
                "-mb-[2px] flex min-h-10 items-center border-b-2 px-3 py-2 text-[0.82rem] font-semibold transition-colors duration-[120ms] ease-out",
                active ? "border-accent text-accent" : "border-transparent text-mute hover:text-ink"
              )}
            >
              {v.label}
            </button>
          );
        })}
      </div>

      {status.kind === "loading" && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-full" />
        </div>
      )}

      {status.kind === "error" && <p className="text-[0.85rem] text-mute">Memory is unavailable.</p>}

      {status.kind === "loaded" && (
        <div className="flex flex-col gap-3">
          <p className="text-[0.78rem] text-mute">{view.blurb}</p>

          <div className="flex flex-wrap items-center gap-3">
            <label className="inline-flex items-center gap-2 text-[0.82rem] text-ink">
              <input
                type="checkbox"
                checked={showForgotten}
                onChange={(event) => setShowForgotten(event.target.checked)}
              />
              Show forgotten
              {forgottenCount > 0 && <span className="text-mute">{`(${forgottenCount})`}</span>}
            </label>
            {view.kind === "lore" && (
              <Button type="button" variant="secondary" disabled={busy} onClick={() => void handleSuggest()}>
                Suggest lore
              </Button>
            )}
          </div>

          {view.kind === "lore" && <AddLoreForm busy={busy} onAdd={handleAdd} />}

          {visibleRows.length === 0 ? (
            <EmptyState title="Nothing to show" reason={view.empty} />
          ) : (
            <ul className="flex flex-col">
              {visibleRows.map((row) => (
                <MemoryRowItem key={row.id} row={row} busy={busy} onChange={handleChange} onDelete={handleDelete} />
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
