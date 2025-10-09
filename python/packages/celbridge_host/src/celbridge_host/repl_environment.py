# repl_environment.py
"""
Celbridge Python REPL environment initialization module.

This module handles the setup and configuration of the Python REPL environment
for Celbridge, including path management, customizations, and version display.
"""

import os
import sys
import platform
import traceback

from . import customize_ipython
from . import exit_lock
from . import exit_message
from .celbridge_host import CelbridgeHost


def setup_global_cel() -> None:
    """Make the 'cel' object available globally in the REPL."""
    
    # Create a CelbridgeHost instance and inject it into the main module's global namespace
    # This makes it available immediately when the REPL starts
    import __main__
    __main__.cel = CelbridgeHost()  # type: ignore[attr-defined]


def setup_python_path() -> None:
    """Add the current project directory to Python path for easy imports."""

    project_dir = os.getcwd()
    if project_dir not in sys.path:
        sys.path.insert(0, project_dir)


def setup_exit_handling() -> None:
    """Configure exit lock and exit message for the REPL."""

    # Prevent the user from exiting the interpreter in Celbridge
    exit_lock.apply_exit_lock()    
    exit_message.register("\nPython interpreter has exited.")


def setup_ipython_customizations() -> None:
    """Apply IPython customizations for better REPL experience."""

    customize_ipython.apply_ipython_customizations()


def display_startup_banner() -> None:
    """Clear console and display Celbridge startup banner with version info."""

    # Clear the console output
    os.system('cls' if os.name == 'nt' else 'clear')
    
    # Display version numbers in banner
    celbridge_version = os.environ.get('CELBRIDGE_VERSION', 'Unknown')
    python_version = platform.python_version()
    
    print(f"Celbridge v{celbridge_version} - Python v{python_version}")
    print("Type cel.help() for a list of available commands.")


def initialize_repl_environment() -> int:
    """
    Initialize the complete Celbridge Python REPL environment.
    
    Returns:
        int: Exit code (0 for success, 1 for failure)
    """

    try:
        setup_python_path()
        setup_exit_handling()
        setup_ipython_customizations()
        setup_global_cel()
        display_startup_banner()
        
        return 0
    
    except Exception:
        print("Error during Celbridge startup:\n", file=sys.stderr)
        traceback.print_exc()
        return 1
