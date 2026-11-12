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
import urllib.parse

API_KEY = open("/key").read().strip()
ROOT = "/data"
MAX_BYTES = 120 * 1024 * 1024
GATE = threading.Semaphore(2)  # at most two songs at once

# uploads that are a different recording of the song unless the wanted title says so itself
BAD = ["live", "cover", "remix", "karaoke", "instrumental", "sped up", "speed up", "slowed", "reverb", "nightcore", "8d", "mashup", "tribute", "acoustic",
       "reaction", "tutorial", "lesson", "piano version", "bass boosted", "loop", "1 hour", "extended", "mv", "fanmade", "fan made"]


VERSION_WORD = re.compile(r"\b(live|remix|mix|acoustic|instrumental|demo|cover|karaoke|session|unplugged|vip|rework|bootleg|extended|sped|slowed|reverb|a cappella|acapella|nightcore)\b", re.I)
PART_MARKER = re.compile(r"^\s*(pt|part)\b", re.I)
TRAILING_GROUP = re.compile(r"\s*[(\[]([^)\]]*)[)\]]\s*$")
DASH_NOTE = re.compile(r"\s+-\s+(?:\d{4}\s+)?(?:remaster(?:ed)?|from\b|single version|album version|original (?:motion picture )?soundtrack|mono\b|stereo\b|bonus|deluxe|explicit|clean|radio edit)[^-]*$", re.I)


def base_title(t):
    """The song's own name: '(feat. X)', '(From "Some Movie")', '(Remastered 2011)', ' - Remastered' go; '(Live)', '(Remix)', '(Part 2)' stay (same rules as the apps)."""
    s = re.sub(r"\s*[\(\[](feat|ft|with|featuring)[^\)\]]*[\)\]]", "", t or "", flags=re.I).strip()
    for _ in range(4):
        m = TRAILING_GROUP.search(s)
        if not m or m.start() == 0 or VERSION_WORD.search(m.group(1)) or PART_MARKER.search(m.group(1)):
            break
        s = s[:m.start()].rstrip()
    s = DASH_NOTE.sub("", s).strip()
    return s or (t or "").strip()


REMIX = re.compile(r"[\(\[]([^\)\]]*\b(?:remix|mix|edit|bootleg|vip|rework|flip|refix|dub)\b[^\)\]]*)[\)\]]|\s[-\u2013]\s([^-\u2013()\[\]]*\b(?:remix|mix|edit|bootleg|vip|rework|flip|refix|dub)\b[^-\u2013()\[\]]*)$", re.I)
PLAIN_VERSION = re.compile(r"^\s*(?:official\s+)?(?:radio|album|single|original|extended)\s+(?:edit|mix|version)\s*$", re.I)


def remix(t):
    """The remix's name ('kryd hoodtrap remix'), or None (a radio edit or original mix is none). Same rules as RemixMatch in FLACie.Core."""
    for m in REMIX.finditer(t or ""):
        r = (m.group(1) or m.group(2) or "").strip()
        if r and not PLAIN_VERSION.search(r):
            return norm(r)
    return None


def norm(s):
    s = unicodedata.normalize("NFKD", s or "").encode("ascii", "ignore").decode().lower()
    s = re.sub(r"\s*[\(\[](feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]", "", s)
    s = re.sub(r"\s+(feat\.?|ft\.?|featuring)\s.*$", "", s)
    return " ".join(re.sub(r"[^a-z0-9]+", " ", s).split())


def lead_artist(a):
    first = re.split(r"\s*(?:;|,|&|/|\sx\s|\s(?:feat\.?|ft\.?|featuring|with)\s)\s*", a or "", flags=re.I)[0]
    n = norm(first)
    return n[4:] if n.startswith("the ") and len(n) > 4 else n


def real_seconds(path):
    """How long the file really plays (ffprobe), or 0 when it cannot be read."""
    try:
        r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", path], capture_output=True, text=True, timeout=30)
        return float(r.stdout.strip() or 0)
    except Exception:
        return 0.0


def preview(real, said):
    """A preview instead of the song: SoundCloud serves a 30 s clip of some tracks while naming the whole track's length (owner, 6 Nov: a 30 s Ice MC
    Megamix filed as the song). Well short of the length the source names, or under 45 s when it names none."""
    if real <= 0:
        return False
    return real < said * 0.8 if said and said > 60 else real < 45


def resolve(rel):
    full = os.path.normpath(os.path.join(ROOT, rel.lstrip("/")))
    if not full.startswith(ROOT + os.sep):
        raise ValueError("path escapes root")
    return full


