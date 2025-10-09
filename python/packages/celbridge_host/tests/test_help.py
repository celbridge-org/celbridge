"""Tests for the help command formatting."""

import io
import sys
from celbridge_host import CelbridgeHost


def test_help_displays_formatted_output():
    """Test that cel.help() displays formatted help text."""
    cel = CelbridgeHost()
    
    # Capture stdout
    captured_output = io.StringIO()
    sys.stdout = captured_output
    
    try:
        cel.help()
        output = captured_output.getvalue()
        
        # Verify output text contains help info
        assert "Available Commands:" in output
        
    finally:
        # Restore stdout
        sys.stdout = sys.__stdout__


def test_help_returns_none():
    """Test that cel.help() returns None (prints only)."""
    cel = CelbridgeHost()
    
    # Capture stdout to suppress output
    captured_output = io.StringIO()
    sys.stdout = captured_output
    
    try:
        result = cel.help()
        assert result is None
    finally:
        sys.stdout = sys.__stdout__
