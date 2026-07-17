import { redirect } from "next/navigation";

/**
 * `/libraries` is retired as a standalone page (SPEC F28.11, STORY-089 AC4) — its CRUD content
 * now lives in the Catalog page's Libraries tab (`app/(authed)/catalog/LibrariesTab.tsx`). This
 * route stays live only to redirect old links/bookmarks; the sidebar has never listed it
 * (unlisted since Q3).
 */
export default function LibrariesPage(): never {
  redirect("/catalog?tab=libraries");
}
