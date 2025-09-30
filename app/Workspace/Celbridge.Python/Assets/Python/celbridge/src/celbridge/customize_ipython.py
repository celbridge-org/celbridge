import atexit
import builtins
import os
import runpy
import shlex
import sys
import platform


def apply_ipython_customizations():

    try:
        # Safe import: fail gracefully if IPython isn’t installed
        from IPython import get_ipython  # type: ignore[import-not-found]
    except Exception:
        return False  # IPython not available; nothing to do

    # Make IPython look like the familiar Python REPL.

    try:
        ip = get_ipython()
    except NameError:
        ip = None

    if ip is not None:
        # Disable Out[] caching and matching bracket highlighting for this session
        try:
            ip.run_line_magic('config', 'InteractiveShell.cache_size = 0')
            ip.run_line_magic('config', 'InteractiveShell.highlight_matching_brackets  = False')
        except Exception:
            if hasattr(ip, 'displayhook'):
                ip.displayhook.cache_size = 0

        # Wipe any existing cached outputs from earlier config
        try:
            oh = ip.user_ns.get('_oh')
            if isinstance(oh, dict):
                oh.clear()
            for k in ('_', '__', '___'):
                if k in ip.user_ns:
                    ip.user_ns[k] = None
        except Exception:
            pass

        # Use plain Python REPL-style prompts
        try:
            from IPython.terminal.prompts import Prompts, Token

            class PythonStylePrompts(Prompts):
                def in_prompt_tokens(self, *a, **k):
                    return [(Token.Prompt, '>>> ')]
                def continuation_prompt_tokens(self, *a, **k):
                    return [(Token.Prompt, '... ')]
                def out_prompt_tokens(self, *a, **k):
                    return []  # hide Out[n]:

            ip.prompts = PythonStylePrompts(ip)
        except Exception:
            pass
