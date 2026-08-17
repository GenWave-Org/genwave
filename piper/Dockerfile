# GenWave piper fallback TTS (gh-#241) — repo-owned multi-arch replacement for the amd64-only
# artibex/piper-http digest compose used to pin. Preserves that wrapper's exact wire shape
# (POST text/plain -> / -> WAV bytes; OPTIONS answered quietly, gh-#64) and env contract
# (MODEL_DOWNLOAD_LINK / MODEL_TARGET_FOLDER / SPEAKER, port 5000) so PiperTtsSynthesizer and
# PiperHealthProbe need zero changes — see piper/server.py.
#
# Builds for linux/amd64 AND linux/arm64: piper-tts publishes abi3 manylinux wheels for both
# (x86_64 + aarch64), so `pip install` never compiles anything on either arch.

# PIN — bookworm variant named explicitly so a Debian base jump is always a deliberate bump.
FROM python:3.14-slim-bookworm

# onnxruntime's manylinux wheel links libgomp, which the slim base does not carry.
RUN apt-get update -qq \
    && apt-get install -y -qq --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# PIN — exact version, never a range: the voice pipeline's output must only ever change on a
# deliberate bump. piper-tts is the maintained piper1-gpl line (rhasspy/piper is archived).
RUN pip install --no-cache-dir --root-user-action=ignore piper-tts==1.6.0

WORKDIR /app
COPY server.py /app/server.py

# Same contract (and default voice override point) as the artibex image compose replaced —
# compose.yaml sets MODEL_DOWNLOAD_LINK explicitly; these defaults just make the image
# runnable stand-alone.
ENV MODEL_DOWNLOAD_LINK="https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/lessac/medium/en_US-lessac-medium.onnx?download=true" \
    MODEL_TARGET_FOLDER="/app/models" \
    SPEAKER="0"

EXPOSE 5000
CMD ["python", "-u", "/app/server.py"]
