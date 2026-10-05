"""FLACie yt-dlp service ("last resort" for songs Soulseek, the open sources and Lidarr do not have).

POST /ytdl  {"artist": "...", "title": "...", "durationSec": 213, "to": "Music/Artist/Artist - Title"}   (X-Api-Key, the file mover's key)
Searches YouTube (official "Topic" uploads and official audio first), checks the length and the title, downloads the audio into
/data/<to>.<ext> and answers {"ok": true, "ext": "m4a", "bytes": 3456789, "durationSec": 213, "channel": "..."}.
It only ever takes the single best matching upload and never a playlist. Taking audio from YouTube is against YouTube's terms of
service; the app keeps this source OFF until the user switches it on in Settings.
"""
import http.server
import json
import os
import re
import shutil
import socketserver
import subprocess
import tempfile
import threading
import unicodedata

API_KEY = open("/key").read().strip()
ROOT = "/data"
MAX_BYTES = 120 * 1024 * 1024
GATE = threading.Semaphore(2)  # at most two songs at once

# uploads that are a different recording of the song unless the wanted title says so itself
BAD = ["live", "cover", "remix", "karaoke", "instrumental", "sped up", "speed up", "slowed", "reverb", "nightcore", "8d", "mashup", "tribute", "acoustic",
       "reaction", "tutorial", "lesson", "piano version", "bass boosted", "loop", "1 hour", "extended", "mv", "fanmade", "fan made"]


def norm(s):
    s = unicodedata.normalize("NFKD", s or "").encode("ascii", "ignore").decode().lower()
    s = re.sub(r"\s*[\(\[](feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]", "", s)
    s = re.sub(r"\s+(feat\.?|ft\.?|featuring)\s.*$", "", s)
    return " ".join(re.sub(r"[^a-z0-9]+", " ", s).split())


def lead_artist(a):
    first = re.split(r"\s*(?:;|,|&|/|\sx\s|\s(?:feat\.?|ft\.?|featuring|with)\s)\s*", a or "", flags=re.I)[0]
    n = norm(first)
    return n[4:] if n.startswith("the ") and len(n) > 4 else n


def resolve(rel):
    full = os.path.normpath(os.path.join(ROOT, rel.lstrip("/")))
    if not full.startswith(ROOT + os.sep):
        raise ValueError("path escapes root")
    return full


def search(query, n=8):
    p = subprocess.run(["yt-dlp", "--no-warnings", "--flat-playlist", "--dump-json", f"ytsearch{n}:{query}"], capture_output=True, text=True, timeout=45)
    out = []
    for line in p.stdout.splitlines():
        try:
            out.append(json.loads(line))
        except ValueError:
            pass
    return out


