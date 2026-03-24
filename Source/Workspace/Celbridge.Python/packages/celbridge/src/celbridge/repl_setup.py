"""REPL environment setup for the Celbridge Python connector.

Handles IPython customization and startup banner.
"""

import atexit
import os
import platform
import sys
import traceback


def _disable_output_caching(ip) -> None:
    """Disable IPython output caching to prevent memory buildup."""
    try:
        ip.run_line_magic('config', 'InteractiveShell.cache_size = 0')
        ip.run_line_magic('config', 'InteractiveShell.highlight_matching_brackets = False')
    except (ValueError, AttributeError):
        if hasattr(ip, 'displayhook'):
            ip.displayhook.cache_size = 0


def _clear_output_history(ip) -> None:
    """Clear any output history variables left from IPython initialization."""
    try:
        output_history = ip.user_ns.get('_oh')
        if isinstance(output_history, dict):
            output_history.clear()
        for key in ('_', '__', '___'):
            if key in ip.user_ns:
                ip.user_ns[key] = None
    except (KeyError, TypeError):
        pass


def _setup_exception_handler(ip) -> None:
    """Configure IPython to display CelError and AttributeError without tracebacks."""
    try:
        from celbridge.cel_proxy import CelError

        def cel_exception_handler(self, exception_type, exception, traceback_obj, tb_offset=None):
            """Display cel proxy exceptions without traceback for a clean REPL experience."""
            if exception_type is AttributeError:
                message = str(exception)
                if "is not a known command" not in message:
                    self.showtraceback()
                    return
            red = "\033[31m"
            reset = "\033[0m"
            print(f"{red}{exception_type.__name__}{reset}: {exception}", file=sys.stderr)

        ip.set_custom_exc((CelError, AttributeError), cel_exception_handler)
    except ImportError:
        pass


def _setup_prompts(ip) -> None:
    """Replace IPython prompts with standard Python-style >>> prompts."""
    try:
        from IPython.terminal.prompts import Prompts, Token  # type: ignore[import-not-found]

        class PythonStylePrompts(Prompts):
            def in_prompt_tokens(self, *a, **k):
                return [(Token.Prompt, '>>> ')]
            def continuation_prompt_tokens(self, *a, **k):
                return [(Token.Prompt, '... ')]
            def out_prompt_tokens(self, *a, **k):
                return []

        ip.prompts = PythonStylePrompts(ip)
    except ImportError:
        pass


def apply_post_startup_customizations() -> None:
    """Apply customizations that require a running IPython instance.

    Called after IPython has started via exec_lines, so get_ipython() returns
    a valid instance.
    """
    try:
        from IPython import get_ipython  # type: ignore[import-not-found]
    except ImportError:
        return

    ip = get_ipython()
    if ip is None:
        return

    _disable_output_caching(ip)
    _clear_output_history(ip)
    _setup_exception_handler(ip)
    _setup_prompts(ip)


# Line executed inside IPython after startup to apply customizations that
# require a running IPython instance (custom prompts, caching).
POST_STARTUP_LINE = "from celbridge.repl_setup import apply_post_startup_customizations; apply_post_startup_customizations()"


def setup_repl() -> None:
    """Initialize the REPL environment before IPython starts.

    Sets up the Python path, exit message, and startup banner.
    """
    try:
        # Add the current project folder to Python path for easy imports
        project_folder = os.getcwd()
        if project_folder not in sys.path:
            sys.path.insert(0, project_folder)

        # Show a helpful message when the user exits the REPL
        atexit.register(lambda: print("\nCelbridge session ended. Type 'celbridge-py' to start a new session."))

        # Clear the console and display Celbridge startup banner
        os.system('cls' if os.name == 'nt' else 'clear')
        celbridge_version = os.environ.get('CELBRIDGE_VERSION', 'Unknown')
        python_version = platform.python_version()
        print(f"Celbridge v{celbridge_version} - Python v{python_version}")
        print("Type help(cel) for a list of available commands.")

    except Exception:
        print("Error during Celbridge startup:\n", file=sys.stderr)
        traceback.print_exc()
        raise
