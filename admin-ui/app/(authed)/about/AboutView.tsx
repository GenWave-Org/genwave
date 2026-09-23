import type { ReactNode } from "react";
import type { AboutResponseDto, AttributionGroupDto } from "@/lib/about-api";
import { formatUptime } from "@/lib/format-clock";
import { isHttpUrl } from "@/lib/safe-external-url";

/** Plain labels for the fixed group order `AttributionsController` projects in (SPEC F169.2) — no
 * radio jargon (Dean's copy rule): "Jingle packs" not "beds", "Font packs"/"Voice packs" read as
 * shipped. An unrecognized future kind falls back to its own raw token rather than failing to
 * render at all. */
const KIND_LABELS: Record<string, string> = {
  "jingle-pack": "Jingle packs",
  "font-pack": "Font packs",
  "voice-pack": "Voice packs",
};

function kindLabel(kind: string): string {
  return KIND_LABELS[kind] ?? kind;
}

/**
 * The About page's own facts, rendered from an already-typed {@link AboutResponseDto} (SPEC
 * F207.1, STORY-474, PLAN T561) — split out from `page.tsx`'s server-side fetch so a jest spec can
 * render this directly off a fixture response with no network/cookies involved, the same
 * page-vs-presentation split this codebase already uses (`SettingsForm`, `GardenerSection`).
 */
export function AboutView({ about }: { about: AboutResponseDto }): ReactNode {
  const hasAttributions = about.attributions.groups.some((group) => group.packs.length > 0);

  return (
    <div className="mt-4 space-y-6">
      <section aria-label="Version and station">
        <p className="text-[0.85rem] text-mute">Version {about.version}</p>
        <h2 className="mt-1 font-display text-[1.1rem] font-semibold text-ink">{about.stationName}</h2>
        {about.tagline.trim() !== "" ? (
          <p data-testid="station-tagline" className="mt-1 text-[0.85rem] text-mute">
            {about.tagline}
          </p>
        ) : null}
      </section>

      <section aria-label="Library and uptime" className="space-y-1 text-[0.85rem] text-ink">
        <p>Tracks in the library: {about.libraryCount}</p>
        <p>Up for {formatUptime(about.uptimeSeconds)}</p>
      </section>

      <section aria-label="Credits">
        <h3 className="font-display text-[0.95rem] font-semibold text-ink">Credits</h3>
        {!hasAttributions ? (
          <p className="mt-2 text-[0.85rem] text-mute">Nothing to credit yet.</p>
        ) : (
          <div className="mt-2 space-y-4">
            {about.attributions.groups.map((group) => (
              <AttributionGroupSection key={group.kind} group={group} />
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

function AttributionGroupSection({ group }: { group: AttributionGroupDto }): ReactNode {
  if (group.packs.length === 0) return null;

  return (
    <div>
      <h4 className="text-[0.8rem] font-semibold uppercase tracking-[0.08em] text-mute">{kindLabel(group.kind)}</h4>
      <ul className="mt-1 space-y-2">
        {group.packs.map((pack) => (
          <li key={pack.slug}>
            <p data-testid="attribution-pack-name" className="text-[0.85rem] font-semibold text-ink">
              {pack.name}
            </p>
            <ul className="ml-3 mt-0.5 space-y-0.5 text-[0.8rem] text-mute">
              {pack.attributions.map((line, index) => (
                <li key={`${pack.slug}-${index}`}>
                  {line.sourceUrl !== null && isHttpUrl(line.sourceUrl) ? (
                    <a
                      href={line.sourceUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="text-accent underline underline-offset-2"
                    >
                      {line.title}
                    </a>
                  ) : (
                    line.title
                  )}
                  {line.creator !== null ? ` — ${line.creator}` : ""} ({line.license})
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ul>
    </div>
  );
}
