"use client";

import { useSyncExternalStore } from "react";
import { NAV_GROUPS, groupIdForPathname, type NavGroupId } from "./nav-items";

/** localStorage key for the manually-opened nav groups (SPEC F203.2, PLAN T559) — a single JSON
 * array of {@link NavGroupId} strings. A group stored here stays open on its own route too
 * (storage isn't purged on route change); collapsing it there is a same-session-only override —
 * it lasts until the next full load or until another route's group is collapsed (see
 * {@link useNavGroupOpenState}'s own remarks). */
export const NAV_OPEN_STORAGE_KEY = "genwave.nav.open";

const KNOWN_GROUP_IDS: ReadonlySet<string> = new Set(NAV_GROUPS.map((group) => group.id));

/** True when `value` is a real, current group id — not just any string. Guards against stale or
 * hand-edited storage (a retired group id, plain corruption) leaking into the open set. */
function isNavGroupId(value: unknown): value is NavGroupId {
  return typeof value === "string" && KNOWN_GROUP_IDS.has(value);
}

const EMPTY_OPEN_IDS: ReadonlySet<NavGroupId> = new Set();

/** Reads the persisted set of manually-opened group ids. Any failure — no `localStorage` (SSR),
 * a browser that throws on access, a full/blocked store, malformed JSON, a non-array payload,
 * unrecognized ids — degrades to "nothing stored" rather than throwing (SPEC F203.2's storage
 * try/catch). */
function readStoredOpenIds(): ReadonlySet<NavGroupId> {
  try {
    const raw = window.localStorage.getItem(NAV_OPEN_STORAGE_KEY);
    if (raw === null) {
      return EMPTY_OPEN_IDS;
    }
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) {
      return EMPTY_OPEN_IDS;
    }
    const ids = parsed.filter(isNavGroupId);
    return ids.length > 0 ? new Set(ids) : EMPTY_OPEN_IDS;
  } catch {
    return EMPTY_OPEN_IDS;
  }
}

/** Persists `openIds`. Swallows any failure — full/blocked/absent storage — so a toggle click
 * never throws; the in-memory store below still holds the click's effect for the rest of the
 * session, it just won't survive a reload (SPEC F203.2, AC7). */
function writeStoredOpenIds(openIds: ReadonlySet<NavGroupId>): void {
  try {
    window.localStorage.setItem(NAV_OPEN_STORAGE_KEY, JSON.stringify([...openIds]));
  } catch {
    // Storage unavailable this session — nothing to recover, nothing to surface.
  }
}

export interface NavGroupOpenState {
  isOpen(groupId: NavGroupId): boolean;
  toggle(groupId: NavGroupId): void;
}

// ---------------------------------------------------------------------------
// Shared module-level store (PLAN T559 fix round).
//
// `Sidebar` and `MobileNav` are both always mounted (the authed layout renders both — the
// persistent one hidden below 1024px, the drawer's trigger hidden above it) as two SEPARATE React
// trees: MobileNav is not a descendant of Sidebar, so a toggle in one can only ever reach the
// other through state that lives OUTSIDE either component tree — React state (`useState`) can't
// do that, no matter how the reading is arranged, because each hook call would own its own copy.
// `useSyncExternalStore` is the supported way to read AND subscribe to that kind of external,
// mutable, cross-tree singleton, and its `getServerSnapshot` argument is exactly the hook for
// degrading to "nothing stored" on the server (`getServerSnapshot` below), so the SSR HTML and the
// client's first paint always agree before hydration — no manual mount-then-effect dance needed to
// dodge a mismatch (the authed layout is a server component rendering these client components;
// `localStorage` doesn't exist there).
//
// `openOverrides` is read from `localStorage` exactly once per page load — on the first CLIENT
// read (`hydrated` below) — then only ever changes through `toggleStoredGroup`, which updates the
// in-memory snapshot FIRST and persists second, so a `setItem` throw still leaves the click's
// effect standing for the rest of the session (it just won't survive a reload).
//
// `collapsedRouteGroupId` tracks a same-session collapse of the CURRENT route's own group by that
// group's id, not a bare boolean (finding #2 of the T559 review round): a route change moves
// `routeGroupId` to a different id, so `collapsedRouteGroupId !== routeGroupId` is true — the new
// route's group reads as open — the instant the new render happens, with no reset effect and thus
// no closed-for-a-frame flash. It is never written to storage: F203.2's "default = route rule"
// means the route's own group always reopens on the next full load.
//
// `resetForTests` exists because Jest doesn't reset modules between `it`s in the same file (see
// `jest.setup.ts`'s `toast.dismiss()` for the identical module-global-leak problem, gh-#516) — it
// clears the in-memory copy after every test via that same `afterEach`; specs that toggle a group
// clear `localStorage` themselves.
// ---------------------------------------------------------------------------

