# 🔐 Security Policy

## Reporting a vulnerability

**Please do not open a public issue for a suspected vulnerability.**

Report it privately through GitHub: **Security → Report a vulnerability** on this repository. That opens a private advisory only the maintainer can see, with room for details and a proof of concept.

What to expect:

- An acknowledgment within **7 days**.
- An assessment and, for confirmed issues, a fix with credit to you (unless you prefer otherwise).
- Details made public only after a fix is available.

## Supported versions

GenWave is developed on `main`. Security fixes land there and in the latest release. Older releases are not patched, so upgrade to stay covered.

## Scope notes for operators

GenWave is a station for your own network. No port is meant to face the internet.

- The Liquidsoap control port (1234) has no authentication by design. The shipped compose file never publishes it, so keep it that way.
- Icecast's `/admin` shares the listener port (8000). It is password-protected but reachable by anyone on your network.
- Secrets live in `.env`, which is gitignored. An empty `ADMIN_PASSWORD` locks the admin plane entirely. Set one to use it.
- Broadcasting your library to the public internet is not a supported use. A personal library does not come with broadcast rights.