def search(query, n=8, site="ytsearch"):
    p = subprocess.run(["yt-dlp", "--no-warnings", "--flat-playlist", "--dump-json", f"{site}{n}:{query}"], capture_output=True, text=True, timeout=45)
    out = []
    for line in p.stdout.splitlines():
        try:
            out.append(json.loads(line))
        except ValueError:
            pass
    return out


def rank(cands, artist, title, dur, relaxed=False):
    a, t = lead_artist(artist), norm(base_title(title))
    tw = set(t.split())
    want_remix = remix(title)
    remixer = (want_remix or "").split(" ")[0] if want_remix else ""
    scored, seen = [], set()
    for c in cands:
        vid = c.get("id")
        if not vid or vid in seen or c.get("live_status") in ("is_live", "is_upcoming"):
            continue
        seen.add(vid)
        ct, chan = norm(c.get("title")), norm(c.get("channel") or c.get("uploader"))
        # a remix keeps its name: only that remix matches it, and an original never takes a remix (F7)
        if remix(c.get("title") or "") != want_remix:
            continue
        if not ((tw <= set(ct.split())) if relaxed else (t in ct)) or not (a in ct or a in chan or (remixer and (remixer in ct or remixer in chan))):
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


# F25: the hosts /playlist reads (anything else is refused), and how many songs it lists at most (same rules as PlaylistLinks in FLACie.Core)
PLAYLIST_HOSTS = {"youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be", "soundcloud.com", "www.soundcloud.com", "m.soundcloud.com"}
PLAYLIST_MAX = 500
SETS_MAX = 50


def playlist_allowed(url):
    try:
        u = urllib.parse.urlparse(url)
    except ValueError:
        return False
    return u.scheme == "https" and (u.hostname or "").lower() in PLAYLIST_HOSTS


def read_playlist(url):
    """F25.1 POST /playlist {"url"}: a YouTube / YouTube Music playlist (--flat-playlist gives titles) or a SoundCloud set (needs the full read: flat
    gives no titles). Answers {name, tracks: [{title, artist, durationMs}]}, artist being the uploader; FLACie cleans YouTube titles itself."""
    if not playlist_allowed(url):
        raise ValueError("only YouTube and SoundCloud playlist links")
    sc = "soundcloud.com" in (urllib.parse.urlparse(url).hostname or "")
    cmd = ["yt-dlp", "--no-warnings", "-J", "--playlist-end", str(PLAYLIST_MAX)] + ([] if sc else ["--flat-playlist"]) + [url]
    p = subprocess.run(cmd, capture_output=True, text=True, timeout=200)
    try:
        j = json.loads(p.stdout or "{}")
    except ValueError:
        j = {}
    tracks = []
    for e in (j.get("entries") or [])[:PLAYLIST_MAX]:
        if not e:
            continue
        title = (e.get("track") or e.get("title") or "").strip()
        artist = (e.get("artist") or e.get("uploader") or e.get("channel") or "").strip()
        if title:
            tracks.append({"title": title[:200], "artist": artist[:200], "durationMs": int((e.get("duration") or 0) * 1000)})
    if not tracks:
        raise LookupError("no songs at that link")
    return {"name": (j.get("title") or "")[:200], "tracks": tracks}


def artist_sets(q):
    """F25.3 POST /artist-sets {"q"}: the artist's own SoundCloud account (the uploader named exactly like the artist among the first five search
    hits; none means no lists) and its sets (names and links only, cheap). Answers {account, sets: [{title, url}]}."""
    q = (q or "").strip()[:100]
    if not q:
        raise ValueError("missing artist")
    p = subprocess.run(["yt-dlp", "--no-warnings", "--flat-playlist", "--print", "%(uploader)s|%(uploader_url)s", f"scsearch5:{q}"], capture_output=True, text=True, timeout=60)
    account = None
    for line in p.stdout.splitlines():
        name, _, url = line.partition("|")
        if norm(name) == norm(q) and url.startswith("https://soundcloud.com/"):
            account = url.rstrip("/")
            break
    if not account:
        return {"account": None, "sets": []}
    p = subprocess.run(["yt-dlp", "--no-warnings", "--flat-playlist", "-J", "--playlist-end", str(SETS_MAX), account + "/sets"], capture_output=True, text=True, timeout=60)
    try:
        j = json.loads(p.stdout or "{}")
    except ValueError:
        j = {}
    sets = [{"title": (e.get("title") or "")[:200], "url": e.get("url") or ""} for e in (j.get("entries") or [])[:SETS_MAX]
            if e and (e.get("url") or "").startswith("https://soundcloud.com/")]
    return {"account": account, "sets": sets}


