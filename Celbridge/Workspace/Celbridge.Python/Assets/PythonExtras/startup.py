import atexit
import builtins
import cel
import exit_lock
import os
import runpy
import shlex
import sys

def _add_sys_paths():

    project_dir = os.getcwd()
    if project_dir not in sys.path:
        sys.path.insert(0, project_dir)

    local_packages = os.path.join(project_dir, "Python", "Lib")
    sys.path.insert(0, local_packages)

    python_scripts = os.path.join(project_dir, "Python")
    sys.path.insert(0, python_scripts)


def _customize_ipython():

    from IPython import get_ipython

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


exit_lock.apply_exit_lock()
_add_sys_paths()
_customize_ipython()

# Display Celbridge version number
version = os.environ.get('CELBRIDGE_VERSION', '')
print(f"Celbridge version {version} - Python Console")
