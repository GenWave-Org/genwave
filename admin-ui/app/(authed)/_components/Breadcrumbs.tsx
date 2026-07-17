"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { ReactNode } from "react";
import { useBreadcrumbTitleOverride } from "./BreadcrumbTitle";

// Section labels for the leading breadcrumb — mirrors the Sidebar's nav copy.
// "libraries" is deliberately absent: /libraries is a redirect into the Catalog
// page's Libraries tab (Q7, SPEC F28.11) and is never itself a rendered route.
const SECTION_LABELS: Record<string, string> = {
  dashboard: "Dashboard",
  live: "Live",
  catalog: "Catalog",
  "safe-content": "Safe content",
  settings: "Settings",
};

/**
 * Shell breadcrumb trail — renders only on nested routes (more than one path
 * segment), e.g. catalog/[mediaId]. Top-level section pages render nothing
 * (SPEC F28.5). The trailing crumb is the raw last path segment (e.g. a
 * mediaId) unless a nested page has claimed a human-readable title via
 * <BreadcrumbTitle> (SPEC F28.5, STORY-090 AC4 — e.g. Catalog → <track title>).
 */
export function Breadcrumbs(): ReactNode {
  const pathname = usePathname();
  const titleOverride = useBreadcrumbTitleOverride();
  const segments = pathname.split("/").filter((segment) => segment.length > 0);

  if (segments.length < 2) {
    return null;
  }

  const section = segments[0];
  const rawLeaf = segments[segments.length - 1];
  if (section === undefined || rawLeaf === undefined) {
    return null;
  }

  const sectionLabel = SECTION_LABELS[section] ?? section;
  const leafLabel = titleOverride ?? rawLeaf;

  return (
    <nav aria-label="Breadcrumb">
      <ol className="flex items-center gap-2 text-[0.82rem] text-mute">
        <li>
          <Link href={`/${section}`} className="hover:text-ink">
            {sectionLabel}
          </Link>
        </li>
        <li aria-hidden="true">/</li>
        <li aria-current="page" className="font-semibold text-ink">
          {leafLabel}
        </li>
      </ol>
    </nav>
  );
}
