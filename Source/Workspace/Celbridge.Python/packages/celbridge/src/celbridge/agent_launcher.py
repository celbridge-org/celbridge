"""Agent launcher for starting coding agents with sandboxed Celbridge MCP access.

Provides methods for launching coding agents (e.g. Claude Code CLI) from the
Celbridge Python REPL. Each agent gets a sandboxed environment with access only
to Celbridge MCP tools, web fetch and web search.
"""

import json
import os
import shutil
import subprocess
import sys


def _get_project_folder() -> str:
    """Get the project folder path from the environment."""
    project_folder = os.environ.get("CELBRIDGE_PROJECT_FOLDER")
    if not project_folder:
        raise RuntimeError(
            "CELBRIDGE_PROJECT_FOLDER is not set. "
            "Agent launching is only available from the Celbridge console.")
    return project_folder


def _write_mcp_config(project_folder: str) -> str:
    """Write or update the .mcp.json file with the Celbridge MCP server entry.

    Preserves any other MCP server entries the user has configured.
    Only writes the file if the content has actually changed.
    Returns the path to the config file.
    """
    config_path = os.path.join(project_folder, ".mcp.json")

    existing_content = ""
    existing_config: dict = {}
    if os.path.exists(config_path):
        try:
            with open(config_path, "r", encoding="utf-8") as config_file:
                existing_content = config_file.read()
            existing_config = json.loads(existing_content)
        except (json.JSONDecodeError, OSError):
            existing_config = {}

    mcp_servers = existing_config.get("mcpServers", {})
    mcp_servers["celbridge"] = {
        "type": "http",
        "url": "http://127.0.0.1:${CELBRIDGE_MCP_PORT}/mcp"
    }
    existing_config["mcpServers"] = mcp_servers

    new_content = json.dumps(existing_config, indent=2) + "\n"

    if new_content != existing_content:
        with open(config_path, "w", encoding="utf-8") as config_file:
            config_file.write(new_content)

    return config_path


_BOOTSTRAP_PROMPT = (
    "Your first non-proxy tool call this session will return three "
    "auto-attached blocks ahead of the result: the current app state, the "
    "current open documents, and the agent_instructions orientation guide. "
    "Read all three before deciding what to do — the state snapshots tell "
    "you which project is loaded, which feature flags are enabled, and "
    "which documents the user has open. Per-tool, namespace, and concept "
    "guides arrive automatically on first use of each tool; if your context "
    "auto-compacts and you need a guide back, call guides_read(['<name>'])."
)


def launch_claude() -> None:
    """Launch Claude Code CLI with sandboxed access to Celbridge MCP tools.

    Writes the .mcp.json config file and starts Claude in the current terminal.
    Claude has the Celbridge MCP tools and the built-in WebFetch and WebSearch
    tools, all pre-approved, and no file editing, bash access, or other
    built-in tools. A bootstrap system prompt explains the auto-attached state
    snapshots and orientation guide that arrive on the first tool call. Tool
    guides arrive automatically on first use of each tool through the
    auto-attach response filter.
    """
    if not shutil.which("claude"):
        print(
            "Claude Code CLI ('claude') was not found on the system PATH.\n"
            "Please install Claude Code CLI and try again.",
            file=sys.stderr)
        return

    project_folder = _get_project_folder()
    _write_mcp_config(project_folder)

    # --tools governs built-in availability only (MCP tools arrive via
    # --mcp-config regardless), so WebFetch and WebSearch are the only built-in
    # tools the session has. --allowedTools pre-approves them along with the
    # Celbridge MCP tools.
    launch_command = [
        "claude",
        "--strict-mcp-config",
        "--mcp-config", ".mcp.json",
        "--tools", "WebFetch,WebSearch",
        "--allowedTools", "mcp__celbridge__*,WebFetch,WebSearch",
        "--append-system-prompt", _BOOTSTRAP_PROMPT,
    ]

    print("Launching restricted Claude Code CLI with Celbridge tools, web fetch and web search.")
    subprocess.run(launch_command, cwd=project_folder, check=False)
