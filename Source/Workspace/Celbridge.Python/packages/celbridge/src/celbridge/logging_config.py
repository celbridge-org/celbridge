"""Logging configuration for the Celbridge Python connector."""

import os
import logging
from pathlib import Path
from datetime import datetime
import glob


def configure_logging(log_level: str = "INFO", log_dir: str | None = None, max_log_files: int = 10) -> None:
    """Configure logging for the Celbridge Python connector.

    Args:
        log_level: Logging level (DEBUG, INFO, WARNING, ERROR, CRITICAL).
                   Can be overridden by PYTHON_LOG_LEVEL environment variable.
        log_dir: Optional path to log folder. If provided, creates timestamped log files.
                 Can be set via PYTHON_LOG_DIR environment variable.
        max_log_files: Maximum number of log files to keep. Older files are deleted.
                       Can be overridden by PYTHON_LOG_MAX_FILES environment variable.
    """
    # Allow environment variables to override defaults
    log_level = os.environ.get('PYTHON_LOG_LEVEL', log_level).upper()
    log_dir = os.environ.get('PYTHON_LOG_DIR', log_dir)
    max_log_files = int(os.environ.get('PYTHON_LOG_MAX_FILES', max_log_files))

    # Convert string level to logging constant
    numeric_level = getattr(logging, log_level, logging.INFO)

    # Set up handlers
    handlers = []

    # File log handler with timestamped filename
    log_file = None
    if log_dir:
        log_path = Path(log_dir)
        log_path.mkdir(parents=True, exist_ok=True)

        # Clean up old log files, keeping only the most recent max_log_files
        existing_logs = sorted(glob.glob(str(log_path / 'celbridge_*.log')))
        if len(existing_logs) >= max_log_files:
            files_to_delete = existing_logs[:len(existing_logs) - max_log_files + 1]
            for old_log in files_to_delete:
                try:
                    os.remove(old_log)
                except OSError:
                    pass

        # Create timestamped filename, e.g. celbridge_20251016_143052.log
        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        log_file = log_path / f'celbridge_{timestamp}.log'

        file_handler = logging.FileHandler(log_file, mode='w', encoding='utf-8')
        file_handler.setLevel(numeric_level)

        formatter = logging.Formatter(
            '%(asctime)s - %(name)s - %(levelname)s - %(message)s',
            datefmt='%Y-%m-%d %H:%M:%S'
        )
        file_handler.setFormatter(formatter)

        handlers.append(file_handler)

    # Configure root logger
    logging.basicConfig(
        level=numeric_level,
        handlers=handlers,
        force=True
    )

    # Suppress noisy third-party debug logs
    for noisy_logger in ["asyncio"]:
        logging.getLogger(noisy_logger).setLevel(logging.INFO)

    logger = logging.getLogger(__name__)
    logger.debug(f"Python logging configured: level={log_level}, dir={log_dir}, file={log_file}")

    celbridge_log_file = os.environ.get("CELBRIDGE_LOG_FILE")
    if celbridge_log_file:
        logger.info(f"The Celbridge application log is here: '{celbridge_log_file}'")
