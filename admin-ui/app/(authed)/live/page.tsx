import { redirect } from "next/navigation";

/**
 * `/live` is retired (SPEC F203.4) — the booth log narrates every pick, ratings live on Catalog
 * rows, and this route's own now-playing/history/rating surface duplicated that. This route
 * stays live only to redirect old links/bookmarks; the nav hasn't listed it since T558.
 */
export default function LivePage(): never {
  redirect("/");
}
