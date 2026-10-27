#!/usr/bin/env python3
"""Smoke-test a built gramps-web-mcp: the Docker image or a Claude Desktop bundle.

The release workflows run it on what they are about to publish, so an image or
a bundle that does not start, does not answer MCP, or loses a setting on the
way fails before anyone downloads it:

    python3 scripts/smoke-test.py image IMAGE [--version X.Y.Z]
    python3 scripts/smoke-test.py mcpb BUNDLE.mcpb [--version X.Y.Z]

Both start a stand-in for Gramps Web on 127.0.0.1 that answers the token and
metadata routes and accepts only the user name and password the test sets.
With it, the server must, over each transport tested:

- answer initialize with serverInfo.version equal to --version (default: the
  <Version> in GrampsWeb.Mcp.csproj);
- list tools, among them search, get_object, list_objects and get_reference,
  with the editing tools exactly when GRAMPS_READ_ONLY is not true, and list
  prompts and resources;
- run get_reference with topic metadata, which logs in to the stand-in and
  returns its tree name.

image runs the image twice. First with its own defaults (HTTP on port 8080)
and MCP_API_KEY set: GET /health answers 200 without a key; an MCP request
without a key or with a wrong one gets 401, one with the key but from a browser
origin that is not allowed gets 403, and X-Api-Key works like Bearer; with the
key, the MCP checks above; the HEALTHCHECK command
(dotnet /app/GrampsWeb.Mcp.dll --health-check) exits 0 in the container, and
the image does not run as root. Then with MCP_TRANSPORT=stdio under
docker run -i, the MCP checks. The containers use the host network to reach
the stand-in, so this needs Docker on Linux.

mcpb unpacks the bundle and starts its server as Claude Desktop does: the
command, arguments and environment from manifest.json with ${__dirname}
replaced; the Gramps URL, tree ID, user name and password filled in as a user
would; every other setting at its manifest default, inserted as is (the upload
folder stays ${HOME}/Downloads, which Claude Desktop passes unexpanded); and a
setting with no default left as the literal ${user_config.…} placeholder.
Then the MCP checks over stdio. manifest.json must carry the expected version
and declare every setting its environment refers to.

Needs Python 3.9 or later and nothing else. Exit code: 0 when every check
passes, 1 when one fails, 2 on a usage error.
"""

from __future__ import annotations

import argparse
import http.client
import json
import os
import queue
import re
import secrets
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "GrampsWeb.Mcp" / "GrampsWeb.Mcp.csproj"

PROTOCOL_VERSION = "2025-06-18"
CORE_TOOLS = ("search", "get_object", "list_objects", "get_reference")
EDIT_TOOL_PREFIXES = ("create_", "update_", "delete_", "undo_")

TREE_ID = "smoke-tree"
TREE_NAME = "Smoke Test Tree"
USERNAME = "smoke-user"

# The port and MCP path the Dockerfile sets (ASPNETCORE_URLS, MCP_PATH).
IMAGE_PORT = 8080
IMAGE_MCP_PATH = "/mcp"

# The first answer waits for the process to start, a single-file binary to unpack itself and .NET to JIT.
START_TIMEOUT = 90
REQUEST_TIMEOUT = 30
EXIT_TIMEOUT = 15


class CheckFailed(Exception):
    pass


def check(condition: bool, message: str) -> None:
    if not condition:
        raise CheckFailed(message)


def ok(message: str) -> None:
    print(f"  ok  {message}", flush=True)


def heading(message: str) -> None:
    print(f"\n== {message}", flush=True)


def tail(text: str, lines: int = 60) -> str:
    return "\n".join(text.splitlines()[-lines:])


def project_version() -> str:
    match = re.search(r"<Version>([^<]+)</Version>", PROJECT.read_text(encoding="utf-8"))
    if not match:
        raise CheckFailed(f"no <Version> in {PROJECT}")
    return match.group(1).strip()


