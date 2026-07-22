"use client";

import type { ReactNode } from "react";
import { personaNameOrFallback, usePersonaDirectory } from "@/lib/use-persona-directory";
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
 *
 * Persona-taste attribution (SPEC F84.1, F84.6-F84.7; STORY-215): each row only carries a bare
 * `personaId` on the wire, so this component additionally owns `usePersonaDirectory` (one fetch
 * per mount, id -> name) and hands `BoothLogFeed` a resolver closure rather than the raw map —
 * `BoothLogFeed` stays a pure presentational component that never has to know the directory's own
 * loading/error shape.
 */
export function BoothLogView({ timeZone }: BoothLogViewProps = {}): ReactNode {
  const feed = useBoothLogFeed();
  const personaDirectory = usePersonaDirectory();

  return (
    <BoothLogFeed
      entries={feed.entries}
      error={feed.error}
      nextBefore={feed.nextBefore}
      loadingMore={feed.loadingMore}
      loadMoreError={feed.loadMoreError}
      onLoadMore={feed.loadMore}
      timeZone={timeZone}
      personaName={(personaId) => personaNameOrFallback(personaDirectory, personaId)}
    />
  );
}
