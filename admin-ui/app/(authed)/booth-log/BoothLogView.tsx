"use client";

import type { ReactNode } from "react";
import { BoothLogFeed } from "./BoothLogFeed";
import { useBoothLogFeed } from "./useBoothLogFeed";

interface BoothLogViewProps {
  /** Test-only injection point, threaded to the row timestamp formatter; production omits this and gets the browser's local zone. */
  timeZone?: string;
}

/**
 * The Booth log page's content (PLAN T40, STORY-195, SPEC F72.1-F72.2): the narrative feed —
 * track starts, patter airs, mode changes — newest-first with keyset "Load more" paging. State
 * lives in `useBoothLogFeed`; this component only wires it to the presentational `BoothLogFeed`.
 */
export function BoothLogView({ timeZone }: BoothLogViewProps = {}): ReactNode {
  const feed = useBoothLogFeed();

  return (
    <BoothLogFeed
      entries={feed.entries}
      error={feed.error}
      nextBefore={feed.nextBefore}
      loadingMore={feed.loadingMore}
      loadMoreError={feed.loadMoreError}
      onLoadMore={feed.loadMore}
      timeZone={timeZone}
    />
  );
}
