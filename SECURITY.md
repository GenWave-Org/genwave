# 🔐 Security Policy

## Reporting a vulnerability

**Please do not open a public issue for a suspected vulnerability.**

Report it privately via GitHub's built-in flow: **Security → Report a vulnerability** on this repository. That opens a private advisory only the maintainer can see, with room for details, a proof of concept, and coordinated disclosure.

What to expect:

- An acknowledgment within **7 days**.
- An assessment and, for confirmed issues, a fix plan with credit to you (unless you prefer otherwise).
- Public disclosure coordinated with the fix — GenWave stations are self-hosted, so operators need a release to upgrade to before details go public.

## Supported versions

GenWave Home is developed on `main`; security fixes land there and in the latest release. Older releases are not patched — upgrade to the latest release to stay covered.

## Scope notes for operators

GenWave is designed for deployment on a private network or behind your own reverse proxy:

- The Liquidsoap control port (1234) is unauthenticated by design and is **never published** by the shipped compose file — do not expose it.
- Icecast's `/admin` shares the public listener port (8000), password-protected but reachable — put a TLS reverse proxy in front before exposing it to the internet.
- Secrets live in `.env` (gitignored). An empty `ADMIN_PASSWORD` disables the admin auth gate entirely — local-development convenience only, never for a reachable deployment.
