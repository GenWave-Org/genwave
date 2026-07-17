import { NextRequest, NextResponse } from "next/server";

const SESSION_COOKIE = "genwave-auth";

// Paths that are publicly accessible without a session cookie
const PUBLIC_PATHS = ["/login", "/healthz"];

function isPublicPath(pathname: string): boolean {
  return (
    PUBLIC_PATHS.some((p) => pathname === p || pathname.startsWith(p + "/")) ||
    pathname.startsWith("/_next/") ||
    pathname.startsWith("/favicon") ||
    pathname === "/icon.png"
  );
}

export function middleware(request: NextRequest): NextResponse {
  const { pathname } = request.nextUrl;

  if (isPublicPath(pathname)) {
    return NextResponse.next();
  }

  const session = request.cookies.get(SESSION_COOKIE);

  if (!session) {
    const loginUrl = new URL("/login", request.url);
    loginUrl.searchParams.set("return", pathname);
    return NextResponse.redirect(loginUrl, { status: 307 });
  }

  return NextResponse.next();
}

export const config = {
  // Run on all paths except Next.js internals, static files, and /api/* — the latter is
  // rewritten to the C# backend, which enforces its own auth; gating it here would 307 the
  // browser's same-origin API/login calls to /login. icon.png (STORY-106, the App Router
  // favicon convention — the operator's GenWave-logo.png, which superseded R11's SVG mark)
  // is exempted the same way favicon.ico already is — a 307 to /login for an
  // <img>/<link rel="icon"> request serves a redirect body, not the icon.
  matcher: ["/((?!api|_next/static|_next/image|favicon.ico|icon.png).*)"],
};
