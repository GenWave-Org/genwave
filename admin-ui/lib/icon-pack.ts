/**
 * The safe, client-side `gw-icon-pack` parser (SPEC F130.1/F130.3, STORY-337, PLAN T304) — the
 * ONE place remote or stored icon-pack JSON text is turned into a shape `IconPackRenderer.tsx` is
 * allowed to draw. This is the security boundary security-web asked for on this task: every field
 * is read through a runtime `typeof`/shape check, every tag against a closed seven-member
 * whitelist, every `fill`/`stroke` against the exact `none | currentColor` two-token vocabulary,
 * every icon-map KEY against the same `[a-z][a-z0-9-]*` name grammar and 64-character cap
 * `GenWave.Host.Icons.IconPackDefinitionParser.IconNameText`/`MaxIconNameChars` enforce server-side,
 * and `d`/`points` against that same parser's `PathDataText`/`PointsText` character grammars — no
 * `JSON.parse(text)` result ever reaches a caller un-narrowed, and nothing here ever throws past its
 * own `try`.
 *
 * <b>Trusted differently, validated identically, everywhere.</b> Three call sites feed this parser
 * text of three different trust levels — a NOT-yet-installed catalog entry's raw manifest (never
 * seen `IconPackDefinitionParser.Validate` at all), an installed pack's own already-canonical
 * definition (validated once, at install time, by the server), and the station's own currently
 * ACTIVE pack (re-validated again server-side on every read, `IconPackController.Active`'s own
 * remarks) — but this module treats all three identically, defensively, from scratch. That is
 * deliberate: a browser rendering DOM from ANY remote-influenced JSON must never lean on a trust
 * boundary that lives in a different process, even one this same station already enforced once.
 *
 * <b>Malformed → skip, never crash — EXCEPT the icon-name key gate (PLAN T302/T304 review rider).</b>
 * Pathological-but-VALID geometry (a negative radius, a coordinate like `1e300`) is a FINITE
 * JavaScript number — never `NaN`/`Infinity` — so it parses through untouched; the 16×16 `viewBox`
 * an icon renders inside clips whatever that draws, the same "SVG itself bounds it" reasoning the
 * server-side parser's own remarks give. Genuinely malformed PRIMITIVE input (an unknown tag, a
 * non-numeric attribute, a literal colour, a hostile `d`/`points` string) is dropped at the
 * SMALLEST possible grain: one bad primitive inside an otherwise-fine icon is simply omitted from
 * that icon's own element list, rather than discarding the whole icon or the whole pack over one
 * offending element. An out-of-shape icon-map KEY is the one deliberate exception to that
 * smallest-grain posture: it nulls the WHOLE document (a parse failure, degrading to the exact same
 * "could not be read" state every other parse failure already shows) rather than merely dropping
 * that one entry — mirroring `IconPackDefinitionParser.TryValidateIcon`'s own whole-document reject
 * for a bad map key server-side (PLAN T302 review F1's "Invalid" ruling). A browser rendering
 * pre-install detail off a hostile manifest must never quietly narrow the document down to the keys
 * it happened to like; an author who shaped one key as `</svg><script>…` or five thousand characters
 * long gets an honest "could not be read", the same as any other malformed document, not a silently
 * thinned icon list.
 */

/** The only two fill/stroke tokens this schema can express (SPEC F130.1) — hue stays token-bound;
 * a literal colour is structurally unrepresentable by this type, not merely rejected at the edge. */
export type IconFillMode = "none" | "currentColor";

export interface IconPackStyle {
  strokeWidth: number;
  fill: IconFillMode;
}

interface IconElementColors {
  fill?: IconFillMode;
  stroke?: IconFillMode;
}

export interface IconPathElement extends IconElementColors {
  tag: "path";
  d: string;
}

export interface IconRectElement extends IconElementColors {
  tag: "rect";
  x: number;
  y: number;
  width: number;
  height: number;
  rx?: number;
  ry?: number;
}

export interface IconCircleElement extends IconElementColors {
  tag: "circle";
  cx: number;
  cy: number;
  r: number;
}

export interface IconEllipseElement extends IconElementColors {
  tag: "ellipse";
  cx: number;
  cy: number;
  rx: number;
  ry: number;
}

