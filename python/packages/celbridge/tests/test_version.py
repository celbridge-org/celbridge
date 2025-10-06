"""Tests for the version command."""

import json
from typer.testing import CliRunner
from celbridge.__main__ import app
from celbridge import __version__

runner = CliRunner()


def test_version_text_format():
    """Test version command with text format."""
    result = runner.invoke(app, ["version", "--format", "text"])
    assert result.exit_code == 0
    assert f"celbridge {__version__}" in result.stdout


def test_version_text_format_default():
    """Test version command with default (text) format."""
    result = runner.invoke(app, ["version"])
    assert result.exit_code == 0
    assert f"celbridge {__version__}" in result.stdout


def test_version_json_format():
    """Test version command with JSON format."""
    result = runner.invoke(app, ["version", "--format", "json"])
    assert result.exit_code == 0
    
    # Parse JSON output
    data = json.loads(result.stdout)
    assert data["version"] == __version__
    assert data["api"] == "1.0"


def test_version_invalid_format():
    """Test version command with invalid format."""
    result = runner.invoke(app, ["version", "--format", "invalid"])
    assert result.exit_code == 1
    # Error messages go to stdout in Typer's CliRunner
    output = result.stdout + result.stderr
    assert "Unknown format" in output