class FakeGramps:
    """Stands in for Gramps Web: the token routes and /api/metadata/, nothing else."""

    def __init__(self, username: str, password: str) -> None:
        self.username = username
        self.password = password
        self.access_token = secrets.token_urlsafe(24)
        self.refresh_token = secrets.token_urlsafe(24)
        self.log: list[str] = []
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), self._handler())
        self.url = f"http://127.0.0.1:{self.server.server_address[1]}"
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)

    def __enter__(self) -> FakeGramps:
        self.thread.start()
        return self

    def __exit__(self, *exc: object) -> None:
        self.server.shutdown()
        self.server.server_close()

    def respond(self, method: str, path: str, authorization: str, body: bytes) -> tuple[int, object]:
        bearer = authorization[7:] if authorization.lower().startswith("bearer ") else None
        if method == "POST" and path == "/api/token/":
            try:
                login = json.loads(body or b"{}")
            except ValueError:
                login = {}
            if login.get("username") == self.username and login.get("password") == self.password:
                return 200, {"access_token": self.access_token, "refresh_token": self.refresh_token}
            return 403, {"error": {"code": 403, "message": "Wrong username or password"}}
        if method == "POST" and path == "/api/token/refresh/":
            if bearer == self.refresh_token:
                return 200, {"access_token": self.access_token}
            return 401, {"msg": "Signature verification failed"}
        if method == "GET" and path == "/api/metadata/":
            if bearer != self.access_token:
                return 401, {"msg": "Signature verification failed"}
            return 200, {
                "database": {"id": TREE_ID, "name": TREE_NAME, "type": "sqlite"},
                "gramps": {"version": "6.0.0"},
                "gramps_webapi": {"version": "3.0.0"},
                "object_counts": {"people": 0, "families": 0, "events": 0},
            }
        return 404, {"error": {"code": 404, "message": "Not found"}}

    def _handler(self) -> type[BaseHTTPRequestHandler]:
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def _serve(self) -> None:
                length = int(self.headers.get("Content-Length") or 0)
                body = self.rfile.read(length) if length else b""
                path = urlsplit(self.path).path
                status, payload = fake.respond(
                    self.command, path, self.headers.get("Authorization", ""), body)
                fake.log.append(f"{self.command} {path} -> {status}")
                data = json.dumps(payload).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

            do_GET = do_POST = _serve

            def log_message(self, format: str, *args: object) -> None:
                pass

        return Handler


def answer(message: dict, what: str) -> dict:
    """The result of a JSON-RPC response, or a failure naming the request."""
    if "error" in message:
        raise CheckFailed(f"{what} returned an error: {json.dumps(message['error'])}")
    result = message.get("result")
    check(isinstance(result, dict), f"{what} returned no result: {json.dumps(message)}")
    return result


class StdioSession:
    """An MCP client on a server process's stdin and stdout, one JSON message per line."""

    def __init__(self, command: list[str], env: dict[str, str]) -> None:
        self.stderr = tempfile.TemporaryFile()
        self.process = subprocess.Popen(
            command, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=self.stderr)
        self.lines: queue.Queue[bytes | None] = queue.Queue()
        self.next_id = 0
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self) -> None:
        for line in self.process.stdout:
            self.lines.put(line)
        self.lines.put(None)

    def _send(self, message: dict) -> None:
        try:
            self.process.stdin.write(json.dumps(message).encode() + b"\n")
            self.process.stdin.flush()
        except OSError as ex:
            raise CheckFailed(f"cannot write to the server: {ex}") from ex

    def request(self, method: str, params: dict | None = None, timeout: float = REQUEST_TIMEOUT) -> dict:
        self.next_id += 1
        self._send({"jsonrpc": "2.0", "id": self.next_id, "method": method, "params": params or {}})
        deadline = time.monotonic() + timeout
        while True:
            try:
                line = self.lines.get(timeout=max(0.0, deadline - time.monotonic()))
            except queue.Empty:
                raise CheckFailed(f"{method}: no answer within {timeout:.0f} s") from None
            if line is None:
                raise CheckFailed(f"{method}: the server closed stdout (exit code {self.process.poll()})")
            try:
                message = json.loads(line)
            except ValueError:
                raise CheckFailed(f"{method}: stdout carries a line that is not JSON: {line[:200]!r}") from None
            if "method" in message and "id" in message:
                # A request from the server, such as ping; this client offers no capabilities.
                self._send({"jsonrpc": "2.0", "id": message["id"], "result": {}} if message["method"] == "ping"
                           else {"jsonrpc": "2.0", "id": message["id"],
                                 "error": {"code": -32601, "message": "Method not found"}})
            elif message.get("id") == self.next_id:
                return answer(message, method)

    def notify(self, method: str) -> None:
        self._send({"jsonrpc": "2.0", "method": method})

    def close(self) -> int | None:
        """Closes stdin as a client does when it disconnects; returns the exit code, or None if it had to be killed."""
        try:
            self.process.stdin.close()
        except OSError:
            pass
        try:
            return self.process.wait(timeout=EXIT_TIMEOUT)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()
            return None

    def server_log(self) -> str:
        self.stderr.seek(0)
        return self.stderr.read().decode(errors="replace")