def rank(cands, artist, title, dur):
    a, t = lead_artist(artist), norm(title)
    scored, seen = [], set()
    for c in cands:
        vid = c.get("id")
        if not vid or vid in seen or c.get("live_status") in ("is_live", "is_upcoming"):
            continue
        seen.add(vid)
        ct, chan = norm(c.get("title")), norm(c.get("channel") or c.get("uploader"))
        if t not in ct or not (a in ct or a in chan):
            continue
        d = c.get("duration") or 0
        if dur > 0 and d > 0 and abs(d - dur) > 8:
            continue
        if any(re.search(rf"\b{re.escape(b)}\b", ct) and not re.search(rf"\b{re.escape(b)}\b", t + " " + a) for b in BAD):
            continue
        score = 0
        if (c.get("channel") or c.get("uploader") or "").endswith("- Topic"):
            score += 6  # YouTube's auto-generated official audio
        if a and a in chan:
            score += 3
        if "official audio" in ct or ct.endswith(" audio"):
            score += 2
        if "vevo" in chan:
            score += 1
        if dur > 0 and d > 0:
            score += max(0, 3 - abs(d - dur) // 2)
        score += min(2, (c.get("view_count") or 0) // 50_000_000)
        scored.append((score, c))
    scored.sort(key=lambda x: -x[0])
    return [c for _, c in scored[:3]]


def probe(rel):
    """ffprobe of a file under /data: what the Info panel shows for songs that were just downloaded."""
    full = resolve(rel)
    if not os.path.isfile(full):
        raise FileNotFoundError("no such file")
    p = subprocess.run(["ffprobe", "-v", "error", "-print_format", "json", "-show_format", "-show_streams", full], capture_output=True, text=True, timeout=30)
    j = json.loads(p.stdout or "{}")
    a = next((s for s in j.get("streams", []) if s.get("codec_type") == "audio"), {})
    f = j.get("format", {})
    dur = float(f.get("duration") or a.get("duration") or 0)
    return {
        "codec": a.get("codec_name", ""),
        "bitrateKbps": int(a.get("bit_rate") or f.get("bit_rate") or 0) // 1000,
        "sampleRate": int(a.get("sample_rate") or 0),
        "bitDepth": int(a.get("bits_per_raw_sample") or a.get("bits_per_sample") or 0),
        "channels": int(a.get("channels") or 0),
        "size": int(f.get("size") or os.path.getsize(full)),
        "durationMs": int(dur * 1000),
    }


def fetch_best(artist, title, dur, dest_noext):
    cands = rank(search(f"{artist} {title} official audio") + search(f"{artist} - {title} topic", 5), artist, title, dur)
    if not cands:
        raise LookupError("no matching upload")
    last = "download failed"
    for c in cands:
        tmp = tempfile.mkdtemp(prefix="ytdl-")
        try:
            p = subprocess.run(["yt-dlp", "--no-warnings", "--no-playlist", "-f", "bestaudio[ext=m4a]/bestaudio", "--max-filesize", "80M",
                                "-o", os.path.join(tmp, "a.%(ext)s"), f"https://www.youtube.com/watch?v={c['id']}"], capture_output=True, text=True, timeout=150)
            files = [f for f in os.listdir(tmp) if f.startswith("a.")]
            if p.returncode != 0 or not files:
                last = (p.stderr or "download failed").strip().splitlines()[-1][:200] if p.stderr.strip() else "download failed"
                continue
            src = os.path.join(tmp, files[0])
            ext = files[0].rsplit(".", 1)[-1]
            if ext == "webm":
                ext = "opus"
            tagged = os.path.join(tmp, "t." + ext)
            r = subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", src, "-vn", "-c:a", "copy", "-metadata", f"title={title}", "-metadata", f"artist={artist}", tagged],
                               capture_output=True, text=True, timeout=120)
            final = tagged if r.returncode == 0 and os.path.getsize(tagged) > 10_000 else src
            size = os.path.getsize(final)
            if size < 10_000 or size > MAX_BYTES:
                last = "file size out of range"
                continue
            dest = f"{dest_noext}.{ext}"
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            try:
                shutil.copyfile(final, dest + ".part")  # no copystat: the NAS filesystem refuses to take over permissions
                os.replace(dest + ".part", dest)
            except Exception:
                if os.path.exists(dest + ".part"):
                    os.remove(dest + ".part")
                raise
            return {"ok": True, "ext": ext, "bytes": size, "durationSec": c.get("duration") or 0, "channel": c.get("channel") or c.get("uploader") or ""}
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
    raise RuntimeError(last)


class Handler(http.server.BaseHTTPRequestHandler):
    def _reply(self, code, obj):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        if self.headers.get("X-Api-Key") != API_KEY:
            return self._reply(401, {"error": "unauthorized"})
        if self.path == "/probe":
            try:
                body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
                return self._reply(200, probe(str(body["path"])))
            except FileNotFoundError as e:
                return self._reply(404, {"error": str(e)})
            except Exception as e:
                return self._reply(400, {"error": str(e)})
        if self.path != "/ytdl":
            return self._reply(404, {"error": "not found"})
        try:
            body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
            artist, title = str(body["artist"]).strip(), str(body["title"]).strip()
            if not artist or not title:
                raise ValueError("artist and title are required")
            dest = resolve(str(body["to"]))
        except Exception as e:
            return self._reply(400, {"error": str(e)})
        for ext in ("m4a", "opus", "mp3", "webm"):  # already there: nothing to do
            if os.path.exists(f"{dest}.{ext}"):
                return self._reply(200, {"ok": True, "ext": ext, "existing": True, "bytes": os.path.getsize(f"{dest}.{ext}"), "durationSec": 0, "channel": ""})
        with GATE:
            try:
                self._reply(200, fetch_best(artist, title, int(body.get("durationSec") or 0), dest))
            except LookupError as e:
                self._reply(404, {"error": str(e)})
            except Exception as e:
                self._reply(502, {"error": str(e)})

    def log_message(self, format, *args):
        pass


class Server(socketserver.ThreadingMixIn, socketserver.TCPServer):
    daemon_threads = True
    allow_reuse_address = True


with Server(("0.0.0.0", 8091), Handler) as httpd:
    httpd.serve_forever()
