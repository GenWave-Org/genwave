"use server";

import { redirect } from "next/navigation";
import { cookies, headers } from "next/headers";
import { safeReturnTo } from "@/lib/safe-return";

const LOGIN_API = "/api/auth/login";
const LOGOUT_API = "/api/auth/logout";

// Resolve the internal base URL for server-side fetches through the Next.js
// dev server or the standalone server. In Docker the rewrite proxy is always
// localhost:3000; outside Docker PORT may differ.
function internalBaseUrl(): string {
  const port = process.env["PORT"] ?? "3000";
  return `http://localhost:${port}`;
}

// The C# api returns the auth cookie via Set-Cookie. Because this runs as a server-side fetch
// (not the browser's own request), that Set-Cookie does NOT reach the browser automatically —
// we must copy it onto Next's response cookie store. Handles login (set) and logout (expire).
async function forwardSetCookies(response: Response): Promise<void> {
  const store = await cookies();
  const setCookies =
    (response.headers as Headers & { getSetCookie?: () => string[] }).getSetCookie?.() ?? [];

  for (const raw of setCookies) {
    const [nameValue, ...attrs] = raw.split(";").map((p) => p.trim());
    if (!nameValue) continue;
    const eq = nameValue.indexOf("=");
    if (eq < 0) continue;
    const name = nameValue.slice(0, eq);
    const value = nameValue.slice(eq + 1);

    const opts: {
      httpOnly: boolean;
      sameSite: "lax";
      path: string;
      maxAge?: number;
      expires?: Date;
    } = { httpOnly: true, sameSite: "lax", path: "/" };

    for (const attr of attrs) {
      const i = attr.indexOf("=");
      const key = (i < 0 ? attr : attr.slice(0, i)).toLowerCase();
      const val = i < 0 ? "" : attr.slice(i + 1);
      if (key === "max-age") opts.maxAge = Number(val);
      else if (key === "expires") opts.expires = new Date(val);
      else if (key === "path") opts.path = val || "/";
    }

    store.set(name, value, opts);
  }
}

export interface LoginState {
  error: string | null;
}

export async function login(
  returnTo: string,
  _prev: LoginState,
  formData: FormData
): Promise<LoginState> {
  const password = formData.get("password");

  if (typeof password !== "string") {
    return { error: "Invalid credentials" };
  }

  const requestHeaders = await headers();
  const cookieHeader = requestHeaders.get("cookie") ?? "";

  let response: Response;
  try {
    response = await fetch(`${internalBaseUrl()}${LOGIN_API}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        cookie: cookieHeader,
      },
      body: JSON.stringify({ password }),
      redirect: "manual",
    });
  } catch {
    return { error: "Invalid credentials" };
  }

  if (!response.ok && response.status !== 204) {
    return { error: "Invalid credentials" };
  }

  // Copy the api's auth cookie onto the browser, then redirect; middleware sees the cookie.
  await forwardSetCookies(response);
  redirect(safeReturnTo(returnTo));
}

export async function logout(): Promise<never> {
  const requestHeaders = await headers();
  const cookieHeader = requestHeaders.get("cookie") ?? "";

  try {
    const response = await fetch(`${internalBaseUrl()}${LOGOUT_API}`, {
      method: "POST",
      headers: { cookie: cookieHeader },
    });
    // Forward the api's cookie-clearing Set-Cookie so the browser drops it too.
    await forwardSetCookies(response);
  } catch {
    // ignore — proceed to redirect regardless
  }

  redirect("/login");
}
