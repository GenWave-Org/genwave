"use client";

import { useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";
import { isHttpUrl } from "@/lib/safe-external-url";
import { LicenseBadge } from "./catalog-badges";
import { prettifySlug } from "./format-slug";
import type { CatalogEntryDetailDto } from "./types";

export interface JinglePackDetailPanelProps {
  slug: string;
  detail: CatalogEntryDetailDto;
  /** Whether THIS slug already has an installed pack — sourced from `GET /api/jingle-packs`'s own
   * listing (`PersonaCatalogClient`'s `installedJinglePackSlugs` prop) — see `VoicePackDetailPanel`'s
   * own remarks for why this kind reads server truth via `router.refresh()` rather than a locally-
   * flipped `Set`. */
  isInstalled: boolean;
  onInstallClick: () => void;
}

interface ParsedJinglePackAsset {
  file: string;
  role: string;
  title: string;
  license: string;
  attributionCreator: string | null;
  attributionSourceUrl: string | null;
}

interface ParsedJinglePackManifest {
  packName: string;
  assets: ParsedJinglePackAsset[];
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function parseAsset(raw: unknown): ParsedJinglePackAsset | null {
  if (!isPlainObject(raw)) return null;

  const { file, role, title, license, attribution } = raw;
  if (typeof file !== "string" || file === "") return null;
  if (typeof role !== "string" || role === "") return null;
  if (typeof title !== "string" || title === "") return null;
  if (typeof license !== "string" || license === "") return null;

  if (attribution === undefined) {
    return { file, role, title, license, attributionCreator: null, attributionSourceUrl: null };
  }

  // Present but out of shape is a manifest contract violation, not a per-asset omission — the
  // whole document degrades to "could not be read" the same way an out-of-shape `packName` does.
  if (!isPlainObject(attribution) || typeof attribution.creator !== "string" || attribution.creator === "") return null;
  if (typeof attribution.sourceUrl !== "string" || attribution.sourceUrl === "") return null;

  return { file, role, title, license, attributionCreator: attribution.creator, attributionSourceUrl: attribution.sourceUrl };
}

/**
 * The safe, client-side `gw-jingle-pack` manifest reader (SPEC F165.1) — mirrors
 * `VoicePackDetailPanel`'s own `parseVoicePackManifest` posture: shape/non-empty checks only, no
 * throw, malformed → `null`. An `attribution` sub-object present but out of shape fails the WHOLE
 * document (see {@link parseAsset}'s own remarks) rather than being silently dropped — a manifest
 * that half-declares a credit is a contract violation this panel refuses to guess at, not an
 * absent-attribution asset (CC0 assets legitimately carry none at all).
 */
function parseJinglePackManifest(rawText: string): ParsedJinglePackManifest | null {
  let parsed: unknown;
  try {
    parsed = JSON.parse(rawText) as unknown;
  } catch {
    return null;
  }
  if (!isPlainObject(parsed)) return null;

  const { packName, assets: rawAssets } = parsed;
  if (typeof packName !== "string" || packName === "") return null;
  if (!Array.isArray(rawAssets) || rawAssets.length === 0) return null;

  const assets: ParsedJinglePackAsset[] = [];
  for (const rawAsset of rawAssets) {
    const asset = parseAsset(rawAsset);
    if (asset === null) return null;
    assets.push(asset);
  }

  return { packName, assets };
}

/** Plain names only (gh-#707) — never the raw `bed`/`sting`/`station_id` wire token in visible
 * text. An unrecognised role (should never arrive off a manifest this station's own install route
 * already validated) falls back to the raw token rather than throwing — the "never crash on a
 * pre-install read" posture every other defensive parser on this surface already holds to. */
const ROLE_LABELS: Readonly<Record<string, string>> = {
  bed: "Background music",
  sting: "Sting",
  station_id: "Station ID",
};

function roleLabel(role: string): string {
  return ROLE_LABELS[role] ?? role;
}

/** CC0 first, then CC-BY, then anything else alphabetically — deterministic regardless of the
 * manifest's own asset order, so "CC0 · CC-BY" never renders as "CC-BY · CC0" depending on which
 * asset happened to list its license first. */
const KNOWN_LICENSE_ORDER = ["CC0", "CC-BY"];

function aggregateLicenses(assets: readonly ParsedJinglePackAsset[]): string {
  const present = new Set(assets.map((asset) => asset.license));
  const known = KNOWN_LICENSE_ORDER.filter((license) => present.has(license));
  const unknown = [...present].filter((license) => !KNOWN_LICENSE_ORDER.includes(license)).sort();
  return [...known, ...unknown].join(" · ");
}

/**
 * A jingle pack entry's detail view (SPEC F165, STORY-397, PLAN T418) — mirrors
 * `VoicePackDetailPanel`'s own shape (name, an Install/Re-install button opening the shared confirm
 * modal, an "Installed" chip, a self-contained Uninstall via `useConfirm()` + `router.refresh()`)
 * with no preview gate at all — a jingle pack declares no preview clip (unlike a voice pack's own
 * F103.5 honest-preview contract), so Install renders as soon as the manifest parses, the same
 * `disabled={!parsed}`-shaped posture `AdPackDetailPanel` already uses for its own preview-less
 * kind.
 *
 * The asset table's own Title · Kind · License · Credit columns are READ-ONLY review, the same
 * "reviewing installs nothing" rule every pack kind's detail panel already states — Kind reads the
 * plain name `roleLabel` resolves (gh-#707: never the raw `bed`/`sting`/`station_id` token), Credit
 * links a CC-BY asset's `creator` to its `sourceUrl` ONLY when that URL is `http:`/`https:` (an
 * `<a>` pointed at anything else — `javascript:`, a bare string that isn't a URL at all — renders
 * the creator name as plain text instead, never an unsafe href), and reads "—" for a CC0 asset
 * (SPEC F165.2's own contract: attribution rides ONLY a CC-BY asset). `rel="noopener noreferrer"`
 * on every external link (security-web: an author-declared URL is untrusted output).
 */
export function JinglePackDetailPanel({ slug, detail, isInstalled, onInstallClick }: JinglePackDetailPanelProps): ReactNode {
  const manifest = detail.card === null ? null : parseJinglePackManifest(detail.card);
  const [uninstalling, setUninstalling] = useState(false);
  const router = useRouter();
  const confirm = useConfirm();

  async function handleUninstall(): Promise<void> {
    const displayName = manifest?.packName ?? prettifySlug(slug);
    const confirmed = await confirm({
      title: "Uninstall jingle pack",
      consequence: `Uninstall "${displayName}"? Every clip it added is removed from this station immediately.`,
      confirmLabel: "Uninstall",
      destructive: true,
    });
    if (!confirmed) return;

    setUninstalling(true);
    try {
      const resp = await fetch(`/api/jingle-packs/${encodeURIComponent(slug)}`, { method: "DELETE" });
      if (resp.status === 204) {
        toast.success(`"${displayName}" uninstalled.`);
        router.refresh();
        return;
      }
      toast.error(await readErrorMessage(resp));
    } catch {
      toast.error("Network error — check your connection");
    } finally {
      setUninstalling(false);
    }
  }

  if (manifest === null) {
    return (
      <div className="flex flex-col gap-4">
        <h2 className="font-display text-[1.1rem] text-ink">{prettifySlug(slug)}</h2>
        <p className="text-[0.85rem] text-mute">This pack&apos;s manifest could not be read.</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="font-display text-[1.1rem] text-ink">{manifest.packName}</h2>
          <LicenseBadge license={aggregateLicenses(manifest.assets)} />
          {isInstalled && <Chip>Installed</Chip>}
        </div>
        <div className="flex items-center gap-2">
          {isInstalled && (
            <Button
              type="button"
              variant="secondary"
              disabled={uninstalling}
              onClick={() => {
                void handleUninstall();
              }}
            >
              Uninstall
            </Button>
          )}
          <Button type="button" variant="primary" onClick={onInstallClick}>
            {isInstalled ? "Re-install" : "Install"}
          </Button>
        </div>
      </div>

      <table className="w-full text-left text-[0.82rem] text-ink">
        <thead>
          <tr className="text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-mute">
            <th scope="col" className="pb-1 pr-2">
              Title
            </th>
            <th scope="col" className="pb-1 pr-2">
              Kind
            </th>
            <th scope="col" className="pb-1 pr-2">
              License
            </th>
            <th scope="col" className="pb-1">
              Credit
            </th>
          </tr>
        </thead>
        <tbody>
          {manifest.assets.map((asset) => (
            <tr key={asset.file} className="border-t border-line">
              <td className="py-1.5 pr-2">{asset.title}</td>
              <td className="py-1.5 pr-2">{roleLabel(asset.role)}</td>
              <td className="py-1.5 pr-2">{asset.license}</td>
              <td className="py-1.5">
                {asset.attributionCreator === null ? (
                  "—"
                ) : asset.attributionSourceUrl !== null && isHttpUrl(asset.attributionSourceUrl) ? (
                  <a
                    href={asset.attributionSourceUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-accent underline underline-offset-2"
                  >
                    {asset.attributionCreator}
                  </a>
                ) : (
                  asset.attributionCreator
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
        Data only until you confirm — installing fetches and stores the audio itself
      </p>
    </div>
  );
}
