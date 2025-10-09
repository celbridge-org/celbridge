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
        
        # Verify output text contains formatted help info
        assert "Available Commands:" in output
        
    finally:
        # Restore stdout
        sys.stdout = sys.__stdout__
