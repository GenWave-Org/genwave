import type { ReactNode } from "react";
import { buttonVariants } from "@/components/ui/button";
import { personaSlug } from "./persona-slug";
import type { PersonaDto } from "./types";

interface PersonaExportLinkProps {
  persona: PersonaDto;
}

/**
 * Export action (SPEC F79.1, STORY-208/209 wiring, PLAN T68): a plain anchor to
 * `GET /api/personas/{slug}/export`, not a fetch+blob re-implementation — the browser's own
 * navigation handles the `Content-Disposition: attachment` download and carries the session
 * cookie same-origin (`next.config.ts`'s `/api/*` rewrite already proxies this to the backend),
 * exactly like any other download link. `buttonVariants` styles it as a secondary action button
 * so it reads consistently alongside Edit/Activate/Delete rather than as a bare text link.
 */
export function PersonaExportLink({ persona }: PersonaExportLinkProps): ReactNode {
  return (
    <a
      href={`/api/personas/${personaSlug(persona.name)}/export`}
      className={buttonVariants({ variant: "secondary" })}
      aria-label={`Export ${persona.name}`}
    >
      Export
    </a>
  );
}