def http_request(
    url: str,
    method: str = "GET",
    body: dict | None = None,
    headers: dict[str, str] | None = None,
    timeout: float = REQUEST_TIMEOUT,
) -> tuple[int, http.client.HTTPMessage, bytes]:
    parts = urlsplit(url)
    connection = http.client.HTTPConnection(parts.hostname, parts.port, timeout=timeout)
    try:
        data = json.dumps(body).encode() if body is not None else None
        all_headers = {"Content-Type": "application/json"} if data is not None else {}
        all_headers.update(headers or {})
        connection.request(method, parts.path or "/", body=data, headers=all_headers)
        response = connection.getresponse()
        return response.status, response.headers, response.read()
    finally:
        connection.close()


def initialize_params() -> dict:
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "capabilities": {},
        "clientInfo": {"name": "gramps-web-mcp-smoke-test", "version": "1.0.0"},
    }


class HttpSession:
    """An MCP client for the Streamable HTTP transport; answers come as JSON or as server-sent events."""

    def __init__(self, url: str, headers: dict[str, str]) -> None:
        self.url = url
        self.headers = {"Accept": "application/json, text/event-stream", **headers}
        self.next_id = 0

    def _post(self, message: dict) -> tuple[int, http.client.HTTPMessage, bytes]:
        try:
            status, headers, body = http_request(self.url, "POST", message, self.headers)
        except OSError as ex:
            raise CheckFailed(f"POST {self.url}: {ex}") from ex
        session_id = headers.get("Mcp-Session-Id")
        if session_id:
            self.headers["Mcp-Session-Id"] = session_id
        return status, headers, body

    def request(self, method: str, params: dict | None = None, timeout: float = REQUEST_TIMEOUT) -> dict:
        self.next_id += 1
        status, headers, body = self._post(
            {"jsonrpc": "2.0", "id": self.next_id, "method": method, "params": params or {}})
        check(status == 200, f"{method}: HTTP {status} {body[:300]!r}")
        content_type = headers.get("Content-Type", "")
        if content_type.startswith("text/event-stream"):
            messages = [json.loads(data) for data in event_data(body.decode())]
        else:
            messages = [json.loads(body)]
        for message in messages:
            if message.get("id") == self.next_id:
                result = answer(message, method)
                if method == "initialize" and "protocolVersion" in result:
                    self.headers["MCP-Protocol-Version"] = result["protocolVersion"]
                return result
        raise CheckFailed(f"{method}: the answer holds no response to request {self.next_id}: {body[:300]!r}")

    def notify(self, method: str) -> None:
        status, _, body = self._post({"jsonrpc": "2.0", "method": method})
        check(200 <= status < 300, f"{method}: HTTP {status} {body[:300]!r}")


def event_data(stream: str) -> list[str]:
    """The data of each server-sent event; an event's data lines join with newlines."""
    events = []
    for block in re.split(r"\r?\n\r?\n", stream):
        data = [line[5:].removeprefix(" ") for line in block.splitlines() if line.startswith("data:")]
        if data:
            events.append("\n".join(data))
    return events


def list_all(session: StdioSession | HttpSession, method: str, key: str) -> list[dict]:
    items: list[dict] = []
    cursor = None
    while True:
        result = session.request(method, {"cursor": cursor} if cursor else {})
        items.extend(result.get(key) or [])
        cursor = result.get("nextCursor")
        if not cursor:
            return items


