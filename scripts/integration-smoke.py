#!/usr/bin/env python3
"""Run the real Go daemon against the cross-platform C# core checks.
Uses disposable fixture files and passes temporary credentials only via child env.
"""
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.request

root = Path(__file__).resolve().parents[1]
server = Path(os.environ.get("SAVE_SERVER_BIN", str(root / ".build/save-server")))
dotnet = os.environ.get("DOTNET", "dotnet")
checks = root / "client/CoreChecks/bin/Debug/net10.0/CoreChecks.dll"
with tempfile.TemporaryDirectory(prefix="server-smoke-", dir=root / ".build") as temp:
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    with open(root / ".build/integration-server.log", "w") as log:
        process = subprocess.Popen([str(server), "--data", temp, "serve", "--listen", f"127.0.0.1:{port}", "--allow-http-dev", "--min-free-bytes", "0"], stdout=log, stderr=log)
        try:
            for attempt in range(100):
                if process.poll() is not None:
                    raise RuntimeError("test server exited; see .build/integration-server.log")
                try:
                    with urllib.request.urlopen(base + "/healthz", timeout=1) as response:
                        if response.status == 200:
                            break
                except OSError:
                    time.sleep(0.1)
            else:
                raise RuntimeError("test server did not start")
            def admin(*args):
                result = subprocess.run([str(server), "--data", temp, *args], check=True, capture_output=True, text=True)
                return json.loads(result.stdout)
            admin("user", "add", "--id", "a", "--name", "A")
            admin("library", "add", "--id", "friends", "--name", "Friends")
            admin("world", "add", "--id", "shared", "--name", "Shared", "--library", "friends", "--save-key", "3ad85aea")
            admin("world", "add", "--id", "ksd", "--name", "Game 1.0", "--library", "friends", "--save-key", "world.ksd")
            admin("member", "grant", "--user", "a", "--library", "friends")
            credential = admin("token", "issue", "--user", "a", "--device", "fixture", "--days", "1")
            env = os.environ.copy()
            env["ENSHROUDED_TEST_URL"] = base
            env["ENSHROUDED_TEST_TOKEN"] = credential["token"]
            result = subprocess.run([dotnet, str(checks)], env=env)
            if result.returncode:
                sys.exit(result.returncode)
            admin("token", "revoke", "--id", credential["credential_id"])
            request = urllib.request.Request(base + "/v1/me", headers={"Authorization": "Bearer " + credential["token"]})
            try:
                urllib.request.urlopen(request, timeout=2)
                raise RuntimeError("revoked credential unexpectedly accepted")
            except urllib.error.HTTPError as error:
                if error.code != 401:
                    raise
            print("PASS real-daemon credential revocation")
        finally:
            process.terminate()
            try:
                process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
