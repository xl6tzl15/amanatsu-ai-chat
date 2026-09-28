#!/usr/bin/env python3
"""Restart only this installation's loopback AI-chat bridge on explicit request."""

from __future__ import annotations

import argparse
import ast
import json
import socket
import subprocess
import sys
import time
import urllib.request
from datetime import datetime
from pathlib import Path

import psutil


def option_value(argv: list[str], name: str) -> str | None:
    try:
        return argv[argv.index(name) + 1]
    except (ValueError, IndexError):
        return None


def matching_bridges(script: Path, config: Path, port: int) -> list[psutil.Process]:
    matches = []
    for process in psutil.process_iter(["cmdline"]):
        try:
            argv = process.info["cmdline"] or []
            cwd = Path(process.cwd())
            script_args = [part for part in argv[1:] if Path(part).name.lower() == "bridge.py"]
            if len(script_args) != 1:
                continue
            running_script = (cwd / script_args[0]).resolve()
            config_arg = option_value(argv, "--config")
            running_config = (cwd / config_arg).resolve() if config_arg else None
            running_port = option_value(argv, "--port")
            if running_script != script or running_config != config or running_port != str(port):
                continue
            connections = (process.net_connections(kind="inet") if hasattr(process, "net_connections")
                           else process.connections(kind="inet"))
            if not any(connection.status == psutil.CONN_LISTEN
                       and connection.laddr.port == port
                       and connection.laddr.ip in ("127.0.0.1", "::1")
                       for connection in connections):
                continue
            matches.append(process)
        except (psutil.AccessDenied, psutil.NoSuchProcess, OSError, ValueError):
            continue
    return matches


def health(port: int) -> dict:
    with urllib.request.urlopen(f"http://127.0.0.1:{port}/health", timeout=2) as response:
        return json.load(response)


def restart(root: Path, config: Path, port: int) -> dict:
    root = root.resolve()
    # The helper ships next to bridge.py; the Mod folder may be nested (e.g. plugins/SELF).
    candidates = [Path(__file__).resolve().parent / "bridge.py",
                  root / "BepInEx/plugins/AmanatsuAiChat/bridge.py",
                  root / "ModSource/AiChat/bridge.py",
                  *sorted((root / "BepInEx/plugins").glob("**/AmanatsuAiChat/bridge.py"))]
    candidates = list(dict.fromkeys(c.resolve() for c in candidates))
    script = next((c for c in candidates if c.is_file()), candidates[0])
    legacy_scripts = [c for c in candidates if c != script]
    config = config.resolve()
    if not script.is_file() or not config.is_file():
        raise RuntimeError("bridge script or config is missing")
    ast.parse(script.read_text(encoding="utf-8"), filename=str(script))
    settings = json.loads(config.read_text(encoding="utf-8-sig"))
    if not isinstance(settings, dict) or not settings.get("url") or not settings.get("model"):
        raise RuntimeError("bridge config needs a URL and model")
    matches = matching_bridges(script, config, port)
    for legacy_script in legacy_scripts:
        matches += matching_bridges(legacy_script, config, port)
    if len(matches) > 1:
        raise RuntimeError("multiple matching bridges found; no process was stopped")
    if matches:
        process = matches[0]
        executable = process.exe()
        process.terminate()
        try:
            process.wait(timeout=8)
        except psutil.TimeoutExpired as exc:
            raise RuntimeError("bridge did not stop; no new process was started") from exc
    else:
        with socket.socket() as connection:
            connection.settimeout(1)
            if connection.connect_ex(("127.0.0.1", port)) == 0:
                raise RuntimeError("port is occupied by an unmanaged process")
        executable = sys.executable

    logs = root / "BepInEx/config/amanatsu.ai-chat/bridge-logs"
    logs.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    with (logs / f"bridge-{stamp}.log").open("w", encoding="utf-8") as output, \
         (logs / f"bridge-{stamp}.err").open("w", encoding="utf-8") as errors:
        child = subprocess.Popen(
            [executable, "-u", str(script), "--config", str(config), "--port", str(port)],
            cwd=root, stdin=subprocess.DEVNULL, stdout=output, stderr=errors,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0), close_fds=True,
        )
    for _ in range(30):
        if child.poll() is not None:
            raise RuntimeError("new bridge exited during startup; check bridge-logs")
        try:
            status = health(port)
            if "thinking_enabled" not in status:
                raise RuntimeError("new bridge did not load the updated code")
            return {"ok": True, "pid": child.pid, "health": status}
        except (OSError, ValueError):
            time.sleep(0.25)
    raise RuntimeError("new bridge did not pass the health check; check bridge-logs")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        parser.error("port must be 1024..65535")
    try:
        print(json.dumps(restart(args.root, args.config, args.port), ensure_ascii=False), flush=True)
    except Exception as exc:
        print(json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False), flush=True)
        sys.exit(1)


if __name__ == "__main__":
    main()
