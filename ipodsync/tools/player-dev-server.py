#!/usr/bin/env python3
"""Headless stand-in for ipod-player/ipod-server.ps1, for testing the player UI.

Serves ipod-player.html and the same API as the PowerShell helper (/api/info,
/ipod/<path>, /api/upload, /api/apply-edits) against any folder, without opening
a browser window. Point it at a FAKE root (see tools/fake-root-regression.sh)
when exercising edits:

    python tools/player-dev-server.py <ipod-root> [port]

/api/apply-edits runs the real engine exactly like the helper does: a dry run,
or --yes when the request has ?commit=1.
"""
import http.server, json, os, subprocess, sys, tempfile, urllib.parse, uuid

ROOT = os.path.abspath(sys.argv[1])
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 8790
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HTML = os.path.join(REPO, 'ipod-player', 'ipod-player.html')
CLI = next((p for p in [os.path.join(REPO, 'src', 'IpodSync.Cli', 'bin', c, 'net9.0', 'IpodSync.Cli' + e)
                        for c in ('Release', 'Debug') for e in ('.exe', '')] if os.path.exists(p)), None)
CONTROL = 'iTunes_Control' if os.path.isdir(os.path.join(ROOT, 'iTunes_Control')) else 'iPod_Control'
UPLOADS = os.path.join(tempfile.gettempdir(), 'ipod-uploads')


class Handler(http.server.BaseHTTPRequestHandler):
    def _send(self, code, body=b'', ctype='application/json', extra=None):
        self.send_response(code)
        self.send_header('Content-Type', ctype)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Accept-Ranges', 'bytes')
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        if self.command != 'HEAD':
            self.wfile.write(body)

    def do_HEAD(self):
        self.do_GET()

    def do_GET(self):
        path = urllib.parse.unquote(urllib.parse.urlparse(self.path).path)
        if path in ('/', '/index.html'):
            return self._send(200, open(HTML, 'rb').read(), 'text/html; charset=utf-8')
        if path == '/api/info':
            info = {'label': os.path.basename(ROOT.rstrip('\\/')) or 'iPod', 'sub': CONTROL, 'control': CONTROL,
                    'ffmpeg': True, 'editor': CLI is not None}
            return self._send(200, json.dumps(info).encode())
        if path.startswith('/ipod/'):
            full = os.path.abspath(os.path.join(ROOT, path[6:]))
            if not full.startswith(ROOT) or not os.path.isfile(full):
                return self._send(404)
            size = os.path.getsize(full)
            rng = self.headers.get('Range')
            with open(full, 'rb') as f:
                if rng and rng.startswith('bytes='):
                    a, _, b = rng[6:].partition('-')
                    start = int(a) if a else 0
                    end = min(int(b), size - 1) if b else size - 1
                    f.seek(start)
                    return self._send(206, f.read(max(0, end - start + 1)), 'application/octet-stream',
                                      {'Content-Range': f'bytes {start}-{end}/{size}'})
                return self._send(200, f.read(), 'application/octet-stream')
        self._send(404)

    def do_POST(self):
        parsed = urllib.parse.urlparse(self.path)
        body = self.rfile.read(int(self.headers.get('Content-Length', 0)))
        if parsed.path == '/api/upload':
            name = os.path.basename(urllib.parse.parse_qs(parsed.query).get('name', ['upload.bin'])[0])
            os.makedirs(UPLOADS, exist_ok=True)
            dest = os.path.join(UPLOADS, uuid.uuid4().hex + '-' + name)
            open(dest, 'wb').write(body)
            return self._send(200, json.dumps({'ok': True, 'path': dest, 'name': name}).encode())
        if parsed.path == '/api/apply-edits':
            if CLI is None:
                return self._send(200, json.dumps({'ok': False, 'error': 'engine not built'}).encode())
            commit = 'commit=1' in parsed.query
            fd, tmp = tempfile.mkstemp(suffix='.json')
            os.write(fd, body); os.close(fd)
            args = [CLI, 'apply-edits', ROOT, '--changes', tmp] + (['--yes'] if commit else [])
            proc = subprocess.run(args, capture_output=True, text=True, encoding='utf-8', errors='replace')
            os.remove(tmp)
            out = {'ok': proc.returncode == 0, 'commit': commit, 'exit': proc.returncode, 'output': proc.stdout + proc.stderr}
            return self._send(200, json.dumps(out).encode())
        self._send(404)

    def log_message(self, fmt, *args):
        sys.stderr.write('%s %s\n' % (self.command, self.path))


print(f'player dev server: http://localhost:{PORT}/  root={ROOT}  engine={CLI}', flush=True)
http.server.ThreadingHTTPServer(('127.0.0.1', PORT), Handler).serve_forever()
