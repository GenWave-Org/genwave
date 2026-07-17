// Server-side fetch wrapper for calling the C# backend API.
// Must only be imported in Server Components, Server Actions, or Route Handlers.

const BACKEND_URL =
  process.env["BACKEND_URL"] ?? "http://localhost:5000";

interface ApiGetOptions {
  /** The value of the cookie header from the incoming request (forwarded from the browser). */
  cookies: string;
}

/**
 * Performs a GET request to the C# backend, propagating the session cookie.
 *
 * Single-station deployment: scope is the one configured station, resolved
 * server-side from StationContext — there is no X-Station-Id header.
 *
 * @param path    - The API path, e.g. "/api/media". Must start with "/".
 * @param opts    - Cookie string from the incoming request.
 * @returns       The raw fetch Response. Callers are responsible for checking `response.ok`.
 */
export async function apiGet(
  path: string,
  opts: ApiGetOptions
): Promise<Response> {
  if (!path.startsWith("/")) {
    throw new Error(`apiGet: path must start with "/", got: ${path}`);
  }

  const url = `${BACKEND_URL}${path}`;

  const requestHeaders: Record<string, string> = {
    cookie: opts.cookies,
    accept: "application/json",
  };

  return fetch(url, {
    method: "GET",
    headers: requestHeaders,
    // Never cache authenticated responses
    cache: "no-store",
  });
}
