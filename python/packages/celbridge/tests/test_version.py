"""Tests for the version command."""

import json
import pytest
from typer.testing import CliRunner
from celbridge.__main__ import app
from celbridge import __version__


@pytest.fixture
def runner():
    """Create a fresh CliRunner instance for each test."""
    return CliRunner()


def test_version_command(runner):
    """Test version command returns JSON output."""
    result = runner.invoke(app, ["version"])
    assert result.exit_code == 0
    
    # Parse JSON output
    data = json.loads(result.stdout)
    assert data["version"] == __version__
    assert data["api"] == "1.0"
