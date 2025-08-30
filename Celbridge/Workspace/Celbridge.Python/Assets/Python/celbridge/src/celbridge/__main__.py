import os, sys
import platform
import traceback

from . import customize_ipython
from . import exit_lock
from . import exit_message

def _main(argv=None) -> int:
    try:
        # Add project dir to the path so we can easily import from Python scripts in the project
        project_dir = os.getcwd()
        if project_dir not in sys.path:
            sys.path.insert(0, project_dir)

        # Prevent the user from exiting the interpreter in Celbridge
        # exit_lock.apply_exit_lock()

        exit_message.register("\nPython interpreter has exited.")

        # Customize the appearance of the IPython REPL
        customize_ipython.apply_ipython_customizations()

        # Display version numbers in banner
        celbridge_version = os.environ.get('CELBRIDGE_VERSION', '')
        python_version = platform.python_version()

        # Clear the console output
        os.system('cls' if os.name == 'nt' else 'clear')

        print(f"Celbridge v{celbridge_version} - Python v{python_version}")
        return 0

    except Exception:
        print("Error during Celbridge startup:\n", file=sys.stderr)
        traceback.print_exc()
        return 1


if __name__ == "__main__":
    code = _main()
    if code != 0:
        raise SystemExit(code)
