# Multi-stage build for the GenWave control-plane API.
# Build context is the repository root (see compose.yaml: context: .).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Tag-derived version (SPEC F65.1, STORY-175): build.sh computes this via `git describe` and
# passes it as a build-arg. Never committed to source — no csproj <Version>. Defaults to
# 0.0.0-dev so a plain `docker build`/`docker compose build` (no --build-arg) still works.
ARG GW_VERSION=0.0.0-dev

# Restore against just the project files first for better layer caching. Restoring Host pulls the
# whole reference graph, so every referenced csproj must be present here — plus the central
# package versions (Directory.Packages.props) and the single-source pin (nuget.config), without
# which the version-less PackageReferences fail restore (NU1015).
COPY Directory.Packages.props nuget.config ./
COPY src/GenWave.Abstractions/GenWave.Abstractions.csproj src/GenWave.Abstractions/
COPY src/GenWave.Core/GenWave.Core.csproj src/GenWave.Core/
COPY src/GenWave.Loudness/GenWave.Loudness.csproj src/GenWave.Loudness/
COPY src/GenWave.MediaLibrary/GenWave.MediaLibrary.csproj src/GenWave.MediaLibrary/
COPY src/GenWave.Tts/GenWave.Tts.csproj src/GenWave.Tts/
COPY src/GenWave.Orchestration/GenWave.Orchestration.csproj src/GenWave.Orchestration/
COPY src/GenWave.Host/GenWave.Host.csproj src/GenWave.Host/
RUN dotnet restore src/GenWave.Host/GenWave.Host.csproj

# Copy the sources and publish. Only AssemblyInformationalVersion is stamped from GW_VERSION —
# AssemblyVersion stays the SDK default so binding redirects/strong-naming are unaffected.
COPY src/ src/
RUN dotnet publish src/GenWave.Host/GenWave.Host.csproj -c Release -o /app /p:UseAppHost=false \
    "/p:InformationalVersion=${GW_VERSION}"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# ffmpeg (the Debian package bundles libswresample, so ebur128 peak=true works) is required by the
# media library's enrichment stage (PRD §8).
# aubio-tools supplies `aubio tempo`, the BPM estimator AubioBpmAnalyzer shells out to (SPEC F46.5);
# ffmpeg still does the PCM decode since aubio's own codec support varies by build.
# curl is required by the compose healthcheck (`curl -fsS .../health`) — the aspnet runtime image
# ships with neither curl nor wget, and its /bin/sh is dash (no /dev/tcp), so there is no
# zero-dependency probe alternative.
# espeak-ng supplies the `espeak-ng` CLI EspeakRespellOracle shells out to for the pronunciation
# rules editor's respell→IPA assist (SPEC F126.2, STORY-324, PLAN T278) — small (no audio backend
# is pulled in; -q never opens a sound device), so it rides the same runtime-stage apt-get pass as
# ffmpeg/aubio-tools rather than earning its own RUN layer.
# None of these four carry an explicit =<version> pin: the house posture is the base image's own
# fixed OS release (its apt sources point at ONE frozen snapshot, currently Ubuntu noble/universe)
# is the pin — reproducible because the FROM tag above is, not because this line names a version.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ffmpeg aubio-tools espeak-ng curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "GenWave.Host.dll"]
