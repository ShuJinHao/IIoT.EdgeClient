#!/usr/bin/env python3
import argparse
import json
import signal
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class Handler(BaseHTTPRequestHandler):
    server_version = "FakeEdgeReleaseCloud/1.0"

    def log_message(self, _format, *_args):
        return

    def _write_json(self, status, value):
        payload = json.dumps(value).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        if self.command != "HEAD":
            try:
                self.wfile.write(payload)
            except BrokenPipeError:
                pass

    def do_GET(self):
        if self.path.endswith("/download"):
            self._write_json(200, {"sourceCommit": "fake-source"})
            return
        if "/human/client-releases/catalog" in self.path:
            if self.path.startswith("/catalog-error/"):
                self._write_json(500, {"error": "catalog_unavailable", "detail": "injected failure"})
                return
            if self.path.startswith("/existing/"):
                self._write_json(
                    200,
                    {
                        "host": {
                            "versions": [
                                {
                                    "version": "9.9.9",
                                    "status": "Published",
                                    "downloadUrl": "/download",
                                }
                            ]
                        },
                        "plugins": [
                            {
                                "moduleId": "Homogenization",
                                "versions": [
                                    {
                                        "version": self.server.plugin_version,
                                        "targetRuntime": "win-x64",
                                        "downloadUrl": "/download",
                                    }
                                ],
                            }
                        ],
                    },
                )
                return
            self._write_json(200, {"host": {"versions": []}, "plugins": []})
            return
        if self.path == "/ok-json":
            self._write_json(200, {"host": {"versions": []}, "plugins": []})
            return
        if self.path == "/error-json":
            self._write_json(
                409,
                {
                    "error": "duplicate",
                    "detail": "already exists",
                    "accessToken": "secret-token-must-not-leak",
                },
            )
            return
        if self.path == "/slow":
            time.sleep(5)
            self._write_json(200, {"ok": True})
            return
        self._write_json(404, {"error": "not_found", "path": self.path})

    def do_HEAD(self):
        if self.path.endswith("/download"):
            self._write_json(200, {"ok": True})
            return
        self._write_json(404, {"error": "not_found", "path": self.path})

    def do_POST(self):
        content_length = int(self.headers.get("Content-Length", "0"))
        payload = self.rfile.read(content_length)
        with Path(self.server.request_log).open("a", encoding="utf-8") as stream:
            stream.write(json.dumps({"path": self.path, "size": len(payload)}) + "\n")
        if self.path == "/upload-error" or self.path.startswith("/error/"):
            self._write_json(413, {"error": "bundle_too_large", "limit": 123})
            return
        if self.path.startswith("/slow-upload/"):
            time.sleep(5)
        if self.path.endswith("/human/client-releases/edge-release-bundles"):
            self._write_json(
                200,
                {
                    "channel": "stable",
                    "version": "9.9.9",
                    "sourceCommit": "fake-source",
                    "previousSourceCommit": "fake-previous",
                    "bundleSize": len(payload),
                    "uploadSeconds": 0.01,
                    "uploadRateLimitMbps": 1000,
                    "installerPath": "/fake/installers/stable/9.9.9",
                    "velopackPath": "/fake/velopack/stable",
                    "components": ["host:9.9.9"],
                    "archivedVersions": [],
                    "deletedInstallerVersions": [],
                    "deletedVelopackFiles": [],
                    "cleanupSucceeded": True,
                    "cleanupWarning": "",
                    "verificationUrls": ["/download"],
                    "changedCommits": ["fake release note"],
                },
            )
            return
        if self.path.endswith("/human/client-releases/plugin-packages"):
            self._write_json(
                200,
                {
                    "moduleId": "Homogenization",
                    "displayName": "Fake Homogenization",
                    "channel": "stable",
                    "version": self.server.plugin_version,
                    "targetRuntime": "win-x64",
                    "downloadUrl": "/download",
                    "sha256": "FAKE",
                    "packageSize": len(payload),
                    "uploadSeconds": 0.01,
                    "verificationUrls": ["/download"],
                },
            )
            return
        if self.path == "/upload":
            self._write_json(200, {"ok": True, "verificationUrls": ["/download"]})
            return
        self._write_json(404, {"error": "not_found", "path": self.path})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port-file", required=True)
    parser.add_argument("--request-log", required=True)
    parser.add_argument("--plugin-version", default="1.0.0")
    args = parser.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    server.request_log = args.request_log
    server.plugin_version = args.plugin_version
    Path(args.port_file).write_text(str(server.server_port), encoding="ascii")

    def stop(_signum, _frame):
        raise KeyboardInterrupt

    signal.signal(signal.SIGTERM, stop)
    try:
        server.serve_forever(poll_interval=0.1)
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
