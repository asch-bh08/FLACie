import http.server
import json
import mimetypes
import os
import re
import shutil
import socketserver
import urllib.parse
import urllib.request
import ipaddress
import socket

API_KEY = os.environ["API_KEY"]
ROOT = "/data"
MAX_FETCH = 700 * 1024 * 1024


def check_public(url):
    p = urllib.parse.urlparse(url)
    if p.scheme != "https" or not p.hostname:
        raise ValueError("only https URLs")
    for info in socket.getaddrinfo(p.hostname, p.port or 443, proto=socket.IPPROTO_TCP):
        ip = ipaddress.ip_address(info[4][0])
        if ip.is_private or ip.is_loopback or ip.is_link_local or ip.is_reserved or ip.is_multicast:
            raise ValueError("address not allowed")


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        check_public(newurl)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


class Handler(http.server.BaseHTTPRequestHandler):
    def _reply(self, code, obj):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _authed(self):
        if self.headers.get("X-Api-Key") != API_KEY:
            self._reply(401, {"error": "unauthorized"})
            return False
        return True

    def _resolve(self, rel):
        full = os.path.normpath(os.path.join(ROOT, rel.lstrip("/")))
        if not full.startswith(ROOT + os.sep):
            raise ValueError("path escapes root")
        return full

    def do_POST(self):
        if not self._authed():
            return
        if self.path == "/fetch":
            self._fetch()
            return
        if self.path != "/move":
            self._reply(404, {"error": "not found"})
            return
        length = int(self.headers.get("Content-Length", 0))
        try:
            body = json.loads(self.rfile.read(length) or b"{}")
            src = self._resolve(body["from"])
            dst = self._resolve(body["to"])
            if not os.path.isfile(src):
                raise FileNotFoundError(f"source not found: {body['from']}")
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.move(src, dst)
            self._reply(200, {"ok": True})
        except Exception as e:
            self._reply(400, {"error": str(e)})

    def _fetch(self):
        """POST /fetch {"url": "https://...", "to": "relative/path.ext"}: downloads a public https file straight into the library.
        Used for the open sources (Internet Archive, Jamendo, Audius). Refuses private/loopback addresses (also after redirects),
        non-https URLs and files over MAX_FETCH; written as .part and renamed only when complete."""
        length = int(self.headers.get("Content-Length", 0))
        tmp = None
        try:
            body = json.loads(self.rfile.read(length) or b"{}")
            url = body["url"]
            dst = self._resolve(body["to"])
            if os.path.exists(dst):
                self._reply(200, {"ok": True, "existing": True})
                return
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            tmp = dst + ".part"
            opener = urllib.request.build_opener(SafeRedirect)
            check_public(url)
            req = urllib.request.Request(url, headers={"User-Agent": "FLACie/1.0 (personal music player)"})
            total = 0
            with opener.open(req, timeout=60) as r, open(tmp, "wb") as out:
                while True:
                    chunk = r.read(65536)
                    if not chunk:
                        break
                    total += len(chunk)
                    if total > MAX_FETCH:
                        raise ValueError("file too large")
                    out.write(chunk)
            if total < 10_000:
                raise ValueError("file too small to be a song")
            os.replace(tmp, dst)
            self._reply(200, {"ok": True, "bytes": total})
        except Exception as e:
            if tmp and os.path.exists(tmp):
                os.remove(tmp)
            self._reply(400, {"error": str(e)})

    def do_GET(self):
        if not self._authed():
            return
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path != "/file":
            self._reply(404, {"error": "not found"})
            return
        try:
            rel = urllib.parse.parse_qs(parsed.query).get("path", [None])[0]
            if not rel:
                raise ValueError("missing path")
            full = self._resolve(rel)
            if not os.path.isfile(full):
                raise FileNotFoundError(f"not found: {rel}")
        except Exception as e:
            self._reply(400, {"error": str(e)})
            return

        size = os.path.getsize(full)
        content_type = mimetypes.guess_type(full)[0] or "application/octet-stream"
        range_header = self.headers.get("Range")
        start, end = 0, size - 1
        status = 200
        if range_header:
            m = re.match(r"bytes=(\d*)-(\d*)", range_header)
            if m and (m.group(1) or m.group(2)):
                if m.group(1):
                    start = int(m.group(1))
                    end = int(m.group(2)) if m.group(2) else size - 1
                else:
                    start = size - int(m.group(2))
                    end = size - 1
                status = 206
        length = end - start + 1

        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(length))
        self.send_header("Accept-Ranges", "bytes")
        if status == 206:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.end_headers()
        with open(full, "rb") as f:
            f.seek(start)
            remaining = length
            while remaining > 0:
                chunk = f.read(min(65536, remaining))
                if not chunk:
                    break
                try:
                    self.wfile.write(chunk)
                except (BrokenPipeError, ConnectionResetError):
                    break
                remaining -= len(chunk)

    def log_message(self, format, *args):
        pass


class ThreadingServer(socketserver.ThreadingMixIn, socketserver.TCPServer):
    daemon_threads = True
    allow_reuse_address = True


with ThreadingServer(("0.0.0.0", 8090), Handler) as httpd:
    httpd.serve_forever()