FIND_MAX = 12


def find_sets(q):
    """POST /find-sets {"q"} (owner, 12 Nov: "if I search for a playlist I saw on Spotify or my mate's one it should be in my library"): anyone's
    public SoundCloud playlists whose name matches, through yt-dlp's own SoundCloud API access (search/playlists; names, links and counts only, cheap;
    their songs are read with /playlist when one is opened). Answers {sets: [{title, url, uploader, tracks, likes, art}]}."""
    q = (q or "").strip()[:120]
    if len(q) < 3:
        raise ValueError("missing name")
    import yt_dlp
    ydl = yt_dlp.YoutubeDL({"quiet": True, "no_warnings": True})
    ie = ydl.get_info_extractor("SoundcloudSearch")
    ie.initialize()
    r = ie._call_api(ie._API_V2_BASE + "search/playlists", q, query={"q": q, "limit": FIND_MAX, "linked_partitioning": 1}, headers=ie._HEADERS)
    sets = []
    for e in (r.get("collection") or [])[:FIND_MAX]:
        url = (e or {}).get("permalink_url") or ""
        if not url.startswith("https://soundcloud.com/") or "/sets/" not in url:
            continue
        sets.append({"title": (e.get("title") or "")[:200], "url": url, "uploader": ((e.get("user") or {}).get("username") or "")[:200],
                     "tracks": int(e.get("track_count") or 0), "likes": int(e.get("likes_count") or 0), "art": (e.get("artwork_url") or "")[:400]})
    return {"sets": sets}


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


def fetch_best(artist, title, dur, dest_noext, source="youtube"):
    bt = base_title(title)
    sc = source == "soundcloud"
    # SoundCloud (F7): scsearch, and the producer's original file ("download") when the track offers one
    found = search(f"{artist} {bt}", 8, "scsearch") if sc else search(f"{artist} {bt} official audio") + search(f"{artist} - {bt} topic", 5)
    cands = rank(found, artist, title, dur) or rank(found, artist, title, dur, relaxed=True)
    if not cands:
        raise LookupError("no matching upload")
    last = "download failed"
    for c in cands:
        tmp = tempfile.mkdtemp(prefix="ytdl-")
        try:
            url = c.get("webpage_url") or c.get("url") if sc else f"https://www.youtube.com/watch?v={c['id']}"
            p = subprocess.run(["yt-dlp", "--no-warnings", "--no-playlist", "-f", "download/bestaudio" if sc else "bestaudio[ext=m4a]/bestaudio", "--max-filesize", "80M",
                                "-o", os.path.join(tmp, "a.%(ext)s"), url], capture_output=True, text=True, timeout=150)
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
            real = real_seconds(final)
            if preview(real, c.get("duration") or 0):
                last = f"only a {int(real)} s preview"
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
            return {"ok": True, "ext": ext, "source": source, "bytes": size, "durationSec": int(real) or c.get("duration") or 0, "channel": c.get("channel") or c.get("uploader") or ""}
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
    raise RuntimeError(last)


SAFE = re.compile(r'[\\/:*?"<>|\x00-\x1f]')
TITLE_NOISE = re.compile(r"\s*[(\[](?:official\s*(?:music\s*)?(?:video|audio)|lyrics?(?:\s*video)?|audio|hd|hq|free download|out now)[)\]]", re.I)


def clean_name(s):
    return SAFE.sub(" ", s or "").strip().strip(".")[:120].strip() or "Unknown"


def split_title(title, uploader):
    """"Artist - Title (Remix)" names the artist; otherwise the uploader is the artist (a producer's own remix on their SoundCloud)."""
    t = TITLE_NOISE.sub("", title or "").strip()
    m = re.match(r"^(.+?)\s+[-–—]\s+(.+)$", t)
    if m:
        return m.group(1).strip(), m.group(2).strip()
    return re.sub(r"\s*-\s*Topic$|VEVO$", "", uploader or "", flags=re.I).strip() or "Unknown", t or "Unknown"


