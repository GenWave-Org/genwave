import { redirect } from "next/navigation";

/**
 * `/wardrobe` is retired (SPEC F203.4) — its font, icon and avatar pack tabs are replaced by the
 * Community Catalog (`/persona-catalog`). This route stays live only to redirect old links/bookmarks.
 */
export default function WardrobePage(): never {
  redirect("/persona-catalog");
}
