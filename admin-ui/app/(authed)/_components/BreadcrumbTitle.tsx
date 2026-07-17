"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

interface BreadcrumbTitleContextValue {
  title: string | null;
  setTitle: (title: string | null) => void;
}

// Default (no provider mounted, or no page has claimed the slot yet):
// Breadcrumbs falls back to the raw last path segment (e.g. a mediaId) —
// Q3's original behavior. Only nested-route pages that know a human title
// mount <BreadcrumbTitle> to claim it.
const BreadcrumbTitleContext = createContext<BreadcrumbTitleContextValue>({
  title: null,
  setTitle: () => {},
});

interface BreadcrumbTitleProviderProps {
  children: ReactNode;
}

/**
 * Mounted once in the authed shell layout, wrapping both the header
 * (Breadcrumbs) and the routed page content, so a page several levels below
 * the header can still reach up and set the trailing crumb (SPEC F28.5,
 * STORY-090 AC4). Plain React Context rather than a module-level store: the
 * latter would be a mutable singleton shared across concurrent SSR requests
 * in the same Node process — a real cross-request leak risk, not just a
 * style preference.
 */
export function BreadcrumbTitleProvider({ children }: BreadcrumbTitleProviderProps): ReactNode {
  const [title, setTitle] = useState<string | null>(null);
  return (
    <BreadcrumbTitleContext.Provider value={{ title, setTitle }}>
      {children}
    </BreadcrumbTitleContext.Provider>
  );
}

/** Read side: Breadcrumbs' override for the trailing crumb, or null to fall back to the raw path segment. */
export function useBreadcrumbTitleOverride(): string | null {
  return useContext(BreadcrumbTitleContext).title;
}

interface BreadcrumbTitleProps {
  /**
   * Human-readable label for the trailing breadcrumb (e.g. a track title).
   * Callers pass their own fallback (e.g. the raw id) when no title is
   * known — this component doesn't distinguish "no override" from
   * "override with the raw id".
   */
  title: string;
}

/**
 * Write side: mounted by a nested-route page to claim the trailing crumb.
 * Clears its title on unmount so navigating to a different nested page
 * never leaves a stale crumb behind.
 */
export function BreadcrumbTitle({ title }: BreadcrumbTitleProps): null {
  const { setTitle } = useContext(BreadcrumbTitleContext);

  useEffect(() => {
    setTitle(title);
    return () => setTitle(null);
  }, [title, setTitle]);

  return null;
}