def fetch_url(url, to_dir):
    """POST /ytdl {"url", "toDir"} (owner, 8 Nov): exactly the song at a SoundCloud or YouTube link, filed as Artist/Artist - Title (SoundCloud|YouTube).ext
    under [to_dir]. Answers the artist and title it was filed under and its path from the library root."""
    if not playlist_allowed(url):
        raise ValueError("only https SoundCloud and YouTube links")
    p = subprocess.run(["yt-dlp", "--no-warnings", "--no-playlist", "-J", url], capture_output=True, text=True, timeout=60)
    if p.returncode != 0:
        raise LookupError((p.stderr or "could not read that link").strip().splitlines()[-1][:200] if (p.stderr or "").strip() else "could not read that link")
    info = json.loads(p.stdout)
    sc = "soundcloud.com" in (urllib.parse.urlparse(url).hostname or "")
    artist, title = split_title(info.get("title") or "", info.get("artist") or info.get("uploader") or info.get("channel") or "")
    a = clean_name(artist)
    rel = "/".join(x for x in [to_dir.strip("/"), a, f"{a} - {clean_name(title)} ({'SoundCloud' if sc else 'YouTube'})"] if x)
    dest_noext = resolve(rel)
    for ext in ("m4a", "opus", "mp3", "webm", "wav", "flac", "aiff"):
        if os.path.exists(f"{dest_noext}.{ext}"):
            return {"ok": True, "ext": ext, "existing": True, "artist": artist, "title": title, "path": f"{rel}.{ext}", "durationSec": int(info.get("duration") or 0)}
    tmp = tempfile.mkdtemp(prefix="ytdl-")
    try:
        d = subprocess.run(["yt-dlp", "--no-warnings", "--no-playlist", "-f", "download/bestaudio" if sc else "bestaudio[ext=m4a]/bestaudio", "--max-filesize", "80M",
                            "-o", os.path.join(tmp, "a.%(ext)s"), url], capture_output=True, text=True, timeout=170)
        files = [f for f in os.listdir(tmp) if f.startswith("a.")]
        if d.returncode != 0 or not files:
            raise RuntimeError((d.stderr or "").strip().splitlines()[-1][:200] if (d.stderr or "").strip() else "download failed")
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
            raise RuntimeError("file size out of range")
        real = real_seconds(final)
        if preview(real, info.get("duration") or 0):
            raise RuntimeError(f"only a {int(real)} s preview is public")
        dest = f"{dest_noext}.{ext}"
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        try:
            shutil.copyfile(final, dest + ".part")  # no copystat: the NAS filesystem refuses to take over permissions
            os.replace(dest + ".part", dest)
        except Exception:
            if os.path.exists(dest + ".part"):
                os.remove(dest + ".part")
            raise
        return {"ok": True, "ext": ext, "artist": artist, "title": title, "path": f"{rel}.{ext}", "bytes": size, "durationSec": int(real) or int(info.get("duration") or 0)}
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


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
        if self.path in ("/playlist", "/artist-sets", "/find-sets"):
            try:
                body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
            except Exception as e:
                return self._reply(400, {"error": str(e)})
            with GATE:
                try:
                    if self.path == "/find-sets":
                        return self._reply(200, find_sets(str(body.get("q") or "")))
                    return self._reply(200, read_playlist(str(body.get("url") or "")) if self.path == "/playlist" else artist_sets(str(body.get("q") or "")))
                except ValueError as e:
                    return self._reply(400, {"error": str(e)})
                except LookupError as e:
                    return self._reply(404, {"error": str(e)})
                except Exception as e:
                    return self._reply(502, {"error": str(e)})
        if self.path != "/ytdl":
            return self._reply(404, {"error": "not found"})
        try:
            body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
        except Exception as e:
            return self._reply(400, {"error": str(e)})
        if body.get("url"):
            with GATE:
                try:
                    return self._reply(200, fetch_url(str(body["url"]), str(body.get("toDir") or "")))
                except ValueError as e:
                    return self._reply(400, {"error": str(e)})
                except LookupError as e:
                    return self._reply(404, {"error": str(e)})
                except Exception as e:
                    return self._reply(502, {"error": str(e)})
        try:
            artist, title = str(body["artist"]).strip(), str(body["title"]).strip()
            if not artist or not title:
                raise ValueError("artist and title are required")
            dest = resolve(str(body["to"]))
        except Exception as e:
            return self._reply(400, {"error": str(e)})
        source = "soundcloud" if str(body.get("source") or "") == "soundcloud" else "youtube"
        for ext in ("m4a", "opus", "mp3", "webm", "wav", "flac", "aiff"):  # already there: nothing to do
            if os.path.exists(f"{dest}.{ext}"):
                return self._reply(200, {"ok": True, "ext": ext, "existing": True, "bytes": os.path.getsize(f"{dest}.{ext}"), "durationSec": 0, "channel": ""})
        with GATE:
            try:
                self._reply(200, fetch_best(artist, title, int(body.get("durationSec") or 0), dest, source))
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
