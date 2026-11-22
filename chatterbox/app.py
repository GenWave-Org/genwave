"""Chatterbox Turbo on CPU behind the GenWave speech contract (SPEC F219.4, F219.5, F219.12, F219.28).

Adapted from the Voice Off bench shim (genwave-voiceoff-bench, shims/chatterbox + shims/common).
Voices are the clip files in CLIPS_DIR: a voice id is the file name without ".wav", read at request time.
"""
import io
import logging
import os
import re
import sys
import threading
from collections import OrderedDict
from contextlib import asynccontextmanager
from pathlib import Path

os.environ.setdefault("TQDM_DISABLE", "1")


def torch_threads() -> int:
    raw = os.environ.get("GENWAVE_VOICE_CPUS", "64")
    try:
        value = int(raw)
    except ValueError:
        value = 0
    if not 1 <= value <= 4096:
        sys.exit(f"GENWAVE_VOICE_CPUS must be a whole number from 1 to 4096, got {raw!r}.")
    return value


THREADS = torch_threads()  # before torch loads, so a bad value fails fast

import numpy as np  # noqa: E402
import soundfile as sf  # noqa: E402
import torch  # noqa: E402
from fastapi import FastAPI, HTTPException, Response  # noqa: E402
from soundfile import LibsndfileError  # noqa: E402
from pydantic import BaseModel  # noqa: E402

torch.set_num_threads(THREADS)

CLIPS_DIR = Path(os.environ.get("CLIPS_DIR", "/clips"))
MAX_INPUT_CHARS = 2000
MAX_SLUG_CHARS = 64  # VoiceClipSlug caps a base at 48 plus a "-NN" suffix; anything longer cannot be a clip
# Turbo stops at 1000 speech tokens (~40 s, ~1000 chars of speech at most), silently truncating. Each render stays well below.
CHUNK_CHARS = 300
CHUNK_GAP_SECONDS = 0.15
# Pinned Turbo snapshot (Resemble AI, MIT); weights are fetched once, then loaded with no hub call.
TURBO_REPO = "ResembleAI/chatterbox-turbo"
TURBO_REVISION = "749d1c1a46eb10492095d68fbcf55691ccf137cd"
TURBO_FILES = ["*.safetensors", "*.json", "*.txt", "*.pt", "*.model"]
CONDS_CACHE_SIZE = 8
MIN_CLIP_SECONDS = 5.0  # Turbo asserts a reference longer than this
logger = logging.getLogger("uvicorn.error")
SLUG = re.compile(r"\A[a-z0-9]+(-[a-z0-9]+)*\Z")  # Python spelling of the C# \A...\z
# response_format -> (soundfile format, subtype, media type)
FORMATS = {
    "wav": ("WAV", "PCM_16", "audio/wav"),
    "flac": ("FLAC", "PCM_16", "audio/flac"),
    "mp3": ("MP3", None, "audio/mpeg"),
    "ogg": ("OGG", "VORBIS", "audio/ogg"),
}

_render_lock = threading.Lock()  # one render at a time: the model is not thread-safe
_model = None
# (voice, st_mtime_ns, st_size) -> prepared conditionals; a re-uploaded clip has a new key, so it is never served stale.
_conds: OrderedDict = OrderedDict()


def fetch_weights() -> str:
    from huggingface_hub import snapshot_download
    from huggingface_hub.errors import LocalEntryNotFoundError

    try:
        return snapshot_download(TURBO_REPO, revision=TURBO_REVISION, allow_patterns=TURBO_FILES, local_files_only=True)
    except LocalEntryNotFoundError:
        return snapshot_download(TURBO_REPO, revision=TURBO_REVISION, allow_patterns=TURBO_FILES,
                                 token=os.getenv("HF_TOKEN") or None)


@asynccontextmanager
async def lifespan(_: FastAPI):
    global _model
    from chatterbox.tts_turbo import ChatterboxTurboTTS

    _model = ChatterboxTurboTTS.from_local(fetch_weights(), "cpu")
    yield


app = FastAPI(lifespan=lifespan, docs_url=None, redoc_url=None, openapi_url=None)


class SpeechRequest(BaseModel):
    input: str
    voice: str
    response_format: str = "wav"
    speed: float | None = None  # accepted and ignored: Turbo has no speed control


