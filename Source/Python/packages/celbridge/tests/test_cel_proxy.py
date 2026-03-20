"""Tests for CelProxy method name conversion and dispatch."""

from unittest.mock import MagicMock
from celbridge.cel_proxy import CelProxy, _snake_to_pascal


def test_snake_to_pascal_single_word():
    """Test converting a single word."""
    assert _snake_to_pascal("log") == "Log"


def test_snake_to_pascal_multiple_words():
    """Test converting multi-word snake_case."""
    assert _snake_to_pascal("get_app_version") == "GetAppVersion"


def test_proxy_converts_method_name():
    """Test that CelProxy converts snake_case to PascalCase when calling the broker."""
    mock_client = MagicMock()
    mock_client.call.return_value = "1.0.0"

    cel = CelProxy(mock_client)
    result = cel.get_app_version()

    mock_client.call.assert_called_once_with("GetAppVersion")
    assert result == "1.0.0"


def test_proxy_passes_kwargs():
    """Test that CelProxy passes keyword arguments to the broker."""
    mock_client = MagicMock()
    mock_client.call.return_value = None

    cel = CelProxy(mock_client)
    cel.some_method(name="Alice", count=42)

    mock_client.call.assert_called_once_with("SomeMethod", name="Alice", count=42)


def test_log_uses_notify():
    """Test that cel.log() uses notify (fire-and-forget) instead of call."""
    mock_client = MagicMock()

    cel = CelProxy(mock_client)
    cel.log(message="hello")

    mock_client.notify.assert_called_once_with("Log", message="hello")
    mock_client.call.assert_not_called()
