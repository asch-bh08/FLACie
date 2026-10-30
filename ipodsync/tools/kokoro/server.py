# FLACie's own DJ voice: Kokoro (open-source text-to-speech, Apache 2.0) on frank's CPU, so the DJ has a voice with no daily limit.
# POST /tts {"text": "...", "voice": "am_liam", "speed": 1.0}  ->  audio/mpeg (an MP3 of the line)
# GET /healthz -> "ok"; GET /voices -> the voice names. Listens on loopback only (see README.md); FLACie Web is the only caller.
import io, json, os, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import lameenc
import numpy as np
import onnxruntime as ort
from kokoro_onnx import Kokoro

from sounds import has_marks, marks_for_misaki, phonemes

try:
    # misaki: Kokoro's own reader (its dictionary, and Kokoro's alphabet of sounds); espeak reads the words misaki does not know
    from misaki import en, espeak
except Exception:  # an image without misaki still talks, the old way
    en = None

MODEL = os.environ.get("KOKORO_MODEL", "/models/kokoro-v1.0.int8.onnx")
VOICES = os.environ.get("KOKORO_VOICES", "/models/voices-v1.0.bin")
THREADS = int(os.environ.get("KOKORO_THREADS", "0")) or os.cpu_count() or 4
PORT = int(os.environ.get("KOKORO_PORT", "8092"))
# a touch slower than Kokoro's own pace: at 1.0 the DJ rushed names and titles together (owner, 30 Oct: "too fast or the wrong pace")
SPEED = float(os.environ.get("KOKORO_SPEED", "0.92"))
MAX_CHARS = 600

# all cores for one line at a time: one line finishes sooner than two lines sharing them
opts = ort.SessionOptions()
opts.intra_op_num_threads = THREADS
opts.inter_op_num_threads = 1
opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
kokoro = Kokoro.from_session(ort.InferenceSession(MODEL, sess_options=opts, providers=["CPUExecutionProvider"]), VOICES)
names = set(kokoro.get_voices())
one_at_a_time = threading.Lock()
readers = {}


def misaki(text, british):
    """The line in Kokoro's alphabet, read by misaki (marked names as their sounds), or None when misaki is missing or fails."""
    if en is None:
        return None
    try:
        if british not in readers:
            readers[british] = en.G2P(trf=False, british=british, fallback=espeak.EspeakFallback(british=british))
        ps, _ = readers[british](marks_for_misaki(text, british))
        ps = "".join(c for c in ps if c in kokoro.tokenizer.vocab).strip()
        return ps or None
    except Exception:
        return None


def mp3(audio, rate):
    pcm = (np.clip(audio, -1.0, 1.0) * 32767).astype(np.int16).tobytes()
    enc = lameenc.Encoder()
    enc.set_bit_rate(96); enc.set_in_sample_rate(rate); enc.set_channels(1); enc.set_quality(2)
    return enc.encode(pcm) + enc.flush()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):  # quiet: the lines are the listener's, and FLACie logs what it needs
        pass

    def send(self, code, body, kind="text/plain"):
        self.send_response(code); self.send_header("Content-Type", kind); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)

    def do_GET(self):
        if self.path == "/healthz": return self.send(200, b"ok")
        if self.path == "/voices": return self.send(200, json.dumps(sorted(names)).encode(), "application/json")
        self.send(404, b"not found")

    def do_POST(self):
        if self.path != "/tts": return self.send(404, b"not found")
        try:
            n = int(self.headers.get("Content-Length", "0"))
            if n <= 0 or n > 8192: return self.send(400, b"bad body")
            req = json.loads(self.rfile.read(n))
            text = str(req.get("text", "")).strip()
            voice = str(req.get("voice", "am_liam"))
            speed = min(1.5, max(0.6, float(req.get("speed", SPEED))))
            if not text or len(text) > MAX_CHARS or voice not in names: return self.send(400, b"bad text or voice")
            lang = "en-gb" if voice[:1] == "b" else "en-us"
            t = time.time()
            with one_at_a_time:
                # a name marked with its sounds ("[Sia](/sˈiːə/)") is said exactly: the line goes in as sounds (sounds.py)
                ps = misaki(text, lang == "en-gb")
                if ps:
                    audio, rate = kokoro.create(ps, voice=voice, speed=speed, lang=lang, is_phonemes=True)
                elif has_marks(text):
                    audio, rate = kokoro.create(phonemes(text, lambda t: kokoro.tokenizer.phonemize(t, lang)), voice=voice, speed=speed, lang=lang, is_phonemes=True)
                else:
                    audio, rate = kokoro.create(text, voice=voice, speed=speed, lang=lang)
            body = mp3(audio, rate)
            self.send_response(200); self.send_header("Content-Type", "audio/mpeg"); self.send_header("Content-Length", str(len(body)))
            self.send_header("X-Took-Ms", str(int((time.time() - t) * 1000))); self.send_header("X-Audio-Ms", str(int(len(audio) / rate * 1000))); self.end_headers()
            self.wfile.write(body)
        except Exception as e:
            self.send(500, type(e).__name__.encode())


if __name__ == "__main__":
    # one warm-up line, so the first real one is not slow
    with one_at_a_time:
        ps = misaki("Hello.", False)
        kokoro.create(ps or "Hello.", voice="am_liam", speed=SPEED, lang="en-us", is_phonemes=ps is not None)
        misaki("Hello.", True)
    # lines wait their turn on the lock; the listen queue must hold them all (the default 5 refused connections when FLACie made its stock
    # phrases, replies and a line at once)
    ThreadingHTTPServer.request_queue_size = 128
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