def clip_path(voice: str) -> Path:
    """The clip file for a voice id, or HTTPException 400. The one check both the list and speech use."""
    if len(voice) > MAX_SLUG_CHARS or not SLUG.match(voice):
        raise HTTPException(400, f"Voice '{voice[:64]}' is not a valid voice id (lowercase letters, digits and hyphens, up to {MAX_SLUG_CHARS}).")
    try:
        root = CLIPS_DIR.resolve()
        path = (root / f"{voice}.wav").resolve()
        usable = path.parent == root and path.is_file()
    except OSError:
        usable = False
    if not usable:
        raise HTTPException(400, f"Unknown voice '{voice}'.")
    return path


def voice_ids() -> list[str]:
    if not CLIPS_DIR.is_dir():
        return []
    ids = []
    for p in CLIPS_DIR.glob("*.wav"):
        try:
            clip_path(p.stem)
        except HTTPException:
            continue
        ids.append(p.stem)
    return sorted(ids)


def check_clip(voice: str, path: Path) -> None:
    """400 for a clip Turbo cannot clone from, so a bad file never reaches the model."""
    try:
        duration = sf.info(str(path)).duration
    except (LibsndfileError, EOFError) as exc:
        logger.warning("Voice %s: clip %s is unreadable: %s", voice, path.name, getattr(exc, "error_string", None) or exc)
        raise HTTPException(400, f"Voice '{voice}' has a clip the engine cannot use.") from exc
    if duration <= MIN_CLIP_SECONDS:
        logger.warning("Voice %s: clip %s is %.2f s, not longer than %.0f s", voice, path.name, duration, MIN_CLIP_SECONDS)
        raise HTTPException(400, f"Voice '{voice}' has a clip the engine cannot use (shorter than 5 s).")


def use_voice(voice: str, path: Path) -> None:
    stat = path.stat()
    key = (voice, stat.st_mtime_ns, stat.st_size)
    if key in _conds:
        _conds.move_to_end(key)
    else:
        check_clip(voice, path)
        _model.prepare_conditionals(str(path))  # no except: a failure here is a real engine fault and stays a 5xx
        _conds[key] = _model.conds
        while len(_conds) > CONDS_CACHE_SIZE:
            _conds.popitem(last=False)
    _model.conds = _conds[key]


def chunk_text(text: str) -> list[str]:
    """Sentence-aligned chunks of at most CHUNK_CHARS; a [tag] stays inside the sentence it is written in."""
    pieces: list[str] = []
    for sentence in re.split(r"(?<=[.!?])\s+", text.strip()):
        while len(sentence) > CHUNK_CHARS:  # one run-on sentence: cut at the last space inside the limit
            cut = sentence.rfind(" ", 0, CHUNK_CHARS)
            cut = cut if cut > 0 else CHUNK_CHARS
            pieces.append(sentence[:cut])
            sentence = sentence[cut:].lstrip()
        if sentence:
            pieces.append(sentence)
    chunks: list[str] = []
    for piece in pieces:
        if chunks and len(chunks[-1]) + 1 + len(piece) <= CHUNK_CHARS:
            chunks[-1] += " " + piece
        else:
            chunks.append(piece)
    return chunks


@app.get("/v1/audio/voices")
def list_voices() -> dict:
    return {"voices": voice_ids()}


@app.options("/v1/audio/speech")
def speech_options() -> Response:
    return Response(status_code=204, headers={"Allow": "POST, OPTIONS"})


@app.post("/v1/audio/speech")
def speech(req: SpeechRequest) -> Response:
    if req.response_format not in FORMATS:
        raise HTTPException(400, f"response_format must be one of {', '.join(FORMATS)}.")
    if not req.input.strip() or len(req.input) > MAX_INPUT_CHARS:
        raise HTTPException(400, f"The input is empty or longer than {MAX_INPUT_CHARS} characters.")
    path = clip_path(req.voice)
    parts: list[np.ndarray] = []
    with _render_lock:
        use_voice(req.voice, path)
        for chunk in chunk_text(req.input):  # sound tags such as [clear throat] pass through to Turbo
            if parts:
                parts.append(np.zeros(int(_model.sr * CHUNK_GAP_SECONDS), dtype=np.float32))
            parts.append(_model.generate(chunk).squeeze(0).cpu().numpy())
    audio = np.concatenate(parts)
    if audio.size == 0:
        raise HTTPException(500, "The engine returned no audio.")
    fmt, subtype, media_type = FORMATS[req.response_format]
    buf = io.BytesIO()
    sf.write(buf, audio, int(_model.sr), format=fmt, subtype=subtype)
    return Response(buf.getvalue(), media_type=media_type)


@app.get("/health")
def health() -> dict:
    return {"ok": _model is not None}