def check_mcp(session: StdioSession | HttpSession, label: str, version: str, read_only: bool) -> None:
    result = session.request("initialize", initialize_params(), timeout=START_TIMEOUT)
    server_version = str((result.get("serverInfo") or {}).get("version", ""))
    # .NET reports the assembly version, which adds a fourth part: 2.6.0 becomes 2.6.0.0.
    check(re.fullmatch(re.escape(version) + r"(\.0)?(\+.*)?", server_version) is not None,
          f"{label}: serverInfo.version is {server_version!r}, expected {version}")
    session.notify("notifications/initialized")
    ok(f"{label}: initialize (protocol {result.get('protocolVersion')}, server version {server_version})")

    tools = {tool["name"] for tool in list_all(session, "tools/list", "tools")}
    missing = [name for name in CORE_TOOLS if name not in tools]
    check(not missing, f"{label}: tools/list lacks {', '.join(missing)}; it lists {', '.join(sorted(tools))}")
    edit_tools = sorted(name for name in tools if name.startswith(EDIT_TOOL_PREFIXES))
    if read_only:
        check(not edit_tools, f"{label}: read-only, yet tools/list offers {', '.join(edit_tools)}")
    else:
        check("update_person" in tools, f"{label}: not read-only, yet tools/list lacks update_person")
    ok(f"{label}: tools/list ({len(tools)} tools, {len(edit_tools)} that edit"
       f"{', read-only' if read_only else ''})")

    prompts = list_all(session, "prompts/list", "prompts")
    resources = list_all(session, "resources/list", "resources")
    check(bool(prompts), f"{label}: prompts/list is empty")
    check(bool(resources), f"{label}: resources/list is empty")
    ok(f"{label}: prompts/list ({len(prompts)}), resources/list ({len(resources)})")

    result = session.request("tools/call", {"name": "get_reference", "arguments": {"topic": "metadata"}})
    text = "\n".join(part.get("text", "") for part in result.get("content") or [])
    check(not result.get("isError"), f"{label}: get_reference failed: {text[:500]}")
    check(TREE_NAME in text, f"{label}: get_reference metadata does not name the tree: {text[:500]}")
    ok(f"{label}: get_reference metadata read the tree from the stand-in Gramps Web")


def gramps_env(gramps: FakeGramps) -> dict[str, str]:
    return {
        "GRAMPS_API_URL": gramps.url,
        "GRAMPS_USERNAME": gramps.username,
        "GRAMPS_PASSWORD": gramps.password,
        "GRAMPS_TREE_ID": TREE_ID,
    }


def clean_environment() -> dict[str, str]:
    """This process's environment without server settings that would leak into the server under test."""
    return {key: value for key, value in os.environ.items()
            if not key.startswith(("GRAMPS_", "MCP_", "ASPNETCORE_", "DOTNET_URLS", "DOTNET_HTTP_PORTS"))}


def run_stdio(command: list[str], env: dict[str, str], label: str, version: str, read_only: bool) -> None:
    session = StdioSession(command, env)
    try:
        check_mcp(session, label, version, read_only)
    except CheckFailed:
        session.close()
        print(f"--- server log ({label}) ---\n{tail(session.server_log())}", flush=True)
        raise
    exit_code = session.close()
    check(exit_code is not None, f"{label}: the server kept running {EXIT_TIMEOUT} s after stdin closed")
    ok(f"{label}: exits when the client closes stdin (exit code {exit_code})")


def docker(*args: str, env: dict[str, str] | None = None, timeout: float = 120) -> subprocess.CompletedProcess:
    return subprocess.run(["docker", *args], env=env, capture_output=True, text=True, timeout=timeout)


def smoke_image(image: str, version: str) -> None:
    check(shutil.which("docker") is not None, "docker is not on PATH")
    inspected = docker("image", "inspect", "--format", "{{.Config.User}}", image)
    check(inspected.returncode == 0, f"cannot inspect {image}: {inspected.stderr.strip()}")
    user = inspected.stdout.strip()
    check(user not in ("", "root", "0") and not user.startswith(("root:", "0:")),
          f"{image} runs as root (USER {user!r})")
    ok(f"{image} runs as user {user}")

    api_key = secrets.token_urlsafe(24)
    with FakeGramps(USERNAME, secrets.token_urlsafe(16)) as gramps:
        # The values travel in the docker client's environment, not on its command line (-e NAME without =).
        env = {**clean_environment(), **gramps_env(gramps), "MCP_API_KEY": api_key}
        passed = [arg for name in gramps_env(gramps) for arg in ("-e", name)]
        try:
            smoke_image_http(image, version, api_key, env, passed, gramps)
            heading(f"{image} over stdio")
            run_stdio(["docker", "run", "--rm", "-i", "--network", "host", *passed,
                       "-e", "MCP_TRANSPORT=stdio", image],
                      env, "stdio", version, read_only=False)
        except CheckFailed:
            print(f"--- stand-in Gramps Web requests ---\n" + "\n".join(gramps.log), flush=True)
            raise


