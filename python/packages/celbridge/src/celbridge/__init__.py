"""Celbridge CLI package.

A command-line interface for managing Celbridge projects.
Provides commands for project management, file operations, builds, and more.
"""

__all__ = ["__version__"]

try:
    from importlib.metadata import version, PackageNotFoundError
    try:
        __version__ = version("celbridge")
    except PackageNotFoundError:
        __version__ = "0.0.0.0"
except ImportError:
    __version__ = "0.0.0.0"
