"""Tests for the __main__ entry point."""

import pytest
from celbridge.__main__ import (
    ResolvedLaunch,
    _build_bootstrap_command,
    _build_exec_lines,
    _resolve_launch,
    _resolve_rpc_port,
)
from celbridge.repl_setup import POST_STARTUP_LINE


def test_resolve_rpc_port_returns_valid_port(monkeypatch):
    """Test that a valid port string is parsed correctly."""
    monkeypatch.setenv("CELBRIDGE_RPC_PORT", "49820")
    assert _resolve_rpc_port() == 49820


def test_resolve_rpc_port_exits_when_not_set(monkeypatch):
    """Test that missing CELBRIDGE_RPC_PORT causes SystemExit."""
    monkeypatch.delenv("CELBRIDGE_RPC_PORT", raising=False)
    with pytest.raises(SystemExit) as exit_info:
        _resolve_rpc_port()
    assert "not set" in str(exit_info.value)


def test_resolve_rpc_port_exits_on_invalid_value(monkeypatch):
    """Test that a non-numeric port value causes SystemExit."""
    monkeypatch.setenv("CELBRIDGE_RPC_PORT", "not_a_number")
    with pytest.raises(SystemExit) as exit_info:
        _resolve_rpc_port()
    assert "invalid value" in str(exit_info.value)


def test_resolve_rpc_port_exits_on_empty_string(monkeypatch):
    """Test that an empty string port value causes SystemExit."""
    monkeypatch.setenv("CELBRIDGE_RPC_PORT", "")
    with pytest.raises(SystemExit) as exit_info:
        _resolve_rpc_port()
    assert "invalid value" in str(exit_info.value)


def test_build_exec_lines_without_a_startup_script():
    """Test that only the REPL customizations run when no startup script is set."""
    assert _build_exec_lines('') == [POST_STARTUP_LINE]
    assert _build_exec_lines(None) == [POST_STARTUP_LINE]
    assert _build_exec_lines('   \n  ') == [POST_STARTUP_LINE]


def test_build_exec_lines_runs_the_startup_script_as_one_cell():
    """Test that the script is a single entry, so multi-line constructs run as one cell."""
    script = "for i in range(3):\n    print(i)\n"
    assert _build_exec_lines(script) == [POST_STARTUP_LINE, script]


def test_resolve_launch_outside_a_console_needs_no_bootstrap():
    """Test that a bare invocation with no console environment keeps the direct tool-venv path."""
    resolved = _resolve_launch({}, [])
    assert resolved.requires_bootstrap is False
    assert resolved.ipython_arguments == []


def test_resolve_launch_reads_the_console_configuration():
    """Test that a bare invocation in a python console reproduces that console's launch."""
    environ = {
        "CELBRIDGE_PYTHON_VERSION": "3.13",
        "CELBRIDGE_PYTHON_WITH": "numpy\npandas>=2",
        "CELBRIDGE_PYTHON_ARGS": "-i",
        "CELBRIDGE_PYTHON_OFFLINE": "1",
    }
    resolved = _resolve_launch(environ, [])
    assert resolved == ResolvedLaunch("3.13", ["numpy", "pandas>=2"], True, ["-i"])
    assert resolved.requires_bootstrap is True


def test_resolve_launch_appends_typed_arguments_to_the_configured_ones():
    """Test that typed arguments add to the console's configured interpreter arguments."""
    environ = {"CELBRIDGE_PYTHON_ARGS": "-i"}
    assert _resolve_launch(environ, ["-q"]).ipython_arguments == ["-i", "-q"]


def test_resolve_launch_dependencies_only_still_bootstraps():
    """Test that a console configuring only packages still re-execs through uv."""
    resolved = _resolve_launch({"CELBRIDGE_PYTHON_WITH": "numpy"}, [])
    assert resolved.requires_bootstrap is True
    assert resolved.with_packages == ["numpy"]


def test_build_bootstrap_command_builds_full_uv_run_command():
    """Test the uv run command for a fully specified launch."""
    resolved = ResolvedLaunch("3.13", ["numpy"], True, ["-i"])
    environ = {
        "CELBRIDGE_UV": "/apps/python/uv",
        "CELBRIDGE_WHEEL": "/apps/python/celbridge-0.1.0-py3-none-any.whl",
        "CELBRIDGE_UV_CACHE_DIR": "/project/.celbridge/python/uv_cache",
    }
    command = _build_bootstrap_command(resolved, environ)
    assert command == [
        "/apps/python/uv", "run",
        "--cache-dir", "/project/.celbridge/python/uv_cache",
        "--offline",
        "--no-project",
        "--python", "3.13",
        "--managed-python",
        "--with", "/apps/python/celbridge-0.1.0-py3-none-any.whl",
        "--with", "numpy",
        "python", "-m", "celbridge",
        "-i",
    ]


def test_build_bootstrap_command_omits_absent_options():
    """Test that cache dir, offline, and python version are omitted when not provided."""
    resolved = ResolvedLaunch(None, ["requests"], False, [])
    environ = {
        "CELBRIDGE_UV": "/apps/python/uv",
        "CELBRIDGE_WHEEL": "/apps/python/celbridge-0.1.0-py3-none-any.whl",
    }
    command = _build_bootstrap_command(resolved, environ)
    assert command == [
        "/apps/python/uv", "run",
        "--no-project",
        "--managed-python",
        "--with", "/apps/python/celbridge-0.1.0-py3-none-any.whl",
        "--with", "requests",
        "python", "-m", "celbridge",
    ]


def test_build_bootstrap_command_requires_console_environment():
    """Test that missing CELBRIDGE_UV or CELBRIDGE_WHEEL causes SystemExit."""
    resolved = ResolvedLaunch(None, [], True, [])
    with pytest.raises(SystemExit) as exit_info:
        _build_bootstrap_command(resolved, {})
    assert "CELBRIDGE_UV" in str(exit_info.value)
