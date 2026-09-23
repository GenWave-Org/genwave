"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { useId } from "react";
import { logout } from "@/app/login/actions";
import { cn } from "@/lib/utils";
import { Icon } from "./Icon";
import {
  NAV_BOTTOM,
  NAV_FOOTER,
  NAV_GROUP_HEADING_CLASSES,
  NAV_LINK_CLASSES,
  NAV_TOP,
  isActiveSection,
  visibleNavGroups,
  type NavGroup,
  type NavItem,
} from "./nav-items";
import { useNavGroupOpenState } from "./useNavGroupOpenState";

export interface NavSectionsProps {
  /** Current route (`usePathname()`) — drives both the active-link highlight and which group
   * opens by the route rule (SPEC F203.2). */
  pathname: string;
  /** Mirrors `Sidebar`/`MobileNav`'s own `catalogEnabled` prop (PLAN T102, SPEC F90.1). */
  catalogEnabled: boolean;
  /** Called after every nav `Link` click and the sign-out submit. `MobileNav` passes this to
   * close its drawer; `Sidebar` has nothing to close and leaves it undefined. */
  onNavigate?: () => void;
}

/**
 * The nav model both `Sidebar` (persistent, ≥1024px) and `MobileNav` (drawer, <1024px) render:
 * `NAV_TOP`, the visible groups as collapsible sections, `NAV_BOTTOM`, then the `NAV_FOOTER`
 * entries (About, Sign out) — SPEC F203.1's "Sidebar and MobileNav render the same model", now
 * true of the markup itself rather than two hand-kept-in-sync copies of it (PLAN T559 fix round,
 * finding #5). Neither component owns any of this JSX; they differ only in the shell around it (a
 * persistent `<aside>` vs a Radix `Dialog.Content` drawer) and in whether a navigation should also
 * close that shell (`onNavigate`).
 */
export function NavSections({ pathname, catalogEnabled, onNavigate }: NavSectionsProps): ReactNode {
  const idRoot = useId();
  const { isOpen, toggle } = useNavGroupOpenState(pathname);
  const groups = visibleNavGroups(catalogEnabled);

  return (
    <>
      <nav aria-label="Sections" className="flex-1 px-3">
        <ul className="flex flex-col gap-1">
          {NAV_TOP.map((item) => (
            <NavLinkItem key={item.href} item={item} pathname={pathname} onNavigate={onNavigate} />
          ))}
        </ul>

        <div className="mt-2 flex flex-col gap-1">
          {groups.map((group) => (
            <NavGroupSection
              key={group.id}
              group={group}
              idRoot={idRoot}
              pathname={pathname}
              open={isOpen(group.id)}
              onToggle={() => toggle(group.id)}
              onNavigate={onNavigate}
            />
          ))}
        </div>

        <ul className="mt-2 flex flex-col gap-1">
          {NAV_BOTTOM.map((item) => (
            <NavLinkItem key={item.href} item={item} pathname={pathname} onNavigate={onNavigate} />
          ))}
        </ul>
      </nav>

      <NavFooter onNavigate={onNavigate} />
    </>
  );
}

/** One `<li><Link></li>` nav destination, active-link highlight included (SPEC F28.5) — the block
 * every list in this file renders, instead of six near-identical copies across `Sidebar` and
 * `MobileNav` (finding #5). */
function NavLinkItem({
  item,
  pathname,
  onNavigate,
}: {
  item: NavItem;
  pathname: string;
  onNavigate?: () => void;
}): ReactNode {
  const active = isActiveSection(pathname, item.href);
  return (
    <li>
      <Link
        href={item.href}
        aria-current={active ? "page" : undefined}
        onClick={onNavigate}
        className={cn(
          NAV_LINK_CLASSES,
          active ? "bg-accent/10 text-accent" : "text-mute hover:bg-surface hover:text-ink"
        )}
      >
        <Icon name={item.iconName} className="shrink-0" />
        {item.label}
      </Link>
    </li>
  );
}

/** One collapsible group section — a real `<button aria-expanded aria-controls>` heading (a11y)
 * over its `<ul>` of `NavLinkItem`s (SPEC F203.2). */
function NavGroupSection({
  group,
  idRoot,
  pathname,
  open,
  onToggle,
  onNavigate,
}: {
  group: NavGroup;
  idRoot: string;
  pathname: string;
  open: boolean;
  onToggle: () => void;
  onNavigate?: () => void;
}): ReactNode {
  const listId = `${idRoot}-group-${group.id}`;
  return (
    <div>
      <button
        type="button"
        aria-expanded={open}
        aria-controls={listId}
        onClick={onToggle}
        className={NAV_GROUP_HEADING_CLASSES}
      >
        <span>{group.label}</span>
        <span
          aria-hidden="true"
          className={cn("transition-transform duration-[120ms] ease-out", open && "rotate-90")}
        >
          ▸
        </span>
      </button>
      <ul id={listId} hidden={!open} className="flex flex-col gap-1 pb-1">
        {group.items.map((item) => (
          <NavLinkItem key={item.href} item={item} pathname={pathname} onNavigate={onNavigate} />
        ))}
      </ul>
    </div>
  );
}

/** The footer's About link + Sign out submit (`NAV_FOOTER`, STORY-474 AC5) — a real `<footer>`
 * containing both, so "About sits in the footer beside Sign out" is true of the rendered markup
 * (PLAN T559 fix round, finding #4). */
function NavFooter({ onNavigate }: { onNavigate?: () => void }): ReactNode {
  return (
    <footer className="flex flex-col gap-1 border-t border-line px-3 py-4">
      {NAV_FOOTER.map((entry) =>
        entry.kind === "link" ? (
          <Link
            key={entry.href}
            href={entry.href}
            onClick={onNavigate}
            className={cn(NAV_LINK_CLASSES, "text-mute hover:bg-surface hover:text-ink")}
          >
            {entry.iconName ? <Icon name={entry.iconName} className="shrink-0" /> : null}
            {entry.label}
          </Link>
        ) : (
          <form key="sign-out" action={logout} onSubmit={onNavigate}>
            <button
              type="submit"
              className={cn(NAV_LINK_CLASSES, "w-full text-mute hover:bg-surface hover:text-ink")}
            >
              <Icon name={entry.iconName ?? "sign-out"} className="shrink-0" />
              {entry.label}
            </button>
          </form>
        )
      )}
    </footer>
  );
}
