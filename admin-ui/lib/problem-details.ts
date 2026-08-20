/** ProblemDetails (RFC 7807) body shape returned by this station's api on a failed mutation or
 * fetch — `detail` plus the `type` URI (gh-#486) a caller uses to tell two different causes of the
 * same status code apart without parsing `detail`'s human text. */
interface ProblemDetailsBody {
  detail?: string;
  type?: string;
}

function isProblemDetailsBody(raw: unknown): raw is ProblemDetailsBody {
  return typeof raw === "object" && raw !== null;
}

/**
 * Reads a failed response's ProblemDetails `detail` message (falling back to a generic
 * `"Unexpected error (status)"`) and `type` URI (gh-#486; `undefined` when absent — every
 * ProblemDetails this station returned before gh-#486 carried none) in ONE body read — a
 * `Response` body can only be consumed once, so a caller that needs both must read them together
 * rather than calling `resp.json()` a second time. {@link readErrorMessage} is this reader's own
 * detail-only shorthand, unchanged for every existing caller.
 */
export async function readProblemDetails(resp: Response): Promise<{ detail: string; type?: string }> {
  try {
    const raw = (await resp.json()) as unknown;
    if (isProblemDetailsBody(raw)) {
      return {
        detail: typeof raw.detail === "string" && raw.detail !== "" ? raw.detail : `Unexpected error (${resp.status})`,
        type: typeof raw.type === "string" ? raw.type : undefined,
      };
    }
  } catch {
    // malformed or empty body — fall through to the generic message
  }
  return { detail: `Unexpected error (${resp.status})` };
}

/**
 * Extracts the ProblemDetails `detail` message from a failed response, falling back to a generic
 * `"Unexpected error (status)"` message — the detail-first convention every CRUD mutation, the
 * persona import flow, and the Persona Catalog detail-panel fetch all share (contrast
 * `PersonaPreview`'s own title-first reader, which has its own documented reason to differ).
 * Hoisted here (T102 review) so this one small, deterministic reader has a single house
 * implementation instead of a per-feature-folder copy.
 */
export async function readErrorMessage(resp: Response): Promise<string> {
  return (await readProblemDetails(resp)).detail;
}