def smoke_image_http(
    image: str, version: str, api_key: str, env: dict[str, str], passed: list[str], gramps: FakeGramps
) -> None:
    heading(f"{image} over HTTP on port {IMAGE_PORT}")
    name = f"gramps-web-mcp-smoke-{os.getpid()}"
    started = docker("run", "-d", "--name", name, "--network", "host", *passed, "-e", "MCP_API_KEY",
                     image, env=env)
    check(started.returncode == 0, f"docker run failed: {started.stderr.strip()}")
    try:
        base = f"http://127.0.0.1:{IMAGE_PORT}"
        wait_for_health(base + "/health", name)
        ok("GET /health answers 200 without a key")
        check_http_auth(base + IMAGE_MCP_PATH, api_key)

        session = HttpSession(base + IMAGE_MCP_PATH, {"Authorization": f"Bearer {api_key}"})
        check_mcp(session, "http", version, read_only=False)

        probe = docker("exec", name, "dotnet", "/app/GrampsWeb.Mcp.dll", "--health-check")
        check(probe.returncode == 0 and probe.stdout.startswith("healthy:"),
              f"HEALTHCHECK command exited {probe.returncode}: {probe.stdout.strip()} {probe.stderr.strip()}")
        ok(f"HEALTHCHECK command: {probe.stdout.strip()}")
    except CheckFailed:
        logs = docker("logs", name)
        print(f"--- container log ---\n{tail(logs.stdout + logs.stderr)}", flush=True)
        raise
    finally:
        docker("rm", "-f", name)


def wait_for_health(url: str, container: str) -> None:
    deadline = time.monotonic() + START_TIMEOUT
    last = "no answer"
    while time.monotonic() < deadline:
        try:
            status, _, body = http_request(url, timeout=5)
            if status == 200:
                check(json.loads(body).get("status") == "healthy", f"GET /health answered {body[:200]!r}")
                return
            last = f"HTTP {status} {body[:200]!r}"
        except OSError as ex:
            last = str(ex)
        state = docker("inspect", "--format", "{{.State.Status}}", container)
        check(state.stdout.strip() == "running", f"the container stopped ({state.stdout.strip()})")
        time.sleep(1)
    raise CheckFailed(f"GET /health did not answer 200 within {START_TIMEOUT} s: {last}")


def check_http_auth(url: str, api_key: str) -> None:
    def initialize(headers: dict[str, str]) -> tuple[int, http.client.HTTPMessage]:
        status, response_headers, _ = http_request(
            url, "POST", {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": initialize_params()},
            {"Accept": "application/json, text/event-stream", **headers})
        return status, response_headers

    status, headers = initialize({})
    check(status == 401, f"an MCP request without a key got HTTP {status}, expected 401")
    check(headers.get("WWW-Authenticate", "").startswith("Bearer"),
          f"the 401 carries no Bearer challenge: {headers.get('WWW-Authenticate')!r}")
    status, _ = initialize({"Authorization": "Bearer " + secrets.token_urlsafe(24)})
    check(status == 401, f"an MCP request with a wrong key got HTTP {status}, expected 401")
    ok("an MCP request without a key or with a wrong one gets 401")

    status, _ = initialize({"Authorization": f"Bearer {api_key}", "Origin": "https://smoke-test.invalid"})
    check(status == 403, f"an MCP request from an origin not allowed got HTTP {status}, expected 403")
    ok("an MCP request from a browser origin that is not allowed gets 403")

    status, _ = initialize({"X-Api-Key": api_key})
    check(status == 200, f"an MCP request with X-Api-Key got HTTP {status}, expected 200")
    ok("X-Api-Key is accepted like Bearer")


PLACEHOLDER = re.compile(r"\$\{([^}]+)\}")


def format_default(value: object) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, float) and value.is_integer():
        return str(int(value))
    return str(value)


def substitute(value: str, dirname: str, settings: dict[str, str], user_config: dict[str, dict]) -> str:
    """Fills a manifest string as Claude Desktop does; the values it inserts are not expanded again."""
    home = str(Path.home())
    system = {
        "__dirname": dirname,
        "HOME": home,
        "DESKTOP": str(Path(home, "Desktop")),
        "DOCUMENTS": str(Path(home, "Documents")),
        "DOWNLOADS": str(Path(home, "Downloads")),
        "pathSeparator": os.sep,
        "/": os.sep,
    }

    def replace(match: re.Match) -> str:
        name = match.group(1)
        if name in system:
            return system[name]
        if name.startswith("user_config."):
            key = name[len("user_config."):]
            if key in settings:
                return settings[key]
            if "default" in user_config.get(key, {}):
                return format_default(user_config[key]["default"])
        # A setting the user left blank, with no default, arrives as the literal placeholder.
        return match.group(0)

    return PLACEHOLDER.sub(replace, value)


