"""Local-only Yggdrasil fixture; no real account data or remote writes."""
import json
import sys
import time
import io
import zipfile
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

PROFILE = {"id": "0123456789abcdef0123456789abcdef", "name": "LocalPlayer"}

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def do_GET(self):
        if self.path.split('?', 1)[0] == '/launcher-update.exe':
            payload = (Path(sys.argv[1]).parent / 'launcher-update.exe').read_bytes()
            self.send_response(200)
            self.send_header('Content-Length', str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
            return
        if self.path.split('?', 1)[0] == '/task-large.bin':
            total = 4 * 1024 * 1024
            first, last = 0, total - 1
            byte_range = self.headers.get('Range', '')
            if byte_range.startswith('bytes='):
                values = byte_range[6:].split('-', 1)
                first = int(values[0])
                last = int(values[1]) if values[1] else last
            self.send_response(206 if byte_range else 200)
            self.send_header('Content-Length', str(last - first + 1))
            self.send_header('ETag', '"task-fixture-v1"')
            self.send_header('Accept-Ranges', 'bytes')
            if byte_range:
                self.send_header('Content-Range', f'bytes {first}-{last}/{total}')
            self.end_headers()
            try:
                for offset in range(first, last + 1, 16384):
                    self.wfile.write(b'Q' * min(16384, last - offset + 1))
                    self.wfile.flush()
                    time.sleep(0.003)
            except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
                pass
            return
        if self.path.split('?', 1)[0] == "/pack.zip":
            archive = io.BytesIO()
            with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as pack:
                pack.writestr('.minecraft/versions/caumc-download-fixture/caumc-download-fixture.json', json.dumps({
                    'id': 'caumc-download-fixture', 'mainClass': 'net.minecraft.client.main.Main',
                    'type': 'release', 'releaseTime': '2024-08-08T00:00:00Z', 'libraries': [],
                    'arguments': {'game': [], 'jvm': []}, 'assetIndex': {'id': 'fixture'}
                }))
                pack.writestr('.minecraft/versions/caumc-download-fixture/PCL/Setup.ini', 'VersionArgumentIndieV2:True\n')
                game_jar = io.BytesIO()
                with zipfile.ZipFile(game_jar, 'w') as jar:
                    jar.writestr('version.json', json.dumps({'id': '1.20.1', 'name': '1.20.1', 'release_target': '1.20.1'}))
                pack.writestr('.minecraft/versions/caumc-download-fixture/caumc-download-fixture.jar', game_jar.getvalue())
                pack.writestr('.minecraft/versions/caumc-download-fixture/mods/fixture.jar',
                              'updated-mod-marker' if 'mismatch=1' in self.path else 'downloaded-mod-marker')
                pack.writestr('.minecraft/versions/caumc-download-fixture/saves/fixture/level.dat', 'downloaded-world-marker')
            payload = archive.getvalue()
            self.send_response(200)
            self.send_header('Content-Type', 'application/zip')
            self.send_header('Content-Length', str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
            return
        payload = json.dumps({**PROFILE, "properties": []}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_POST(self):
        data = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
        status, reply = 200, {}
        if self.path.endswith("/authenticate"):
            if data["username"] == "delayed":
                time.sleep(0.5)
            if data["username"] == "wrong-password":
                status, reply = 403, {"error": "ForbiddenOperationException", "errorMessage": "Incorrect username or password"}
            elif data["username"] == "no-role":
                reply = {"accessToken": "empty-profile-token", "clientToken": "local-client", "availableProfiles": []}
            else:
                reply = {"accessToken": "initial-local-token", "clientToken": "local-client", "availableProfiles": [PROFILE]}
        elif self.path.endswith("/validate"):
            status = 204 if data["accessToken"] == "refreshed-local-token" else 403
            reply = {} if status == 204 else {"error": "ForbiddenOperationException", "errorMessage": "Invalid token"}
        elif self.path.endswith("/refresh"):
            reply = {"accessToken": "refreshed-local-token", "clientToken": "local-client", "selectedProfile": PROFILE}
        elif self.path.endswith("/invalidate"):
            status = 204
        else:
            status = 404
        payload = json.dumps(reply).encode() if status != 204 else b""
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

server = HTTPServer(("127.0.0.1", 0), Handler)
Path(sys.argv[1]).write_text(f"http://127.0.0.1:{server.server_port}/api/yggdrasil", encoding="utf-8")
server.serve_forever()
