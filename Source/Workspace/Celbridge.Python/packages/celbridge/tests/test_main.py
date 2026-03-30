"""Tests for the __main__ entry point."""

import pytest
from celbridge.__main__ import _resolve_rpc_port


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
