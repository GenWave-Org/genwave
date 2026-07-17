# icecast/Dockerfile — self-owned Icecast2 image.
# We build our own (rather than depend on a third-party image) so the config interface and
# the source/admin passwords are entirely under our control. Passwords are injected from the
# environment at container start via envsubst (Icecast's XML can't read env natively).

FROM debian:bookworm-slim

RUN apt-get update \
 && apt-get install -y --no-install-recommends icecast2 gettext-base \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /var/log/icecast2 \
 && chown -R icecast2:icecast /var/log/icecast2
# gettext-base provides `envsubst`. The icecast2 package creates the icecast2 user + web/admin assets.

COPY icecast.xml.tmpl /etc/icecast2/icecast.xml.tmpl
COPY entrypoint.sh    /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 8000
# Run unprivileged (no <changeowner> needed in the config). NOTE: USER takes no inline comment —
# the '#...' would be parsed as part of the username — so this comment must be on its own line.
USER icecast2
ENTRYPOINT ["/entrypoint.sh"]
