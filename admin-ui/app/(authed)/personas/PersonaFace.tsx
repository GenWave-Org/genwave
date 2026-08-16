"use client";

import { useState, type ReactNode } from "react";
import { PersonaIcon } from "../_components/icons";

export interface PersonaFaceProps {
  /** The persona whose face this renders — the admin read route (PLAN T296, Host's
   * `PersonaAvatarController.Get`) is keyed by this id, never a token: an authed operator reading
   * their own console has no need for the F88/opaque-token capability-URL indirection the public
   * spectator door (T298) will use instead. */
  personaId: number;
  /** Used only for the `alt`/placeholder `aria-label` text — never rendered as anything a write
   * could act on. */
  personaName: string;
  /** Cache-bust key, bumped by the parent (`PersonasClient`'s own `avatarVersion` state) after
   * EVERY upload/remove/apply-from-pack write this session — the write responses each carry a
   * freshly-rotated token (SPEC F129.1), but this component never threads that token through: a
   * plain incrementing counter is enough to force a fresh request past whatever the browser cached
   * under this SAME url on the previous version, and `key={version}` below both remounts the `img`
   * (a clean fresh element, never a stale `src` the browser might reuse) and resets this
   * component's own `hasError` state, so a face that just replaced a prior FAILURE renders instead
   * of staying stuck on the placeholder. 0 (the default, first mount) never appends a query param —
   * the plain, ETag-conditional GET is what keeps an ordinary page load cheap; only a write WITHIN
   * this session needs the cache-bust. */
  version?: number;
  /** "sm" for the roster row's inline thumbnail, "lg" for the editor's own portrait. */
  size?: "sm" | "lg";
}

const SIZE_CLASSES: Record<NonNullable<PersonaFaceProps["size"]>, string> = {
  sm: "h-9 w-9",
  lg: "h-24 w-24",
};

/**
 * The worn face — persona card/roster-row AND editor alike (SPEC F128.9, STORY-333, PLAN T296): a
 * plain `<img>` against the admin read route, degrading to the neutral Wireless placeholder on
 * ANY load failure (a genuinely faceless persona's honest 404, a transient network error, an
 * unauthenticated session alike — this component draws no distinction, mirrors
 * `AvatarItemFace`'s own "no gate/reason detail" restraint one level up in scale) — NEVER a broken
 * image icon. `key={version}` is what makes a write-triggered re-render actually re-attempt the
 * `img` load rather than staying wedged on a stale `hasError` from before the write.
 */
export function PersonaFace({ personaId, personaName, version = 0, size = "sm" }: PersonaFaceProps): ReactNode {
  return (
    <PersonaFaceImage
      key={version}
      personaId={personaId}
      personaName={personaName}
      version={version}
      size={size}
    />
  );
}

function PersonaFaceImage({
  personaId,
  personaName,
  version,
  size,
}: Required<PersonaFaceProps>): ReactNode {
  const [hasError, setHasError] = useState(false);
  const dimensionClass = SIZE_CLASSES[size];

  if (hasError) {
    return <PersonaFacePlaceholder personaName={personaName} dimensionClass={dimensionClass} />;
  }

  const src = `/api/personas/${personaId}/avatar${version > 0 ? `?v=${version}` : ""}`;
  return (
    <img
      src={src}
      alt={`${personaName}'s face`}
      loading="lazy"
      onError={() => setHasError(true)}
      className={`${dimensionClass} shrink-0 rounded-full border border-line object-cover`}
    />
  );
}

/** The neutral Wireless placeholder (SPEC F128.9: "never a broken image") — house icons only, no
 * emoji: reuses `PersonaIcon`, the SAME microphone glyph the nav rail already uses for "DJ
 * persona", rather than a bespoke silhouette this feature would own alone. */
function PersonaFacePlaceholder({
  personaName,
  dimensionClass,
}: {
  personaName: string;
  dimensionClass: string;
}): ReactNode {
  return (
    <span
      role="img"
      aria-label={`${personaName} has no face set`}
      className={`${dimensionClass} inline-flex shrink-0 items-center justify-center rounded-full border border-line bg-surface-2 text-mute`}
    >
      <PersonaIcon className="h-1/2 w-1/2" />
    </span>
  );
}