interface NavOpenSnapshot {
  openOverrides: ReadonlySet<NavGroupId>;
  collapsedRouteGroupId: NavGroupId | null;
}

const EMPTY_SNAPSHOT: NavOpenSnapshot = { openOverrides: EMPTY_OPEN_IDS, collapsedRouteGroupId: null };

let hydrated = false;
let snapshot: NavOpenSnapshot = EMPTY_SNAPSHOT;
const listeners = new Set<() => void>();

function notify(): void {
  for (const listener of listeners) {
    listener();
  }
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot(): NavOpenSnapshot {
  if (!hydrated) {
    hydrated = true;
    const openOverrides = readStoredOpenIds();
    // Reuse the EMPTY_SNAPSHOT reference (rather than spreading a structurally-identical copy)
    // when nothing was stored, so useSyncExternalStore's Object.is check sees "no change" and
    // skips the otherwise-needless re-render after hydration in the common no-toggle case.
    snapshot = openOverrides === EMPTY_OPEN_IDS ? EMPTY_SNAPSHOT : { ...snapshot, openOverrides };
  }
  return snapshot;
}

/** The server (and pre-hydration client) view: no `localStorage`, so "nothing stored" — matches
 * what `getSnapshot` would return before its own first, client-only, hydration read. */
function getServerSnapshot(): NavOpenSnapshot {
  return EMPTY_SNAPSHOT;
}

function toggleStoredGroup(groupId: NavGroupId): void {
  const next = new Set(getSnapshot().openOverrides);
  if (next.has(groupId)) {
    next.delete(groupId);
  } else {
    next.add(groupId);
  }
  snapshot = { ...snapshot, openOverrides: next };
  writeStoredOpenIds(next);
  notify();
}

function toggleRouteGroup(routeGroupId: NavGroupId): void {
  const current = getSnapshot();
  snapshot = { ...current, collapsedRouteGroupId: current.collapsedRouteGroupId === routeGroupId ? null : routeGroupId };
  notify();
}

/** Test-only: resets the shared store to its pre-hydration state. Test harness only — imported by
 * `jest.setup.ts`'s `afterEach` and by specs that need a mid-test reset — see the module remarks
 * above. */
export function resetNavGroupOpenStateForTests(): void {
  hydrated = false;
  snapshot = EMPTY_SNAPSHOT;
}

/**
 * Which nav groups are open, read from ONE store shared by every `Sidebar`/`MobileNav` instance
 * (SPEC F203.2, PLAN T559) so the two surfaces can never disagree — see the module remarks above
 * for why that has to be a module-level external store rather than component state. On mount, the
 * route's own group opens (F203.2's route rule) and any group the user previously opened by hand —
 * read from `localStorage` — opens alongside it (STORY-471 AC6).
 *
 * The route's own group is deliberately never written to storage: collapsing it is a same-session
 * convenience only, tracked by ITS id so a route change can never leave a stale "collapsed" flag
 * applied to the new route's group (see `collapsedRouteGroupId` above). Every OTHER group's toggle
 * is a standing choice and is persisted immediately.
 */
export function useNavGroupOpenState(pathname: string): NavGroupOpenState {
  const routeGroupId = groupIdForPathname(pathname);
  const { openOverrides, collapsedRouteGroupId } = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  function isOpen(groupId: NavGroupId): boolean {
    if (groupId === routeGroupId) {
      return collapsedRouteGroupId !== routeGroupId;
    }
    return openOverrides.has(groupId);
  }

  function toggle(groupId: NavGroupId): void {
    if (groupId === routeGroupId) {
      toggleRouteGroup(groupId);
    } else {
      toggleStoredGroup(groupId);
    }
  }

  return { isOpen, toggle };
}
