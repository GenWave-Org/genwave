import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { EditTrackForm } from "./EditTrackForm";
import { MoveToLibraryAction } from "./MoveToLibraryAction";
import { ReanalyzePanel } from "./ReanalyzePanel";
import { BreadcrumbTitle } from "../../_components/BreadcrumbTitle";
import type { LibraryDto } from "@/lib/library";

interface AdminMediaDto {
  mediaId: string;
  locator: string;
  format: string;
  state: string;
  durationMs: number | null;
  title: string | null;
  artist: string | null;
  album: string | null;
  genre: string | null;
  year: number | null;
  integratedLufs: number | null;
  truePeakDbtp: number | null;
  measurable: boolean | null;
  cueInSec: number | null;
  cueOutSec: number | null;
  eligible?: boolean;
  libraryId?: number | null;
  tagsEditedAt?: string | null;
}

interface MediaDetailPageProps {
  params: Promise<{ mediaId: string }>;
}

function formatMs(ms: number): string {
  const totalSeconds = Math.floor(ms / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

function formatNullable(value: number | null, unit?: string): string {
  if (value === null) return "—";
  return unit !== undefined ? `${value} ${unit}` : String(value);
}

/**
 * Brass pill badge (999px radius, `--accent-2`) — the quiet/informational treatment, not the
 * `--danger` warning one: scope is curation, not a trust boundary (SPEC F23.6/F43.6). Mirrors
 * ApplyModeBadge's brass variant (SettingsForm.tsx) and the "Out of rotation" copy in
 * SafeContentClient.tsx.
 */
function NotInRotationBadge(): ReactNode {
  return (
    <span
      aria-label="Not in rotation"
      className="inline-flex items-center rounded-[999px] border border-accent-2 bg-transparent px-2.5 py-1 font-sans text-[0.68rem] font-semibold not-italic uppercase tracking-[0.12em] text-accent-2"
    >
      Not in rotation
    </span>
  );
}

export default async function MediaDetailPage({
  params,
}: MediaDetailPageProps): Promise<ReactNode> {
  const { mediaId } = await params;
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  // Fetch media detail and all libraries in parallel.
  const [mediaResp, librariesResp] = await Promise.all([
    apiGet(`/api/media/${mediaId}`, { cookies: cookieHeader }),
    apiGet("/api/libraries", { cookies: cookieHeader }),
  ]);

  if (mediaResp.status === 403) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Access denied</h1>
        <p className="mt-2 text-[0.85rem] text-mute">
          You do not have permission to view this media item.
        </p>
      </main>
    );
  }

  if (mediaResp.status === 404) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Not found</h1>
        <p className="mt-2 text-[0.85rem] text-mute">The requested media item does not exist.</p>
      </main>
    );
  }

  if (!mediaResp.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Media detail</h1>
        <p className="mt-2 text-[0.85rem] text-mute">Unable to load media item.</p>
      </main>
    );
  }

  const item = (await mediaResp.json()) as AdminMediaDto;
  // Extract the weak ETag returned by the backend for optimistic concurrency
  const etag = mediaResp.headers.get("etag") ?? "";
  // SPEC F43.1/F43.5: the scope 403 is repealed — an out-of-scope row now returns 200 + this
  // header instead of failing the fetch above, so the page must badge it rather than block it.
  const outOfScope = mediaResp.headers.get("x-out-of-scope") === "true";

  // Libraries are best-effort — if they fail, Move-to-library is hidden rather than blocking.
  const libraries: LibraryDto[] =
    librariesResp.ok ? ((await librariesResp.json()) as LibraryDto[]) : [];

  const subtitle = [item.artist, item.album].filter((v): v is string => v !== null).join(" — ");

  return (
    <main className="max-w-3xl">
      {/* Claims the shell's trailing breadcrumb (Catalog → <track title>, falling back to the
          mediaId when untitled) — SPEC F28.5, STORY-090 AC4. */}
      <BreadcrumbTitle title={item.title ?? item.mediaId} />

      <div className="flex flex-wrap items-center gap-3">
        <h1 className="font-display text-[1.6rem] italic text-ink">{item.title ?? item.mediaId}</h1>
        {outOfScope && <NotInRotationBadge />}
      </div>
      {subtitle !== "" && <p className="mt-1 text-[0.9rem] text-mute">{subtitle}</p>}

      <section aria-label="Details" className="mt-6 rounded-[6px] border border-line bg-surface p-6">
        <table className="w-full text-[0.85rem]">
          <tbody className="divide-y divide-line">
            <tr>
              <th scope="row" className="w-40 py-2 pr-4 text-left font-semibold text-accent-2">ID</th>
              {/* AC2 (SPEC F28.13): break-all, not overflow-x-auto — an
                  unbreakable id/path value must never push this 2-column
                  table's min-content width past 390px and scroll the page
                  body sideways (Q11 review). */}
              <td className="break-all py-2 text-ink">{item.mediaId}</td>
            </tr>
            <tr>
              <th scope="row" className="py-2 pr-4 text-left font-semibold text-accent-2">State</th>
              <td className="py-2 text-ink">{item.state}</td>
            </tr>
            <tr>
              <th scope="row" className="py-2 pr-4 text-left font-semibold text-accent-2">Format</th>
              <td className="py-2 text-ink">{item.format}</td>
            </tr>
            <tr>
              <th scope="row" className="py-2 pr-4 text-left font-semibold text-accent-2">Duration</th>
              <td className="py-2 tabular-nums text-ink">
                {item.durationMs !== null ? formatMs(item.durationMs) : "—"}
              </td>
            </tr>
            <tr>
              <th scope="row" className="py-2 pr-4 text-left font-semibold text-accent-2">Loudness (LUFS)</th>
              <td className="py-2 tabular-nums text-ink">{formatNullable(item.integratedLufs, "LUFS")}</td>
            </tr>
            <tr>
              <th scope="row" className="py-2 pr-4 text-left font-semibold text-accent-2">True peak (dBTP)</th>
              <td className="py-2 tabular-nums text-ink">{formatNullable(item.truePeakDbtp, "dBTP")}</td>
            </tr>
            <tr>
              <th scope="row" className="py-2 pr-4 text-left font-semibold text-accent-2">Cue in</th>
              <td className="py-2 tabular-nums text-ink">{formatNullable(item.cueInSec, "s")}</td>
            </tr>
            <tr>
              <th scope="row" className="py-2 pr-4 text-left font-semibold text-accent-2">Cue out</th>
              <td className="py-2 tabular-nums text-ink">{formatNullable(item.cueOutSec, "s")}</td>
            </tr>
            <tr>
              <th scope="row" className="py-2 pr-4 text-left font-semibold text-accent-2">Locator</th>
              <td className="break-all py-2 text-ink">{item.locator}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <div className="mt-6 rounded-[6px] border border-line bg-surface p-6">
        <h2 className="font-display text-[1.05rem] font-semibold text-ink">Edit</h2>
        <div className="mt-4">
          <EditTrackForm
            mediaId={item.mediaId}
            initialValues={{
              title: item.title,
              artist: item.artist,
              album: item.album,
              genre: item.genre,
              year: item.year,
              eligible: item.eligible ?? true,
            }}
            etag={etag}
          />
        </div>
      </div>

      {libraries.length > 0 && (
        <div className="mt-6 rounded-[6px] border border-line bg-surface p-6">
          <h2 className="font-display text-[1.05rem] font-semibold text-ink">Move to library</h2>
          <div className="mt-4">
            <MoveToLibraryAction
              mediaId={item.mediaId}
              etag={etag}
              currentLibraryId={item.libraryId ?? null}
              libraries={libraries}
              scopeLibraryIds={[]}
            />
          </div>
        </div>
      )}

      <div className="mt-6 rounded-[6px] border border-line bg-surface p-6">
        <h2 className="font-display text-[1.05rem] font-semibold text-ink">Re-analyze</h2>
        <div className="mt-4">
          <ReanalyzePanel mediaId={item.mediaId} tagsEditedAt={item.tagsEditedAt ?? null} />
        </div>
      </div>
    </main>
  );
}
