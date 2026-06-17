"""Tests for the agent_launcher module."""

import json
import os

import pytest

from celbridge import agent_launcher


def _read_config(project_folder: str) -> dict:
    """Helper to load the .mcp.json file from a project folder as a dict."""
    config_path = os.path.join(project_folder, ".mcp.json")
    with open(config_path, "r", encoding="utf-8") as config_file:
        return json.loads(config_file.read())


def test_write_mcp_config_url_uses_port_placeholder(tmp_path):
    """The URL must embed the ${CELBRIDGE_MCP_PORT} placeholder so the host
    expands it on each MCP request."""
    agent_launcher._write_mcp_config(str(tmp_path))

    config = _read_config(str(tmp_path))
    url = config["mcpServers"]["celbridge"]["url"]
    assert "${CELBRIDGE_MCP_PORT}" in url


def test_write_mcp_config_is_stable_across_calls(tmp_path):
    """Two consecutive launches must produce byte-identical .mcp.json content
    so the existing 'only write if content changed' optimisation skips the
    second write."""
    config_path = agent_launcher._write_mcp_config(str(tmp_path))

    with open(config_path, "r", encoding="utf-8") as config_file:
        first_content = config_file.read()

    first_mtime = os.path.getmtime(config_path)

    agent_launcher._write_mcp_config(str(tmp_path))

    with open(config_path, "r", encoding="utf-8") as config_file:
        second_content = config_file.read()
    second_mtime = os.path.getmtime(config_path)

    assert first_content == second_content
    assert first_mtime == second_mtime


def test_write_mcp_config_preserves_other_servers(tmp_path):
    """The Celbridge entry must not clobber unrelated entries the user has
    configured for other MCP servers."""
    config_path = os.path.join(str(tmp_path), ".mcp.json")
    with open(config_path, "w", encoding="utf-8") as config_file:
        config_file.write(json.dumps({
            "mcpServers": {
                "other-tool": {
                    "type": "stdio",
                    "command": "other-tool"
                }
            }
        }, indent=2) + "\n")

    agent_launcher._write_mcp_config(str(tmp_path))

    config = _read_config(str(tmp_path))
    assert config["mcpServers"]["other-tool"]["command"] == "other-tool"
    assert "celbridge" in config["mcpServers"]


def test_launch_claude_skips_when_claude_cli_missing(monkeypatch, tmp_path, capsys):
    """No subprocess attempt when the CLI is absent."""
    monkeypatch.setenv("CELBRIDGE_PROJECT_FOLDER", str(tmp_path))
    monkeypatch.setattr(agent_launcher.shutil, "which", lambda _name: None)

    run_called = []
    monkeypatch.setattr(
        agent_launcher.subprocess,
        "run",
        lambda *_args, **_kwargs: run_called.append(True))

    agent_launcher.launch_claude()

    assert run_called == []
    captured = capsys.readouterr()
    assert "not found" in captured.err


def _capture_launch_command(monkeypatch, tmp_path):
    """Run launch_claude with the CLI present and return the command list
    passed to subprocess.run."""
    monkeypatch.setenv("CELBRIDGE_PROJECT_FOLDER", str(tmp_path))
    monkeypatch.setattr(agent_launcher.shutil, "which", lambda _name: "claude")

    captured_command = []
    monkeypatch.setattr(
        agent_launcher.subprocess,
        "run",
        lambda command, **_kwargs: captured_command.append(command))

    agent_launcher.launch_claude()

    assert len(captured_command) == 1
    return captured_command[0]


def test_launch_claude_excludes_web_access_by_default(monkeypatch, tmp_path):
    """With the web-access-tools flag off, no built-in tools are enabled and
    only the Celbridge MCP tools are allowed."""
    monkeypatch.delenv("CELBRIDGE_WEB_ACCESS_TOOLS", raising=False)

    command = _capture_launch_command(monkeypatch, tmp_path)

    tools_value = command[command.index("--tools") + 1]
    allowed_value = command[command.index("--allowedTools") + 1]
    assert tools_value == ""
    assert allowed_value == "mcp__celbridge__*"


def test_launch_claude_includes_web_access_when_flag_enabled(monkeypatch, tmp_path):
    """With CELBRIDGE_WEB_ACCESS_TOOLS=1, WebFetch and WebSearch are added to both
    the available built-in tools and the allowlist; nothing else is enabled."""
    monkeypatch.setenv("CELBRIDGE_WEB_ACCESS_TOOLS", "1")

    command = _capture_launch_command(monkeypatch, tmp_path)

    tools_value = command[command.index("--tools") + 1]
    allowed_value = command[command.index("--allowedTools") + 1]
    assert tools_value == "WebFetch,WebSearch"
    assert allowed_value == "mcp__celbridge__*,WebFetch,WebSearch"
