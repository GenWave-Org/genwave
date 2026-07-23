import type { ReactNode } from "react";
import type { BoothLogFiredRule, BoothLogPick } from "@/lib/booth-log-api";
import { cn } from "@/lib/utils";
import { ExplorationIcon } from "./icons";

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
 */
export function PickChips({ pick, className }: PickChipsProps): ReactNode {
  if (pick === undefined) return null;

  if (pick.isExploration) {
    return <ExplorationBadge className={className} />;
  }

  if (pick.firedRules.length === 0) return null;

  return (
    <ul aria-label="Fired rules" className={cn("m-0 flex list-none flex-wrap gap-1.5 p-0", className)}>
      {pick.firedRules.map((rule, index) => (
        // Rule summaries are operator prose (F86.1), not guaranteed unique per row (e.g. two
        // different rules both matching "this pick") — index is stable within one immutable
        // stamped pick, which never re-orders under this component.
        <li key={`${rule.summary}-${index}`}>
          <RuleChip rule={rule} />
        </li>
      ))}
    </ul>
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
function ExplorationBadge({ className }: { className?: string }): ReactNode {
  return (
    <span
      className={cn(
        "inline-flex w-fit items-center gap-1 rounded-[999px] border border-accent-2 px-2 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-accent-2",
        className
      )}
    >
      <ExplorationIcon />
      Exploration pick
    </span>
  );
}

/** Always-signed weight, one-to-two decimal places (SPEC F86.3's "+0.6" example) — `signDisplay:
 * "always"` puts the `+` on a positive weight explicitly rather than relying on the ambient
 * absence of a `-` to imply it. */
function formatSignedWeight(weight: number): string {
  return new Intl.NumberFormat("en-US", {
    signDisplay: "always",
    minimumFractionDigits: 1,
    maximumFractionDigits: 2,
  }).format(weight);
}
