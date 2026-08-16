import type { ReactNode } from "react";
import { cn } from "@/lib/utils";
import type { IconPackDefinition, IconPackElement, IconPackStyle } from "@/lib/icon-pack";

export interface IconPackGlyphProps {
  /** One icon's already-parsed, whitelist-safe element list — see `lib/icon-pack.ts`'s own
   * remarks for the parse/validate boundary; nothing reaches this component that hasn't already
   * passed the fixed tag/attribute map there. */
  elements: readonly IconPackElement[];
  style: IconPackStyle;
  className?: string;
}

/**
 * The safe icon-pack renderer (SPEC F130.3, STORY-337, PLAN T304) — maps an already-parsed
 * `IconPackElement` list into SVG primitives inside the SAME 16×16 frame `icons.tsx`'s own
 * `IconBase` uses, so a pack-drawn glyph sits in the admin chrome indistinguishably from a house
 * one. NO `dangerouslySetInnerHTML` anywhere in this file — every element is built from the
 * parsed JSON via a FIXED per-tag attribute map (`renderElement`'s own switch below), never a
 * spread of arbitrary object keys, so a remote pack can only ever emit the seven whitelisted tags
 * with the exact geometry/fill/stroke attributes SPEC F130.1 defines — the fixed-attribute-map
 * discipline security-web asked for as this task's own security boundary.
 *
 * <b>Never crashes on pathological-but-VALID geometry</b> (PLAN T302/T304 review rider — negative
 * radii, `1e300`-scale coordinates): `lib/icon-pack.ts` only ever hands this component a FINITE
 * JavaScript number for every geometry attribute, so nothing here needs a further range check —
 * the 16×16 `viewBox` itself clips whatever such a value draws, the same reasoning the whitelist's
 * own "numeric geometry only" rule rests on server-side.
 *
 * `stroke="currentColor"` is hardcoded, matching `IconBase` — SPEC F130.1's pack-level `style`
 * carries no separate stroke CHOICE (only `strokeWidth`/`fill`), so every pack-drawn glyph strokes
 * with the ambient text color exactly like a house icon does; `fill` is the one axis a pack
 * actually varies (the "filled-vs-stroked variety" ruling).
 */
export function IconPackGlyph({ elements, style, className }: IconPackGlyphProps): ReactNode {
  return (
    <svg
      viewBox="0 0 16 16"
      width="16"
      height="16"
      fill={style.fill}
      stroke="currentColor"
      strokeWidth={style.strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={className}
    >
      {elements.map((element, index) => renderElement(element, index))}
    </svg>
  );
}

/** The fixed per-tag attribute map (the security boundary this file's own remarks name) — every
 * case reads ONLY the attributes SPEC F130.1 defines for that tag, never a spread of `element`'s
 * own keys. Exhaustive over `IconPackElement`'s closed union: an eighth tag added to `lib/icon-
 * pack.ts` without a case here fails `tsc` via the `default` arm's own `never` assignment below. */
function renderElement(element: IconPackElement, key: number): ReactNode {
  switch (element.tag) {
    case "path":
      return <path key={key} d={element.d} fill={element.fill} stroke={element.stroke} />;
    case "rect":
      return (
        <rect
          key={key}
          x={element.x}
          y={element.y}
          width={element.width}
          height={element.height}
          rx={element.rx}
          ry={element.ry}
          fill={element.fill}
          stroke={element.stroke}
        />
      );
    case "circle":
      return <circle key={key} cx={element.cx} cy={element.cy} r={element.r} fill={element.fill} stroke={element.stroke} />;
    case "ellipse":
      return (
        <ellipse key={key} cx={element.cx} cy={element.cy} rx={element.rx} ry={element.ry} fill={element.fill} stroke={element.stroke} />
      );
    case "line":
      return <line key={key} x1={element.x1} y1={element.y1} x2={element.x2} y2={element.y2} fill={element.fill} stroke={element.stroke} />;
    case "polyline":
      return <polyline key={key} points={element.points} fill={element.fill} stroke={element.stroke} />;
    case "polygon":
      return <polygon key={key} points={element.points} fill={element.fill} stroke={element.stroke} />;
    default: {
      const exhaustive: never = element;
      return exhaustive;
    }
  }
}

export interface IconPackSpecimenRowProps {
  definition: IconPackDefinition;
  className?: string;
}

/** Bounds how many glyphs one specimen row ever draws — a pre-install shelf preview has not been
 * through the server's own `MaxIconsPerPack` (512) cap yet, so this component bounds its OWN
 * render count defensively rather than trusting an upstream limit it cannot see from here. Well
 * above any real seed pack's own icon-name-contract-sized (24-ish) set. */
const MAX_SPECIMEN_GLYPHS = 48;

/**
 * A pack's own icon set, drawn small (SPEC F130.3, STORY-337, PLAN T304) — the Wardrobe Icons
 * tab's per-pack row and the shelf's icon-kind detail panel both render through this ONE component,
 * ordinal-sorted by name for a stable, deterministic layout. Every glyph renders through the SAME
 * `IconPackGlyph` the active-chrome resolver (`Icon.tsx`) uses — no separate, less-defensive path
 * for a "just previewing" render.
 */
export function IconPackSpecimenRow({ definition, className }: IconPackSpecimenRowProps): ReactNode {
  const entries = Object.entries(definition.icons)
    .sort(([a], [b]) => a.localeCompare(b))
    .slice(0, MAX_SPECIMEN_GLYPHS);

  if (entries.length === 0) {
    return <p className="text-[0.85rem] text-mute">This pack declares no icons.</p>;
  }

  return (
    <ul aria-label="Icon specimens" className={cn("flex flex-wrap gap-2", className)}>
      {entries.map(([name, elements]) => (
        <li
          key={name}
          className="flex h-8 w-8 items-center justify-center rounded-[3px] border border-line bg-surface-2 text-ink"
        >
          <IconPackGlyph elements={elements} style={definition.style} className="h-5 w-5" />
        </li>
      ))}
    </ul>
  );
}
