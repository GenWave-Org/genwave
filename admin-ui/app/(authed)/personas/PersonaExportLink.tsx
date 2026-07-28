import type { ReactNode } from "react";
import { buttonVariants } from "@/components/ui/button";
import type { PersonaDto } from "./types";

interface PersonaExportLinkProps {
  persona: PersonaDto;
  /** Optional side-effect fired on the anchor's own `click` (PLAN T128, F1 fix) — e.g.
   * `FireModal`'s export-gate tracker. Bound directly to the `<a>`, not a wrapping element: an
   * anchor's click handler only runs for an actual activation (mouse click or keyboard Enter),
   * never for a click that merely lands somewhere nearby in the same container. The roster row
   * call site leaves this unset. */
  onClick?: () => void;
}

/**
 * Export action (SPEC F79.1, STORY-208/209 wiring, PLAN T68): a plain anchor to
 * `GET /api/personas/{slug}/export`, not a fetch+blob re-implementation — the browser's own
 * navigation handles the `Content-Disposition: attachment` download and carries the session
 * cookie same-origin (`next.config.ts`'s `/api/*` rewrite already proxies this to the backend),
 * exactly like any other download link. `buttonVariants` styles it as a secondary action button
 * so it reads consistently alongside Edit/Delete rather than as a bare text link.
 *
 * `href` uses `persona.slug` — the server's own stored slug (PLAN T128 review fix) — NEVER a
 * client-side `personaSlug(persona.name)` re-derivation: an imported persona's slug can diverge
 * from a fresh slugify of its current name (the import route's slug and the card's `name` field
 * are independent — see `PersonaDto.slug`'s own remarks), which 404'd this exact link inside the
 * Fire modal's export-first parachute. The Fire modal's Delete gate is click-based, not
 * response-based, so that 404 used to fail silently at precisely the moment the parachute matters.
 */
export function PersonaExportLink({ persona, onClick }: PersonaExportLinkProps): ReactNode {
  return (
    <a
      href={`/api/personas/${persona.slug}/export`}
      className={buttonVariants({ variant: "secondary" })}
      aria-label={`Export ${persona.name}`}
      onClick={onClick}
    >
      Export
    </a>
  );
}
