"""Tests for the file mover's /retire and /health (smart downloads: an old copy is moved aside once its Hi-Res copy has been verified, never deleted).

Run from anywhere:  python -I ipodsync/tools/filemove/test_server.py
It starts the file mover on a free local port with a temporary music folder; nothing outside that folder is touched.
"""
import importlib.util
import json
import os
import shutil
import sys
import tempfile
import threading
import unittest
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
KEY = "test-key"


def load_server(root):
    os.environ["API_KEY"] = KEY
    os.environ["FILEMOVE_ROOT"] = root
    spec = importlib.util.spec_from_file_location("filemove_server", os.path.join(HERE, "server.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


class RetireTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = os.path.realpath(tempfile.mkdtemp(prefix="filemove-test-"))
        cls.mod = load_server(cls.root)
        cls.httpd = cls.mod.ThreadingServer(("127.0.0.1", 0), cls.mod.Handler)
        cls.base = f"http://127.0.0.1:{cls.httpd.server_address[1]}"
        threading.Thread(target=cls.httpd.serve_forever, daemon=True).start()

    @classmethod
    def tearDownClass(cls):
        cls.httpd.shutdown()
        cls.httpd.server_close()
        shutil.rmtree(cls.root, ignore_errors=True)

    def setUp(self):
        for name in os.listdir(self.root):
            p = os.path.join(self.root, name)
            shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)

    def put(self, rel, data=b"old song"):
        full = os.path.join(self.root, rel)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "wb") as f:
            f.write(data)
        return full

    def call(self, method, path, body=None, key=KEY):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(self.base + path, data=data, method=method, headers={"X-Api-Key": key, "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=10) as r:
                return r.status, json.loads(r.read() or b"{}")
        except urllib.error.HTTPError as e:
            return e.code, json.loads(e.read() or b"{}")

    def test_retire_moves_the_file_into_the_replaced_folder_and_deletes_nothing(self):
        self.put("Artist/Artist - Song.m4a", b"the old copy")
        code, j = self.call("POST", "/retire", {"path": "Artist/Artist - Song.m4a"})
        self.assertEqual(200, code, j)
        self.assertTrue(j["ok"])
        self.assertFalse(os.path.exists(os.path.join(self.root, "Artist", "Artist - Song.m4a")))
        kept = os.path.join(self.root, ".flacie-replaced", "Artist", "Artist - Song.m4a")
        with open(kept, "rb") as f:
            self.assertEqual(b"the old copy", f.read())
        self.assertEqual(".flacie-replaced/Artist/Artist - Song.m4a", j["to"])

    def test_retiring_a_second_file_of_the_same_name_keeps_both(self):
        self.put("Artist/Artist - Song.mp3", b"first")
        self.call("POST", "/retire", {"path": "Artist/Artist - Song.mp3"})
        self.put("Artist/Artist - Song.mp3", b"second")
        code, j = self.call("POST", "/retire", {"path": "Artist/Artist - Song.mp3"})
        self.assertEqual(200, code, j)
        folder = os.path.join(self.root, ".flacie-replaced", "Artist")
        contents = []
        for n in os.listdir(folder):
            with open(os.path.join(folder, n), "rb") as f:
                contents.append(f.read())
        contents.sort()
        self.assertEqual([b"first", b"second"], contents)

    def test_a_path_outside_the_music_folder_is_refused(self):
        outside = tempfile.NamedTemporaryFile(delete=False, dir=os.path.dirname(self.root))
        outside.write(b"not music")
        outside.close()
        try:
            for bad in ["../" + os.path.basename(outside.name), "Artist/../../" + os.path.basename(outside.name)]:
                code, _ = self.call("POST", "/retire", {"path": bad})
                self.assertEqual(400, code, bad)
            # an absolute path is read inside the music folder (its leading slash dropped, as /move does) or refused: never followed out of it
            for bad in [outside.name, "/etc/passwd"]:
                code, _ = self.call("POST", "/retire", {"path": bad})
                self.assertIn(code, (400, 404), bad)
            self.assertTrue(os.path.exists(outside.name))
        finally:
            os.remove(outside.name)

    def test_the_music_folder_itself_and_folders_are_refused(self):
        self.put("Artist/Artist - Song.flac")
        for bad in ["", ".", "Artist", "Artist/"]:
            code, _ = self.call("POST", "/retire", {"path": bad})
            self.assertIn(code, (400, 404), bad)
        self.assertTrue(os.path.isfile(os.path.join(self.root, "Artist", "Artist - Song.flac")))

    def test_a_file_already_retired_is_not_moved_again(self):
        self.put(".flacie-replaced/Artist/Artist - Song.flac")
        code, _ = self.call("POST", "/retire", {"path": ".flacie-replaced/Artist/Artist - Song.flac"})
        self.assertEqual(400, code)

    def test_a_missing_file_is_404(self):
        code, _ = self.call("POST", "/retire", {"path": "Nobody/Nobody - Nothing.flac"})
        self.assertEqual(404, code)

    def test_retire_needs_the_key(self):
        self.put("Artist/Artist - Song.m4a")
        code, _ = self.call("POST", "/retire", {"path": "Artist/Artist - Song.m4a"}, key="wrong")
        self.assertEqual(401, code)
        self.assertTrue(os.path.exists(os.path.join(self.root, "Artist", "Artist - Song.m4a")))

    def test_health_says_which_routes_this_file_mover_has(self):
        code, j = self.call("GET", "/health")
        self.assertEqual(200, code)
        self.assertTrue(j["ok"])
        self.assertIn("retire", j["routes"])
        self.assertIn("move", j["routes"])
        code, _ = self.call("GET", "/health", key="wrong")
        self.assertEqual(401, code)

    def test_move_still_works(self):
        self.put("inbox/peer/Song.flac", b"x")
        code, _ = self.call("POST", "/move", {"from": "inbox/peer/Song.flac", "to": "Artist/Artist - Song.flac"})
        self.assertEqual(200, code)
        self.assertTrue(os.path.exists(os.path.join(self.root, "Artist", "Artist - Song.flac")))


if __name__ == "__main__":
    unittest.main(verbosity=2)
