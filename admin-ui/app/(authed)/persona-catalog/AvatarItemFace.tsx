"use client";

import { useState, type ReactNode } from "react";
import { Chip } from "@/components/ui/chip";
import { clampPackDisplayText } from "@/lib/clamp-pack-display-text";
import { prettifySlug } from "./format-slug";

export interface AvatarItemFaceProps {
  /** The pack's catalog slug — used to build the asset route below. Already validated shape
   * (`CatalogIndexValidator.SlugSegment`) by the time it reaches this component, since it only ever
   * arrives via an already-fetched detail entry — never raw operator input. */
  slug: string;
  /** This item's display name (SPEC F128.1's `items[].name`) — a manifest field with NO server-side
   * length gate on this pre-install read (see `avatar-format.ts`'s own remarks); clamped here (T294
   * rider 2) before it ever reaches the DOM as a label or an `alt`. */
  name: string;
  /** The item's bare filename on the pack's own `assets[]`, or `null` when the manifest names a file
   * the index's own hash-verified `assets[]` never actually declared (`CatalogAvatarItemDto`'s own
   * remarks) — such an item renders no image at all, never an attempted fetch against an unverified
   * name (T294 rider). */
  file: string | null;
  /** An OPTIONAL "pairs well with" catalog persona slug (SPEC F128.1) — an OFFER only, rendered as a
   * plain chip; this component never applies it to anything. */
  suggestedPersona: string | null;
}

/**
 * One avatar pack item's own face tile (SPEC F128.1, F128.4, PLAN T294) — the pack's real,
 * hash-verified PNG loaded through the SAME transient proxied asset route the F104 font specimen
 * precedent already established (`GET /api/catalog/entries/{slug}/assets/{file}`,
 * `CatalogController.Asset`, `Cache-Control: no-store`), a plain `<img>` element rather than
 * `SpecimenBlock`'s own `fetch` + `Blob` + CSS Font Loading API machinery: that machinery exists
 * SPECIFICALLY because a font face has to reach `document.fonts` before anything can render text SET
 * in it, and a bare CSS `url()` reference fails silently with nothing this app could read to explain
 * why (`SpecimenBlock`'s own remarks). Neither reason applies to a PNG — a browser already renders an
 * `<img>` natively, and already fires a real, observable `onError` when the request fails (401/404/
 * 502/503 alike), which is enough to satisfy the SAME "no crash, visible degraded copy on failure"
 * contract SPEC F104.4/AC3 states for a specimen, without hand-rolling a second fetch/cleanup cycle
 * for what could be up to `AvatarPackController.MaxPackItems` (64) tiles at once. `loading="lazy"`
 * (native, no library) keeps a large pack's own off-screen tiles from all firing at once. Same-origin
 * credentials ride an `<img>` request exactly as they ride a `fetch()` — see `SpecimenBlock`'s own
 * remarks for the same point made about a font asset.
 *
 * Nothing here is EVER installed by rendering — a browser's own native image load is not
 * `station`-side persistence (the F104 "specimen" contract's actual concern; see `SpecimenBlock`'s
 * own CACHING remarks), and this component issues no request of its own beyond the one `<img>` GET.
 */
export function AvatarItemFace({ slug, name, file, suggestedPersona }: AvatarItemFaceProps): ReactNode {
  const [failed, setFailed] = useState(false);
  const clampedName = clampPackDisplayText(name);

  return (
    <li className="flex flex-col items-center gap-1.5 rounded-[6px] border border-line bg-surface-2 p-3 text-center">
      {file !== null && !failed ? (
        <img
          src={`/api/catalog/entries/${encodeURIComponent(slug)}/assets/${encodeURIComponent(file)}`}
          alt={clampedName}
          loading="lazy"
          onError={() => setFailed(true)}
          className="h-16 w-16 rounded-[6px] border border-line object-cover"
        />
      ) : (
        // `file === null` (an undeclared manifest file, T294 rider) and a genuine load failure share
        // the SAME degraded tile — neither has a real face to show, and this component has no further
        // detail worth surfacing about which of the two happened (mirrors SpecimenBlock's own "no
        // gate/reason detail" restraint, one level down in scale).
        <div className="flex h-16 w-16 items-center justify-center rounded-[6px] border border-line bg-surface text-[0.62rem] text-mute">
          No face
        </div>
      )}
      {/* Plain text ONLY — see this file's own remarks; React's default escaping, never
          dangerouslySetInnerHTML. */}
      <p className="text-[0.75rem] text-ink">{clampedName}</p>
      {/* `suggestedPersona` is already a shape-checked catalog slug (`CatalogAvatarItemDto`'s own
          remarks — a real slug, ≤64 chars), never free-form prose, so this reads `prettifySlug`
          rather than the clamp above (the same "OFFER, not applied" chip `PersonaOfferDialog`
          renders one level up in `PersonaCatalogClient`, for the SAME wording). */}
      {suggestedPersona !== null && <Chip>Suggested: {prettifySlug(suggestedPersona)}</Chip>}
    </li>
  );
}
