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
