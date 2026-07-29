/**
 * Station Imaging content kinds (gh-#149) — the wire/storage tokens POST /api/safe-segments
 * accepts (`kind`) and GET /api/media rows carry back (`imagingKind`), with their display labels.
 * Mirrors `ImagingKindTokens` in GenWave.Core — the C# side owns validation; this module only
 * renders and submits the same four tokens.
 *
 * Kinds are METADATA-ONLY for now: playout/safe-loop behavior is identical for every kind — a
 * future issue wires kind-aware rotation. A row with no stored kind (null/undefined — every
 * segment authored before gh-#149) displays as the Liner default, matching the API's own
 * absent-means-liner rule.
 */
export const IMAGING_KINDS = [
  { token: "liner", label: "Liner" },
  { token: "station_id", label: "Station ID" },
  { token: "jingle", label: "Jingle" },
  { token: "promo", label: "Promo" },
] as const;

export type ImagingKindToken = (typeof IMAGING_KINDS)[number]["token"];

export const DEFAULT_IMAGING_KIND: ImagingKindToken = "liner";

/** Display label for a stored kind token; null/undefined/unknown fall back to the Liner default. */
export function imagingKindLabel(token: string | null | undefined): string {
  const found = IMAGING_KINDS.find((kind) => kind.token === token);
  return (found ?? IMAGING_KINDS[0]).label;
}
