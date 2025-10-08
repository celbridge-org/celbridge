import sys
from . import repl_environment


def _main(argv=None) -> int:
    """
    Main entry point for the Celbridge Host module.
    """
    return repl_environment.initialize_repl_environment()


if __name__ == "__main__":
    code = _main()
    if code != 0:
        raise SystemExit(code)
