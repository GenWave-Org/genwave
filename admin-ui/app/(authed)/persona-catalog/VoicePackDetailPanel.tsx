"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Chip } from "@/components/ui/chip";
import { LicenseBadge } from "./catalog-badges";
import { prettifySlug } from "./format-slug";
import { InstallToggle } from "./InstallToggle";
import type { CatalogEntryDetailDto } from "./types";

export interface VoicePackDetailPanelProps {
  slug: string;
  detail: CatalogEntryDetailDto;
  /** Whether THIS slug already has an installed pack — sourced from `GET /api/voice-packs`'s own
   * listing (`PersonaCatalogClient`'s `installedVoicePackSlugs` prop). */
  isInstalled: boolean;
  onInstallClick: () => void;
  /** Fires once `DELETE /api/voice-packs/{slug}` resolves as removed (2xx, or 404 for an
   * already-gone pack) (SPEC F204.2, PLAN T564) — the caller removes this slug from its own
   * installed set so the row flips without a reload. */
  onUninstalled: (slug: string) => void;
}

interface ParsedVoicePackManifest {
  packName: string;
  engine: string;
  preview: string;
  voiceIds: string[];
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * The safe, client-side `gw-voice-pack` manifest reader (SPEC F164.1) — mirrors `parseIconPackDefinition`'s
 * own "never trust the wire blindly" posture for a pre-install manifest, but structurally simpler:
 * a voice pack declares no geometry/whitelist surface, only display strings and voice ids, so a
 * shape/non-empty check per field is the whole gate. Malformed → `null`, never a thrown exception —
 * the SAME "could not be read" degrade every other kind's own defensive parser already uses.
 */
function parseVoicePackManifest(rawText: string): ParsedVoicePackManifest | null {
  let parsed: unknown;
  try {
    parsed = JSON.parse(rawText) as unknown;
  } catch {
    return null;
  }
  if (!isPlainObject(parsed)) return null;

  const { packName, engine, preview, voices } = parsed;
  if (typeof packName !== "string" || packName === "") return null;
  if (typeof engine !== "string" || engine === "") return null;
  if (typeof preview !== "string" || preview === "") return null;
  if (!Array.isArray(voices) || voices.length === 0) return null;

  const voiceIds: string[] = [];
  for (const voice of voices) {
    if (!isPlainObject(voice) || typeof voice.voiceId !== "string" || voice.voiceId === "") return null;
    voiceIds.push(voice.voiceId);
  }

  return { packName, engine, preview, voiceIds };
}

/** The preview clip's own load/decode state machine (F103.5 / STORY-397 AC4) — `"ready"` splits
 * further into `canPlay` because a 200 fetch is not proof enough on its own (the bytes could be
 * truncated, or not actually audio); only the `<audio>` element's own `canplay` event, fired after
 * the browser's decoder has actually looked at the bytes, is. */
type PreviewState =
  | { kind: "loading" }
  | { kind: "ready"; objectUrl: string; canPlay: boolean }
  | { kind: "load-error" };

/**
 * A voice pack entry's detail view (SPEC F164, STORY-397 AC4, PLAN T418) — mirrors `IconDetailPanel`'s
 * own shape (name, an `InstallToggle` opening the shared confirm modal, an "Installed" chip) with
 * two departures this kind's own contract forces.
 *
 * <b>The honest-preview gate (F103.5 / AC4).</b> Every other pack kind's Install button renders the
 * instant its manifest parses; this one additionally fetches the manifest's own declared `preview`
 * clip through the SAME hash-verified asset proxy `SpecimenBlock` already uses
 * (`GET /api/catalog/entries/{slug}/assets/{file}`), feeds it to a real `<audio>` element, and keeps
 * the Install button UNRENDERED — not merely disabled — until that element itself fires `canplay`.
 * A fetch failure OR a decode failure (the element's own `error` event) both degrade to the exact
 * same visible copy this task's own dispatch pins verbatim: "The preview could not be loaded, so
 * this pack cannot be installed from here." An unparseable MANIFEST (`detail.card` itself failing
 * {@link parseVoicePackManifest}) is a separate, earlier failure — it never reaches the preview
 * fetch at all, and shows its own "manifest could not be read" copy instead (mirrors
 * `AdPackDetailPanel`'s own `!parsed` branch).
 *
 * <b>`InstallToggle` (SPEC F204.1).</b> Uninstall renders whenever installed; Install waits for the
 * preview gate above (F103.5).
 */
export function VoicePackDetailPanel({ slug, detail, isInstalled, onInstallClick, onUninstalled }: VoicePackDetailPanelProps): ReactNode {
  const manifest = detail.card === null ? null : parseVoicePackManifest(detail.card);
  // Hoisted out of `manifest` (T418 review round 1 finding O2): `manifest` is re-derived from
  // `detail` every render, so depending on it directly would re-run this effect every render too —
  // only its own `preview` filename (a plain string) is stable across renders that don't actually
  // change it, which is what lets the dependency array below be exhaustive without looping.
  const previewFile = manifest?.preview ?? null;
  const [preview, setPreview] = useState<PreviewState>({ kind: "loading" });

  useEffect(() => {
    if (previewFile === null) return;

    let cancelled = false;
    let objectUrl: string | null = null;
    setPreview({ kind: "loading" });

    const assetUrl = `/api/catalog/entries/${encodeURIComponent(slug)}/assets/${encodeURIComponent(previewFile)}`;

    (async () => {
      try {
        const resp = await fetch(assetUrl);
        if (cancelled) return;
        if (!resp.ok) {
          setPreview({ kind: "load-error" });
          return;
        }

        const blob = await resp.blob();
        if (cancelled) return;

        const url = URL.createObjectURL(blob);
        objectUrl = url;
        setPreview({ kind: "ready", objectUrl: url, canPlay: false });
      } catch {
        if (!cancelled) setPreview({ kind: "load-error" });
      }
    })();

    return () => {
      cancelled = true;
      if (objectUrl !== null) URL.revokeObjectURL(objectUrl);
    };
  }, [slug, previewFile]);

  if (manifest === null) {
    return (
      <div className="flex flex-col gap-4">
        <h2 className="font-display text-[1.1rem] text-ink">{prettifySlug(slug)}</h2>
        <p className="text-[0.85rem] text-mute">This pack&apos;s manifest could not be read.</p>
      </div>
    );
  }

  const canInstall = preview.kind === "ready" && preview.canPlay;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="font-display text-[1.1rem] text-ink">{manifest.packName}</h2>
          {/* Every installable voice pack is synthetic by SPEC F164.2's own engine gate — a static
              label, no manifest field to read (see `LicenseBadge`'s own remarks). `kind="voices"`
              because this is NOT a licence — voice packs carry no license field at all — so the
              badge's aria-label reads "Voices: Synthetic voices", not "License: …" (T418 review
              round 1 finding F3). */}
          <LicenseBadge license="Synthetic voices" kind="voices" />
          {isInstalled && <Chip>Installed</Chip>}
        </div>
        <div className="flex items-center gap-2">
          {/* Uninstall always renders once installed, regardless of preview state; Install is NOT
              rendered — not merely disabled — until the preview proves playable (F103.5/AC4), so the
              toggle itself is withheld until one or the other condition is true. */}
          {(isInstalled || canInstall) && (
            <InstallToggle
              slug={slug}
              displayName={manifest.packName}
              isInstalled={isInstalled}
              deletePath="/api/voice-packs"
              kindLabel="voice pack"
              removedNoun="voice"
              onInstallClick={onInstallClick}
              onUninstalled={onUninstalled}
            />
          )}
        </div>
      </div>

      <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
        {manifest.voiceIds.length} voice{manifest.voiceIds.length === 1 ? "" : "s"}: {manifest.voiceIds.join(", ")}
      </p>

      {preview.kind === "load-error" ? (
        <p role="alert" className="text-[0.85rem] text-danger">
          The preview could not be loaded, so this pack cannot be installed from here.
        </p>
      ) : (
        <div className="flex flex-col gap-1">
          <audio
            controls
            aria-label="Voice pack preview"
            src={preview.kind === "ready" ? preview.objectUrl : undefined}
            onCanPlay={() => setPreview((prev) => (prev.kind === "ready" ? { ...prev, canPlay: true } : prev))}
            onError={() => setPreview({ kind: "load-error" })}
            className="w-full"
          />
          {!canInstall && <p className="text-[0.85rem] text-mute">Loading the preview…</p>}
        </div>
      )}

      <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
        Transient preview — browsing installs nothing
      </p>
    </div>
  );
}
