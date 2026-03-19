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


def test_help_with_specific_command():
    """Test that cel.help('command') displays help for a specific command."""
    cel = CelbridgeHost()
    
    # Capture stdout
    captured_output = io.StringIO()
    sys.stdout = captured_output
    
    try:
        cel.help("greet")
        output = captured_output.getvalue()
        
        # Verify output contains specific command help
        assert "cel.greet(" in output
        assert "Greet someone with a custom message" in output
        
    finally:
        # Restore stdout
        sys.stdout = sys.__stdout__


def test_help_with_invalid_command():
    """Test that cel.help('invalid') displays error message."""
    cel = CelbridgeHost()
    
    # Capture stdout
    captured_output = io.StringIO()
    sys.stdout = captured_output
    
    try:
        cel.help("nonexistent")
        output = captured_output.getvalue()
        
        # Verify error message is displayed
        assert "Command 'nonexistent' not found" in output
        
    finally:
        # Restore stdout
        sys.stdout = sys.__stdout__
