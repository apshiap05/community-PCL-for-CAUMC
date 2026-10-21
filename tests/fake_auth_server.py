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
        if self.path.split('?', 1)[0] == "/pack.zip":
            archive = io.BytesIO()
            with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as pack:
                pack.writestr('.minecraft/versions/caumc-download-fixture/caumc-download-fixture.json', json.dumps({
                    'id': 'caumc-download-fixture', 'mainClass': 'net.minecraft.client.main.Main',
                    'type': 'release', 'releaseTime': '2024-08-08T00:00:00Z', 'libraries': [],
                    'arguments': {'game': [], 'jvm': []}, 'assetIndex': {'id': 'fixture'}
                }))
                pack.writestr('.minecraft/versions/caumc-download-fixture/PCL/Setup.ini', 'VersionArgumentIndieV2:True\n')
                pack.writestr('.minecraft/versions/caumc-download-fixture/mods/fixture.jar', 'downloaded-mod-marker')
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
