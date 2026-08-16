"use client";

import { useState, type ReactNode } from "react";

export interface PersonaCardReviewFaceProps {
  /** The catalog entry's own slug — used to build the asset route below. Already validated shape
   * (`CatalogIndexValidator.SlugSegment`) by the time it reaches this component: it only ever
   * arrives as `PersonaCardReviewModalProps.catalogSlug`, itself only ever a value this page's own
   * already-fetched shelf/detail supplied — never raw operator input. */
  slug: string;
  /** The entry's own sidecar face — the bare filename `CatalogEntryDetailDto.personaAvatarFile`
   * carries (SPEC F128.2, F128.7, PLAN T292/T297), or `null` when this persona entry declares no
   * face. `null` renders NOTHING — no empty slot, no placeholder box (this component's own caller
   * already gates on this being non-null before mounting it at all; the type stays nullable here so
   * a caller can pass the wire field straight through without its own extra guard). */
  file: string | null;
  /** The persona's own name (already reviewed elsewhere on this same card) — this component's own
   * `alt` text, never a second free-text field of its own. */
  personaName: string;
}

/**
 * The F90.6 trust modal's own face render (SPEC F128.7, STORY-334, PLAN T297) — the entry's real,
 * hash-verified PNG loaded through the SAME transient proxied asset route the F104 font specimen /
 * T294 avatar-pack precedent already established (`GET /api/catalog/entries/{slug}/assets/{file}`,
 * `CatalogController.Asset`, `Cache-Control: no-store`), a plain `<img>` element — mirrors
 * `AvatarItemFace`'s own reasoning for why a bare `<img>` (not `SpecimenBlock`'s fetch+Blob+Font
 * Loading API machinery, which exists specifically for a FONT face) is the right shape for a PNG: a
 * browser renders one natively and already fires a real, observable `onError`.
 *
 * ZERO WRITES, exactly like every other render this modal performs before Confirm (SPEC F90.6's own
 * required stop) — a browser's own native image GET is not a `station`-side persistence action, and
 * this component issues no request of its own beyond that one `<img>` GET.
 *
 * A load failure (401/404/502/503 alike) hides the image entirely rather than showing a broken-image
 * glyph or a fabricated placeholder (SPEC F128.9's own "no fabricated art, no broken-image glyph"
 * posture) — this modal has no grid of face-slots to degrade INTO the way `AvatarItemFace`'s own
 * tile does; it simply stops claiming a face exists.
 */
export function PersonaCardReviewFace({ slug, file, personaName }: PersonaCardReviewFaceProps): ReactNode {
  const [failed, setFailed] = useState(false);

  if (file === null || failed) return null;

  return (
    <img
      src={`/api/catalog/entries/${encodeURIComponent(slug)}/assets/${encodeURIComponent(file)}`}
      alt={personaName}
      loading="lazy"
      onError={() => setFailed(true)}
      className="h-14 w-14 shrink-0 rounded-[6px] border border-line object-cover"
    />
  );
}
