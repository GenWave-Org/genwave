"use client";

import type { ReactNode } from "react";
import {
  BoothLogIcon,
  CatalogIcon,
  CloseIcon,
  DashboardIcon,
  EditorIcon,
  ExplorationIcon,
  HealthIcon,
  LiveIcon,
  MenuIcon,
  MoonIcon,
  PersonaCatalogIcon,
  PersonaIcon,
  RestoreIcon,
  SafeContentIcon,
  ScheduleIcon,
  SettingsIcon,
  ShowsIcon,
  SignOutIcon,
  SunIcon,
  TasteThumbDownIcon,
  TasteThumbUpIcon,
  VoteDownIcon,
  VoteUpIcon,
  WardrobeIcon,
  type IconProps,
} from "./icons";
import { useActiveIconPackDefinition } from "./IconPackContext";
import { IconPackGlyph } from "./IconPackRenderer";

/**
 * The icon-name contract's own house table (SPEC F130.2, STORY-337, PLAN T304) — every icon slot
 * the admin chrome renders, mapped to its `icons.tsx` component, mirroring
 * `GenWave.Host.Icons.IconNameContract.Names` (kebab-cased, "Icon"-suffix-stripped, in that file's
 * own declaration order — see that type's own PARITY remarks for the C#-side half of this pin;
 * `icon-pack-renderer.spec.tsx`'s own `TheHouseIconTableMatchesIconsTsxExports` fact pins the TS
 * side the identical way). `as const satisfies Record<...>` (not a bare object literal): TypeScript
 * excess-property AND completeness checking on the object literal means a renamed/added/removed
 * `icons.tsx` export shows up as a `tsc --noEmit` failure here, not a silent drift a runtime test
 * would have to catch.
 */
const HOUSE_ICONS = {
  dashboard: DashboardIcon,
  live: LiveIcon,
  catalog: CatalogIcon,
  "safe-content": SafeContentIcon,
  health: HealthIcon,
  persona: PersonaIcon,
  "persona-catalog": PersonaCatalogIcon,
  "booth-log": BoothLogIcon,
  settings: SettingsIcon,
  "sign-out": SignOutIcon,
  sun: SunIcon,
  moon: MoonIcon,
  menu: MenuIcon,
  close: CloseIcon,
  "vote-up": VoteUpIcon,
  "vote-down": VoteDownIcon,
  restore: RestoreIcon,
  "taste-thumb-up": TasteThumbUpIcon,
  "taste-thumb-down": TasteThumbDownIcon,
  schedule: ScheduleIcon,
  shows: ShowsIcon,
  wardrobe: WardrobeIcon,
  editor: EditorIcon,
  exploration: ExplorationIcon,
} as const satisfies Record<string, (props: IconProps) => ReactNode>;

/** The icon-name contract (SPEC F130.2) — every name a pack MAY cover, and every name a call site
 * may request via `Icon`. */
export type IconName = keyof typeof HOUSE_ICONS;

/** Every contract name, in `icons.tsx`'s own declaration order — exported for the parity fact
 * (`icon-pack-renderer.spec.tsx`) rather than each spec re-deriving `Object.keys(HOUSE_ICONS)`
 * itself. */
export const ICON_NAMES: readonly IconName[] = Object.keys(HOUSE_ICONS) as IconName[];

export interface NamedIconProps extends IconProps {
  name: IconName;
}

/**
 * Resolves ONE icon-chrome slot to its active-pack glyph, falling back per-name to the house
 * component (SPEC F130.3 "a safe renderer... any name the active pack lacks renders the house
 * icon", F130.2's per-subset coverage) — the swap point every nav/control icon in the admin chrome
 * now renders through (PLAN T304), replacing what used to be a direct `icons.tsx` import at each
 * call site. `useActiveIconPackDefinition`'s own default (`null`, no `IconPackProvider` ancestor)
 * already reads as "house icons", so this degrades identically whether the active pack is
 * genuinely unset, mid-fail-open after an uninstall (SPEC F130.5), or simply absent from an
 * isolated render (a component test with no shell above it) — see `IconPackContext.tsx`'s own
 * remarks.
 *
 * An icon whose pack-declared element list parsed down to zero USABLE elements (every primitive
 * malformed) is treated exactly like a name the pack never declared at all — falls through to the
 * house component, never an empty `<svg>`.
 */
export function Icon({ name, className }: NamedIconProps): ReactNode {
  const pack = useActiveIconPackDefinition();
  const elements = pack?.icons[name];

  if (pack && elements && elements.length > 0) {
    return <IconPackGlyph elements={elements} style={pack.style} className={className} />;
  }

  const HouseIcon = HOUSE_ICONS[name];
  return <HouseIcon className={className} />;
}
