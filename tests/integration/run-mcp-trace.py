#!/usr/bin/env python3
"""
Drive the dotnet-lsp-mcp server via JSON-RPC over stdio and capture call-graph
output for ExampleApp. Writes results as pretty-printed JSON to mcp-output.json.
"""
import json
import os
import subprocess
import sys
import threading
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SERVER_DIR = ROOT / "server"
EXAMPLE_DIR = ROOT / "tests" / "integration" / "ExampleApp"
SOLUTION = EXAMPLE_DIR / "MyApp.sln"
HOME_CTRL = EXAMPLE_DIR / "MyApp" / "Controllers" / "HomeController.cs"
OUTPUT_FILE = ROOT / "tests" / "integration" / "mcp-output.json"


def main():
    cmd = ["dotnet", "run", "--no-build", "--project", str(SERVER_DIR)]
    print(f"Spawning: {' '.join(cmd)}", file=sys.stderr)
    proc = subprocess.Popen(
        cmd,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
    )

    # Drain stderr in background so it doesn't block
    stderr_lines = []

    def drain_stderr():
        for line in proc.stderr:
            stderr_lines.append(line.rstrip())

    threading.Thread(target=drain_stderr, daemon=True).start()

    next_id = [0]

    def send(method, params=None, is_notification=False):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        if not is_notification:
            next_id[0] += 1
            msg["id"] = next_id[0]
        line = json.dumps(msg) + "\n"
        proc.stdin.write(line)
        proc.stdin.flush()
        return msg.get("id")

    def read_response(expected_id, timeout=180):
        deadline = time.time() + timeout
        while time.time() < deadline:
            line = proc.stdout.readline()
            if not line:
                raise RuntimeError(f"Server closed stdout while waiting for id={expected_id}")
            line = line.strip()
            if not line:
                continue
            try:
                msg = json.loads(line)
            except json.JSONDecodeError:
                print(f"[non-JSON stdout line]: {line}", file=sys.stderr)
                continue
            if msg.get("id") == expected_id:
                return msg
            # Otherwise it's a notification or unrelated — keep reading
            print(f"[unexpected msg id={msg.get('id')}]: {line[:120]}", file=sys.stderr)
        raise TimeoutError(f"Timed out waiting for id={expected_id}")

    def call_tool(name, args=None, timeout=180):
        rid = send("tools/call", {"name": name, "arguments": args or {}})
        resp = read_response(rid, timeout=timeout)
        if "error" in resp:
            return {"_error": resp["error"]}
        # Tool result has content[].text — each text is a JSON string from the tool
        content = resp.get("result", {}).get("content", [])
        if content and content[0].get("type") == "text":
            try:
                return json.loads(content[0]["text"])
            except json.JSONDecodeError:
                return {"_raw": content[0]["text"]}
        return resp.get("result", {})

    results = {}

    try:
        # 1. initialize
        rid = send(
            "initialize",
            {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "trace-driver", "version": "1.0"},
            },
        )
        init = read_response(rid)
        print(f"[init] server: {init.get('result', {}).get('serverInfo')}", file=sys.stderr)

        send("notifications/initialized", {}, is_notification=True)

        # Discover actual tool names (framework may transform PascalCase)
        rid = send("tools/list", {})
        tools_resp = read_response(rid)
        tool_names = [t["name"] for t in tools_resp.get("result", {}).get("tools", [])]
        print(f"[tools/list] {tool_names}", file=sys.stderr)
        results["_tools_list"] = tool_names

        def find(prefix):
            for t in tool_names:
                if t.lower().replace("_", "").replace("-", "") == prefix.lower().replace("_", "").replace("-", ""):
                    return t
            raise KeyError(f"No tool matches {prefix}; available: {tool_names}")

        T_LOAD = find("LoadSolution")
        T_MEDIATR = find("GetMediatRMappings")
        T_IMPL = find("FindImplementations")
        T_CALLEES = find("FindCalleesFromLocation")

        # 2. LoadSolution (slow — Roslyn opens .sln + MSBuild)
        print("[LoadSolution]…", file=sys.stderr)
        results["LoadSolution"] = call_tool(
            T_LOAD, {"solutionPath": str(SOLUTION)}, timeout=300
        )

        # 3. MediatR map
        print("[GetMediatRMappings]…", file=sys.stderr)
        results["GetMediatRMappings"] = call_tool(T_MEDIATR)

        # 4. Implementations of the three abstracted services
        for iface in ("IUserService", "INotificationService", "IPaymentProcessor"):
            print(f"[FindImplementations {iface}]…", file=sys.stderr)
            results[f"FindImplementations:{iface}"] = call_tool(
                T_IMPL, {"interfaceName": iface}
            )

        # 5. Callees from the two endpoints. Pin column at the access modifier (5)
        # — GetMethodSymbolAtPositionAsync walks up to the enclosing method.
        endpoints = [
            ("Index", 24),
            ("ProcessUserAction", 57),
            ("GetUserDetails", 145),
            ("ManageUser", 173),
        ]
        for name, line in endpoints:
            print(f"[FindCalleesFromLocation {name} L{line}]…", file=sys.stderr)
            results[f"FindCalleesFromLocation:{name}"] = call_tool(
                T_CALLEES,
                {
                    "filePath": str(HOME_CTRL),
                    "line": line,
                    "column": 5,
                    "maxDepth": 8,
                    "limit": 500,
                },
                timeout=240,
            )

    finally:
        try:
            proc.stdin.close()
        except Exception:
            pass
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()

    OUTPUT_FILE.write_text(json.dumps(results, indent=2))
    print(f"\nWrote {OUTPUT_FILE}", file=sys.stderr)
    # Also dump a tiny stderr tail so we can spot Roslyn errors
    if stderr_lines:
        print("\n--- last 20 server stderr lines ---", file=sys.stderr)
        for line in stderr_lines[-20:]:
            print(line, file=sys.stderr)


if __name__ == "__main__":
    main()
