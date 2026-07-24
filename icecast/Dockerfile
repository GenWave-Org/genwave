# icecast/Dockerfile — self-owned Icecast image, built from source (SPEC F88.1, PLAN T82).
#
# Debian bookworm's `icecast2` apt package is vanilla 2.4.4: it silently drops the ICY `url=`
# metadata param a later feature needs, and it EOLs 2026-12-31. This Dockerfile instead compiles
# the pinned upstream 2.5.0 release from source (tarball URL + SHA256 verified below).
#
# Icecast 2.5 also raises its libigloo (runtime framework) requirement to >= 0.9.4 — Debian
# bookworm only ships 0.9.2 — so libigloo is ALSO built from source, from its own pinned tag
# archive + SHA256, before configuring icecast against it.
#
# Passwords are injected from the environment at container start via envsubst (Icecast's XML
# can't read env natively) — see entrypoint.sh, which is unchanged by this rewrite.

# ---------------------------------------------------------------------------
# Build stage — compiles libigloo, then icecast, against it. Nothing from this stage reaches the
# runtime image except the icecast binary, libigloo's shared library, and icecast's installed
# share/ assets (explicit COPY --from below) — no compiler, no source tree, no headers.
# ---------------------------------------------------------------------------
FROM debian:bookworm-slim AS build

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      build-essential pkg-config m4 autoconf automake libtool ca-certificates curl \
      libxml2-dev libxslt1-dev libvorbis-dev libogg-dev libssl-dev libcurl4-openssl-dev librhash-dev \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /usr/src

# --- libigloo 0.9.4 ---
# Not published as a release tarball upstream: pinned to the v0.9.4 tag's source archive from the
# project's own GitLab (xiph/icecast-libigloo), verified by SHA256. GitLab's tag archives are
# stable/reproducible for a given ref — re-downloaded twice at authoring time, byte-identical.
ARG IGLOO_TARBALL_URL=https://gitlab.xiph.org/xiph/icecast-libigloo/-/archive/v0.9.4/icecast-libigloo-v0.9.4.tar.gz
ARG IGLOO_TARBALL_SHA256=bcb15a2ab0c2630c4e7f81ce5fc3fb82f02996beb2d95d77aaf38763fb06deee

RUN curl -fsSL -o igloo.tar.gz "${IGLOO_TARBALL_URL}" \
 && echo "${IGLOO_TARBALL_SHA256}  igloo.tar.gz" | sha256sum -c - \
 && tar xzf igloo.tar.gz && rm igloo.tar.gz \
 && cd icecast-libigloo-v0.9.4 \
 && autoreconf -isf \
 && ./configure --prefix=/usr/local \
 && make -j"$(nproc)" \
 && make install \
 && ldconfig

# --- Icecast 2.5.0 ---
ARG ICECAST_TARBALL_URL=https://downloads.xiph.org/releases/icecast/icecast-2.5.0.tar.gz
ARG ICECAST_TARBALL_SHA256=d9aa07c7429aec19d950ff6fd425c371f77158cd34ff220fc191b2c186c67c7a

RUN curl -fsSL -o icecast.tar.gz "${ICECAST_TARBALL_URL}" \
 && echo "${ICECAST_TARBALL_SHA256}  icecast.tar.gz" | sha256sum -c - \
 && tar xzf icecast.tar.gz && rm icecast.tar.gz \
 && cd icecast-2.5.0 \
 && PKG_CONFIG_PATH=/usr/local/lib/pkgconfig ./configure --prefix=/usr/local \
 && make -j"$(nproc)" \
 && make install

# ---------------------------------------------------------------------------
# Runtime stage — slim image: no compiler, no autotools, no headers. Only icecast's own runtime
# shared-library deps (icecast's transitive deps — TLS, LDAP, etc. — come along automatically as
# apt Depends of the packages named here).
# ---------------------------------------------------------------------------
FROM debian:bookworm-slim

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      gettext-base \
      libxml2 libxslt1.1 libvorbis0a libogg0 libcurl4 libssl3 librhash0 \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --system icecast \
 && useradd --system --no-create-home --shell /usr/sbin/nologin --gid icecast icecast2 \
 && mkdir -p /var/log/icecast2 \
 && chown -R icecast2:icecast /var/log/icecast2
# gettext-base provides `envsubst`. The icecast2 user + icecast group replace what the apt
# icecast2 package used to create via its postinst — this image no longer installs that package.

COPY --from=build /usr/local/bin/icecast              /usr/local/bin/icecast
COPY --from=build /usr/local/lib/libigloo.so.0.0.0     /usr/local/lib/libigloo.so.0.0.0
COPY --from=build /usr/local/share/icecast/            /usr/local/share/icecast/
# Only the real shared-object file is copied, not its SONAME symlink: `COPY --from` on a glob
# dereferences symlinks into duplicate regular files, which `ldconfig` below then warns about
# ("... is not a symbolic link") instead of managing. `ldconfig` reads libigloo's embedded SONAME
# (libigloo.so.0) and creates that symlink itself — the unversioned `.so` name is a build-time-only
# artifact (for `-ligloo` at link time) and isn't needed here.

RUN ldconfig \
 && ln -s /usr/local/bin/icecast /usr/local/bin/icecast2 \
 && ln -s /usr/local/share/icecast /usr/share/icecast2
# The source build's binary is named `icecast` (not `icecast2`), and its web/admin assets install
# under /usr/local/share/icecast (icecast's own pkgdatadir) rather than /usr/share/icecast2. These
# two symlinks are the entire reconciliation needed: entrypoint.sh's `exec icecast2 ...` and
# icecast.xml.tmpl's <webroot>/<adminroot> under /usr/share/icecast2/{web,admin} keep working
# completely unchanged.

COPY icecast.xml.tmpl /etc/icecast2/icecast.xml.tmpl
COPY entrypoint.sh    /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 8000
# Run unprivileged (no <changeowner> needed in the config). NOTE: USER takes no inline comment —
# the '#...' would be parsed as part of the username — so this comment must be on its own line.
USER icecast2
ENTRYPOINT ["/entrypoint.sh"]
