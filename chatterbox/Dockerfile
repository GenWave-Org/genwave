# Chatterbox Turbo 350M (Resemble AI, MIT) on CPU behind the GenWave speech contract (SPEC F219.28).
# Weights download on first start into HF_HOME=/models (a volume); clips are mounted at /clips.
FROM python:3.11-slim-bookworm@sha256:a83ba18aef0f865b1a4a611c2cf460fe446f2795673bbb9fe43be570439e3b39

ENV PYTHONUNBUFFERED=1 PIP_NO_CACHE_DIR=1 PIP_DISABLE_PIP_VERSION_CHECK=1 \
    HF_HOME=/models CLIPS_DIR=/clips NUMBA_CACHE_DIR=/tmp/numba-cache TQDM_DISABLE=1

# libsndfile ships inside the soundfile wheel; no apt packages needed.
WORKDIR /app
COPY requirements-torch.txt requirements.txt ./
RUN pip install --index-url https://download.pytorch.org/whl/cpu -r requirements-torch.txt \
 && pip install -r requirements.txt

RUN useradd --system --uid 10001 --home-dir /app genwave && mkdir /models /clips && chown genwave /models
COPY app.py ./
USER genwave

EXPOSE 8880
HEALTHCHECK --interval=30s --timeout=5s --start-period=1800s --retries=3 \
  CMD python -c "import urllib.request as u; u.urlopen('http://127.0.0.1:8880/health', timeout=4)" || exit 1
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8880"]
