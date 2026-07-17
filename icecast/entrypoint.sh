#!/bin/sh
# icecast/entrypoint.sh — render passwords from env into the config, then run Icecast.
# Only the two password placeholders are substituted (explicit list), so nothing else in the
# XML is touched. The rendered file goes to /tmp (writable by the unprivileged icecast2 user).
set -e

: "${ICECAST_SOURCE_PASSWORD:?ICECAST_SOURCE_PASSWORD is required}"
: "${ICECAST_ADMIN_PASSWORD:?ICECAST_ADMIN_PASSWORD is required}"

envsubst '${ICECAST_SOURCE_PASSWORD} ${ICECAST_ADMIN_PASSWORD}' \
  < /etc/icecast2/icecast.xml.tmpl > /tmp/icecast.xml

exec icecast2 -c /tmp/icecast.xml
