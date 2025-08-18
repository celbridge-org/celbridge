import os
import sys
import shlex
import runpy
import traceback
import subprocess
import shutil
import importlib
from typing import Tuple, List, Optional

def _resolve_script(script_path: str, args) -> Tuple[Optional[str], Optional[List[str]]]:
    """
    Parse script_path and arguments.
    Returns (abs_path, arg_list) or (None, None) if invalid.
    """

    if args:
        # e.g. run("myscript.py", "1", "2")
        path_part = script_path
        arg_list = [str(a) for a in args]
    else:
        # e.g. "myscript.py --x 1"
        parts = shlex.split(script_path, posix=(os.name != 'nt'))
        if not parts:
            print("No script specified.")
            return None, None
        path_part, *arg_list = parts

    return path_part, arg_list


def run(script_path: str, *args):
    """
    Execute a Python script in the current process. Arguments can be appended in the script_path or as arguments to this function.
    """
    abs_path, arg_list = _resolve_script(script_path, args)
    if not abs_path:
        return

    # Store the current arguments
    old_argv = sys.argv[:]
    try:
        # Populate arguments for calling the script
        sys.argv = [abs_path] + (arg_list or [])
        runpy.run_path(abs_path, run_name="__main__")
    except SystemExit:
        # Swallow exit so the host REPL keeps running.
        pass
    except Exception as e:
        print(f"Error while running script ({e.__class__.__name__}): {e}")
        traceback.print_exc()
    finally:
        # Restore the original arguments
        sys.argv = old_argv


def install():
    """
    Ensure a Python/Lib directory and requirements.txt exist.
    If requirements.txt exists, delete and reinstall all required packages.
    """
    cwd = os.getcwd()
    python_dir = os.path.join(cwd, "Python")
    lib_dir = os.path.join(python_dir, "Lib")
    scripts_dir = os.path.join(python_dir, "Scripts")
    requirements_file = os.path.join(python_dir, "requirements.txt")

    # Ensure the base Python folder exists
    os.makedirs(python_dir, exist_ok=True)

    # First run just creates an empty requirements.txt file.
    if not os.path.isfile(requirements_file):
        os.makedirs(lib_dir, exist_ok=True)
        open(requirements_file, "w").close()
        print(f"Add required packages to 'Python/Lib/requirements.txt' and run cel.install() again to install packages.")
        return

    # Delete previously installed packages
    # This slows down the install process, but ensures we don't get orphaned packages.
    for path in (lib_dir, scripts_dir):
        if os.path.isdir(path):
            shutil.rmtree(path, ignore_errors=True)

    # Recreate target lib folder
    # os.makedirs(lib_dir, exist_ok=True)

    # Install fresh
    print(f"Installing packages...")
    subprocess.check_call([
        sys.executable, "-m", "pip", "install",
        "--target", lib_dir,
        "-r", requirements_file,
        #"-qq", # Supress debug output
        "--only-binary=:all:"
    ])

    importlib.invalidate_caches()
    print("Packages installed.")
