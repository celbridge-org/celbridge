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
