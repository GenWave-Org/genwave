import type { ReactNode } from "react";
import type { BoothLogFiredRule, BoothLogPick } from "@/lib/booth-log-api";
import { cn } from "@/lib/utils";
import { Icon } from "./Icon";

interface PickChipsProps {
  /** The stamped row's `pick` field, or `undefined` for a row/airing with no stamp at all (SPEC
   * F86.2) — `undefined` renders nothing, not an empty container, so an unstamped row's markup is
   * byte-identical to before this component existed. */
  pick: BoothLogPick | undefined;
  className?: string;
}

/**
 * Fired-rule chips + exploration badge (SPEC F86.3-F86.5, STORY-217, PLAN T75) — shared between
 * the booth log (born here) and the Live now-playing card (T76 reuses this same component against
 * the same stamped booth-log row the taste thumbs already target, F86.4), so the two surfaces can
 * never drift onto different "why this pick" renderings.
 *
 * Chips XOR badge is enforced STRUCTURALLY, not by trusting the wire's own invariant that an
 * exploration pick's `firedRules` is always empty (F83.2): `isExploration` is checked first and
 * short-circuits to the badge alone, so a malformed payload that somehow carried both could never
 * render both.
 *
 * The rotation chip (SPEC F151.4, STORY-371, PLAN T370) is neither — it renders ALONGSIDE whichever
 * of the two above (or alone, when a rung-0 pick fired no taste rules): `pick.nudge` is independent
 * of `isExploration`/`firedRules` by construction (the ranker's own additive rotation term applies
 * to every rung-0 pick regardless of taste bias or the exploration roll, SPEC F151.1). LOW (T370
 * review): it is NEVER an `<li>` inside the `aria-label="Fired rules"` list — a rotation nudge is
 * not a fired taste rule, so it sits outside that list as its own sibling, the same way
 * `ExplorationBadge` already does. Both call sites guard `pick.nudge !== undefined` themselves
 * (the SAME contract `RuleChip`'s own caller uses — a plain, always-defined `nudge: number` prop,
 * never an internal null-check inside the chip component).
 */
export function PickChips({ pick, className }: PickChipsProps): ReactNode {
  if (pick === undefined) return null;

  if (pick.isExploration) {
    return (
      <span className={cn("inline-flex flex-wrap items-center gap-1.5", className)}>
        <ExplorationBadge />
        {pick.nudge !== undefined && <RotationChip nudge={pick.nudge} />}
      </span>
    );
  }

  if (pick.firedRules.length === 0 && pick.nudge === undefined) return null;

  return (
    <span className={cn("inline-flex flex-wrap items-center gap-1.5", className)}>
      {pick.firedRules.length > 0 && (
        <ul aria-label="Fired rules" className="m-0 flex list-none flex-wrap gap-1.5 p-0">
          {pick.firedRules.map((rule, index) => (
            // Rule summaries are operator prose (F86.1), not guaranteed unique per row (e.g. two
            // different rules both matching "this pick") — index is stable within one immutable
            // stamped pick, which never re-orders under this component.
            <li key={`${rule.summary}-${index}`}>
              <RuleChip rule={rule} />
            </li>
          ))}
        </ul>
      )}
      {pick.nudge !== undefined && <RotationChip nudge={pick.nudge} />}
    </span>
  );
}

/** One fired-rule chip (SPEC F86.3): "{summary} {signed weight}", e.g. "The Weeknd +0.6" — a
 * single text node (not summary/weight split across elements) so it reads and queries as one
 * scannable phrase, matching the 3px-radius bordered source-tag convention (SourceChip,
 * BoothLogKindBadge) rather than the pill treatment reserved for state badges. */
function RuleChip({ rule }: { rule: BoothLogFiredRule }): ReactNode {
  return (
    <span className="inline-flex items-center rounded-[3px] border border-line bg-surface-2 px-1.5 py-0.5 text-[0.72rem] tabular-nums text-ink">
      {rule.summary} {formatSignedWeight(rule.weight)}
    </span>
  );
}

/** The exploration badge (SPEC F86.5): pill treatment (999px) reserved for state badges (ON AIR)
 * rather than the rule chips' bordered-tag treatment — an exploration pick is a state of the
 * pick itself, not one attributed rule among several. */
function ExplorationBadge(): ReactNode {
  return (
    <span className="inline-flex w-fit items-center gap-1 rounded-[999px] border border-accent-2 px-2 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-accent-2">
      <Icon name="exploration" />
      Exploration pick
    </span>
  );
}

/** The rotation chip (SPEC F151.4, STORY-371, PLAN T370): "Rotation {signed nudge}", e.g.
 * "Rotation +0.6" — the caller (`PickChips`) always guards `pick.nudge !== undefined` before
 * rendering this (LOW, T370 review: the SAME always-defined-prop contract `RuleChip` uses — no
 * internal null-check here). The backend already gates on `|nudge| >= 0.2` (SPEC F86 chip
 * threshold) before ever putting a value on the wire; this component never re-applies it.
 * Bordered-tag treatment, matching `RuleChip` — a rotation nudge is a scored term like a fired
 * taste rule, not a pick-wide state the way exploration is. */
function RotationChip({ nudge }: { nudge: number }): ReactNode {
  return (
    <span className="inline-flex items-center rounded-[3px] border border-line bg-surface-2 px-1.5 py-0.5 text-[0.72rem] tabular-nums text-ink">
      Rotation {formatSignedNudge(nudge)}
    </span>
  );
}

/** Always-signed number formatting shared by `formatSignedWeight`/`formatSignedNudge` (LOW, T370
 * review — one `Intl.NumberFormat` shape, parameterized by `maximumFractionDigits`, rather than two
 * independently hand-rolled configs that could drift). `signDisplay: "always"` puts the `+` on a
 * positive value explicitly rather than relying on the ambient absence of a `-` to imply it. */
function formatSigned(value: number, maximumFractionDigits: number): string {
  return new Intl.NumberFormat("en-US", {
    signDisplay: "always",
    minimumFractionDigits: 1,
    maximumFractionDigits,
  }).format(value);
}

/** A fired rule's signed weight, one-to-two decimal places (SPEC F86.3's "+0.6" example). */
function formatSignedWeight(weight: number): string {
  return formatSigned(weight, 2);
}

/** A rotation nudge's signed value, exactly one decimal place (SPEC F151.4's "Rotation +0.6"
 * example) — narrower than `formatSignedWeight` since a nudge's own domain (`[-1, 1]`) never needs
 * a second decimal. */
function formatSignedNudge(nudge: number): string {
  return formatSigned(nudge, 1);
}
