import builtins
import os
import sys
import runpy

_original_exit = getattr(builtins, "exit", None)
_original_quit = getattr(builtins, "quit", None)
_original_sys_exit = sys.exit
_original_os_exit = getattr(os, "_exit", None)

_original_run_module = runpy.run_module
_original_run_path = runpy.run_path

_allow_exit = False
_installed = False


def _no_exit(code=0):
    if _allow_exit:
        _restore_exit_funcs()
        _original_sys_exit(code)


def _lock_exit_funcs():
    builtins.exit = _no_exit
    builtins.quit = _no_exit
    sys.exit = _no_exit
    if _original_os_exit is not None:
        os._exit = _no_exit


def _restore_exit_funcs():
    if _original_exit is None:
        if hasattr(builtins, "exit"):
            delattr(builtins, "exit")
    else:
        builtins.exit = _original_exit

    if _original_quit is None:
        if hasattr(builtins, "quit"):
            delattr(builtins, "quit")
    else:
        builtins.quit = _original_quit

    sys.exit = _original_sys_exit
    if _original_os_exit is not None:
        os._exit = _original_os_exit


def _wrap_allow_exit(fn):
    def wrapper(*args, **kwargs):
        global _allow_exit
        prev = _allow_exit
        _allow_exit = True
        try:
            return fn(*args, **kwargs)
        finally:
            _allow_exit = prev
    return wrapper


def _attach_ipython_hooks():
    """money patches IPython's exit paths to block exiting."""

    try:
        from IPython import get_ipython 
        ip = get_ipython()
    except Exception:
        ip = None

    if not ip:
        return

    # Only patch once per IPython instance
    if getattr(ip, "_exit_lock_patched", False) is True:
        return

    # 1. Block Ctrl-D / exit via ask_exit()
    orig_ask_exit = getattr(ip, "ask_exit", None)
    if orig_ask_exit is not None:
        def _guarded_ask_exit(*a, **k):
            if _allow_exit:
                return orig_ask_exit(*a, **k)
            raise RuntimeError("Exit is disabled in interactive mode (Ctrl-D/exit)")
        ip.ask_exit = _guarded_ask_exit  # type: ignore[attr-defined]
        ip._exit_lock_orig_ask_exit = orig_ask_exit  # stash for restore

    # 2. Replace user-ns 'exit'/'quit' with our guard (prevents firing ask_exit)
    try:
        ip.user_ns["exit"] = _no_exit
        ip.user_ns["quit"] = _no_exit
    except Exception:
        pass

    # 3. Allow exit during %run foo.py (safe_execfile)
    if hasattr(ip, "safe_execfile"):
        ip._exit_lock_orig_safe_execfile = ip.safe_execfile
        ip.safe_execfile = _wrap_allow_exit(ip.safe_execfile)

    # 4. Allow exit during %run on .ipy/.ipynb (safe_execfile_ipy)
    if hasattr(ip, "safe_execfile_ipy"):
        ip._exit_lock_orig_safe_execfile_ipy = ip.safe_execfile_ipy
        ip.safe_execfile_ipy = _wrap_allow_exit(ip.safe_execfile_ipy)

    # 5. Allow exit during `%run -m pkg` (safe_run_module)
    if hasattr(ip, "safe_run_module"):
        ip._exit_lock_orig_safe_run_module = ip.safe_run_module
        ip.safe_run_module = _wrap_allow_exit(ip.safe_run_module)

    ip._exit_lock_patched = True


def apply_exit_lock():
    """Prevents user from exiting the REPL."""
    global _installed
    if not _installed:
        runpy.run_module = _wrap_allow_exit(_original_run_module)
        runpy.run_path   = _wrap_allow_exit(_original_run_path)
        _lock_exit_funcs()
        _installed = True
    _attach_ipython_hooks()  # (re)attach if IPython just got initialized
