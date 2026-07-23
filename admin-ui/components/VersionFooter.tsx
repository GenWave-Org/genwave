import type { ReactNode } from "react";

// gh-#7 — the build's version stamp, e.g. "GenWave Home v2.4.1". NEXT_PUBLIC_GW_VERSION is
// inlined at `next build` time from the Docker build's GW_VERSION arg (the same git-describe
// value build.sh/release.yml thread into the api image's InformationalVersion, SPEC F65.1);
// a dev server or an image built without the arg falls back to the 0.0.0-dev sentinel.
const VERSION = process.env.NEXT_PUBLIC_GW_VERSION || "0.0.0-dev";

// The product edition. This repository IS GenWave Home (the published images label themselves
// "GenWave Home ..."); another edition ships from its own tree with its own constant.
const EDITION = "Home";

/**
 * gh-#7 — fixed bottom-right version/edition stamp, rendered from the root layout so it appears
 * on every page (login included). Decorative chrome: pointer-events-none keeps it from ever
 * stealing a click from real controls, aria-hidden keeps it out of the accessibility tree.
 */
export function VersionFooter(): ReactNode {
  return (
    <div
      aria-hidden="true"
      className="pointer-events-none fixed bottom-2 right-3 z-50 select-none text-xs text-mute"
    >
      {`GenWave ${EDITION} v${VERSION.replace(/^v/, "")}`}
    </div>
  );
}
