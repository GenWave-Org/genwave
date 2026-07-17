# Multi-stage build for the GenWave control-plane API.
# Build context is the repository root (see compose.yaml: context: .).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

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

# Copy the sources and publish.
COPY src/ src/
RUN dotnet publish src/GenWave.Host/GenWave.Host.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# ffmpeg (the Debian package bundles libswresample, so ebur128 peak=true works) is required by the
# media library's enrichment stage (PRD §8).
# aubio-tools supplies `aubio tempo`, the BPM estimator AubioBpmAnalyzer shells out to (SPEC F46.5);
# ffmpeg still does the PCM decode since aubio's own codec support varies by build.
# curl is required by the compose healthcheck (`curl -fsS .../health`) — the aspnet runtime image
# ships with neither curl nor wget, and its /bin/sh is dash (no /dev/tcp), so there is no
# zero-dependency probe alternative.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ffmpeg aubio-tools curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "GenWave.Host.dll"]
