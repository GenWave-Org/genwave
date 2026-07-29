"""GenWave piper fallback TTS server (gh-#241).

Wire shape — byte-for-byte the contract PiperTtsSynthesizer and PiperHealthProbe already speak
(the same one artibex/piper-http's Flask wrapper served):

  * POST /  with a text/plain body  -> 200, raw WAV bytes of the rendered speech.
  * OPTIONS /                       -> 200, no body, NO log line (the api probes this every
    30s; per-request logging is suppressed so a healthy server never floods the fleet's log
    telemetry the way GET-probing the old image did — gh-#64).
  * GET /                           -> 200, short usage text. (The old wrapper 500'd here;
    nothing in GenWave GETs this port, so answering politely is strictly additive.)

Exactly one voice is baked in per container lifetime: MODEL_DOWNLOAD_LINK's .onnx (plus its
.onnx.json config) is fetched into MODEL_TARGET_FOLDER on first boot — persisted in compose's
piper_models volume — and served until the container stops. The port only opens AFTER the model
is downloaded and loaded, so compose's TCP-connect healthcheck passing means "ready to render",
not merely "process exists".

Renders serialize on a lock (espeak phonemization is not thread-safe, and one render at a time
bounds memory under compose's 768m cap) while the threading server keeps OPTIONS/GET answering
instantly — a slow render must never look like an outage (the gh-#125 lesson).
"""

import io
import os
import threading
import urllib.request
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit, urlunsplit

from piper import PiperVoice, SynthesisConfig

PORT = 5000


def download(url: str, destination: Path) -> None:
    """Fetch url to destination once; a .part temp file keeps interrupted pulls restartable."""
    if destination.exists():
        return
    partial = destination.with_name(destination.name + ".part")
    print(f"downloading {url} -> {destination}", flush=True)
    urllib.request.urlretrieve(url, partial)  # noqa: S310 — operator-configured HTTPS model URL
    partial.rename(destination)


def ensure_model(link: str, target_folder: Path) -> Path:
    """Materialize the voice's .onnx and .onnx.json beside each other; return the .onnx path.

    The config URL is the model URL with `.json` appended to the PATH part (query string kept),
    matching how rhasspy/piper-voices lays out en_US-lessac-medium.onnx / .onnx.json on
    HuggingFace. PiperVoice.load guesses "<model>.json" on its own, so the naming must agree.
    """
    target_folder.mkdir(parents=True, exist_ok=True)
    split = urlsplit(link)
    onnx_path = target_folder / Path(split.path).name
    config_link = urlunsplit(split._replace(path=split.path + ".json"))
    download(link, onnx_path)
    download(config_link, onnx_path.with_name(onnx_path.name + ".json"))
    return onnx_path


def make_handler(voice: PiperVoice, syn_config: SynthesisConfig) -> type[BaseHTTPRequestHandler]:
    render_lock = threading.Lock()

    class PiperHandler(BaseHTTPRequestHandler):
        def log_message(self, format: str, *args: object) -> None:  # noqa: A002 — stdlib signature
            pass  # quiet by design: see module docstring (gh-#64)

        def _reply(self, status: int, content_type: str, body: bytes) -> None:
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_OPTIONS(self) -> None:
            self.send_response(200)
            self.send_header("Allow", "OPTIONS, GET, POST")
            self.send_header("Content-Length", "0")
            self.end_headers()

        def do_GET(self) -> None:
            self._reply(200, "text/plain; charset=utf-8",
                        b"GenWave piper: POST text/plain to / for WAV\n")

        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length", "0"))
            text = self.rfile.read(length).decode("utf-8", errors="replace").strip()
            if not text:
                self._reply(400, "text/plain; charset=utf-8", b"empty text\n")
                return
            buffer = io.BytesIO()
            with render_lock, wave.open(buffer, "wb") as wav_file:
                voice.synthesize_wav(text, wav_file, syn_config=syn_config)
            self._reply(200, "audio/wav", buffer.getvalue())

    return PiperHandler


def main() -> None:
    link = os.environ["MODEL_DOWNLOAD_LINK"]
    target_folder = Path(os.environ.get("MODEL_TARGET_FOLDER", "/app/models"))
    speaker = int(os.environ.get("SPEAKER", "0"))

    onnx_path = ensure_model(link, target_folder)
    voice = PiperVoice.load(onnx_path)
    # SPEAKER only means anything on a multi-speaker voice; passing an id to a single-speaker
    # model is at best noise, so mirror upstream's behavior and drop it there.
    speaker_id = speaker if voice.config.num_speakers > 1 else None
    syn_config = SynthesisConfig(speaker_id=speaker_id)

    server = ThreadingHTTPServer(("0.0.0.0", PORT), make_handler(voice, syn_config))
    print(f"piper ready: voice={onnx_path.name} speaker_id={speaker_id} port={PORT}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
