# Engine image — Liquidsoap 2.4.4 extended with curl for the boot-time config fetch.
#
# The entrypoint.sh wrapper runs BEFORE liquidsoap starts: it fetches GW_XFADE_MIN /
# GW_XFADE_MAX from the api's /internal/engine-config endpoint (which includes the
# station.settings overlay) and exports them into the environment, then exec's liquidsoap.
# If the api is unreachable the compose-set GW_XFADE_* env vars are used as fallback.
#
# PIN: matches the version in compose.yaml — operator/setting syntax shifts across versions.
FROM savonet/liquidsoap:v2.4.4

# The savonet image is Debian-based; apt-get is available.
# curl is the only addition — keep the layer lean.
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# Copy the entrypoint wrapper — it fetches effective xfade config from the api at boot.
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Return to the image's default user (liquidsoap image uses root; retain the upstream default).
USER liquidsoap

ENTRYPOINT ["/entrypoint.sh"]