def extract(bundle: Path, target: Path) -> None:
    """Unpacks the bundle and restores the Unix file modes it stores, as unzip does."""
    with zipfile.ZipFile(bundle) as archive:
        for info in archive.infolist():
            path = archive.extract(info, target)
            mode = (info.external_attr >> 16) & 0o777
            if mode and os.name != "nt":
                os.chmod(path, mode)


def smoke_mcpb(bundle: Path, version: str) -> None:
    check(bundle.is_file(), f"{bundle} does not exist")
    with tempfile.TemporaryDirectory(prefix="mcpb-smoke-") as tmp, \
            FakeGramps(USERNAME, secrets.token_urlsafe(16)) as gramps:
        heading(f"{bundle.name} over stdio, as Claude Desktop starts it")
        target = Path(tmp)
        extract(bundle, target)
        manifest = json.loads((target / "manifest.json").read_text(encoding="utf-8"))
        check(manifest.get("version") == version,
              f"manifest.json has version {manifest.get('version')!r}, expected {version}")

        user_config: dict[str, dict] = manifest.get("user_config") or {}
        mcp_config = (manifest.get("server") or {}).get("mcp_config") or {}
        settings = {
            "gramps_api_url": gramps.url,
            "gramps_tree_id": TREE_ID,
            "gramps_username": gramps.username,
            "gramps_password": gramps.password,
        }
        referenced = set(re.findall(r"\$\{user_config\.([^}]+)\}", json.dumps(mcp_config)))
        undeclared = sorted(referenced - set(user_config))
        check(not undeclared, f"manifest.json uses settings it does not declare: {', '.join(undeclared)}")
        unfilled = sorted(key for key, spec in user_config.items()
                          if spec.get("required") and "default" not in spec and key not in settings)
        check(not unfilled, f"required settings this test has no value for: {', '.join(unfilled)}")
        ok(f"manifest.json: version {version}, {len(referenced)} settings, all declared")

        dirname = str(target)
        command = [substitute(part, dirname, settings, user_config)
                   for part in [mcp_config.get("command", ""), *(mcp_config.get("args") or [])]]
        server_env = {key: substitute(str(value), dirname, settings, user_config)
                      for key, value in (mcp_config.get("env") or {}).items()}
        binary = Path(command[0])
        if os.name == "nt" and not binary.suffix:
            binary = binary.with_suffix(".exe")
        check(binary.is_file(), f"the server command {command[0]} is not in the bundle")
        if os.name != "nt" and not os.access(binary, os.X_OK):
            print(f"::warning::{bundle.name} stores the server binary without the executable bit", flush=True)
            binary.chmod(binary.stat().st_mode | 0o111)

        for key in sorted(server_env):
            shown = "(set)" if key == "GRAMPS_PASSWORD" else server_env[key]
            print(f"      {key}={shown}", flush=True)
        read_only = server_env.get("GRAMPS_READ_ONLY", "").strip().lower() in ("true", "1")
        try:
            run_stdio(command, {**clean_environment(), **server_env}, "stdio", version, read_only)
        except CheckFailed:
            print(f"--- stand-in Gramps Web requests ---\n" + "\n".join(gramps.log), flush=True)
            raise


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("kind", choices=("image", "mcpb"), help="what to test")
    parser.add_argument("target", help="the image reference, or the path of the .mcpb file")
    parser.add_argument("--version", help="the version the server must report (default: the csproj <Version>)")
    args = parser.parse_args()

    try:
        version = args.version or project_version()
        if args.kind == "image":
            smoke_image(args.target, version)
        else:
            smoke_mcpb(Path(args.target), version)
    except CheckFailed as ex:
        print(f"\nFAILED: {ex}", flush=True)
        if os.environ.get("GITHUB_ACTIONS") == "true":
            print(f"::error title=Smoke test::{ex}", flush=True)
        return 1
    except (OSError, subprocess.SubprocessError, ValueError) as ex:
        print(f"\nFAILED: {type(ex).__name__}: {ex}", flush=True)
        if os.environ.get("GITHUB_ACTIONS") == "true":
            print(f"::error title=Smoke test::{type(ex).__name__}: {ex}", flush=True)
        return 1

    print("\nAll smoke checks passed.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
