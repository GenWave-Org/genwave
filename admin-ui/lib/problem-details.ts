/** ProblemDetails (RFC 7807) body shape returned by this station's api on a failed mutation or
 * fetch — only the `detail` field any caller here has ever needed. */
interface ProblemDetailsBody {
  detail?: string;
}

function isProblemDetailsBody(raw: unknown): raw is ProblemDetailsBody {
  return typeof raw === "object" && raw !== null;
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
  try {
    const raw = (await resp.json()) as unknown;
    if (isProblemDetailsBody(raw) && typeof raw.detail === "string" && raw.detail !== "") {
      return raw.detail;
    }
  } catch {
    // malformed or empty body — fall through to the generic message
  }
  return `Unexpected error (${resp.status})`;
}