export interface IconLineElement extends IconElementColors {
  tag: "line";
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

export interface IconPolylineElement extends IconElementColors {
  tag: "polyline";
  points: string;
}

export interface IconPolygonElement extends IconElementColors {
  tag: "polygon";
  points: string;
}

/** The closed seven-tag whitelist (SPEC F130.1) — every member `IconPackRenderer.tsx`'s own
 * exhaustive switch must handle; adding an eighth tag anywhere is a compile error until both files
 * agree. */
export type IconPackElement =
  | IconPathElement
  | IconRectElement
  | IconCircleElement
  | IconEllipseElement
  | IconLineElement
  | IconPolylineElement
  | IconPolygonElement;

export interface IconPackDefinition {
  style: IconPackStyle;
  /** Every icon this definition declares, keyed by name — both names inside AND outside the house
   * icon-name contract (SPEC F130.2: an out-of-contract name is still whitelist-valid, ordinary
   * data). An icon whose own element list parsed down to zero usable elements is never admitted
   * here at all — see this module's own remarks. */
  icons: Readonly<Record<string, readonly IconPackElement[]>>;
}

const MIN_STROKE_WIDTH = 0.5;
const MAX_STROKE_WIDTH = 3;

// The exact character grammars `IconPackDefinitionParser.PathDataText`/`PointsText` enforce
// server-side (SPEC F130.1) — hyphen LAST, a literal, never a `+`–`e` range (that class's own
// remarks record the 2026-08-15 SPEC correction this mirrors).
const PATH_DATA_PATTERN = /^[MmLlHhVvCcSsQqTtAaZz0-9 ,.+eE-]+$/;
const POINTS_PATTERN = /^[0-9 ,.+-]+$/;

// Mirrors `IconPackDefinitionParser.IconNameText` exactly (PLAN T304 fix round) — lowercase
// letters, digits and hyphens, starting with a letter. The out-of-contract-but-whitelist-valid
// case (SPEC F130.2) is unaffected: this gate is purely about CHARACTER SHAPE, not membership in
// `IconNameContract.Names`.
const ICON_NAME_PATTERN = /^[a-z][a-z0-9-]*$/;

// Mirrors `IconPackDefinitionParser.MaxIconNameChars` exactly — a map-KEY character-shape bound,
// not a slow-walk ceiling (see that constant's own remarks).
const MAX_ICON_NAME_CHARS = 64;

/**
 * Unlike C#'s DEFAULT `$` (which, without `RegexOptions.Multiline`, still matches just BEFORE a
 * single trailing line terminator — the exact reason
 * `IconPackDefinitionParser.PathDataText`/`PointsText`/`IconNameText` all anchor with `\A...\z`
 * rather than `^...$` server-side), JavaScript's `$` (no `/m` flag) is already TRUE end-of-input
 * only — it never admits a trailing newline the way .NET's does. The explicit `\r`/`\n` pre-check
 * below is therefore not compensating for a JS quirk that does not exist (none of
 * `PATH_DATA_PATTERN`/`POINTS_PATTERN`/`ICON_NAME_PATTERN` admits `\n`/`\r` in their own character
 * class either, so an embedded line terminator already fails the class walk on its own); it is
 * deliberate defense in depth, kept in lockstep with the server's own belt-and-suspenders posture
 * rather than leaned on as this module's only gate.
 */
function matchesGrammar(pattern: RegExp, text: string): boolean {
  return !/[\r\n]/.test(text) && pattern.test(text);
}

/**
 * Gates one icon-map KEY against the same character grammar + length cap
 * `IconPackDefinitionParser.TryValidateIcon` enforces server-side — length is checked before shape,
 * mirroring that method's own "an oversized name is never itself echoed back into a message at full
 * length" ordering (there is no message here to protect, but the ordering costs nothing to mirror).
 */
function isValidIconName(name: string): boolean {
  return name.length <= MAX_ICON_NAME_CHARS && matchesGrammar(ICON_NAME_PATTERN, name);
}

/** A `{}`-shaped accumulator with NO inherited `Object.prototype`, so a hostile icon-map key
 * literally named `__proto__` becomes an ordinary own data property instead of invoking
 * `Object.prototype`'s `__proto__` accessor and reparenting this accumulator's own prototype to
 * whatever value that key carried. Kept even though {@link isValidIconName} already rejects
 * `__proto__` on shape alone (it starts with `_`, outside `[a-z]`) — defense in depth for this exact
 * accumulator, not reliant on the key gate being the only thing standing between hostile JSON and a
 * reparented object. */
function createIconAccumulator(): Record<string, readonly IconPackElement[]> {
  return Object.create(null) as Record<string, readonly IconPackElement[]>;
}

function isColorToken(value: unknown): value is IconFillMode {
  return value === "none" || value === "currentColor";
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readFiniteNumber(record: Record<string, unknown>, attr: string): number | undefined {
  const value = record[attr];
  return isFiniteNumber(value) ? value : undefined;
}

const INVALID_COLOR = Symbol("invalid-icon-color");

/** Reads an OPTIONAL `fill`/`stroke` override — absent is fine (inherits the pack's own style
 * block); present-but-not-`none`/`currentColor` marks the whole ELEMENT unusable (the caller drops
 * it), never silently swallowed into a blank/omitted attribute that would change what the glyph
 * actually draws. */
function readColorAttr(
  record: Record<string, unknown>,
  attr: "fill" | "stroke"
): IconFillMode | undefined | typeof INVALID_COLOR {
  if (!(attr in record)) return undefined;
  const value = record[attr];
  return isColorToken(value) ? value : INVALID_COLOR;
}

function parseElement(raw: unknown): IconPackElement | null {
  if (!isPlainObject(raw) || typeof raw.tag !== "string") return null;

  const fill = readColorAttr(raw, "fill");
  const stroke = readColorAttr(raw, "stroke");
  if (fill === INVALID_COLOR || stroke === INVALID_COLOR) return null;

  switch (raw.tag) {
    case "path": {
      const d = raw.d;
      return typeof d === "string" && matchesGrammar(PATH_DATA_PATTERN, d)
        ? { tag: "path", d, fill, stroke }
        : null;
    }
    case "rect": {
      const x = readFiniteNumber(raw, "x");
      const y = readFiniteNumber(raw, "y");
      const width = readFiniteNumber(raw, "width");
      const height = readFiniteNumber(raw, "height");
      if (x === undefined || y === undefined || width === undefined || height === undefined) return null;
      return { tag: "rect", x, y, width, height, rx: readFiniteNumber(raw, "rx"), ry: readFiniteNumber(raw, "ry"), fill, stroke };
    }
    case "circle": {
      const cx = readFiniteNumber(raw, "cx");
      const cy = readFiniteNumber(raw, "cy");
      const r = readFiniteNumber(raw, "r");
      return cx === undefined || cy === undefined || r === undefined ? null : { tag: "circle", cx, cy, r, fill, stroke };
    }
    case "ellipse": {
      const cx = readFiniteNumber(raw, "cx");
      const cy = readFiniteNumber(raw, "cy");
      const rx = readFiniteNumber(raw, "rx");
      const ry = readFiniteNumber(raw, "ry");
      if (cx === undefined || cy === undefined || rx === undefined || ry === undefined) return null;
      return { tag: "ellipse", cx, cy, rx, ry, fill, stroke };
    }
    case "line": {
      const x1 = readFiniteNumber(raw, "x1");
      const y1 = readFiniteNumber(raw, "y1");
      const x2 = readFiniteNumber(raw, "x2");
      const y2 = readFiniteNumber(raw, "y2");
      if (x1 === undefined || y1 === undefined || x2 === undefined || y2 === undefined) return null;
      return { tag: "line", x1, y1, x2, y2, fill, stroke };
    }
    case "polyline": {
      const points = raw.points;
      return typeof points === "string" && matchesGrammar(POINTS_PATTERN, points)
        ? { tag: "polyline", points, fill, stroke }
        : null;
    }
    case "polygon": {
      const points = raw.points;
      return typeof points === "string" && matchesGrammar(POINTS_PATTERN, points)
        ? { tag: "polygon", points, fill, stroke }
        : null;
    }
    default:
      return null;
  }
}

/** Parses one icon's own element ARRAY, dropping every individually-malformed primitive (this
 * module's own "skip the glyph" remarks) — `null` when `raw` is not even an array at all (the icon
 * itself is unusable, distinct from "usable but empty" — both fall back to the house icon at the
 * resolver, see `Icon.tsx`). */
function parseIconElements(raw: unknown): readonly IconPackElement[] | null {
  if (!Array.isArray(raw)) return null;
  const elements: IconPackElement[] = [];
  for (const item of raw) {
    const parsed = parseElement(item);
    if (parsed !== null) elements.push(parsed);
  }
  return elements;
}

function parseStyle(raw: unknown): IconPackStyle | null {
  if (!isPlainObject(raw)) return null;
  const strokeWidth = raw.strokeWidth;
  if (!isFiniteNumber(strokeWidth) || strokeWidth < MIN_STROKE_WIDTH || strokeWidth > MAX_STROKE_WIDTH) return null;
  const fill = raw.fill;
  return isColorToken(fill) ? { strokeWidth, fill } : null;
}

/**
 * Parses + validates one `gw-icon-pack` document's raw JSON text (SPEC F130.1) into a shape safe
 * to draw, or `null` for anything this module cannot make sense of — malformed JSON, a missing/
 * out-of-range style block, or a non-object `icons` member. NEVER throws: every failure mode
 * (`JSON.parse` itself, a shape surprise) is caught and degrades to `null`, the SAME "house icons"
 * shape an absent/uninstalled active pack already resolves to (SPEC F130.5's fail-open uninstall).
 */
export function parseIconPackDefinition(rawText: string): IconPackDefinition | null {
  let parsed: unknown;
  try {
    parsed = JSON.parse(rawText) as unknown;
  } catch {
    return null;
  }

  if (!isPlainObject(parsed)) return null;

  const style = parseStyle(parsed.style);
  if (style === null) return null;

  const rawIcons = parsed.icons;
  if (!isPlainObject(rawIcons)) return null;

  const icons = createIconAccumulator();
  for (const [name, rawElements] of Object.entries(rawIcons)) {
    // An out-of-shape KEY rejects the WHOLE document — see this module's own "Malformed → skip,
    // never crash" remarks for why a key gate is the one exception to per-primitive skipping.
    if (!isValidIconName(name)) return null;

    const elements = parseIconElements(rawElements);
    if (elements !== null && elements.length > 0) icons[name] = elements;
  }

  return { style, icons };
}
