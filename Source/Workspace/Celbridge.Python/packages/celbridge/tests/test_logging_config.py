"""Tests for logging configuration."""

import logging
import os
from pathlib import Path

from celbridge.logging_config import configure_logging


def test_configure_logging_creates_log_file(tmp_path):
    """Test that configure_logging creates a timestamped log file."""
    configure_logging(log_dir=str(tmp_path))

    log_files = list(tmp_path.glob("celbridge_*.log"))
    assert len(log_files) == 1


def test_configure_logging_respects_log_level(tmp_path):
    """Test that the configured log level is applied."""
    configure_logging(log_level="WARNING", log_dir=str(tmp_path))

    root_logger = logging.getLogger()
    assert root_logger.level == logging.WARNING


def test_configure_logging_cleans_old_files(tmp_path):
    """Test that old log files are deleted when max is exceeded."""
    for i in range(5):
        (tmp_path / f"celbridge_{i:04d}.log").write_text(f"log {i}")

    configure_logging(log_dir=str(tmp_path), max_log_files=3)

    log_files = sorted(tmp_path.glob("celbridge_*.log"))
    assert len(log_files) <= 3


def test_configure_logging_env_override(tmp_path, monkeypatch):
    """Test that environment variables override defaults."""
    monkeypatch.setenv("PYTHON_LOG_LEVEL", "DEBUG")
    configure_logging(log_dir=str(tmp_path))

    root_logger = logging.getLogger()
    assert root_logger.level == logging.DEBUG


def test_configure_logging_without_log_dir():
    """Test that logging works without a log directory (no file handler)."""
    configure_logging(log_dir=None)

    root_logger = logging.getLogger()
    assert root_logger.level is not None
