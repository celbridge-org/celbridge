"""Integration tests for host wrapper that calls the real CLI."""

import pytest
from celbridge_host import CelbridgeHost, cel


def test_version_json_format():
    """Test getting version information in JSON format."""
    result = cel.version(format="json")
    
    # Verify structure
    assert isinstance(result, dict)
    assert "version" in result
    assert "api" in result
    
    # Verify values
    assert result["version"] == "0.1.0"
    assert result["api"] == "1.0"


def test_version_text_format():
    """Test getting version information in text format."""
    result = cel.version(format="text")
    
    assert isinstance(result, str)
    assert "celbridge" in result
    assert "0.1.0" in result


def test_celbridge_host_class_instance():
    """Test creating and using a CelbridgeHost instance directly."""
    host = CelbridgeHost()
    
    result = host.version(format="json")
    assert result


def test_invalid_command():
    """Test that invalid commands raise appropriate errors."""
    with pytest.raises(RuntimeError, match="CLI command failed"):
        cel.nonexistent_command(format="json")


def test_invalid_json_response():
    """Test that invalid format options are handled correctly."""
    # The CLI should error on invalid format
    with pytest.raises(Exception):  # Could be RuntimeError or other
        cel.version(format="invalid_format")
