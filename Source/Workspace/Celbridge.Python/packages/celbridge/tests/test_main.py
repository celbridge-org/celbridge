"""Tests for the __main__ entry point."""

import pytest

from celbridge.__main__ import (
    DIAGNOSTIC_OSC_CODE,
    ResolvedLaunch,
    _build_bootstrap_command,
    _build_exec_lines,
    _build_probe_command,
    _emit_diagnostic,
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
        "CELBRIDGE_PYTHON_OFFLINE": "1",
    }
    resolved = _resolve_launch(environ, [])
    assert resolved == ResolvedLaunch("3.13", ["numpy", "pandas>=2"], True, [])
    assert resolved.requires_bootstrap is True


def test_resolve_launch_takes_arguments_only_from_the_command_line():
    """Test that IPython flags come from the command line, never from a console's configuration."""
    environ = {"CELBRIDGE_PYTHON_ARGS": "-i"}
    assert _resolve_launch(environ, ["-q"]).ipython_arguments == ["-q"]


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


def test_build_probe_command_forces_offline_with_a_no_op_payload():
    """Test that the probe runs the launch it is measuring, offline, without starting the REPL."""
    resolved = ResolvedLaunch("3.13", ["numpy"], False, ["-i"])
    environ = {
        "CELBRIDGE_UV": "/apps/python/uv",
        "CELBRIDGE_WHEEL": "/apps/python/celbridge-0.1.0-py3-none-any.whl",
        "CELBRIDGE_UV_CACHE_DIR": "/project/.celbridge/python/uv_cache",
    }
    command = _build_probe_command(resolved, environ)
    assert command == [
        "/apps/python/uv", "run",
        "--cache-dir", "/project/.celbridge/python/uv_cache",
        "--offline",
        "--no-project",
        "--python", "3.13",
        "--managed-python",
        "--with", "/apps/python/celbridge-0.1.0-py3-none-any.whl",
        "--with", "numpy",
        "python", "-c", "",
    ]


def test_build_probe_command_matches_the_launch_it_measures():
    """Test that the probe differs from the real launch only in offline mode and the payload."""
    resolved = ResolvedLaunch("3.12", ["pandas>=2"], False, ["-q"])
    environ = {
        "CELBRIDGE_UV": "/apps/python/uv",
        "CELBRIDGE_WHEEL": "/apps/python/celbridge-0.1.0-py3-none-any.whl",
    }
    probe = _build_probe_command(resolved, environ)
    launch = _build_bootstrap_command(resolved._replace(offline=True), environ)

    payload_index = probe.index("python")
    assert probe[:payload_index] == launch[:payload_index]
    assert probe[payload_index:] == ["python", "-c", ""]


def test_emit_diagnostic_writes_a_private_osc_sequence(capsys):
    """Test that a diagnostic is a private OSC, which a terminal that saw it would render as nothing."""
    _emit_diagnostic("python-probe mode=offline ms=412")

    expected = f"\x1b]{DIAGNOSTIC_OSC_CODE};python-probe mode=offline ms=412\x07"
    assert capsys.readouterr().out == expected
