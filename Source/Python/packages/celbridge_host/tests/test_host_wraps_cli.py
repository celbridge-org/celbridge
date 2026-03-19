"""Integration tests for host wrapper that calls the real CLI."""

import pytest
from celbridge_host import CelbridgeHost


@pytest.fixture
def cel():
    """Create a fresh CelbridgeHost instance for each test."""
    return CelbridgeHost()


def test_version_command(cel):
    """Test getting version information."""
    result = cel.version()
    
    # Verify structure
    assert isinstance(result, dict)
    assert "version" in result
    assert "api" in result
    
    # Verify values are non-empty strings
    assert isinstance(result["version"], str)
    assert len(result["version"]) > 0
    assert isinstance(result["api"], str)
    assert len(result["api"]) > 0


def test_invalid_command(cel, capsys):
    """Test that invalid commands display errors cleanly without raising."""
    result = cel.nonexistent_command()
    
    # Should return None
    assert result is None
    
    # Error should be printed to stdout
    captured = capsys.readouterr()
    assert "No such command" in captured.out


def test_missing_required_parameter_shows_help(cel, capsys):
    """Test that missing required parameters display error and command help."""
    result = cel.greet()
    
    # Should return None
    assert result is None
    
    # Error and help should be printed to stdout
    captured = capsys.readouterr()
    assert "Missing argument 'NAME'" in captured.out
    
    # Should display usage information with command signature
    assert "Usage: cel.greet(" in captured.out
    assert "name" in captured.out  # Required parameter
    assert "greeting=" in captured.out  # Optional parameter with default
    
    # Should display command description
    assert "Greet someone with a custom message" in captured.out


def test_greet_command_with_required_parameter(cel):
    """Test greet command with required parameter."""
    result = cel.greet("World")
    
    # Verify structure
    assert isinstance(result, dict)
    assert "message" in result
    assert "name" in result
    assert "greeting" in result
    
    # Verify values
    assert result["message"] == "Hello, World!"
    assert result["name"] == "World"
    assert result["greeting"] == "Hello"


def test_greet_command_with_optional_parameter(cel):
    """Test greet command with both required and optional parameters."""
    result = cel.greet("Alice", greeting="Hi")
    
    # Verify structure
    assert isinstance(result, dict)
    
    # Verify values
    assert result["message"] == "Hi, Alice!"
    assert result["name"] == "Alice"
    assert result["greeting"] == "Hi"

