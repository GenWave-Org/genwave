"use client";

import * as Dialog from "@radix-ui/react-dialog";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState, type ReactNode } from "react";
import { logout } from "@/app/login/actions";
import { Tooltip } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";
import { CloseIcon, MenuIcon, SignOutIcon } from "./icons";
import { NAV_ITEMS, NAV_LINK_CLASSES, isActiveSection } from "./nav-items";

const ICON_BUTTON_CLASSES =
  "flex h-10 w-10 shrink-0 items-center justify-center rounded-[6px] text-mute transition-colors duration-[120ms] ease-out hover:bg-surface hover:text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent";

interface MobileNavProps {
  /**
   * Station name for the drawer header wordmark, server-fetched by the
   * authed layout (SPEC F44.7) — mirrors `Sidebar`'s `stationName` prop so
   * both surfaces always agree. Optional here — defaults to "GenWave" — so
   * isolated component tests that render `<MobileNav />` without the shell
   * still see the product brand.
   */
  stationName?: string;
}

/**
 * Sub-1024px nav drawer (SPEC F28.13): a hamburger in the shell header opens
 * the same `NAV_ITEMS` the persistent `Sidebar` renders at ≥1024px, inside a
 * Radix Dialog reused for the trap (as confirm-dialog.tsx does — FocusScope
 * + DismissableLayer are Radix's job, not hand-rolled here). Unlike
 * `useConfirm()`, this trigger is a real rendered `<Dialog.Trigger>`, so
 * Radix's built-in onCloseAutoFocus already refocuses it on close with no
 * extra wiring. Every nav Link and the sign-out submit close the drawer
 * explicitly so a route change never leaves it open over the new page. Both
 * icon-only triggers (hamburger, drawer close) carry a hover/focus tooltip
 * with the same copy as their aria-label (SPEC F62.1–F62.2) — the `Tooltip`
 * wraps each `Dialog.Trigger`/`Dialog.Close` from the outside, not as their
 * `asChild` target, so Radix's own prop-cloning onto the real `<button>` is
 * unaffected.
 */
export function MobileNav({ stationName = "GenWave" }: MobileNavProps): ReactNode {
  const pathname = usePathname();
  const [open, setOpen] = useState(false);

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Tooltip label="Open navigation">
        <Dialog.Trigger asChild>
          <button type="button" aria-label="Open navigation" className={cn(ICON_BUTTON_CLASSES, "lg:hidden")}>
            <MenuIcon />
          </button>
        </Dialog.Trigger>
      </Tooltip>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none lg:hidden" />
        <Dialog.Content
          aria-label="Navigation"
          className="fixed inset-y-0 left-0 z-50 flex h-full w-[215px] max-w-[85vw] flex-col border-r-2 border-line bg-surface-2 transition-transform duration-200 ease-out motion-reduce:transition-none data-[state=closed]:-translate-x-full data-[state=open]:translate-x-0 lg:hidden"
        >
          <Dialog.Title className="sr-only">Navigation</Dialog.Title>
          <Dialog.Description className="sr-only">Jump to a section of the console.</Dialog.Description>

          <div className="flex items-center justify-between px-5 py-5">
            <span className="font-display text-xl italic text-ink">{stationName}</span>
            <Tooltip label="Close navigation">
              <Dialog.Close asChild>
                <button type="button" aria-label="Close navigation" className={ICON_BUTTON_CLASSES}>
                  <CloseIcon />
                </button>
              </Dialog.Close>
            </Tooltip>
          </div>

          <nav aria-label="Sections" className="flex-1 px-3">
            <ul className="flex flex-col gap-1">
              {NAV_ITEMS.map(({ href, label, Icon }) => {
                const active = isActiveSection(pathname, href);
                return (
                  <li key={href}>
                    <Link
                      href={href}
                      aria-current={active ? "page" : undefined}
                      onClick={() => setOpen(false)}
                      className={cn(
                        NAV_LINK_CLASSES,
                        active
                          ? "bg-accent/10 text-accent"
                          : "text-mute hover:bg-surface hover:text-ink"
                      )}
                    >
                      <Icon className="shrink-0" />
                      {label}
                    </Link>
                  </li>
                );
              })}
            </ul>
          </nav>

          <form action={logout} className="border-t border-line px-3 py-4" onSubmit={() => setOpen(false)}>
            <button
              type="submit"
              className={cn(NAV_LINK_CLASSES, "w-full text-mute hover:bg-surface hover:text-ink")}
            >
              <SignOutIcon className="shrink-0" />
              Sign out
            </button>
          </form>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
